using IrcClient.Core.Events;
using IrcClient.Core.Models;
using Serilog;

namespace IrcClient.Core.Services;

/// <summary>
/// Manages multiple IRC server connections.
/// </summary>
/// <remarks>
/// <para>Acts as a central hub for managing multiple IRC connections, aggregating events from all
/// connected servers into unified event handlers.</para>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Add/remove server connections dynamically</description></item>
///   <item><description>Connect/disconnect all servers at once</description></item>
///   <item><description>Aggregated events for UI binding</description></item>
/// </list>
/// </remarks>
public class IrcClientManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, IrcConnection> _connections = new();
    private bool _disposed;

    /// <summary>
    /// Gets all active connections, keyed by server ID.
    /// </summary>
    public IReadOnlyDictionary<string, IrcConnection> Connections => _connections;

    // Aggregate events from all connections
    public event EventHandler<IrcConnectionEventArgs>? ServerConnected;
    public event EventHandler<IrcConnectionEventArgs>? ServerDisconnected;
    public event EventHandler<IrcChannelMessageEventArgs>? ChannelMessage;
    public event EventHandler<IrcPrivateMessageEventArgs>? PrivateMessage;
    public event EventHandler<IrcChannelEventArgs>? ChannelJoined;
    public event EventHandler<IrcChannelEventArgs>? ChannelParted;
    public event EventHandler<IrcUserEventArgs>? UserJoined;
    public event EventHandler<IrcUserEventArgs>? UserParted;
    public event EventHandler<IrcUserEventArgs>? UserQuit;
    public event EventHandler<IrcNickChangedEventArgs>? NickChanged;
    public event EventHandler<IrcErrorEventArgs>? Error;
    public event EventHandler<IrcRawMessageEventArgs>? RawMessage;
    public event EventHandler<IrcChannelEventArgs>? TopicChanged;
    public event EventHandler<IrcChannelEventArgs>? UserListUpdated;
    public event EventHandler<IrcServerMessageEventArgs>? ServerMessage;
    public event EventHandler<IrcChannelListEventArgs>? ChannelListReceived;
    public event EventHandler<IrcServerEventArgs>? ChannelListComplete;
    public event EventHandler<IrcReconnectEventArgs>? Reconnecting;
    public event EventHandler<IrcWhoisEventArgs>? WhoisReceived;
    public event EventHandler<(IrcServer Server, int LatencyMs)>? LatencyUpdated;

    public IrcClientManager()
    {
        _logger = SerilogConfig.ForContext<IrcClientManager>();
    }

    /// <summary>
    /// Adds a new server and creates an IrcConnection for it.
    /// </summary>
    /// <param name="server">The server configuration to add.</param>
    /// <returns>The created connection instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a server with the same ID already exists.</exception>
    public IrcConnection AddServer(IrcServer server)
    {
        if (_connections.ContainsKey(server.Id))
        {
            throw new InvalidOperationException($"Server with ID {server.Id} already exists");
        }

        var connection = new IrcConnection(server);
        
        // Subscribe to events
        connection.Connected += (s, e) => ServerConnected?.Invoke(this, e);
        connection.Disconnected += (s, e) => ServerDisconnected?.Invoke(this, e);
        connection.ChannelMessage += (s, e) => ChannelMessage?.Invoke(this, e);
        connection.PrivateMessage += (s, e) => PrivateMessage?.Invoke(this, e);
        connection.ChannelJoined += (s, e) => ChannelJoined?.Invoke(this, e);
        connection.ChannelParted += (s, e) => ChannelParted?.Invoke(this, e);
        connection.UserJoined += (s, e) => UserJoined?.Invoke(this, e);
        connection.UserParted += (s, e) => UserParted?.Invoke(this, e);
        connection.UserQuit += (s, e) => UserQuit?.Invoke(this, e);
        connection.NickChanged += (s, e) => NickChanged?.Invoke(this, e);
        connection.Error += (s, e) => Error?.Invoke(this, e);
        connection.RawMessageReceived += (s, e) => RawMessage?.Invoke(this, e);
        connection.TopicChanged += (s, e) => TopicChanged?.Invoke(this, e);
        connection.UserListUpdated += (s, e) => UserListUpdated?.Invoke(this, e);
        connection.ServerMessage += (s, e) => ServerMessage?.Invoke(this, e);
        connection.ChannelListReceived += (s, e) => ChannelListReceived?.Invoke(this, e);
        connection.ChannelListComplete += (s, e) => ChannelListComplete?.Invoke(this, new IrcServerEventArgs(server));
        connection.Reconnecting += (s, e) => Reconnecting?.Invoke(this, e);
        connection.WhoisReceived += (s, e) => WhoisReceived?.Invoke(this, e);
        connection.LatencyUpdated += (s, latency) => LatencyUpdated?.Invoke(this, (server, latency));

        _connections[server.Id] = connection;
        _logger.Information("Added server {Name} ({Host})", server.Name, server.Hostname);

        return connection;
    }

    /// <summary>
    /// Disconnects and removes a server.
    /// </summary>
    /// <param name="serverId">The unique identifier of the server to remove.</param>
    public async Task RemoveServerAsync(string serverId)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            return;
        }

        await connection.DisconnectAsync();
        connection.Dispose();
        _connections.Remove(serverId);
        _logger.Information("Removed server {Id}", serverId);
    }

    /// <summary>
    /// Gets a connection by server ID.
    /// </summary>
    /// <param name="serverId">The unique identifier of the server.</param>
    /// <returns>The connection if found; otherwise, null.</returns>
    public IrcConnection? GetConnection(string serverId)
    {
        return _connections.TryGetValue(serverId, out var connection) ? connection : null;
    }

    /// <summary>
    /// Connects to all servers that have AutoConnect enabled.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _connections.Values
            .Where(c => c.Server.AutoConnect)
            .Select(c => c.ConnectAsync(cancellationToken));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Disconnects from all connected servers.
    /// </summary>
    /// <param name="quitMessage">Optional quit message to send.</param>
    public async Task DisconnectAllAsync(string? quitMessage = null)
    {
        var tasks = _connections.Values
            .Where(c => c.IsConnected)
            .Select(c => c.DisconnectAsync(quitMessage));

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
        _connections.Clear();

        GC.SuppressFinalize(this);
    }
}
