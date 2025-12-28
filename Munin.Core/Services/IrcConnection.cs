using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Munin.Core.Events;
using Munin.Core.Models;
using Serilog;

namespace Munin.Core.Services;

/// <summary>
/// Manages a connection to an IRC server with SSL/TLS support.
/// </summary>
/// <remarks>
/// <para>The main class for handling an IRC connection, including:</para>
/// <list type="bullet">
///   <item><description>TCP/SSL connection management</description></item>
///   <item><description>IRCv3 capability negotiation</description></item>
///   <item><description>SASL authentication (PLAIN, SCRAM-SHA-256)</description></item>
///   <item><description>Flood protection and message queuing</description></item>
///   <item><description>Automatic reconnection</description></item>
///   <item><description>Latency measurement</description></item>
/// </list>
/// <para>This class raises events for all IRC protocol messages that the UI layer can subscribe to.</para>
/// </remarks>
public class IrcConnection : IDisposable
{
    private readonly ILogger _logger;
    private readonly IrcMessageParser _parser = new();
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private bool _disposed;
    private bool _intentionalDisconnect;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 5;
    
    /// <summary>
    /// Whether automatic reconnection is enabled.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;
    
    /// <summary>
    /// Delay in seconds between reconnect attempts.
    /// </summary>
    public int ReconnectDelaySeconds { get; set; } = 5;
    
    /// <summary>
    /// Minimum TLS version for SSL connections.
    /// Options: "Tls12", "Tls13", "None" (allows any version).
    /// </summary>
    public string MinimumTlsVersion { get; set; } = "Tls12";
    
    /// <summary>
    /// Enable certificate revocation checking (OCSP/CRL).
    /// When enabled, the system will check if server certificates have been revoked.
    /// </summary>
    public bool EnableCertificateRevocationCheck { get; set; } = true;
    
    /// <summary>
    /// Event raised when a reconnect attempt starts.
    /// </summary>
    public event EventHandler<IrcReconnectEventArgs>? Reconnecting;

    public IrcServer Server { get; }
    public string CurrentNickname { get; private set; } = string.Empty;
    
    /// <summary>
    /// Returns true if connected to the IRC server (directly or via relay).
    /// </summary>
    public bool IsConnected => 
        Server.State == ConnectionState.Connected && 
        _stream != null && 
        (_tcpClient?.Connected == true || Server.Relay?.Enabled == true);

    /// <summary>
    /// Custom words that trigger highlight in addition to nickname.
    /// </summary>
    public List<string> HighlightWords { get; set; } = new();

    // Events
    public event EventHandler<IrcConnectionEventArgs>? Connected;
    public event EventHandler<IrcConnectionEventArgs>? Disconnected;
    public event EventHandler<IrcMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<IrcChannelMessageEventArgs>? ChannelMessage;
    public event EventHandler<IrcPrivateMessageEventArgs>? PrivateMessage;
    public event EventHandler<IrcChannelEventArgs>? ChannelJoined;
    public event EventHandler<IrcChannelEventArgs>? ChannelParted;
    public event EventHandler<IrcUserEventArgs>? UserJoined;
    public event EventHandler<IrcUserEventArgs>? UserParted;
    public event EventHandler<IrcUserEventArgs>? UserQuit;
    public event EventHandler<IrcNickChangedEventArgs>? NickChanged;
    public event EventHandler<IrcErrorEventArgs>? Error;
    public event EventHandler<IrcRawMessageEventArgs>? RawMessageReceived;
    public event EventHandler<IrcChannelEventArgs>? TopicChanged;
    public event EventHandler<IrcChannelEventArgs>? UserListUpdated;
    public event EventHandler<IrcServerMessageEventArgs>? ServerMessage;
    public event EventHandler<IrcChannelListEventArgs>? ChannelListReceived;
    public event EventHandler? ChannelListComplete;
    public event EventHandler<IrcWhoisEventArgs>? WhoisReceived;
    public event EventHandler<int>? LatencyUpdated;
    public event EventHandler<IrcWhoEventArgs>? WhoReceived;
    public event EventHandler? WhoComplete;
    public event EventHandler<IrcWhowasEventArgs>? WhowasReceived;
    public event EventHandler<IrcChannelModeListEventArgs>? ChannelModeListReceived;
    public event EventHandler<IrcBatchEventArgs>? BatchComplete;
    public event EventHandler<Dh1080CompleteEventArgs>? Dh1080KeyExchangeComplete;
    public event EventHandler<IrcMonitorEventArgs>? MonitorStatusChanged;
    public event EventHandler<IrcTypingEventArgs>? TypingNotification;
    public event EventHandler<IrcReactionEventArgs>? ReactionReceived;
    public event EventHandler<IrcReadMarkerEventArgs>? ReadMarkerReceived;
    
    // WHOIS info accumulator
    private WhoisInfo? _pendingWhois;
    
    // BATCH tracking
    private readonly Dictionary<string, IrcBatch> _activeBatches = new();
    
    // Echo-message deduplication
    private readonly HashSet<string> _sentMessageIds = new();
    private readonly Queue<(string MessageId, DateTime Timestamp)> _sentMessageIdQueue = new();
    private const int MaxSentMessageIds = 100;
    
    // SCRAM authentication state
    private ScramAuthenticator? _scramAuth;
    
    // Flood protection
    private FloodProtector? _floodProtector;
    
    // Channel mode state cache
    private readonly Dictionary<string, ChannelModeState> _channelModes = new(StringComparer.OrdinalIgnoreCase);
    
    // Typing indicators tracking (nick -> last typing timestamp)
    private readonly Dictionary<string, DateTime> _typingIndicators = new();
    
    // FiSH encryption service
    private FishCryptService? _fishCrypt;
    private Dh1080Manager? _dh1080Manager;
    
    /// <summary>
    /// Gets or sets the FiSH encryption service for message encryption.
    /// </summary>
    public FishCryptService? FishCrypt
    {
        get => _fishCrypt;
        set
        {
            _fishCrypt = value;
            if (_fishCrypt != null)
            {
                _dh1080Manager = new Dh1080Manager(_fishCrypt);
                _dh1080Manager.KeyExchangeComplete += OnDh1080KeyExchangeComplete;
                _dh1080Manager.KeyExchangeFailed += OnDh1080KeyExchangeFailed;
            }
        }
    }

    /// <summary>
    /// Handles successful DH1080 key exchange.
    /// </summary>
    private void OnDh1080KeyExchangeComplete(object? sender, Dh1080CompleteEventArgs e)
    {
        _logger.Information("FiSH key exchange complete with {Nick}", e.Nick);
        
        // Raise the public event for UI to handle
        Dh1080KeyExchangeComplete?.Invoke(this, e);
    }

    /// <summary>
    /// Handles failed DH1080 key exchange.
    /// </summary>
    private void OnDh1080KeyExchangeFailed(object? sender, Dh1080FailEventArgs e)
    {
        _logger.Warning("FiSH key exchange failed with {Nick}: {Reason}", e.Nick, e.Reason);
        
        var serverConsole = Server.Channels.FirstOrDefault();
        serverConsole?.Messages.Add(new IrcMessage
        {
            Timestamp = DateTime.Now,
            Type = MessageType.Error,
            Content = $"🔓 DH1080 key exchange failed with {e.Nick}: {e.Reason}"
        });
    }
    
    /// <summary>
    /// Gets the DH1080 key exchange manager.
    /// </summary>
    public Dh1080Manager? Dh1080Manager => _dh1080Manager;

    /// <summary>
    /// Gets or sets the flood protector for rate limiting.
    /// </summary>
    public FloodProtector? FloodProtector
    {
        get => _floodProtector;
        set
        {
            _floodProtector = value;
            if (_floodProtector != null)
            {
                _floodProtector.SendCommandCallback = async cmd => await SendRawDirectAsync(cmd);
            }
        }
    }
    
    // Ping tracking for latency measurement
    private DateTime _lastPingSent;
    private string? _lastPingToken;
    private int _currentLatencyMs;
    
    /// <summary>
    /// Current latency in milliseconds (round-trip time to server).
    /// </summary>
    public int LatencyMs => _currentLatencyMs;

    public IrcConnection(IrcServer server)
    {
        Server = server;
        _logger = SerilogConfig.ForContext<IrcConnection>()
            .ForContext("Server", server.Hostname);
        CurrentNickname = server.Nickname;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Server.State == ConnectionState.Connected || Server.State == ConnectionState.Connecting)
        {
            return;
        }

