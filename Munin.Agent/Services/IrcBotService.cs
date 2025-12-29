using Munin.Agent.Configuration;
using Munin.Core.Events;
using Munin.Core.Models;
using Munin.Core.Services;
using Serilog;

namespace Munin.Agent.Services;

/// <summary>
/// Manages IRC connections for the agent.
/// Wraps IrcConnection with autonomous operation capabilities.
/// </summary>
public class IrcBotService
{
    private readonly ILogger _logger;
    private readonly AgentConfigurationService _configService;
    private readonly Dictionary<string, BotConnection> _connections = new();
    private readonly object _connectionsLock = new();

    /// <summary>
    /// Event raised when an IRC message is received (for forwarding to control clients).
    /// </summary>
    public event EventHandler<IrcMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    public event EventHandler<ConnectionStateEventArgs>? ConnectionStateChanged;

    public IrcBotService(AgentConfigurationService configService)
    {
        _logger = Log.ForContext<IrcBotService>();
        _configService = configService;
    }

    /// <summary>
    /// Initializes connections from configuration.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var config = _configService.Configuration;

        foreach (var serverConfig in config.Servers)
        {
            if (!serverConfig.Enabled)
            {
                _logger.Information("Skipping disabled server: {Server}", serverConfig.Id);
                continue;
            }

            var connection = CreateConnection(serverConfig);
            
            lock (_connectionsLock)
            {
                _connections[serverConfig.Id] = connection;
            }

            if (serverConfig.AutoConnect)
            {
                _logger.Information("Auto-connecting to {Server}", serverConfig.Id);
                _ = ConnectWithRetryAsync(serverConfig.Id, ct);
            }
        }
        
