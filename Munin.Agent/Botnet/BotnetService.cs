using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Munin.Agent.Configuration;
using Munin.Agent.Services;
using Munin.Core.Services;
using Serilog;

namespace Munin.Agent.Botnet;

/// <summary>
/// Manages botnet connections between agents.
/// Implements Eggdrop-style bot linking with modern encryption.
/// </summary>
public class BotnetService : IDisposable
{
    private readonly ILogger _logger;
    private readonly AgentConfigurationService _configService;
    private readonly EncryptionService _encryptionService;
    private readonly IrcBotService _botService;
    
    private readonly ConcurrentDictionary<string, BotnetLink> _links = new();
    private readonly ConcurrentDictionary<string, PartylineSession> _partylineSessions = new();
    
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// Event raised when a partyline message is received.
    /// </summary>
    public event EventHandler<PartylineMessageEventArgs>? PartylineMessage;

    /// <summary>
    /// Event raised when a bot links/unlinks.
    /// </summary>
    public event EventHandler<BotLinkEventArgs>? BotLinkChanged;

    /// <summary>
    /// Event raised when user sync data is received.
    /// </summary>
    public event EventHandler<UserSyncEventArgs>? UserSyncReceived;

    /// <summary>
    /// Event raised when an op request is received.
    /// </summary>
    public event EventHandler<OpRequestEventArgs>? OpRequestReceived;

    public BotnetService(
        AgentConfigurationService configService,
        EncryptionService encryptionService,
        IrcBotService botService)
    {
        _logger = Log.ForContext<BotnetService>();
        _configService = configService;
        _encryptionService = encryptionService;
        _botService = botService;
    }

