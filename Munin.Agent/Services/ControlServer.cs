using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Munin.Agent.Configuration;
using Munin.Agent.Protocol;
using Serilog;

namespace Munin.Agent.Services;

/// <summary>
/// TLS control server that accepts remote control connections from Munin UI.
/// </summary>
public class ControlServer : BackgroundService
{
    private readonly ILogger _logger;
    private readonly AgentConfigurationService _configService;
    private readonly AgentSecurityService _securityService;
    private readonly IrcBotService _botService;
    private readonly List<ControlConnection> _connections = new();
    private readonly object _connectionsLock = new();
    private TcpListener? _listener;
    private X509Certificate2? _certificate;
    private uint _sequenceCounter;

    /// <summary>
    /// Gets whether the control server is currently running and accepting connections.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets the number of connected clients.
    /// </summary>
    public int ConnectionCount
    {
        get
        {
            lock (_connectionsLock)
            {
                return _connections.Count;
            }
        }
    }

    public ControlServer(
        AgentConfigurationService configService,
        AgentSecurityService securityService,
        IrcBotService botService)
    {
        _logger = Log.ForContext<ControlServer>();
        _configService = configService;
        _securityService = securityService;
        _botService = botService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = _configService.Configuration;
        
        if (!config.ControlServer.Enabled)
        {
            _logger.Information("Control server is disabled");
            return;
        }

        // Load certificate
        if (!LoadCertificate())
        {
            _logger.Error("Failed to load control server certificate");
            return;
        }

        // Initialize security with auth token
        var authToken = _configService.GetDecryptedValue(config.ControlServer.AuthToken);
        if (string.IsNullOrEmpty(authToken))
        {
            _logger.Error("Auth token is not configured");
            return;
        }
        _securityService.Initialize(authToken);

        // Start listening
        var address = config.ControlServer.BindAddress == "*" 
            ? IPAddress.Any 
            : IPAddress.Parse(config.ControlServer.BindAddress);
        
        _listener = new TcpListener(address, config.ControlServer.Port);
        
        try
        {
            _listener.Start();
            IsRunning = true;
            _logger.Information("Control server listening on {Address}:{Port}", 
                config.ControlServer.BindAddress, config.ControlServer.Port);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                    _ = HandleClientAsync(client, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error accepting client connection");
                }
            }
        }
        finally
        {
            IsRunning = false;
            _listener.Stop();
            
            // Close all connections
            lock (_connectionsLock)
            {
                foreach (var connection in _connections)
                {
                    connection.Dispose();
                }
                _connections.Clear();
            }

            _securityService.Cleanup();
        }
    }

    private bool LoadCertificate()
    {
        var config = _configService.Configuration;
        var certPath = config.ControlServer.CertificatePath;
        var certPassword = _configService.GetDecryptedValue(config.ControlServer.CertificatePassword);

        if (string.IsNullOrEmpty(certPath))
        {
            _logger.Error("Certificate path is not configured");
            return false;
        }

        if (!File.Exists(certPath))
        {
            _logger.Error("Certificate file not found: {Path}", certPath);
            return false;
        }

        try
        {
            _certificate = string.IsNullOrEmpty(certPassword)
                ? new X509Certificate2(certPath)
                : new X509Certificate2(certPath, certPassword);
            
            _logger.Information("Loaded certificate: {Subject}", _certificate.Subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load certificate");
            return false;
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.Information("New connection from {Endpoint}", endpoint);

        // Check IP allowlist
        var config = _configService.Configuration;
        var remoteIp = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address.ToString() ?? "";
        
        if (!_securityService.IsIpAllowed(remoteIp, config.ControlServer.AllowedIps))
        {
            _logger.Warning("Connection rejected - IP not allowed: {IP}", remoteIp);
            client.Close();
            return;
        }

        ControlConnection? connection = null;

        try
        {
            // Wrap in TLS
            var sslStream = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: ValidateClientCertificate);

            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | 
                                          System.Security.Authentication.SslProtocols.Tls13
                },
                ct);

            _logger.Debug("TLS established with {Endpoint} using {Protocol}", 
                endpoint, sslStream.SslProtocol);

            // Create connection object
            connection = new ControlConnection(client, sslStream, endpoint);

            // Perform authentication
            if (!await AuthenticateAsync(connection, ct))
            {
                _logger.Warning("Authentication failed for {Endpoint}", endpoint);
                connection.Dispose();
                return;
            }

            // Add to connected clients
            lock (_connectionsLock)
            {
                _connections.Add(connection);
            }

            _logger.Information("Client authenticated: {Endpoint}", endpoint);

            // Main message loop
            await ProcessMessagesAsync(connection, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Error handling client {Endpoint}", endpoint);
        }
        finally
        {
            if (connection != null)
            {
                lock (_connectionsLock)
                {
                    _connections.Remove(connection);
                }
                connection.Dispose();
            }
            else
            {
                client.Close();
            }
            
            _logger.Information("Client disconnected: {Endpoint}", endpoint);
        }
    }

    private bool ValidateClientCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // Client certificate is optional - we use challenge-response authentication
        return true;
    }

    private async Task<bool> AuthenticateAsync(ControlConnection connection, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(AgentProtocol.AuthTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            // Generate and send challenge
            var nonce = new byte[8];
            Random.Shared.NextBytes(nonce);
            var challenge = _securityService.CreateChallenge();
            
            var challengePayload = new byte[40]; // 8 nonce + 32 challenge
            nonce.CopyTo(challengePayload, 0);
            challenge.CopyTo(challengePayload, 8);

            var challengeMsg = AgentMessage.Create(AgentMessageType.AuthChallenge, challengePayload);
            await AgentMessageSerializer.WriteAsync(connection.Stream, challengeMsg, linkedCts.Token);

            // Wait for response
            var response = await AgentMessageSerializer.ReadAsync(connection.Stream, linkedCts.Token);
            
            if (response?.Type != AgentMessageType.AuthResponse)
            {
                await SendAuthFailureAsync(connection, "Expected AuthResponse", linkedCts.Token);
                return false;
            }

            if (response.Payload.Length != 40)
            {
                await SendAuthFailureAsync(connection, "Invalid response length", linkedCts.Token);
                return false;
            }

            // Verify nonce
            var responseNonce = response.Payload[..8];
            if (!nonce.SequenceEqual(responseNonce))
            {
                await SendAuthFailureAsync(connection, "Nonce mismatch", linkedCts.Token);
                return false;
            }

            // Verify HMAC
            var hmacResponse = response.Payload[8..40];
            if (!_securityService.VerifyAuthentication(challenge, hmacResponse))
            {
                await SendAuthFailureAsync(connection, "Authentication failed", linkedCts.Token);
                return false;
            }

            // Send success with agent info
            var agentInfo = System.Text.Json.JsonSerializer.Serialize(new
            {
                version = typeof(ControlServer).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                hostname = Environment.MachineName,
                platform = Environment.OSVersion.ToString(),
                uptime = Environment.TickCount64
            });

            var successMsg = AgentMessage.Create(AgentMessageType.AuthSuccess, agentInfo);
            await AgentMessageSerializer.WriteAsync(connection.Stream, successMsg, linkedCts.Token);

            connection.IsAuthenticated = true;
            return true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.Warning("Authentication timeout for {Endpoint}", connection.Endpoint);
            return false;
        }
    }

    private async Task SendAuthFailureAsync(ControlConnection connection, string message, CancellationToken ct)
    {
        var msg = AgentMessage.Create(AgentMessageType.AuthFailure, message);
        await AgentMessageSerializer.WriteAsync(connection.Stream, msg, ct);
    }

    private async Task ProcessMessagesAsync(ControlConnection connection, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && connection.IsConnected)
        {
            try
            {
                var message = await AgentMessageSerializer.ReadAsync(connection.Stream, ct);
                
                if (message == null)
                    break;

                var response = await HandleMessageAsync(message, connection, ct);
                
                if (response != null)
                {
                    response.SequenceNumber = message.SequenceNumber;
                    await AgentMessageSerializer.WriteAsync(connection.Stream, response, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, "Error processing message from {Endpoint}", connection.Endpoint);
                break;
            }
        }
    }

    private async Task<AgentMessage?> HandleMessageAsync(
        AgentMessage message, 
        ControlConnection connection, 
        CancellationToken ct)
    {
        _logger.Debug("Received {Type} from {Endpoint}", message.Type, connection.Endpoint);

        return message.Type switch
        {
            AgentMessageType.Ping => HandlePing(message),
            AgentMessageType.GetStatus => HandleGetStatus(),
            AgentMessageType.GetConnections => HandleGetConnections(),
            AgentMessageType.GetChannels => await HandleGetChannelsAsync(message),
            AgentMessageType.GetUsers => await HandleGetUsersAsync(message),
            AgentMessageType.JoinChannel => await HandleJoinChannelAsync(message),
            AgentMessageType.PartChannel => await HandlePartChannelAsync(message),
            AgentMessageType.SendMessage => await HandleSendMessageAsync(message),
            AgentMessageType.SendRaw => await HandleSendRawAsync(message),
            AgentMessageType.Connect => await HandleConnectAsync(message),
            AgentMessageType.Disconnect => await HandleDisconnectAsync(message),
            AgentMessageType.Shutdown => HandleShutdown(),
            _ => AgentMessage.Create(AgentMessageType.NotSupported, new[] { (byte)message.Type })
        };
    }

    private AgentMessage HandlePing(AgentMessage message)
    {
        return new AgentMessage
        {
            Type = AgentMessageType.Pong,
            Payload = message.Payload
        };
    }

    private AgentMessage HandleGetStatus()
    {
        var status = new
        {
            version = typeof(ControlServer).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            uptime = Environment.TickCount64,
            memoryUsage = GC.GetTotalMemory(false),
            connections = _botService.GetConnectionStatus(),
            connectedClients = ConnectionCount
        };

        return AgentMessage.Create(AgentMessageType.Status, 
            System.Text.Json.JsonSerializer.Serialize(status));
    }

    private AgentMessage HandleGetConnections()
    {
        var connections = _botService.GetConnectionStatus();
        return AgentMessage.Create(AgentMessageType.Connections,
            System.Text.Json.JsonSerializer.Serialize(connections));
    }

    private async Task<AgentMessage> HandleGetChannelsAsync(AgentMessage message)
    {
        var serverId = message.GetPayloadString();
        var channels = await _botService.GetChannelsAsync(serverId);
        return AgentMessage.Create(AgentMessageType.Channels,
            System.Text.Json.JsonSerializer.Serialize(channels));
    }

    private async Task<AgentMessage> HandleGetUsersAsync(AgentMessage message)
    {
        var parts = message.GetPayloadString().Split('\0');
        if (parts.Length < 2)
            return AgentMessageSerializer.CreateError("Invalid payload");

        var users = await _botService.GetUsersAsync(parts[0], parts[1]);
        return AgentMessage.Create(AgentMessageType.Users,
            System.Text.Json.JsonSerializer.Serialize(users));
    }

    private async Task<AgentMessage> HandleJoinChannelAsync(AgentMessage message)
    {
        var parts = message.GetPayloadString().Split('\0');
        if (parts.Length < 2)
            return AgentMessageSerializer.CreateError("Invalid payload");

        var key = parts.Length > 2 ? parts[2] : null;
        await _botService.JoinChannelAsync(parts[0], parts[1], key);
        return AgentMessageSerializer.CreateSuccess();
    }

    private async Task<AgentMessage> HandlePartChannelAsync(AgentMessage message)
    {
        var parts = message.GetPayloadString().Split('\0');
        if (parts.Length < 2)
            return AgentMessageSerializer.CreateError("Invalid payload");

        var reason = parts.Length > 2 ? parts[2] : null;
        await _botService.PartChannelAsync(parts[0], parts[1], reason);
        return AgentMessageSerializer.CreateSuccess();
    }

    private async Task<AgentMessage> HandleSendMessageAsync(AgentMessage message)
    {
        var parts = message.GetPayloadString().Split('\0');
        if (parts.Length < 3)
            return AgentMessageSerializer.CreateError("Invalid payload");

        await _botService.SendMessageAsync(parts[0], parts[1], parts[2]);
        return AgentMessageSerializer.CreateSuccess();
    }

    private async Task<AgentMessage> HandleSendRawAsync(AgentMessage message)
    {
        var parts = message.GetPayloadString().Split('\0');
        if (parts.Length < 2)
            return AgentMessageSerializer.CreateError("Invalid payload");

        await _botService.SendRawAsync(parts[0], parts[1]);
        return AgentMessageSerializer.CreateSuccess();
    }

    private async Task<AgentMessage> HandleConnectAsync(AgentMessage message)
    {
        var serverId = message.GetPayloadString();
        await _botService.ConnectAsync(serverId);
        return AgentMessageSerializer.CreateSuccess();
    }

    private async Task<AgentMessage> HandleDisconnectAsync(AgentMessage message)
    {
        var parts = message.GetPayloadString().Split('\0');
        var serverId = parts[0];
        var quitMessage = parts.Length > 1 ? parts[1] : null;
        await _botService.DisconnectAsync(serverId, quitMessage);
        return AgentMessageSerializer.CreateSuccess();
    }

    private AgentMessage HandleShutdown()
    {
        _logger.Warning("Shutdown requested by remote client");
        
        // Initiate shutdown
        Task.Run(async () =>
        {
            await Task.Delay(500);
            Environment.Exit(0);
        });

        return AgentMessageSerializer.CreateSuccess("Shutting down");
    }

    /// <summary>
    /// Broadcasts a message to all connected clients.
    /// </summary>
    public async Task BroadcastAsync(AgentMessage message, CancellationToken ct = default)
    {
        message.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);

        List<ControlConnection> connections;
        lock (_connectionsLock)
        {
            connections = _connections.ToList();
        }

        foreach (var connection in connections)
        {
            try
            {
                if (connection.IsConnected && connection.IsAuthenticated)
                {
                    await AgentMessageSerializer.WriteAsync(connection.Stream, message, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to broadcast to {Endpoint}", connection.Endpoint);
            }
        }
    }
}

/// <summary>
/// Represents a connected control client.
/// </summary>
public sealed class ControlConnection : IDisposable
{
    private readonly TcpClient _client;
    
    public SslStream Stream { get; }
    public string Endpoint { get; }
    public bool IsAuthenticated { get; set; }
    public bool IsConnected => _client.Connected;
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    public ControlConnection(TcpClient client, SslStream stream, string endpoint)
    {
        _client = client;
        Stream = stream;
        Endpoint = endpoint;
    }

    public void Dispose()
    {
        Stream.Dispose();
        _client.Dispose();
    }
}