        await Task.CompletedTask;
    }

    private BotConnection CreateConnection(IrcServerConfiguration serverConfig)
    {
        // Create the IrcServer model
        var ircServer = new IrcServer
        {
            Id = serverConfig.Id,
            Name = serverConfig.Name,
            Hostname = serverConfig.Address,
            Port = serverConfig.Port,
            UseSsl = serverConfig.UseSsl,
            AcceptInvalidCertificates = !serverConfig.VerifyCertificate,
            Nickname = serverConfig.Nickname,
            Username = serverConfig.Username,
            RealName = serverConfig.Realname,
            Password = _configService.GetDecryptedValue(serverConfig.ServerPasswordEncrypted),
            NickServPassword = _configService.GetDecryptedValue(serverConfig.NickservPasswordEncrypted),
            AutoJoinChannels = serverConfig.Channels.Where(c => c.AutoJoin).Select(c => c.Name).ToList()
        };

        // Configure SASL if present
        var saslUsername = _configService.GetDecryptedValue(serverConfig.SaslUsernameEncrypted);
        if (!string.IsNullOrEmpty(saslUsername))
        {
            ircServer.SaslUsername = saslUsername;
            ircServer.SaslPassword = _configService.GetDecryptedValue(serverConfig.SaslPasswordEncrypted);
        }

        var ircConnection = new IrcConnection(ircServer);
        ircConnection.AutoReconnect = serverConfig.AutoReconnect;
        ircConnection.ReconnectDelaySeconds = serverConfig.ReconnectDelaySeconds;

        var botConnection = new BotConnection
        {
            Id = serverConfig.Id,
            Config = serverConfig,
            Connection = ircConnection,
            Server = ircServer
        };

        // Wire up events
        ircConnection.MessageReceived += (s, e) => OnMessageReceived(serverConfig.Id, e.Message);
        ircConnection.Connected += (s, e) => OnConnectionStateChanged(serverConfig.Id, ircServer.State);
        ircConnection.Disconnected += (s, e) => OnConnectionStateChanged(serverConfig.Id, ircServer.State);

        return botConnection;
    }

    private void OnMessageReceived(string serverId, ParsedIrcMessage message)
    {
        MessageReceived?.Invoke(this, new IrcMessageEventArgs(serverId, message));
    }

    private void OnConnectionStateChanged(string serverId, ConnectionState state)
    {
        _logger.Information("Connection {Server} state changed to {State}", serverId, state);
        ConnectionStateChanged?.Invoke(this, new ConnectionStateEventArgs(serverId, state));
    }

    /// <summary>
    /// Connects to a server with automatic retry.
    /// </summary>
    private async Task ConnectWithRetryAsync(string serverId, CancellationToken ct)
    {
        BotConnection? connection;
        lock (_connectionsLock)
        {
            _connections.TryGetValue(serverId, out connection);
        }

        if (connection == null)
        {
            _logger.Warning("Server not found: {Server}", serverId);
            return;
        }

        try
        {
            await connection.Connection.ConnectAsync(ct);
            _logger.Information("Connected to {Server}", serverId);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to connect to {Server}", serverId);
        }
    }

    /// <summary>
    /// Gets the status of all connections.
    /// </summary>
    public List<ConnectionStatus> GetConnectionStatus()
    {
        lock (_connectionsLock)
        {
            return _connections.Values.Select(c => new ConnectionStatus
            {
                Id = c.Id,
                Host = c.Config.Address,
                Port = c.Config.Port,
                Nickname = c.Connection.CurrentNickname,
                State = c.Server.State.ToString(),
                Channels = c.Server.Channels.Select(ch => ch.Name).ToList()
            }).ToList();
        }
    }

    /// <summary>
    /// Gets channels for a connection.
    /// </summary>
    public Task<List<ChannelInfo>> GetChannelsAsync(string serverId)
    {
        lock (_connectionsLock)
        {
            if (_connections.TryGetValue(serverId, out var connection))
            {
                var channels = connection.Server.Channels.Select(c => new ChannelInfo
                {
                    Name = c.Name,
                    Topic = c.Topic ?? "",
                    UserCount = c.Users.Count
                }).ToList();
                
                return Task.FromResult(channels);
            }
        }

        return Task.FromResult(new List<ChannelInfo>());
    }

    /// <summary>
    /// Gets users in a channel.
    /// </summary>
    public Task<List<UserInfo>> GetUsersAsync(string serverId, string channelName)
    {
        lock (_connectionsLock)
        {
            if (_connections.TryGetValue(serverId, out var connection))
            {
                var channel = connection.Server.Channels.FirstOrDefault(c => 
                    c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
                
                if (channel != null)
                {
                    var users = channel.Users.Select(u => new UserInfo
                    {
                        Nickname = u.Nickname,
                        IsOp = u.Mode == UserMode.Operator || u.Mode == UserMode.Admin || u.Mode == UserMode.Owner,
                        IsVoice = u.Mode == UserMode.Voice
                    }).ToList();
                    
                    return Task.FromResult(users);
                }
            }
        }

        return Task.FromResult(new List<UserInfo>());
    }

    /// <summary>
    /// Connects to a server.
    /// </summary>
    public async Task ConnectAsync(string serverId)
    {
        BotConnection? connection;
        lock (_connectionsLock)
        {
            _connections.TryGetValue(serverId, out connection);
        }

        if (connection == null)
            throw new InvalidOperationException($"Server not found: {serverId}");

        await connection.Connection.ConnectAsync();
    }

    /// <summary>
    /// Disconnects from a server.
    /// </summary>
    public async Task DisconnectAsync(string serverId, string? quitMessage = null)
    {
        BotConnection? connection;
        lock (_connectionsLock)
        {
            _connections.TryGetValue(serverId, out connection);
        }

        if (connection == null)
            throw new InvalidOperationException($"Server not found: {serverId}");

        await connection.Connection.DisconnectAsync(quitMessage ?? "Disconnecting");
    }

    /// <summary>
    /// Joins a channel.
    /// </summary>
    public async Task JoinChannelAsync(string serverId, string channel, string? key = null)
    {
        BotConnection? connection;
        lock (_connectionsLock)
        {
            _connections.TryGetValue(serverId, out connection);
        }

        if (connection == null)
            throw new InvalidOperationException($"Server not found: {serverId}");

        await connection.Connection.JoinChannelAsync(channel, key);
    }

    /// <summary>
    /// Parts a channel.
    /// </summary>
    public async Task PartChannelAsync(string serverId, string channel, string? reason = null)
    {
        BotConnection? connection;
        lock (_connectionsLock)
        {
            _connections.TryGetValue(serverId, out connection);
        }

        if (connection == null)
            throw new InvalidOperationException($"Server not found: {serverId}");

        await connection.Connection.PartChannelAsync(channel, reason);
    }

    /// <summary>
    /// Sends a message.
    /// </summary>
    public async Task SendMessageAsync(string serverId, string target, string message)
    {
        BotConnection? connection;
        lock (_connectionsLock)
        {
            _connections.TryGetValue(serverId, out connection);
        }

        if (connection == null)
            throw new InvalidOperationException($"Server not found: {serverId}");

        await connection.Connection.SendMessageAsync(target, message);
    }

    /// <summary>
    /// Sends a raw IRC command.
    /// </summary>
    public async Task SendRawAsync(string serverId, string command)
    {
        BotConnection? connection;
        lock (_connectionsLock)
        {
            _connections.TryGetValue(serverId, out connection);
        }

        if (connection == null)
            throw new InvalidOperationException($"Server not found: {serverId}");

        await connection.Connection.SendRawAsync(command);
    }

    /// <summary>
    /// Shuts down all connections.
    /// </summary>
    public async Task ShutdownAsync(string quitMessage = "Agent shutting down")
    {
        List<BotConnection> connections;
        lock (_connectionsLock)
        {
            connections = _connections.Values.ToList();
        }

        foreach (var connection in connections)
        {
            try
            {
                await connection.Connection.DisconnectAsync(quitMessage);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error disconnecting from {Server}", connection.Id);
            }
        }
    }
}

/// <summary>
/// Represents an IRC bot connection.
/// </summary>
internal class BotConnection
{
    public required string Id { get; set; }
    public required IrcServerConfiguration Config { get; set; }
    public required IrcConnection Connection { get; set; }
    public required IrcServer Server { get; set; }
}

/// <summary>
/// Event args for IRC messages.
/// </summary>
public class IrcMessageEventArgs : EventArgs
{
    public string ServerId { get; }
    public ParsedIrcMessage Message { get; }

    public IrcMessageEventArgs(string serverId, ParsedIrcMessage message)
    {
        ServerId = serverId;
        Message = message;
    }
}

/// <summary>
/// Event args for connection state changes.
/// </summary>
public class ConnectionStateEventArgs : EventArgs
{
    public string ServerId { get; }
    public ConnectionState State { get; }

    public ConnectionStateEventArgs(string serverId, ConnectionState state)
    {
        ServerId = serverId;
        State = state;
    }
}

/// <summary>
/// Connection status DTO.
/// </summary>
public class ConnectionStatus
{
    public string Id { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Nickname { get; set; } = "";
    public string State { get; set; } = "";
    public List<string> Channels { get; set; } = new();
}

/// <summary>
/// Channel info DTO.
/// </summary>
public class ChannelInfo
{
    public string Name { get; set; } = "";
    public string Topic { get; set; } = "";
    public int UserCount { get; set; }
}

/// <summary>
/// User info DTO.
/// </summary>
public class UserInfo
{
    public string Nickname { get; set; } = "";
    public bool IsOp { get; set; }
    public bool IsVoice { get; set; }
}