    /// <summary>
    /// Starts the botnet service.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var config = _configService.Configuration.Botnet;
        if (!config.Enabled)
        {
            _logger.Information("Botnet is disabled");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Start listener for incoming connections
        if (config.ListenPort > 0)
        {
            _listener = new TcpListener(IPAddress.Any, config.ListenPort);
            _listener.Start();
            _logger.Information("Botnet listening on port {Port}", config.ListenPort);
            _ = AcceptConnectionsAsync(_cts.Token);
        }

        // Connect to configured hub bots
        foreach (var bot in config.LinkedBots.Where(b => b.Connect))
        {
            _ = ConnectToBotAsync(bot, _cts.Token);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the botnet service.
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        // Disconnect all links
        foreach (var link in _links.Values)
        {
            try
            {
                await SendMessageAsync(link, new BotnetGoodbye { Reason = "Shutting down" });
                link.Disconnect();
            }
            catch { }
        }

        _links.Clear();
        _partylineSessions.Clear();
    }

    /// <summary>
    /// Gets the list of linked bots.
    /// </summary>
    public IReadOnlyList<BotnetLink> GetLinks()
    {
        return _links.Values.ToList();
    }

    /// <summary>
    /// Gets partyline users.
    /// </summary>
    public IReadOnlyList<PartylineSession> GetPartylineUsers(string channel = "")
    {
        return _partylineSessions.Values
            .Where(s => string.IsNullOrEmpty(channel) || s.Channel == channel)
            .ToList();
    }

    /// <summary>
    /// Sends a partyline message.
    /// </summary>
    public async Task SendPartylineMessageAsync(string nick, string text, string channel = "")
    {
        var message = new BotnetChat
        {
            FromAgent = _configService.Configuration.Name,
            FromNick = nick,
            Channel = channel,
            Text = text
        };

        await BroadcastAsync(message);
        
        // Also deliver locally
        PartylineMessage?.Invoke(this, new PartylineMessageEventArgs(nick, channel, text, _configService.Configuration.Name));
    }

    /// <summary>
    /// Joins the partyline.
    /// </summary>
    public async Task JoinPartylineAsync(string nick, string flags, string channel = "")
    {
        var session = new PartylineSession
        {
            Nick = nick,
            BotName = _configService.Configuration.Name,
            Channel = channel,
            Flags = flags,
            JoinedAt = DateTime.UtcNow
        };

        var key = $"{_configService.Configuration.Name}:{nick}";
        _partylineSessions[key] = session;

        await BroadcastAsync(new BotnetJoin
        {
            FromAgent = _configService.Configuration.Name,
            Nick = nick,
            Channel = channel,
            Flags = flags
        });
    }

    /// <summary>
    /// Leaves the partyline.
    /// </summary>
    public async Task LeavePartylineAsync(string nick, string channel = "", string reason = "")
    {
        var key = $"{_configService.Configuration.Name}:{nick}";
        _partylineSessions.TryRemove(key, out _);

        await BroadcastAsync(new BotnetPart
        {
            FromAgent = _configService.Configuration.Name,
            Nick = nick,
            Channel = channel,
            Reason = reason
        });
    }

    /// <summary>
    /// Requests ops from linked bots.
    /// </summary>
    public async Task RequestOpsAsync(string ircServer, string channel, string nick, string hostmask)
    {
        await BroadcastAsync(new BotnetOpRequest
        {
            FromAgent = _configService.Configuration.Name,
            IrcServer = ircServer,
            Channel = channel,
            Nick = nick,
            Hostmask = hostmask
        });
    }

    /// <summary>
    /// Synchronizes user database with linked bots.
    /// </summary>
    public async Task SyncUsersAsync(List<SyncedUser> users, bool fullSync = false)
    {
        await BroadcastAsync(new BotnetUserSync
        {
            FromAgent = _configService.Configuration.Name,
            Users = users,
            IsFullSync = fullSync
        });
    }

    #region Private Methods

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleIncomingConnectionAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error accepting botnet connection");
            }
        }
    }

    private async Task HandleIncomingConnectionAsync(TcpClient client, CancellationToken ct)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.Information("Incoming botnet connection from {Endpoint}", endpoint);

        try
        {
            var link = new BotnetLink(client);
            
            // Wait for Hello
            var hello = await ReceiveMessageAsync<BotnetHello>(link, ct);
            if (hello == null)
            {
                _logger.Warning("Invalid handshake from {Endpoint}", endpoint);
                client.Close();
                return;
            }

            // Verify bot is in our allowed list
            var botConfig = _configService.Configuration.Botnet.LinkedBots
                .FirstOrDefault(b => b.Name.Equals(hello.AgentName, StringComparison.OrdinalIgnoreCase));
            
            if (botConfig == null)
            {
                _logger.Warning("Unknown bot {Bot} tried to connect", hello.AgentName);
                await SendMessageAsync(link, new BotnetError { Code = "UNKNOWN_BOT", Message = "Not in allowed bot list" });
                client.Close();
                return;
            }

            // Send challenge
            var challengeBytes = RandomNumberGenerator.GetBytes(32);
            var challenge = Convert.ToBase64String(challengeBytes);
            await SendMessageAsync(link, new BotnetChallenge
            {
                FromAgent = _configService.Configuration.Name,
                Challenge = challenge
            });

            // Wait for response
            var response = await ReceiveMessageAsync<BotnetResponse>(link, ct);
            if (response == null || !VerifyResponse(response, botConfig.Password, challengeBytes))
            {
                _logger.Warning("Authentication failed for {Bot}", hello.AgentName);
                await SendMessageAsync(link, new BotnetError { Code = "AUTH_FAILED", Message = "Authentication failed" });
                client.Close();
                return;
            }

            // Success!
            link.BotName = hello.AgentName;
            link.IsAuthenticated = true;
            link.ConnectedAt = DateTime.UtcNow;
            _links[hello.AgentName] = link;

            await SendMessageAsync(link, new BotnetWelcome
            {
                FromAgent = _configService.Configuration.Name,
                AgentName = _configService.Configuration.Name,
                LinkedBots = _links.Keys.ToList()
            });

            BotLinkChanged?.Invoke(this, new BotLinkEventArgs(hello.AgentName, true));
            _logger.Information("Bot {Bot} linked successfully", hello.AgentName);

            // Start message loop
            await MessageLoopAsync(link, ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling botnet connection from {Endpoint}", endpoint);
        }
        finally
        {
            client.Close();
        }
    }

    private async Task ConnectToBotAsync(LinkedBotConfiguration botConfig, CancellationToken ct)
    {
        var retryDelay = 10;
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.Information("Connecting to bot {Bot} at {Host}:{Port}", 
                    botConfig.Name, botConfig.Host, botConfig.Port);

                var client = new TcpClient();
                await client.ConnectAsync(botConfig.Host, botConfig.Port, ct);

                var link = new BotnetLink(client);

                // Send Hello
                await SendMessageAsync(link, new BotnetHello
                {
                    FromAgent = _configService.Configuration.Name,
                    AgentName = _configService.Configuration.Name,
                    Version = "1.0"
                });

                // Wait for challenge
                var challenge = await ReceiveMessageAsync<BotnetChallenge>(link, ct);
                if (challenge == null)
                {
                    _logger.Warning("No challenge from {Bot}", botConfig.Name);
                    client.Close();
                    await Task.Delay(TimeSpan.FromSeconds(retryDelay), ct);
                    continue;
                }

                // Send response
                var responseHash = ComputeResponse(botConfig.Password, Convert.FromBase64String(challenge.Challenge));
                await SendMessageAsync(link, new BotnetResponse
                {
                    FromAgent = _configService.Configuration.Name,
                    Response = responseHash
                });

                // Wait for welcome
                var welcome = await ReceiveMessageAsync<BotnetWelcome>(link, ct);
                if (welcome == null)
                {
                    _logger.Warning("Authentication failed for {Bot}", botConfig.Name);
                    client.Close();
                    await Task.Delay(TimeSpan.FromSeconds(retryDelay), ct);
                    continue;
                }

                // Success!
                link.BotName = botConfig.Name;
                link.IsAuthenticated = true;
                link.ConnectedAt = DateTime.UtcNow;
                _links[botConfig.Name] = link;

                BotLinkChanged?.Invoke(this, new BotLinkEventArgs(botConfig.Name, true));
                _logger.Information("Linked to bot {Bot}", botConfig.Name);

                // Message loop
                await MessageLoopAsync(link, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error connecting to bot {Bot}", botConfig.Name);
            }

            // Disconnected, wait before retry
            BotLinkChanged?.Invoke(this, new BotLinkEventArgs(botConfig.Name, false));
            _links.TryRemove(botConfig.Name, out _);
            
            await Task.Delay(TimeSpan.FromSeconds(retryDelay), ct);
            retryDelay = Math.Min(retryDelay * 2, 300); // Max 5 minutes
        }
    }

    private async Task MessageLoopAsync(BotnetLink link, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && link.IsConnected)
            {
                var message = await ReceiveMessageAsync(link, ct);
                if (message == null)
                    break;

                await HandleMessageAsync(link, message);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in message loop for {Bot}", link.BotName);
        }
        finally
        {
            _links.TryRemove(link.BotName, out _);
            BotLinkChanged?.Invoke(this, new BotLinkEventArgs(link.BotName, false));
            
            // Remove partyline sessions from this bot
            var keysToRemove = _partylineSessions
                .Where(kvp => kvp.Value.BotName == link.BotName)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in keysToRemove)
                _partylineSessions.TryRemove(key, out _);
        }
    }

    private async Task HandleMessageAsync(BotnetLink link, BotnetMessage message)
    {
        switch (message.Type)
        {
            case BotnetMessageType.Ping:
                await SendMessageAsync(link, new BotnetPong { PingId = ((BotnetPing)message).PingId });
                break;

            case BotnetMessageType.Pong:
                link.LastPong = DateTime.UtcNow;
                break;

            case BotnetMessageType.Chat:
                var chat = (BotnetChat)message;
                PartylineMessage?.Invoke(this, new PartylineMessageEventArgs(
                    chat.FromNick, chat.Channel, chat.Text, chat.FromAgent));
                break;

            case BotnetMessageType.Join:
                var join = (BotnetJoin)message;
                var key = $"{join.FromAgent}:{join.Nick}";
                _partylineSessions[key] = new PartylineSession
                {
                    Nick = join.Nick,
                    BotName = join.FromAgent,
                    Channel = join.Channel,
                    Flags = join.Flags,
                    JoinedAt = DateTime.UtcNow
                };
                PartylineMessage?.Invoke(this, new PartylineMessageEventArgs(
                    join.Nick, join.Channel, $"*** {join.Nick} joined partyline", join.FromAgent));
                break;

            case BotnetMessageType.Part:
                var part = (BotnetPart)message;
                _partylineSessions.TryRemove($"{part.FromAgent}:{part.Nick}", out _);
                PartylineMessage?.Invoke(this, new PartylineMessageEventArgs(
                    part.Nick, part.Channel, $"*** {part.Nick} left partyline: {part.Reason}", part.FromAgent));
                break;

            case BotnetMessageType.UserSync:
                var sync = (BotnetUserSync)message;
                UserSyncReceived?.Invoke(this, new UserSyncEventArgs(sync.Users, sync.IsFullSync, sync.FromAgent));
                break;

            case BotnetMessageType.OpRequest:
                var opReq = (BotnetOpRequest)message;
                OpRequestReceived?.Invoke(this, new OpRequestEventArgs(
                    opReq.IrcServer, opReq.Channel, opReq.Nick, opReq.Hostmask, opReq.FromAgent));
                break;

            case BotnetMessageType.Goodbye:
                _logger.Information("Bot {Bot} disconnecting: {Reason}", 
                    link.BotName, ((BotnetGoodbye)message).Reason);
                link.Disconnect();
                break;
        }
    }

    private async Task BroadcastAsync(BotnetMessage message)
    {
        foreach (var link in _links.Values.Where(l => l.IsAuthenticated))
        {
            try
            {
                await SendMessageAsync(link, message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error broadcasting to {Bot}", link.BotName);
            }
        }
    }

    private async Task SendMessageAsync(BotnetLink link, BotnetMessage message)
    {
        var json = JsonSerializer.Serialize<object>(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var wrapper = new { type = (int)message.Type, data = json };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(wrapper) + "\n");
        
        await link.Stream.WriteAsync(bytes);
        await link.Stream.FlushAsync();
    }

    private async Task<T?> ReceiveMessageAsync<T>(BotnetLink link, CancellationToken ct) where T : BotnetMessage
    {
        var message = await ReceiveMessageAsync(link, ct);
        return message as T;
    }

    private async Task<BotnetMessage?> ReceiveMessageAsync(BotnetLink link, CancellationToken ct)
    {
        var line = await link.Reader.ReadLineAsync(ct);
        if (string.IsNullOrEmpty(line))
            return null;

        var wrapper = JsonSerializer.Deserialize<JsonElement>(line);
        var type = (BotnetMessageType)wrapper.GetProperty("type").GetInt32();
        var data = wrapper.GetProperty("data").GetString();
        
        if (string.IsNullOrEmpty(data))
            return null;

        return type switch
        {
            BotnetMessageType.Hello => JsonSerializer.Deserialize<BotnetHello>(data),
            BotnetMessageType.Challenge => JsonSerializer.Deserialize<BotnetChallenge>(data),
            BotnetMessageType.Response => JsonSerializer.Deserialize<BotnetResponse>(data),
            BotnetMessageType.Welcome => JsonSerializer.Deserialize<BotnetWelcome>(data),
            BotnetMessageType.Goodbye => JsonSerializer.Deserialize<BotnetGoodbye>(data),
            BotnetMessageType.Ping => JsonSerializer.Deserialize<BotnetPing>(data),
            BotnetMessageType.Pong => JsonSerializer.Deserialize<BotnetPong>(data),
            BotnetMessageType.Chat => JsonSerializer.Deserialize<BotnetChat>(data),
            BotnetMessageType.Join => JsonSerializer.Deserialize<BotnetJoin>(data),
            BotnetMessageType.Part => JsonSerializer.Deserialize<BotnetPart>(data),
            BotnetMessageType.UserSync => JsonSerializer.Deserialize<BotnetUserSync>(data),
            BotnetMessageType.OpRequest => JsonSerializer.Deserialize<BotnetOpRequest>(data),
            BotnetMessageType.OpGrant => JsonSerializer.Deserialize<BotnetOpGrant>(data),
            BotnetMessageType.Error => JsonSerializer.Deserialize<BotnetError>(data),
            _ => null
        };
    }

    private string ComputeResponse(string password, byte[] challenge)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var combined = new byte[passwordBytes.Length + challenge.Length];
        Buffer.BlockCopy(passwordBytes, 0, combined, 0, passwordBytes.Length);
        Buffer.BlockCopy(challenge, 0, combined, passwordBytes.Length, challenge.Length);
        
        var hash = SHA256.HashData(combined);
        return Convert.ToBase64String(hash);
    }

    private bool VerifyResponse(BotnetResponse response, string password, byte[] challenge)
    {
        var expected = ComputeResponse(password, challenge);
        return response.Response == expected;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();

        foreach (var link in _links.Values)
            link.Dispose();

        _links.Clear();
        _partylineSessions.Clear();
    }
}