        try
        {
            Server.State = ConnectionState.Connecting;
            Server.Capabilities.Reset();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Check if we should connect through MuninRelay
            if (Server.Relay?.Enabled == true)
            {
                await ConnectViaRelayAsync(_cts.Token);
            }
            else
            {
                await ConnectDirectAsync(_cts.Token);
            }

            _reader = new StreamReader(_stream!, Encoding.UTF8);
            _writer = new StreamWriter(_stream!, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };

            // Start reading messages
            _readTask = ReadMessagesAsync(_cts.Token);

            // Start CAP negotiation (IRCv3)
            Server.Capabilities.IsNegotiating = true;
            await SendRawAsync("CAP LS 302");

            // Send registration
            if (!string.IsNullOrEmpty(Server.Password))
            {
                await SendRawAsync($"PASS {Server.Password}");
            }

            await SendRawAsync($"NICK {Server.Nickname}");
            await SendRawAsync($"USER {Server.Username} 0 * :{Server.RealName}");

            var connectionType = Server.Relay?.Enabled == true ? "via MuninRelay" : $"IPv6: {Server.IsIPv6Connected}";
            _logger.Information("Connecting to {Server}:{Port} ({ConnectionType})", Server.Hostname, Server.Port, connectionType);
        }
        catch (Exception ex)
        {
            Server.State = ConnectionState.Disconnected;
            Server.IsIPv6Connected = false;
            _logger.Error(ex, "Failed to connect to {Server}", Server.Hostname);
            Error?.Invoke(this, new IrcErrorEventArgs(Server, $"Connection failed: {ex.Message}", ex));
            throw;
        }
    }

    /// <summary>
    /// Connects to a host with IPv6/IPv4 dual-stack support.
    /// If PreferIPv6 is enabled, tries IPv6 first with fallback to IPv4.
    /// </summary>
    /// <summary>
    /// Connects directly to the IRC server (standard connection).
    /// </summary>
    private async Task ConnectDirectAsync(CancellationToken ct)
    {
        _tcpClient = await ConnectWithIPv6FallbackAsync(Server.Hostname, Server.Port, ct);
        _stream = _tcpClient.GetStream();

        if (Server.UseSsl)
        {
            var sslStream = new SslStream(
                _stream,
                false,
                Server.AcceptInvalidCertificates ? AcceptAllCertificates : null);

            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = Server.Hostname,
                EnabledSslProtocols = GetMinimumSslProtocols(),
                CertificateRevocationCheckMode = EnableCertificateRevocationCheck
                    ? X509RevocationMode.Online
                    : X509RevocationMode.NoCheck
            };

            await sslStream.AuthenticateAsClientAsync(sslOptions, ct);
            _stream = sslStream;
        }
    }

    /// <summary>
    /// Connects to the IRC server through MuninRelay.
    /// The relay handles the actual connection to the IRC server.
    /// </summary>
    private async Task ConnectViaRelayAsync(CancellationToken ct)
    {
        var relay = Server.Relay!;
        _logger.Information("Connecting via MuninRelay at {RelayHost}:{RelayPort}", relay.Host, relay.Port);

        var relayConnector = new RelayConnector();

        // Connect through relay - this returns a stream that's already connected to the target
        // The relay handles SSL to the IRC server if needed
        _stream = await relayConnector.ConnectAsync(
            relay,
            Server.Hostname,
            Server.Port,
            Server.UseSsl,
            ct);

        // Note: We don't set up SSL here because the relay handles the SSL to the IRC server
        // The stream we get back is the raw data stream after relay has connected to target
        _logger.Debug("Relay connection established");
    }

    private async Task<TcpClient> ConnectWithIPv6FallbackAsync(string hostname, int port, CancellationToken cancellationToken)
    {
        var addresses = await System.Net.Dns.GetHostAddressesAsync(hostname, cancellationToken);
        
        // Separate IPv4 and IPv6 addresses
        var ipv6Addresses = addresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6).ToList();
        var ipv4Addresses = addresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
        
        _logger.Debug("DNS resolved {Host}: IPv6={IPv6Count}, IPv4={IPv4Count}", hostname, ipv6Addresses.Count, ipv4Addresses.Count);
        
        // Order addresses based on preference
        var orderedAddresses = Server.PreferIPv6 
            ? ipv6Addresses.Concat(ipv4Addresses).ToList()
            : ipv4Addresses.Concat(ipv6Addresses).ToList();
        
        if (orderedAddresses.Count == 0)
        {
            throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
        }
        
        Exception? lastException = null;
        
        foreach (var address in orderedAddresses)
        {
            try
            {
                var client = new TcpClient(address.AddressFamily);
                await client.ConnectAsync(address, port, cancellationToken);
                
                Server.IsIPv6Connected = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
                _logger.Information("Connected to {Address}:{Port} ({Family})", address, port, address.AddressFamily);
                
                return client;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.Debug("Failed to connect to {Address}: {Error}", address, ex.Message);
            }
        }
        
        throw lastException ?? new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused);
    }

    public async Task DisconnectAsync(string? quitMessage = null)
    {
        if (Server.State == ConnectionState.Disconnected)
        {
            return;
        }

        _intentionalDisconnect = true;
        
        try
        {
            if (IsConnected)
            {
                await SendRawAsync($"QUIT :{quitMessage ?? "Leaving"}");
            }
        }
        catch
        {
            // Ignore errors during quit
        }
        finally
        {
            Cleanup();
            Server.State = ConnectionState.Disconnected;
            Server.ConnectedAt = null;
            Disconnected?.Invoke(this, new IrcConnectionEventArgs(Server));
        }
    }

    /// <summary>
    /// Sends a raw message with optional flood protection.
    /// </summary>
    public async Task SendRawAsync(string message)
    {
        if (_writer == null) return;

        // Use flood protection if enabled
        if (_floodProtector != null && _floodProtector.IsEnabled)
        {
            await _floodProtector.SendAsync(message);
        }
        else
        {
            await SendRawDirectAsync(message);
        }
    }

    /// <summary>
    /// Sends a raw message directly without flood protection.
    /// </summary>
    private async Task SendRawDirectAsync(string message)
    {
        if (_writer == null) return;

        try
        {
            await _writer.WriteLineAsync(message);
            RawMessageReceived?.Invoke(this, new IrcRawMessageEventArgs(Server, message, true));
            _logger.Debug(">>> {Message}", SensitiveDataFilter.MaskSensitiveData(message));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send message");
            Error?.Invoke(this, new IrcErrorEventArgs(Server, "Failed to send message", ex));
        }
    }

    /// <summary>
    /// Sends a message with optional echo-message tracking and FiSH encryption.
    /// </summary>
    public async Task SendMessageAsync(string target, string message)
    {
        // Encrypt with FiSH if a key is set for this target
        var messageToSend = message;
        if (_fishCrypt != null && _fishCrypt.HasKey(Server.Id, target))
        {
            var encrypted = _fishCrypt.Encrypt(Server.Id, target, message);
            if (encrypted != null)
            {
                messageToSend = encrypted;
            }
        }
        
        // If echo-message is enabled, track sent messages for deduplication
        if (Server.Capabilities.HasCapability("echo-message"))
        {
            // Generate a unique msgid for tracking
            var msgId = Guid.NewGuid().ToString();
            lock (_sentMessageIds)
            {
                _sentMessageIds.Add(msgId);
                _sentMessageIdQueue.Enqueue((msgId, DateTime.UtcNow));
                
                // Clean up old message IDs (older than 5 minutes)
                while (_sentMessageIdQueue.Count > 0 && 
                       DateTime.UtcNow - _sentMessageIdQueue.Peek().Timestamp > TimeSpan.FromMinutes(5))
                {
                    var old = _sentMessageIdQueue.Dequeue();
                    _sentMessageIds.Remove(old.MessageId);
                }
            }
            
            // Use labeled-response if available
            if (Server.Capabilities.HasCapability("labeled-response"))
            {
                await SendRawAsync($"@label={msgId} PRIVMSG {target} :{messageToSend}");
            }
            else
            {
                await SendRawAsync($"PRIVMSG {target} :{messageToSend}");
            }
        }
        else
        {
            await SendRawAsync($"PRIVMSG {target} :{messageToSend}");
        }
    }

    public Task SendNoticeAsync(string target, string message)
        => SendRawAsync($"NOTICE {target} :{message}");

    public Task JoinChannelAsync(string channel, string? key = null)
        => SendRawAsync(string.IsNullOrEmpty(key) ? $"JOIN {channel}" : $"JOIN {channel} {key}");

    public Task PartChannelAsync(string channel, string? message = null)
        => SendRawAsync(string.IsNullOrEmpty(message) ? $"PART {channel}" : $"PART {channel} :{message}");
    
    /// <summary>
    /// Sends a PING to measure latency.
    /// </summary>
    public async Task MeasureLatencyAsync()
    {
        if (!IsConnected) return;
        
        _lastPingToken = $"LAG{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _lastPingSent = DateTime.UtcNow;
        await SendRawAsync($"PING :{_lastPingToken}");
    }

    public Task SetNicknameAsync(string nickname)
        => SendRawAsync($"NICK {nickname}");

    public Task SendActionAsync(string target, string action)
        => SendRawAsync($"PRIVMSG {target} :\x01ACTION {action}\x01");

    /// <summary>
    /// Sends a WHO query for a channel or mask.
    /// </summary>
    public Task SendWhoAsync(string target)
        => SendRawAsync($"WHO {target}");

    /// <summary>
    /// Sends a WHOWAS query for a nickname.
    /// </summary>
    public Task SendWhowasAsync(string nickname, int count = 5)
        => SendRawAsync($"WHOWAS {nickname} {count}");

    /// <summary>
    /// Requests the ban list for a channel.
    /// </summary>
    public Task RequestBanListAsync(string channel)
        => SendRawAsync($"MODE {channel} +b");

    /// <summary>
    /// Requests the exception list for a channel.
    /// </summary>
    public Task RequestExceptionListAsync(string channel)
        => SendRawAsync($"MODE {channel} +e");

    /// <summary>
    /// Requests the invite list for a channel.
    /// </summary>
    public Task RequestInviteListAsync(string channel)
        => SendRawAsync($"MODE {channel} +I");

    /// <summary>
    /// Kicks a user from a channel.
    /// </summary>
    public Task KickUserAsync(string channel, string nickname, string? reason = null)
        => SendRawAsync(string.IsNullOrEmpty(reason) 
            ? $"KICK {channel} {nickname}" 
            : $"KICK {channel} {nickname} :{reason}");

    /// <summary>
    /// Sets a ban on a channel.
    /// </summary>
    public Task SetBanAsync(string channel, string mask)
        => SendRawAsync($"MODE {channel} +b {mask}");

    /// <summary>
    /// Removes a ban from a channel.
    /// </summary>
    public Task RemoveBanAsync(string channel, string mask)
        => SendRawAsync($"MODE {channel} -b {mask}");

    /// <summary>
    /// Invites a user to a channel.
    /// </summary>
    public Task InviteUserAsync(string channel, string nickname)
        => SendRawAsync($"INVITE {nickname} {channel}");

    /// <summary>
    /// Adds nicknames to the MONITOR list.
    /// </summary>
    public Task MonitorAddAsync(params string[] nicknames)
        => SendRawAsync($"MONITOR + {string.Join(",", nicknames)}");

    /// <summary>
    /// Removes nicknames from the MONITOR list.
    /// </summary>
    public Task MonitorRemoveAsync(params string[] nicknames)
        => SendRawAsync($"MONITOR - {string.Join(",", nicknames)}");

    /// <summary>
    /// Clears the MONITOR list.
    /// </summary>
    public Task MonitorClearAsync()
        => SendRawAsync("MONITOR C");

    /// <summary>
    /// Lists all monitored nicknames.
    /// </summary>
    public Task MonitorListAsync()
        => SendRawAsync("MONITOR L");

    /// <summary>
    /// Gets the status of all monitored nicknames.
    /// </summary>
    public Task MonitorStatusAsync()
        => SendRawAsync("MONITOR S");

    #region IRCv3 Typing, Reactions, Read Markers, ChatHistory

    /// <summary>
    /// Sends a typing indicator (requires +typing capability).
    /// </summary>
    public async Task SendTypingAsync(string target, TypingState state = TypingState.Active)
    {
        if (!Server.Capabilities.HasCapability("message-tags") || 
            !Server.Capabilities.HasCapability("draft/typing") && 
            !Server.Capabilities.HasCapability("typing"))
        {
            return;
        }

        var stateStr = state switch
        {
            TypingState.Active => "active",
            TypingState.Paused => "paused",
            TypingState.Done => "done",
            _ => "active"
        };

        await SendRawAsync($"@+typing={stateStr} TAGMSG {target}");
    }

    /// <summary>
    /// Sends a reaction to a message (requires +draft/react capability).
    /// </summary>
    public async Task SendReactionAsync(string target, string messageId, string emoji)
    {
        if (!Server.Capabilities.HasCapability("message-tags") ||
            !Server.Capabilities.HasCapability("draft/react") &&
            !Server.Capabilities.HasCapability("react"))
        {
            return;
        }

        await SendRawAsync($"@+draft/react={emoji};+draft/reply={messageId} TAGMSG {target}");
    }

    /// <summary>
    /// Updates read marker for a target (requires read-marker capability).
    /// </summary>
    public async Task MarkAsReadAsync(string target, string? messageId = null, DateTime? timestamp = null)
    {
        if (!Server.Capabilities.HasCapability("draft/read-marker") &&
            !Server.Capabilities.HasCapability("read-marker"))
        {
            return;
        }

        if (!string.IsNullOrEmpty(messageId))
        {
            await SendRawAsync($"MARKREAD {target} timestamp={messageId}");
        }
        else if (timestamp.HasValue)
        {
            var ts = $"timestamp={timestamp.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}";
            await SendRawAsync($"MARKREAD {target} {ts}");
        }
        else
        {
            await SendRawAsync($"MARKREAD {target}");
        }
    }

    /// <summary>
    /// Requests chat history from the server (requires chathistory capability).
    /// </summary>
    public async Task RequestChatHistoryAsync(string target, int limit = 50, string? beforeMsgId = null, DateTime? before = null)
    {
        if (!Server.Capabilities.HasCapability("draft/chathistory") &&
            !Server.Capabilities.HasCapability("chathistory"))
        {
            return;
        }

        string command;
        if (!string.IsNullOrEmpty(beforeMsgId))
        {
            command = $"CHATHISTORY BEFORE {target} msgid={beforeMsgId} {limit}";
        }
        else if (before.HasValue)
        {
            var ts = $"timestamp={before.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}";
            command = $"CHATHISTORY BEFORE {target} {ts} {limit}";
        }
        else
        {
            command = $"CHATHISTORY LATEST {target} * {limit}";
        }

        await SendRawAsync(command);
    }

    /// <summary>
    /// Requests chat history around a specific message.
    /// </summary>
    public async Task RequestChatHistoryAroundAsync(string target, string messageId, int limit = 50)
    {
        if (!Server.Capabilities.HasCapability("draft/chathistory") &&
            !Server.Capabilities.HasCapability("chathistory"))
        {
            return;
        }

        await SendRawAsync($"CHATHISTORY AROUND {target} msgid={messageId} {limit}");
    }

    #endregion

    #region Channel Mode State

    /// <summary>
    /// Gets the cached mode state for a channel.
    /// </summary>
    public ChannelModeState? GetChannelModes(string channel)
    {
        return _channelModes.TryGetValue(channel, out var state) ? state : null;
    }

    /// <summary>
    /// Requests the current modes for a channel.
    /// </summary>
    public Task RequestChannelModesAsync(string channel)
        => SendRawAsync($"MODE {channel}");

    #endregion

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    break;
                }

                // Reset reconnect attempts on successful message
                _reconnectAttempts = 0;
                
                RawMessageReceived?.Invoke(this, new IrcRawMessageEventArgs(Server, line, false));
                _logger.Debug("<<< {Message}", SensitiveDataFilter.MaskSensitiveData(line));

                var parsed = _parser.Parse(line);
                await HandleMessageAsync(parsed);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error reading messages");
            Error?.Invoke(this, new IrcErrorEventArgs(Server, "Connection lost", ex));
        }
        finally
        {
            var wasConnected = Server.State == ConnectionState.Connected;
            if (wasConnected)
            {
                Server.State = ConnectionState.Disconnected;
                Disconnected?.Invoke(this, new IrcConnectionEventArgs(Server));
            }
            
            // Attempt automatic reconnection if not intentional disconnect
            if (wasConnected && AutoReconnect && !_intentionalDisconnect && !_disposed)
            {
                _ = Task.Run(() => AttemptReconnectAsync());
            }
        }
    }
    
    private async Task AttemptReconnectAsync()
    {
        while (_reconnectAttempts < MaxReconnectAttempts && !_disposed && !_intentionalDisconnect)
        {
            _reconnectAttempts++;
            
            _logger.Information("Reconnect attempt {Attempt}/{Max} in {Delay} seconds",
                _reconnectAttempts, MaxReconnectAttempts, ReconnectDelaySeconds);
            
            Reconnecting?.Invoke(this, new IrcReconnectEventArgs(
                Server, _reconnectAttempts, MaxReconnectAttempts, ReconnectDelaySeconds));
            
            // Wait before reconnecting with exponential backoff
            var delay = ReconnectDelaySeconds * _reconnectAttempts;
            await Task.Delay(TimeSpan.FromSeconds(delay));
            
            if (_disposed || _intentionalDisconnect)
            {
                break;
            }
            
            try
            {
                Cleanup();
                await ConnectAsync();
                _logger.Information("Reconnected successfully on attempt {Attempt}", _reconnectAttempts);
                _reconnectAttempts = 0;
                return;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Reconnect attempt {Attempt} failed", _reconnectAttempts);
            }
        }
        
        if (_reconnectAttempts >= MaxReconnectAttempts)
        {
            _logger.Error("Max reconnect attempts ({Max}) reached. Giving up.", MaxReconnectAttempts);
            Error?.Invoke(this, new IrcErrorEventArgs(Server, 
                $"Failed to reconnect after {MaxReconnectAttempts} attempts", null));
        }
    }

    private async Task HandleMessageAsync(ParsedIrcMessage message)
    {
        MessageReceived?.Invoke(this, new IrcMessageReceivedEventArgs(Server, message));

        switch (message.Command)
        {
            case "PING":
                await SendRawAsync($"PONG :{message.Trailing ?? message.GetParameter(0)}");
                break;
                
            case "PONG":
                // Check if this is a response to our latency ping
                var pongToken = message.Trailing ?? message.GetParameter(1);
                if (_lastPingToken != null && pongToken == _lastPingToken)
                {
                    _currentLatencyMs = (int)(DateTime.UtcNow - _lastPingSent).TotalMilliseconds;
                    _lastPingToken = null;
                    LatencyUpdated?.Invoke(this, _currentLatencyMs);
                }
                break;

            case "001": // RPL_WELCOME
                Server.State = ConnectionState.Connected;
                Server.ConnectedAt = DateTime.Now;
                CurrentNickname = message.GetParameter(0) ?? Server.Nickname;
                Connected?.Invoke(this, new IrcConnectionEventArgs(Server));

                // Auto-join channels
                foreach (var channel in Server.AutoJoinChannels)
                {
                    await JoinChannelAsync(channel);
                }

                // NickServ identify
                if (!string.IsNullOrEmpty(Server.NickServPassword))
                {
                    await SendMessageAsync("NickServ", $"IDENTIFY {Server.NickServPassword}");
                }
                break;

            case "JOIN":
                HandleJoin(message);
                break;

            case "PART":
                HandlePart(message);
                break;

            case "QUIT":
                HandleQuit(message);
                break;

            case "NICK":
                HandleNickChange(message);
                break;

            case "PRIVMSG":
                HandlePrivmsg(message);
                break;

            case "NOTICE":
                HandleNotice(message);
                break;

            case "TOPIC":
                HandleTopic(message);
                break;

            case "332": // RPL_TOPIC
                HandleTopicReply(message);
                break;

            case "353": // RPL_NAMREPLY
                HandleNamesReply(message);
                break;

            case "366": // RPL_ENDOFNAMES
                HandleEndOfNames(message);
                break;

            case "433": // ERR_NICKNAMEINUSE
                await HandleNicknameInUse(message);
                break;

            case "KICK":
                HandleKick(message);
                break;

            case "MODE":
                HandleMode(message);
                break;

            case "AWAY":
                HandleAway(message);
                break;

            case "ACCOUNT":
                HandleAccount(message);
                break;

            case "CHGHOST":
                HandleChgHost(message);
                break;

            case "SETNAME":
                HandleSetName(message);
                break;

            case "INVITE":
                HandleInvite(message);
                break;

            case "BATCH":
                HandleBatch(message);
                break;

            case "TAGMSG":
                HandleTagMsg(message);
                break;

            case "MARKREAD":
                HandleMarkRead(message);
                break;

            case "CAP":
                await HandleCapAsync(message);
                break;

            case "AUTHENTICATE":
                await HandleAuthenticateAsync(message);
                break;

            case "ERROR":
                Error?.Invoke(this, new IrcErrorEventArgs(Server, message.Trailing ?? "Server error"));
                break;

            default:
                // Handle numeric replies (server messages, MOTD, etc.)
                if (int.TryParse(message.Command, out var numeric))
                {
                    await HandleNumericReplyAsync(message, numeric);
                }
                break;
        }
    }

    private async Task HandleNumericReplyAsync(ParsedIrcMessage message, int numeric)
    {
        // Get the message content (usually the trailing part or last parameter)
        var content = message.Trailing ?? message.Parameters.LastOrDefault() ?? "";
        
        // Skip if it's just our nickname repeated
        if (content == CurrentNickname) return;

        // Handle ISUPPORT (005) - Parse server capabilities
        if (numeric == 5) // RPL_ISUPPORT
        {
            HandleISupport(message);
            // Continue to display the message
        }

        // Handle LIST replies specially
        if (numeric == 322) // RPL_LIST - channel list entry
        {
            // Format: <client> <channel> <# visible> :<topic>
            if (message.Parameters.Count >= 3)
            {
                var channelName = message.GetParameter(1);
                if (int.TryParse(message.GetParameter(2), out var userCount))
                {
                    var topic = message.Trailing ?? "";
                    
                    // FiSH decryption for encrypted topics in channel list
                    if (_fishCrypt != null && channelName != null && FishCryptService.IsEncrypted(topic))
                    {
                        var decrypted = _fishCrypt.Decrypt(Server.Id, channelName, topic);
                        if (decrypted != null)
                        {
                            topic = decrypted;
                        }
                    }
                    
                    var entry = new ChannelListEntry
                    {
                        Name = channelName ?? "",
                        UserCount = userCount,
                        Topic = topic
                    };
                    ChannelListReceived?.Invoke(this, new IrcChannelListEventArgs(Server, entry));
                }
            }
            return;
        }
        
        if (numeric == 323) // RPL_LISTEND - end of list
        {
            ChannelListComplete?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Handle WHO replies
        if (HandleWhoNumeric(message, numeric))
        {
            return;
        }

        // Handle WHOWAS replies
        if (HandleWhowasNumeric(message, numeric))
        {
            return;
        }

        // Handle ban/exception/invite list replies
        if (HandleChannelModeListNumeric(message, numeric))
        {
            return;
        }

        // Handle MONITOR replies
        if (HandleMonitorNumeric(message, numeric))
        {
            return;
        }

        // Handle specific error numerics
        HandleErrorNumeric(message, numeric);

        // Handle SASL authentication numerics
        if (await HandleSaslNumericAsync(message, numeric))
        {
            return;
        }
        
        // Handle WHOIS replies
        HandleWhoisNumeric(message, numeric);

        // Define which numerics to show
        var showMessage = numeric switch
        {
            // Welcome messages (001-005)
            >= 1 and <= 5 => true,
            // Your unique ID
            42 => true,
            // User/channel modes info
            221 => true,
            // LUSERS - user statistics (250-255)
            >= 250 and <= 255 => true,
            // Local/global users
            265 or 266 => true,
            // Away messages
            301 or 305 or 306 => true,
            // WHOIS replies
            >= 311 and <= 319 => true,
            // MOTD
            372 or 375 or 376 => true,
            // Displayed host
            396 => true,
            // Error messages
            >= 400 and <= 599 => true,
            // SASL messages (900-908) - already handled but show for debug
            >= 900 and <= 908 => true,
            _ => false
        };

        if (showMessage && !string.IsNullOrWhiteSpace(content))
        {
            ServerMessage?.Invoke(this, new IrcServerMessageEventArgs(Server, content, message.Command));
        }
    }

    /// <summary>
    /// Handles RPL_ISUPPORT (005) - Server capability tokens.
    /// </summary>
    private void HandleISupport(ParsedIrcMessage message)
    {
        // ISUPPORT format: :server 005 nickname TOKEN1 TOKEN2=value TOKEN3 :are supported by this server
        // Parameters: [0] = nickname, [1..n-1] = tokens, trailing = description
        
        if (message.Parameters.Count < 2) return;

        // Skip the first parameter (nickname) and collect tokens
        // The last item before trailing is usually "are supported" text, but sometimes
        // it's included in trailing. We skip trailing and parse all other params.
        var tokens = message.Parameters
            .Skip(1) // Skip nickname
            .Where(t => !string.IsNullOrEmpty(t) && !t.Contains(' '))
            .ToList();

        Server.ISupport.ParseTokens(tokens);
        
        // Log network name if detected
        if (!string.IsNullOrEmpty(Server.ISupport.Network) && string.IsNullOrEmpty(Server.Name))
        {
            // Update server display name to network name
            Server.Name = Server.ISupport.Network;
        }
    }

    /// <summary>
    /// Handles CAP command responses (IRCv3 capability negotiation).
    /// </summary>
    private async Task HandleCapAsync(ParsedIrcMessage message)
    {
        // CAP format: CAP <target> <subcommand> [*] :<caps>
        // Example: CAP * LS :multi-prefix sasl
        // Example: CAP * LS * :multi-prefix (multiline, more coming)
        
        if (message.Parameters.Count < 2) return;

        var subcommand = message.GetParameter(1)?.ToUpperInvariant();
        var isMultiline = message.Parameters.Count > 2 && message.GetParameter(2) == "*";
        var capsLine = message.Trailing ?? (isMultiline ? "" : message.GetParameter(2)) ?? "";

        switch (subcommand)
        {
            case "LS":
                Server.Capabilities.ParseAvailableCapabilities(capsLine, isMultiline);
                
                if (!isMultiline)
                {
                    // All caps received, request the ones we want
                    var toRequest = Server.Capabilities.GetCapabilitiesToRequest().ToList();
                    if (toRequest.Count > 0)
                    {
                        await SendRawAsync($"CAP REQ :{string.Join(" ", toRequest)}");
                    }
                    else
                    {
                        // No caps to request, end negotiation
                        await EndCapNegotiationAsync();
                    }
                }
                break;

            case "ACK":
                Server.Capabilities.ProcessAck(capsLine);
                
                // Check if SASL was enabled and we have credentials
                if (Server.Capabilities.HasCapability("sasl") && 
                    !string.IsNullOrEmpty(Server.SaslUsername) && 
                    !string.IsNullOrEmpty(Server.SaslPassword))
                {
                    await StartSaslAuthenticationAsync();
                }
                else
                {
                    await EndCapNegotiationAsync();
                }
                break;

            case "NAK":
                Server.Capabilities.ProcessNak(capsLine);
                // Continue despite rejected caps
                await EndCapNegotiationAsync();
                break;

            case "NEW":
                // cap-notify: server added new capabilities
                Server.Capabilities.ProcessNew(capsLine);
                // Optionally request new caps we want
                var newCaps = capsLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(c => CapabilityManager.WantedCapabilities.Contains(c.Split('=')[0]))
                    .ToList();
                if (newCaps.Count > 0 && Server.Capabilities.IsComplete)
                {
                    await SendRawAsync($"CAP REQ :{string.Join(" ", newCaps.Select(c => c.Split('=')[0]))}");
                }
                break;

            case "DEL":
                // cap-notify: server removed capabilities
                Server.Capabilities.ProcessDel(capsLine);
                break;
        }
    }

    /// <summary>
    /// Starts SASL authentication (supports SCRAM-SHA-256, PLAIN, EXTERNAL).
    /// </summary>
    private async Task StartSaslAuthenticationAsync()
    {
        var methods = Server.Capabilities.SaslMethods;
        
        // Priority: SCRAM-SHA-256 > PLAIN > EXTERNAL
        if (methods.Count == 0 || methods.Contains("SCRAM-SHA-256", StringComparer.OrdinalIgnoreCase))
        {
            _logger.Information("Starting SASL SCRAM-SHA-256 authentication");
            _scramAuth = new ScramAuthenticator(Server.SaslUsername!, Server.SaslPassword!);
            await SendRawAsync("AUTHENTICATE SCRAM-SHA-256");
            return;
        }
        
        if (methods.Contains("PLAIN", StringComparer.OrdinalIgnoreCase))
        {
            _logger.Information("Starting SASL PLAIN authentication");
            _scramAuth = null;
            await SendRawAsync("AUTHENTICATE PLAIN");
            return;
        }
        
        if (methods.Contains("EXTERNAL", StringComparer.OrdinalIgnoreCase) && Server.UseClientCertificate)
        {
            _logger.Information("Starting SASL EXTERNAL authentication");
            _scramAuth = null;
            await SendRawAsync("AUTHENTICATE EXTERNAL");
            return;
        }

        _logger.Warning("No supported SASL method available. Server supports: {Methods}", string.Join(",", methods));
        await EndCapNegotiationAsync();
    }

    /// <summary>
    /// Handles AUTHENTICATE command during SASL.
    /// </summary>
    private async Task HandleAuthenticateAsync(ParsedIrcMessage message)
    {
        var param = message.GetParameter(0);
        
        if (param == "+")
        {
            // SCRAM authentication
            if (_scramAuth != null)
            {
                if (_scramAuth.State == ScramAuthenticatorState.Initial)
                {
                    // Send client-first message
                    var clientFirst = _scramAuth.GetClientFirstMessage();
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFirst));
                    await SendRawAsync($"AUTHENTICATE {base64}");
                }
                else if (_scramAuth.State == ScramAuthenticatorState.WaitingForServerFinal)
                {
                    // Final step - send empty response
                    await SendRawAsync("AUTHENTICATE +");
                }
            }
            else
            {
                // PLAIN authentication
                var authString = $"{Server.SaslUsername}\0{Server.SaslUsername}\0{Server.SaslPassword}";
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
                await SendRawAsync($"AUTHENTICATE {base64}");
            }
        }
        else if (_scramAuth != null && _scramAuth.State == ScramAuthenticatorState.WaitingForServerFirst)
        {
            // Server-first message received
            try
            {
                var serverFirst = Encoding.UTF8.GetString(Convert.FromBase64String(param!));
                var clientFinal = _scramAuth.ProcessServerFirstMessage(serverFirst);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFinal));
                await SendRawAsync($"AUTHENTICATE {base64}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "SCRAM authentication failed");
                Error?.Invoke(this, new IrcErrorEventArgs(Server, $"SCRAM authentication failed: {ex.Message}"));
                await SendRawAsync("AUTHENTICATE *"); // Abort
                await EndCapNegotiationAsync();
            }
        }
        else if (_scramAuth != null && _scramAuth.State == ScramAuthenticatorState.WaitingForServerFinal)
        {
            // Server-final message received
            try
            {
                var serverFinal = Encoding.UTF8.GetString(Convert.FromBase64String(param!));
                if (!_scramAuth.VerifyServerFinalMessage(serverFinal))
                {
                    _logger.Warning("SCRAM server verification failed");
                    Error?.Invoke(this, new IrcErrorEventArgs(Server, "SCRAM server verification failed"));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "SCRAM verification failed");
            }
        }
    }

    /// <summary>
    /// Ends CAP negotiation and allows registration to proceed.
    /// </summary>
    private async Task EndCapNegotiationAsync()
    {
        Server.Capabilities.IsNegotiating = false;
        Server.Capabilities.IsComplete = true;
        await SendRawAsync("CAP END");
        
        var enabledCaps = Server.Capabilities.EnabledCapabilities;
        if (enabledCaps.Count > 0)
        {
            _logger.Information("Enabled capabilities: {Caps}", string.Join(", ", enabledCaps));
        }
    }

    /// <summary>
    /// Handles SASL-related numeric replies.
    /// Returns true if the numeric was handled.
    /// </summary>
    private async Task<bool> HandleSaslNumericAsync(ParsedIrcMessage message, int numeric)
    {
        switch (numeric)
        {
            case 900: // RPL_LOGGEDIN
                // :server 900 nick nick!user@host account :You are now logged in as account
                var loggedInContent = message.Trailing ?? "";
                _logger.Information("SASL: {Message}", loggedInContent);
                return true;

            case 901: // RPL_LOGGEDOUT
                _logger.Information("SASL: Logged out");
                return true;

            case 902: // ERR_NICKLOCKED
                _logger.Warning("SASL: Nick is locked, cannot authenticate");
                await EndCapNegotiationAsync();
                return true;

            case 903: // RPL_SASLSUCCESS
                _logger.Information("SASL authentication successful");
                await EndCapNegotiationAsync();
                return true;

            case 904: // ERR_SASLFAIL
                _logger.Warning("SASL authentication failed");
                Error?.Invoke(this, new IrcErrorEventArgs(Server, "SASL authentication failed"));
                await EndCapNegotiationAsync();
                return true;

            case 905: // ERR_SASLTOOLONG
                _logger.Warning("SASL message too long");
                await EndCapNegotiationAsync();
                return true;

            case 906: // ERR_SASLABORTED
                _logger.Warning("SASL authentication aborted");
                await EndCapNegotiationAsync();
                return true;

            case 907: // ERR_SASLALREADY
                _logger.Information("SASL: Already authenticated");
                await EndCapNegotiationAsync();
                return true;

            case 908: // RPL_SASLMECHS
                // Server lists available SASL mechanisms
                var mechs = message.Trailing?.Split(',') ?? Array.Empty<string>();
                _logger.Information("SASL mechanisms available: {Mechs}", string.Join(", ", mechs));
                return true;

            default:
                return false;
        }
    }

    private void HandleJoin(ParsedIrcMessage message)
    {
        var channelName = message.GetParameter(0);
        if (string.IsNullOrEmpty(channelName) || message.Nick == null) return;

        var isMe = message.Nick.Equals(CurrentNickname, StringComparison.OrdinalIgnoreCase);

        if (isMe)
        {
            var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
            if (channel == null)
            {
                channel = new IrcChannel { Name = channelName };
                Server.Channels.Add(channel);
            }
            channel.IsJoined = true;
            ChannelJoined?.Invoke(this, new IrcChannelEventArgs(Server, channel));
        }
        else
        {
            var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
            if (channel != null)
            {
                // extended-join: :nick!user@host JOIN #channel accountname :realname
                // Standard JOIN: :nick!user@host JOIN #channel
                string? accountName = null;
                string? realName = null;

                if (Server.Capabilities.HasCapability("extended-join"))
                {
                    accountName = message.GetParameter(1);
                    realName = message.Trailing;
                    
                    // "*" means not logged in
                    if (accountName == "*") accountName = null;
                }
                
                // Also check account-tag if present
                if (string.IsNullOrEmpty(accountName))
                {
                    accountName = message.GetAccountName();
                }

                var user = new IrcUser
                {
                    Nickname = message.Nick,
                    Username = message.User,
                    Hostname = message.Host,
                    Account = accountName,
                    RealName = realName
                };
                if (!channel.Users.Any(u => u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase)))
                {
                    channel.Users.Add(user);
                }
                else
                {
                    // Update existing user's info
                    var existingUser = channel.Users.First(u => u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase));
                    existingUser.Account = accountName;
                    if (!string.IsNullOrEmpty(realName))
                        existingUser.RealName = realName;
                }
                
                // Build message content
                var joinContent = !string.IsNullOrEmpty(accountName)
                    ? $"{message.Nick} ({message.User}@{message.Host}) [{accountName}] has joined {channelName}"
                    : $"{message.Nick} ({message.User}@{message.Host}) has joined {channelName}";
                
                channel.Messages.Add(new IrcMessage
                {
                    Timestamp = message.GetTimestamp(),
                    Type = MessageType.Join,
                    Source = message.Nick,
                    Content = joinContent
                });
                UserJoined?.Invoke(this, new IrcUserEventArgs(Server, channel, user));
            }
        }
    }

    private void HandlePart(ParsedIrcMessage message)
    {
        var channelName = message.GetParameter(0);
        if (string.IsNullOrEmpty(channelName) || message.Nick == null) return;

        var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (channel == null) return;

        var isMe = message.Nick.Equals(CurrentNickname, StringComparison.OrdinalIgnoreCase);
        var reason = message.Trailing ?? "";

        if (isMe)
        {
            channel.IsJoined = false;
            channel.Users.Clear();
            ChannelParted?.Invoke(this, new IrcChannelEventArgs(Server, channel));
        }
        else
        {
            var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                channel.Users.Remove(user);
                channel.Messages.Add(new IrcMessage
                {
                    Timestamp = message.GetTimestamp(),
                    Type = MessageType.Part,
                    Source = message.Nick,
                    Content = string.IsNullOrEmpty(reason)
                        ? $"{message.Nick} has left {channelName}"
                        : $"{message.Nick} has left {channelName} ({reason})"
                });
                UserParted?.Invoke(this, new IrcUserEventArgs(Server, channel, user));
            }
        }
    }

    private void HandleQuit(ParsedIrcMessage message)
    {
        if (message.Nick == null) return;

        var reason = message.Trailing ?? "";

        foreach (var channel in Server.Channels.Where(c => c.IsJoined))
        {
            var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                channel.Users.Remove(user);
                channel.Messages.Add(new IrcMessage
                {
                    Timestamp = message.GetTimestamp(),
                    Type = MessageType.Quit,
                    Source = message.Nick,
                    Content = string.IsNullOrEmpty(reason)
                        ? $"{message.Nick} has quit"
                        : $"{message.Nick} has quit ({reason})"
                });
                UserQuit?.Invoke(this, new IrcUserEventArgs(Server, channel, user));
            }
        }
    }

    private void HandleNickChange(ParsedIrcMessage message)
    {
        var oldNick = message.Nick;
        var newNick = message.GetParameter(0) ?? message.Trailing;
        if (oldNick == null || newNick == null) return;

        var isMe = oldNick.Equals(CurrentNickname, StringComparison.OrdinalIgnoreCase);
        if (isMe)
        {
            CurrentNickname = newNick;
        }

        foreach (var channel in Server.Channels.Where(c => c.IsJoined))
        {
            var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(oldNick, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                user.Nickname = newNick;
                channel.Messages.Add(new IrcMessage
                {
                    Timestamp = message.GetTimestamp(),
                    Type = MessageType.Nick,
                    Source = oldNick,
                    Content = $"{oldNick} is now known as {newNick}"
                });
            }
        }

        NickChanged?.Invoke(this, new IrcNickChangedEventArgs(Server, oldNick, newNick));
    }

    private void HandlePrivmsg(ParsedIrcMessage message)
    {
        var target = message.GetParameter(0);
        var content = message.Trailing ?? "";
        if (target == null || message.Nick == null) return;

        // Echo-message deduplication: skip if this is an echo of our own message
        if (Server.Capabilities.HasCapability("echo-message") && 
            message.Nick.Equals(Server.Nickname, StringComparison.OrdinalIgnoreCase))
        {
            if (message.Tags.TryGetValue("msgid", out var msgId) && !string.IsNullOrEmpty(msgId))
            {
                lock (_sentMessageIds)
                {
                    if (_sentMessageIds.Contains(msgId))
                    {
                        // This is an echo of a message we already displayed when sending
                        return;
                    }
                }
            }
            
            // If we have echo-message but no msgid, check by label
            if (message.Tags.TryGetValue("label", out var label) && !string.IsNullOrEmpty(label))
            {
                // Skip labeled messages from ourselves (already tracked)
                return;
            }
        }

        // Check for CTCP
        if (IrcMessageParser.IsCTCP(content))
        {
            HandleCTCP(message);
            return;
        }

        // FiSH decryption - check if message is encrypted
        bool wasEncrypted = false;
        var isChannel = Server.ISupport.IsChannel(target);
        var fishTarget = isChannel ? target : message.Nick;
        
        if (_fishCrypt != null && FishCryptService.IsEncrypted(content))
        {
            var decrypted = _fishCrypt.Decrypt(Server.Id, fishTarget, content);
            if (decrypted != null)
            {
                content = decrypted;
                wasEncrypted = true;
            }
        }

        var isHighlight = CheckHighlight(content);

        // Check if this message is from bouncer playback
        var isFromPlayback = Server.IsReceivingPlayback;
        
        // Also check if message is part of a batch (for chathistory)
        if (!isFromPlayback && message.Tags.TryGetValue("batch", out var batchRef))
        {
            if (_activeBatches.TryGetValue(batchRef, out var batch))
            {
                isFromPlayback = batch.Type.Equals("znc.in/playback", StringComparison.OrdinalIgnoreCase) ||
                                batch.Type.Equals("chathistory", StringComparison.OrdinalIgnoreCase);
            }
        }

        var ircMessage = new IrcMessage
        {
            Timestamp = message.GetTimestamp(),
            Type = MessageType.Normal,
            Source = message.Nick,
            Target = target,
            Content = content,
            IsHighlight = isHighlight,
            IsEncrypted = wasEncrypted,
            IsFromPlayback = isFromPlayback,
            RawMessage = message.RawMessage
        };

        if (isChannel)
        {
            var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (channel != null)
            {
                channel.Messages.Add(ircMessage);
                if (isHighlight) channel.HasMention = true;
                ChannelMessage?.Invoke(this, new IrcChannelMessageEventArgs(Server, channel, ircMessage));
            }
        }
        else
        {
            PrivateMessage?.Invoke(this, new IrcPrivateMessageEventArgs(Server, message.Nick, ircMessage));
        }
    }

    private void HandleNotice(ParsedIrcMessage message)
    {
        var target = message.GetParameter(0);
        var content = message.Trailing ?? "";
        if (target == null) return;

        // Check for DH1080 key exchange (FiSH uses plain text, not CTCP)
        if (content.StartsWith("DH1080_", StringComparison.Ordinal))
        {
            HandleDh1080Notice(message, content);
            return;
        }

        // Check for CTCP inside NOTICE (CTCP replies)
        if (content.StartsWith('\x01') && content.EndsWith('\x01'))
        {
            HandleNoticeCTCP(message);
            return;
        }

        var ircMessage = IrcMessage.CreateNotice(message.Nick ?? message.Prefix ?? "Server", content);

        var isChannel = Server.ISupport.IsChannel(target);
        if (isChannel)
        {
            var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            channel?.Messages.Add(ircMessage);
        }
    }

    /// <summary>
    /// Handles DH1080 key exchange messages received via NOTICE.
    /// FiSH sends these as plain text, not CTCP format.
    /// Format: "DH1080_INIT [pubkey]" or "DH1080_INIT [pubkey] CBC"
    /// </summary>
    private void HandleDh1080Notice(ParsedIrcMessage message, string content)
    {
        if (message.Nick == null) return;

        // Parse the message using Dh1080KeyExchange
        var parsed = Dh1080KeyExchange.ParseMessage(content);
        if (parsed == null) return;

        HandleDh1080(message.Nick, content);
    }

    /// <summary>
    /// Handles CTCP messages received via NOTICE (CTCP replies).
    /// </summary>
    private void HandleNoticeCTCP(ParsedIrcMessage message)
    {
        var content = message.Trailing ?? "";
        var (command, param) = IrcMessageParser.ParseCTCP(content);

        // Handle CTCP replies here if needed (e.g., VERSION reply, PING reply)
        _ = command;
        _ = param;
    }

    private void HandleCTCP(ParsedIrcMessage message)
    {
        var content = message.Trailing ?? "";
        var (command, param) = IrcMessageParser.ParseCTCP(content);

        switch (command)
        {
            case "ACTION":
                var target = message.GetParameter(0);
                if (target == null || message.Nick == null) return;

                var actionMsg = IrcMessage.CreateAction(message.Nick, param ?? "");
                actionMsg.Timestamp = message.GetTimestamp();
                var isChannel = Server.ISupport.IsChannel(target);

                if (isChannel)
                {
                    var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
                    if (channel != null)
                    {
                        channel.Messages.Add(actionMsg);
                        ChannelMessage?.Invoke(this, new IrcChannelMessageEventArgs(Server, channel, actionMsg));
                    }
                }
                else
                {
                    PrivateMessage?.Invoke(this, new IrcPrivateMessageEventArgs(Server, message.Nick, actionMsg));
                }
                break;

            case "VERSION":
                _ = SendNoticeAsync(message.Nick!, $"\x01VERSION IrcClient 1.0\x01");
                break;

            case "PING":
                _ = SendNoticeAsync(message.Nick!, $"\x01PING {param}\x01");
                break;

            case "TIME":
                _ = SendNoticeAsync(message.Nick!, $"\x01TIME {DateTime.Now:F}\x01");
                break;
                
            case "CLIENTINFO":
                _ = SendNoticeAsync(message.Nick!, $"\x01CLIENTINFO ACTION PING VERSION TIME CLIENTINFO USERINFO SOURCE DH1080_INIT DH1080_FINISH\x01");
                break;
                
            case "USERINFO":
                _ = SendNoticeAsync(message.Nick!, $"\x01USERINFO IrcClient User\x01");
                break;
                
            case "SOURCE":
                _ = SendNoticeAsync(message.Nick!, $"\x01SOURCE https://github.com/IrcClient\x01");
                break;
                
            case "DH1080_INIT":
            case "DH1080_FINISH":
                // Reconstruct the message for the handler
                HandleDh1080(message.Nick!, $"{command} {param ?? ""}");
                break;
        }
    }

    /// <summary>
    /// Handles DH1080 key exchange messages.
    /// </summary>
    /// <param name="nick">The nick who sent the message.</param>
    /// <param name="message">The full DH1080 message (e.g., "DH1080_INIT &lt;key&gt; CBC").</param>
    private void HandleDh1080(string nick, string message)
    {
        if (_dh1080Manager == null || string.IsNullOrEmpty(nick)) return;

        // Only log that DH1080 was received, NOT the message content (contains public keys)
        _logger.Debug("HandleDh1080 called: nick={Nick}", nick);

        var response = _dh1080Manager.HandleMessage(Server.Id, nick, message);

        // Only log whether a response was generated, NOT the response content
        _logger.Debug("HandleDh1080 response generated: {HasResponse}", response != null);

        if (response != null)
        {
            // Send the response as a plain NOTICE
            _ = SendNoticeAsync(nick, response);
        }
    }

    private void HandleTopic(ParsedIrcMessage message)
    {
        var channelName = message.GetParameter(0);
        var topic = message.Trailing;
        if (channelName == null) return;

        // FiSH decryption for encrypted topics
        bool wasEncrypted = false;
        if (_fishCrypt != null && topic != null && FishCryptService.IsEncrypted(topic))
        {
            var decrypted = _fishCrypt.Decrypt(Server.Id, channelName, topic);
            if (decrypted != null)
            {
                topic = decrypted;
                wasEncrypted = true;
            }
        }

        var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (channel != null)
        {
            channel.Topic = topic;
            channel.TopicSetBy = message.Nick;
            channel.TopicSetAt = message.GetTimestamp();
            var topicDisplay = wasEncrypted ? $"{topic} 🔐" : topic;
            channel.Messages.Add(new IrcMessage
            {
                Timestamp = message.GetTimestamp(),
                Type = MessageType.Topic,
                Source = message.Nick,
                Content = $"{message.Nick} changed the topic to: {topicDisplay}",
                IsEncrypted = wasEncrypted
            });
            TopicChanged?.Invoke(this, new IrcChannelEventArgs(Server, channel));
        }
    }

    private void HandleTopicReply(ParsedIrcMessage message)
    {
        var channelName = message.GetParameter(1);
        var topic = message.Trailing;
        if (channelName == null) return;

        // FiSH decryption for encrypted topics
        if (_fishCrypt != null && topic != null && FishCryptService.IsEncrypted(topic))
        {
            var decrypted = _fishCrypt.Decrypt(Server.Id, channelName, topic);
            if (decrypted != null)
            {
                topic = decrypted;
            }
        }

        var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (channel != null)
        {
            channel.Topic = topic;
            TopicChanged?.Invoke(this, new IrcChannelEventArgs(Server, channel));
        }
    }

    private void HandleNamesReply(ParsedIrcMessage message)
    {
        var channelName = message.GetParameter(2);
        var names = message.Trailing?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (channelName == null || names == null) return;

        var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (channel == null) return;

        var hasUserhostInNames = Server.Capabilities.HasCapability("userhost-in-names");

        foreach (var name in names)
        {
            string userPart = name;
            string? username = null;
            string? hostname = null;

            // userhost-in-names format: @+nick!user@host
            if (hasUserhostInNames)
            {
                var bangIndex = name.IndexOf('!');
                var atIndex = name.LastIndexOf('@');
                
                if (bangIndex > 0 && atIndex > bangIndex)
                {
                    userPart = name[..bangIndex];
                    username = name[(bangIndex + 1)..atIndex];
                    hostname = name[(atIndex + 1)..];
                }
            }

            var (mode, nick) = ParseUserPrefix(userPart);
            
            var existingUser = channel.Users.FirstOrDefault(u => u.Nickname.Equals(nick, StringComparison.OrdinalIgnoreCase));
            if (existingUser == null)
            {
                channel.Users.Add(new IrcUser
                {
                    Nickname = nick,
                    Username = username,
                    Hostname = hostname,
                    Mode = mode
                });
            }
            else
            {
                // Update existing user's info
                if (username != null) existingUser.Username = username;
                if (hostname != null) existingUser.Hostname = hostname;
                existingUser.Mode = mode;
            }
        }
    }

    private void HandleEndOfNames(ParsedIrcMessage message)
    {
        var channelName = message.GetParameter(1);
        if (channelName == null) return;

        var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (channel != null)
        {
            UserListUpdated?.Invoke(this, new IrcChannelEventArgs(Server, channel));
        }
    }

    private async Task HandleNicknameInUse(ParsedIrcMessage message)
    {
        if (Server.State != ConnectionState.Connected)
        {
            CurrentNickname = Server.Nickname + "_";
            await SendRawAsync($"NICK {CurrentNickname}");
        }
    }

    private void HandleKick(ParsedIrcMessage message)
    {
        var channelName = message.GetParameter(0);
        var kickedNick = message.GetParameter(1);
        var reason = message.Trailing ?? "";
        if (channelName == null || kickedNick == null) return;

        var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (channel == null) return;

        var isMe = kickedNick.Equals(CurrentNickname, StringComparison.OrdinalIgnoreCase);

        if (isMe)
        {
            channel.IsJoined = false;
            channel.Users.Clear();
        }
        else
        {
            var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(kickedNick, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                channel.Users.Remove(user);
            }
        }

        channel.Messages.Add(new IrcMessage
        {
            Timestamp = message.GetTimestamp(),
            Type = MessageType.Kick,
            Source = message.Nick,
            Content = string.IsNullOrEmpty(reason)
                ? $"{kickedNick} was kicked by {message.Nick}"
                : $"{kickedNick} was kicked by {message.Nick} ({reason})"
        });
    }

    private void HandleMode(ParsedIrcMessage message)
    {
        var target = message.GetParameter(0);
        if (target == null) return;

        var isChannel = Server.ISupport.IsChannel(target);
        if (!isChannel) return;

        var channel = Server.Channels.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (channel == null) return;

        var modeStr = string.Join(" ", message.Parameters.Skip(1));
        channel.Messages.Add(new IrcMessage
        {
            Timestamp = message.GetTimestamp(),
            Type = MessageType.Mode,
            Source = message.Nick ?? message.Prefix,
            Content = $"{message.Nick ?? message.Prefix} sets mode {modeStr}"
        });

        // Parse user modes
        if (message.Parameters.Count >= 3)
        {
            var modes = message.GetParameter(1);
            var targetUser = message.GetParameter(2);
            if (modes != null && targetUser != null)
            {
                var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(targetUser, StringComparison.OrdinalIgnoreCase));
                if (user != null)
                {
                    UpdateUserMode(user, modes);
                    UserListUpdated?.Invoke(this, new IrcChannelEventArgs(Server, channel));
                }
            }
        }
    }

    private static void UpdateUserMode(IrcUser user, string modes)
    {
        var adding = true;
        foreach (var c in modes)
        {
            switch (c)
            {
                case '+':
                    adding = true;
                    break;
                case '-':
                    adding = false;
                    break;
                case 'o':
                    user.Mode = adding ? UserMode.Operator : UserMode.Normal;
                    break;
                case 'v':
                    user.Mode = adding ? UserMode.Voice : UserMode.Normal;
                    break;
                case 'h':
                    user.Mode = adding ? UserMode.HalfOperator : UserMode.Normal;
                    break;
                case 'a':
                    user.Mode = adding ? UserMode.Admin : UserMode.Normal;
                    break;
                case 'q':
                    user.Mode = adding ? UserMode.Owner : UserMode.Normal;
                    break;
            }
        }
    }

    /// <summary>
    /// Handles AWAY command (away-notify capability).
    /// Format: :nick!user@host AWAY :message (going away)
    /// Format: :nick!user@host AWAY (returning)
    /// </summary>
    private void HandleAway(ParsedIrcMessage message)
    {
        if (message.Nick == null) return;

        var awayMessage = message.Trailing;
        var isAway = !string.IsNullOrEmpty(awayMessage);

        // Update all channels this user is in
        foreach (var channel in Server.Channels.Where(c => c.IsJoined))
        {
            var user = channel.Users.FirstOrDefault(u => 
                u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase));
            
            if (user != null)
            {
                user.IsAway = isAway;
                user.AwayMessage = awayMessage;
            }
        }
    }

    /// <summary>
    /// Handles ACCOUNT command (account-notify capability).
    /// Format: :nick!user@host ACCOUNT accountname (logged in)
    /// Format: :nick!user@host ACCOUNT * (logged out)
    /// </summary>
    private void HandleAccount(ParsedIrcMessage message)
    {
        if (message.Nick == null) return;

        var accountName = message.GetParameter(0);
        if (accountName == "*") accountName = null;

        // Update all channels this user is in
        foreach (var channel in Server.Channels.Where(c => c.IsJoined))
        {
            var user = channel.Users.FirstOrDefault(u => 
                u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase));
            
            if (user != null)
            {
                user.Account = accountName;
            }
        }
    }

    /// <summary>
    /// Handles CHGHOST command (chghost capability).
    /// Format: :nick!user@host CHGHOST newuser newhost
    /// </summary>
    private void HandleChgHost(ParsedIrcMessage message)
    {
        if (message.Nick == null) return;

        var newUser = message.GetParameter(0);
        var newHost = message.GetParameter(1);

        // Update all channels this user is in
        foreach (var channel in Server.Channels.Where(c => c.IsJoined))
        {
            var user = channel.Users.FirstOrDefault(u => 
                u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase));
            
            if (user != null)
            {
                if (newUser != null) user.Username = newUser;
                if (newHost != null) user.Hostname = newHost;
            }
        }
    }

    /// <summary>
    /// Handles SETNAME command (setname capability).
    /// Format: :nick!user@host SETNAME :New real name
    /// </summary>
    private void HandleSetName(ParsedIrcMessage message)
    {
        if (message.Nick == null) return;

        var newRealName = message.Trailing;

        // Update all channels this user is in
        foreach (var channel in Server.Channels.Where(c => c.IsJoined))
        {
            var user = channel.Users.FirstOrDefault(u => 
                u.Nickname.Equals(message.Nick, StringComparison.OrdinalIgnoreCase));
            
            if (user != null && newRealName != null)
            {
                user.RealName = newRealName;
            }
        }
    }

    /// <summary>
    /// Handles INVITE command (invite-notify capability).
    /// Format: :nick!user@host INVITE target :#channel
    /// </summary>
    private void HandleInvite(ParsedIrcMessage message)
    {
        var targetNick = message.GetParameter(0);
        var channelName = message.GetParameter(1) ?? message.Trailing;
        
        if (targetNick == null || channelName == null) return;

        var isMe = targetNick.Equals(CurrentNickname, StringComparison.OrdinalIgnoreCase);

        if (isMe)
        {
            // We were invited
            ServerMessage?.Invoke(this, new IrcServerMessageEventArgs(
                Server, 
                $"{message.Nick} has invited you to {channelName}", 
                "INVITE"));
        }
        else if (Server.Capabilities.HasCapability("invite-notify"))
        {
            // Someone else was invited (invite-notify)
            var channel = Server.Channels.FirstOrDefault(c => 
                c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
            
            if (channel != null)
            {
                channel.Messages.Add(new IrcMessage
                {
                    Timestamp = message.GetTimestamp(),
                    Type = MessageType.System,
                    Content = $"{message.Nick} invited {targetNick} to the channel"
                });
            }
        }
    }

    /// <summary>
    /// Handles BATCH command for grouped messages.
    /// </summary>
    private void HandleBatch(ParsedIrcMessage message)
    {
        var batchRef = message.GetParameter(0);
        if (string.IsNullOrEmpty(batchRef)) return;

        if (batchRef.StartsWith('+'))
        {
            // Starting a new batch
            var reference = batchRef[1..];
            var batchType = message.GetParameter(1) ?? "";
            var batch = new IrcBatch
            {
                Reference = reference,
                Type = batchType,
                Parameters = message.Parameters.Skip(2).ToList()
            };

            // Check for parent batch
            if (message.Tags.TryGetValue("batch", out var parentRef))
            {
                batch.ParentReference = parentRef;
            }

            _activeBatches[reference] = batch;

            // Handle ZNC playback start
            if (batchType.Equals("znc.in/playback", StringComparison.OrdinalIgnoreCase) ||
                batchType.Equals("chathistory", StringComparison.OrdinalIgnoreCase))
            {
                Server.IsReceivingPlayback = true;
                Server.IsBouncerDetected = true;
                _logger.Information("ZNC playback batch started: {Reference}", reference);
            }
        }
        else if (batchRef.StartsWith('-'))
        {
            // Ending a batch
            var reference = batchRef[1..];
            if (_activeBatches.TryGetValue(reference, out var batch))
            {
                // Handle ZNC playback end
                if (batch.Type.Equals("znc.in/playback", StringComparison.OrdinalIgnoreCase) ||
                    batch.Type.Equals("chathistory", StringComparison.OrdinalIgnoreCase))
                {
                    Server.IsReceivingPlayback = false;
                    _logger.Information("ZNC playback batch complete: {Reference}, {Count} messages", 
                        reference, batch.Messages.Count);
                }

                batch.IsComplete = true;
                _activeBatches.Remove(reference);
                BatchComplete?.Invoke(this, new IrcBatchEventArgs(Server, batch));
            }
        }
    }

    /// <summary>
    /// Handles TAGMSG for typing indicators and reactions.
    /// </summary>
    private void HandleTagMsg(ParsedIrcMessage message)
    {
        var target = message.GetParameter(0);
        if (string.IsNullOrEmpty(target) || message.Nick == null) return;

        // Check for typing indicator
        if (message.Tags.TryGetValue("+typing", out var typingState) ||
            message.Tags.TryGetValue("+draft/typing", out typingState))
        {
            var state = typingState switch
            {
                "active" => TypingState.Active,
                "paused" => TypingState.Paused,
                "done" => TypingState.Done,
                _ => TypingState.Active
            };
            
            TypingNotification?.Invoke(this, new IrcTypingEventArgs(Server, target, message.Nick, state));
        }

        // Check for reaction
        if (message.Tags.TryGetValue("+draft/react", out var emoji) ||
            message.Tags.TryGetValue("+react", out emoji))
        {
            var msgId = "";
            if (message.Tags.TryGetValue("+draft/reply", out msgId) ||
                message.Tags.TryGetValue("+reply", out msgId))
            {
                // This is a reaction
                ReactionReceived?.Invoke(this, new IrcReactionEventArgs(
                    Server, target, message.Nick, msgId ?? "", emoji ?? "", true));
            }
        }
    }

    /// <summary>
    /// Handles MARKREAD for read markers.
    /// </summary>
    private void HandleMarkRead(ParsedIrcMessage message)
    {
        var target = message.GetParameter(0);
        if (string.IsNullOrEmpty(target)) return;

        string? msgId = null;
        DateTime? timestamp = null;

        // Parse timestamp parameter
        var timestampParam = message.GetParameter(1);
        if (!string.IsNullOrEmpty(timestampParam))
        {
            if (timestampParam.StartsWith("timestamp="))
            {
                var ts = timestampParam[10..];
                if (ts.StartsWith("msgid="))
                {
                    msgId = ts[6..];
                }
                else if (DateTime.TryParse(ts, out var dt))
                {
                    timestamp = dt;
                }
            }
        }

        ReadMarkerReceived?.Invoke(this, new IrcReadMarkerEventArgs(Server, target, msgId, timestamp));
    }

    /// <summary>
    /// Handles WHO numeric replies (352, 315).
    /// </summary>
    private bool HandleWhoNumeric(ParsedIrcMessage message, int numeric)
    {
        switch (numeric)
        {
            case 352: // RPL_WHOREPLY
                // Format: <client> <channel> <user> <host> <server> <nick> <flags> :<hopcount> <realname>
                if (message.Parameters.Count >= 7)
                {
                    var trailing = message.Trailing ?? "";
                    var hopAndReal = trailing.Split(' ', 2);
                    
                    var whoInfo = new WhoInfo
                    {
                        Channel = message.GetParameter(1) ?? "*",
                        Username = message.GetParameter(2) ?? "",
                        Hostname = message.GetParameter(3) ?? "",
                        Server = message.GetParameter(4) ?? "",
                        Nickname = message.GetParameter(5) ?? "",
                        Flags = message.GetParameter(6) ?? "",
                        HopCount = hopAndReal.Length > 0 && int.TryParse(hopAndReal[0], out var hop) ? hop : 0,
                        RealName = hopAndReal.Length > 1 ? hopAndReal[1] : ""
                    };
                    
                    // Update user in channel
                    UpdateUserFromWho(whoInfo);
                    
                    WhoReceived?.Invoke(this, new IrcWhoEventArgs(Server, whoInfo));
                }
                return true;

            case 354: // RPL_WHOSPCRPL (WHOX reply)
                // Custom format based on WHOX query
                // Common format: <client> <channel> <user> <host> <nick> <flags> <account> :<realname>
                if (message.Parameters.Count >= 6)
                {
                    var whoInfo = new WhoInfo
                    {
                        Channel = message.GetParameter(1) ?? "*",
                        Username = message.GetParameter(2) ?? "",
                        Hostname = message.GetParameter(3) ?? "",
                        Nickname = message.GetParameter(4) ?? "",
                        Flags = message.GetParameter(5) ?? "",
                        Account = message.Parameters.Count > 6 ? message.GetParameter(6) : null,
                        RealName = message.Trailing ?? ""
                    };
                    
                    if (whoInfo.Account == "0") whoInfo.Account = null;
                    
                    UpdateUserFromWho(whoInfo);
                    WhoReceived?.Invoke(this, new IrcWhoEventArgs(Server, whoInfo));
                }
                return true;

            case 315: // RPL_ENDOFWHO
                WhoComplete?.Invoke(this, EventArgs.Empty);
                return true;

            default:
                return false;
        }
    }

    private void UpdateUserFromWho(WhoInfo who)
    {
        if (who.Channel == "*") return;

        var channel = Server.Channels.FirstOrDefault(c => 
            c.Name.Equals(who.Channel, StringComparison.OrdinalIgnoreCase));
        
        if (channel == null) return;

        var user = channel.Users.FirstOrDefault(u => 
            u.Nickname.Equals(who.Nickname, StringComparison.OrdinalIgnoreCase));
        
        if (user != null)
        {
            user.Username = who.Username;
            user.Hostname = who.Hostname;
            user.RealName = who.RealName;
            user.IsAway = who.IsAway;
            if (!string.IsNullOrEmpty(who.Account))
                user.Account = who.Account;
        }
    }

    /// <summary>
    /// Handles WHOWAS numeric replies (314, 369).
    /// </summary>
    private bool HandleWhowasNumeric(ParsedIrcMessage message, int numeric)
    {
        switch (numeric)
        {
            case 314: // RPL_WHOWASUSER
                // Format: <client> <nick> <user> <host> * :<realname>
                if (message.Parameters.Count >= 5)
                {
                    var info = new WhowasInfo
                    {
                        Nickname = message.GetParameter(1) ?? "",
                        Username = message.GetParameter(2) ?? "",
                        Hostname = message.GetParameter(3) ?? "",
                        RealName = message.Trailing ?? ""
                    };
                    WhowasReceived?.Invoke(this, new IrcWhowasEventArgs(Server, info));
                    
                    // Also show as server message
                    ServerMessage?.Invoke(this, new IrcServerMessageEventArgs(
                        Server,
                        $"WHOWAS {info.Nickname}: {info.Username}@{info.Hostname} ({info.RealName})",
                        "314"));
                }
                return true;

            case 312: // RPL_WHOWASSERVER (when in WHOWAS context)
                // Let this fall through to display
                return false;

            case 369: // RPL_ENDOFWHOWAS
                return true;

            case 406: // ERR_WASNOSUCHNICK
                ServerMessage?.Invoke(this, new IrcServerMessageEventArgs(
                    Server,
                    message.Trailing ?? "There was no such nickname",
                    "406"));
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Handles ban/exception/invite list numerics.
    /// </summary>
    private bool HandleChannelModeListNumeric(ParsedIrcMessage message, int numeric)
    {
        switch (numeric)
        {
            case 367: // RPL_BANLIST
                // Format: <client> <channel> <mask> [<who> <set-ts>]
                HandleModeListEntry(message, 'b');
                return true;

            case 368: // RPL_ENDOFBANLIST
                return true;

            case 346: // RPL_INVITELIST
                HandleModeListEntry(message, 'I');
                return true;

            case 347: // RPL_ENDOFINVITELIST
                return true;

            case 348: // RPL_EXCEPTLIST
                HandleModeListEntry(message, 'e');
                return true;

            case 349: // RPL_ENDOFEXCEPTLIST
                return true;

            default:
                return false;
        }
    }

    private void HandleModeListEntry(ParsedIrcMessage message, char mode)
    {
        var channel = message.GetParameter(1);
        var mask = message.GetParameter(2);
        if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(mask)) return;

        var entry = new ChannelListModeEntry
        {
            Mode = mode,
            Mask = mask,
            SetBy = message.Parameters.Count > 3 ? message.GetParameter(3) : null,
            SetAt = message.Parameters.Count > 4 && long.TryParse(message.GetParameter(4), out var ts) 
                ? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime 
                : null
        };

        ChannelModeListReceived?.Invoke(this, new IrcChannelModeListEventArgs(Server, channel, entry));
    }

    /// <summary>
    /// Handles MONITOR numeric replies (730-734).
    /// </summary>
    private bool HandleMonitorNumeric(ParsedIrcMessage message, int numeric)
    {
        switch (numeric)
        {
            case 730: // RPL_MONONLINE
                // Format: <client> :nick!user@host[,nick!user@host...]
                ParseMonitorList(message.Trailing, true);
                return true;

            case 731: // RPL_MONOFFLINE
                // Format: <client> :nick[,nick...]
                ParseMonitorList(message.Trailing, false);
                return true;

            case 732: // RPL_MONLIST
                // Just a list entry, no action needed
                return true;

            case 733: // RPL_ENDOFMONLIST
                return true;

            case 734: // ERR_MONLISTFULL
                var parts = message.Trailing?.Split(' ') ?? Array.Empty<string>();
                ServerMessage?.Invoke(this, new IrcServerMessageEventArgs(
                    Server,
                    $"Monitor list is full (limit: {(parts.Length > 0 ? parts[0] : "unknown")})",
                    "734"));
                return true;

            default:
                return false;
        }
    }

    private void ParseMonitorList(string? list, bool isOnline)
    {
        if (string.IsNullOrEmpty(list)) return;

        foreach (var entry in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string nickname;
            string? username = null;
            string? hostname = null;

            var bangIndex = entry.IndexOf('!');
            if (bangIndex > 0)
            {
                nickname = entry[..bangIndex];
                var atIndex = entry.IndexOf('@', bangIndex);
                if (atIndex > bangIndex)
                {
                    username = entry[(bangIndex + 1)..atIndex];
                    hostname = entry[(atIndex + 1)..];
                }
            }
            else
            {
                nickname = entry;
            }

            MonitorStatusChanged?.Invoke(this, new IrcMonitorEventArgs(Server, nickname, isOnline, username, hostname));
        }
    }

    /// <summary>
    /// Handles specific error numerics with better messages.
    /// </summary>
    private void HandleErrorNumeric(ParsedIrcMessage message, int numeric)
    {
        var errorMessage = numeric switch
        {
            401 => $"No such nick: {message.GetParameter(1)}",
            402 => $"No such server: {message.GetParameter(1)}",
            403 => $"No such channel: {message.GetParameter(1)}",
            404 => $"Cannot send to channel {message.GetParameter(1)}",
            405 => $"You have joined too many channels",
            406 => $"There was no such nickname: {message.GetParameter(1)}",
            407 => $"Too many targets",
            411 => "No recipient given",
            412 => "No text to send",
            421 => $"Unknown command: {message.GetParameter(1)}",
            432 => $"Erroneous nickname: {message.GetParameter(1)}",
            433 => null, // Handled separately
            436 => $"Nickname collision: {message.GetParameter(1)}",
            441 => $"{message.GetParameter(1)} is not on channel {message.GetParameter(2)}",
            442 => $"You're not on that channel: {message.GetParameter(1)}",
            443 => $"{message.GetParameter(1)} is already on channel {message.GetParameter(2)}",
            461 => $"Not enough parameters for {message.GetParameter(1)}",
            467 => $"Channel key already set for {message.GetParameter(1)}",
            471 => $"Channel {message.GetParameter(1)} is full (+l)",
            473 => $"Channel {message.GetParameter(1)} is invite-only (+i)",
            474 => $"You are banned from {message.GetParameter(1)} (+b)",
            475 => $"Bad channel key for {message.GetParameter(1)} (+k)",
            476 => $"Bad channel mask: {message.GetParameter(1)}",
            477 => $"Channel {message.GetParameter(1)} requires registration",
            478 => $"Ban list for {message.GetParameter(1)} is full",
            481 => "Permission denied: You're not an IRC operator",
            482 => $"You're not a channel operator on {message.GetParameter(1)}",
            483 => "You can't kill a server!",
            484 => "Your connection is restricted",
            485 => $"You're not the original channel operator of {message.GetParameter(1)}",
            491 => "No O-lines for your host",
            501 => "Unknown MODE flag",
            502 => "Cannot change mode for other users",
            _ => null
        };

        // Don't override specific handlers
        if (errorMessage != null)
        {
            Error?.Invoke(this, new IrcErrorEventArgs(Server, errorMessage));
        }
    }

    private (UserMode Mode, string Nickname) ParseUserPrefix(string name)
    {
        if (string.IsNullOrEmpty(name)) return (UserMode.Normal, name);

        var prefixChars = Server.ISupport.PrefixChars;
        var prefixModes = Server.ISupport.PrefixModes;

        // With multi-prefix, a user can have multiple prefixes like "@%+nick"
        // We need to find the highest mode and strip all prefixes
        UserMode highestMode = UserMode.Normal;
        int startIndex = 0;

        // Loop through all leading characters that are prefixes
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var prefixIndex = prefixChars.IndexOf(c);
            
            if (prefixIndex >= 0 && prefixIndex < prefixModes.Length)
            {
                startIndex = i + 1;
                var mode = prefixModes[prefixIndex];
                var userMode = mode switch
                {
                    'q' => UserMode.Owner,       // ~
                    'a' => UserMode.Admin,       // &
                    'o' => UserMode.Operator,    // @
                    'h' => UserMode.HalfOperator,// %
                    'v' => UserMode.Voice,       // +
                    _ => UserMode.Operator       // Default to Operator for unknown high modes
                };
                
                // Use the first (highest priority) mode found
                if (highestMode == UserMode.Normal || (int)userMode < (int)highestMode)
                {
                    highestMode = userMode;
                }
            }
            else
            {
                // Not a prefix, nickname starts here
                break;
            }
        }

        if (startIndex > 0)
        {
            return (highestMode, name[startIndex..]);
        }

        // Fallback for servers without ISUPPORT
        return name[0] switch
        {
            '~' => (UserMode.Owner, name[1..]),
            '&' => (UserMode.Admin, name[1..]),
            '@' => (UserMode.Operator, name[1..]),
            '%' => (UserMode.HalfOperator, name[1..]),
            '+' => (UserMode.Voice, name[1..]),
            _ => (UserMode.Normal, name)
        };
    }

    private static bool AcceptAllCertificates(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) => true;

    /// <summary>
    /// Gets the SSL protocols allowed based on the MinimumTlsVersion setting.
    /// </summary>
    private SslProtocols GetMinimumSslProtocols()
    {
        return MinimumTlsVersion?.ToUpperInvariant() switch
        {
            "TLS13" => SslProtocols.Tls13,
            "TLS12" => SslProtocols.Tls12 | SslProtocols.Tls13,
            "NONE" => SslProtocols.None, // Let the OS decide (may allow older protocols)
            _ => SslProtocols.Tls12 | SslProtocols.Tls13 // Default to TLS 1.2+
        };
    }

    /// <summary>
    /// Checks if a message contains the user's nickname or any highlight words.
    /// </summary>
    private bool CheckHighlight(string content)
    {
        // Check for nickname
        if (content.Contains(CurrentNickname, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check for custom highlight words
        foreach (var word in HighlightWords)
        {
            if (!string.IsNullOrWhiteSpace(word) && 
                content.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
    
    private void HandleWhoisNumeric(ParsedIrcMessage message, int numeric)
    {
        // WHOIS numerics: 311-318
        switch (numeric)
        {
            case 311: // RPL_WHOISUSER - nick user host * :realname
                var nick = message.GetParameter(1);
                if (string.IsNullOrEmpty(nick)) return;
                
                _pendingWhois = new WhoisInfo
                {
                    Nickname = nick,
                    Username = message.GetParameter(2),
                    Hostname = message.GetParameter(3),
                    RealName = message.Trailing
                };
                break;
                
            case 312: // RPL_WHOISSERVER - nick server :serverinfo
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    _pendingWhois.Server = message.GetParameter(2);
                    _pendingWhois.ServerInfo = message.Trailing;
                }
                break;
                
            case 313: // RPL_WHOISOPERATOR - nick :is an IRC operator
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    _pendingWhois.IsOperator = true;
                }
                break;
                
            case 317: // RPL_WHOISIDLE - nick idle signon :info
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    if (int.TryParse(message.GetParameter(2), out var idleSeconds))
                    {
                        _pendingWhois.IdleSeconds = idleSeconds;
                    }
                    if (long.TryParse(message.GetParameter(3), out var signOnUnix))
                    {
                        _pendingWhois.SignonTime = DateTimeOffset.FromUnixTimeSeconds(signOnUnix).LocalDateTime;
                    }
                }
                break;
                
            case 319: // RPL_WHOISCHANNELS - nick :channels
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    var channels = message.Trailing?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                    _pendingWhois.Channels.AddRange(channels);
                }
                break;
                
            case 301: // RPL_AWAY - nick :away message
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    _pendingWhois.IsAway = true;
                    _pendingWhois.AwayMessage = message.Trailing;
                }
                break;
                
            case 671: // RPL_WHOISSECURE - nick :is using a secure connection
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    _pendingWhois.IsSecure = true;
                }
                break;
                
            case 330: // RPL_WHOISACCOUNT - nick account :is logged in as
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    _pendingWhois.Account = message.GetParameter(2);
                }
                break;
                
            case 318: // RPL_ENDOFWHOIS - nick :End of WHOIS list
                if (_pendingWhois != null && message.GetParameter(1) == _pendingWhois.Nickname)
                {
                    WhoisReceived?.Invoke(this, new IrcWhoisEventArgs(Server, _pendingWhois));
                    _pendingWhois = null;
                }
                break;
        }
    }

    private void Cleanup()
    {
        _cts?.Cancel();
        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _reader = null;
        _writer = null;
        _stream = null;
        _tcpClient = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