/// <summary>
/// Represents a connection to another bot.
/// </summary>
public class BotnetLink : IDisposable
{
    private readonly TcpClient _client;
    
    public string BotName { get; set; } = "";
    public bool IsAuthenticated { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastPong { get; set; }
    
    public NetworkStream Stream => _client.GetStream();
    public StreamReader Reader { get; }
    public bool IsConnected => _client.Connected;

    public BotnetLink(TcpClient client)
    {
        _client = client;
        Reader = new StreamReader(Stream, Encoding.UTF8);
    }

    public void Disconnect()
    {
        _client.Close();
    }

    public void Dispose()
    {
        Reader.Dispose();
        _client.Dispose();
    }
}

/// <summary>
/// Represents a partyline session.
/// </summary>
public class PartylineSession
{
    public string Nick { get; set; } = "";
    public string BotName { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Flags { get; set; } = "";
    public DateTime JoinedAt { get; set; }
}

// Event args

public class PartylineMessageEventArgs : EventArgs
{
    public string Nick { get; }
    public string Channel { get; }
    public string Message { get; }
    public string FromBot { get; }

    public PartylineMessageEventArgs(string nick, string channel, string message, string fromBot)
    {
        Nick = nick;
        Channel = channel;
        Message = message;
        FromBot = fromBot;
    }
}

public class BotLinkEventArgs : EventArgs
{
    public string BotName { get; }
    public bool IsLinked { get; }

    public BotLinkEventArgs(string botName, bool isLinked)
    {
        BotName = botName;
        IsLinked = isLinked;
    }
}

public class UserSyncEventArgs : EventArgs
{
    public List<SyncedUser> Users { get; }
    public bool IsFullSync { get; }
    public string FromBot { get; }

    public UserSyncEventArgs(List<SyncedUser> users, bool isFullSync, string fromBot)
    {
        Users = users;
        IsFullSync = isFullSync;
        FromBot = fromBot;
    }
}

public class OpRequestEventArgs : EventArgs
{
    public string IrcServer { get; }
    public string Channel { get; }
    public string Nick { get; }
    public string Hostmask { get; }
    public string FromBot { get; }

    public OpRequestEventArgs(string ircServer, string channel, string nick, string hostmask, string fromBot)
    {
        IrcServer = ircServer;
        Channel = channel;
        Nick = nick;
        Hostmask = hostmask;
        FromBot = fromBot;
    }
}
