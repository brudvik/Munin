using Serilog;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Munin.Core.Services;

/// <summary>
/// Implements an Ident server (RFC 1413) for IRC authentication.
/// </summary>
/// <remarks>
/// <para>The Ident Protocol provides a means to determine the identity of a user of a
/// particular TCP connection. IRC servers often query this to verify users.</para>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Listens on configurable port (default 113)</description></item>
///   <item><description>Responds to ident queries with configurable username</description></item>
///   <item><description>Supports multiple concurrent connections</description></item>
///   <item><description>Configurable OS type (UNIX, WIN32, OTHER)</description></item>
///   <item><description>Optional hidden user mode for privacy</description></item>
/// </list>
/// </remarks>
public class IdentServer : IDisposable
{
    private readonly ILogger _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;
    
    /// <summary>
    /// Active connection lookups: maps (localPort, remotePort) to username.
    /// </summary>
    private readonly ConcurrentDictionary<(int LocalPort, int RemotePort), string> _activeConnections = new();

    /// <summary>
    /// Gets or sets the default username to return for ident queries.
    /// </summary>
    public string DefaultUsername { get; set; } = Environment.UserName;

    /// <summary>
    /// Gets or sets the operating system identifier to return.
    /// Valid values: UNIX, WIN32, OTHER
    /// </summary>
    public string OperatingSystem { get; set; } = "WIN32";

    /// <summary>
    /// Gets or sets the port to listen on (default 113).
    /// </summary>
    public int Port { get; set; } = 113;

    /// <summary>
    /// Gets or sets whether the server is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to hide the username (return HIDDEN-USER error).
    /// </summary>
    public bool HideUser { get; set; } = false;

    /// <summary>
    /// Gets or sets the idle timeout in seconds before closing connections.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets whether the server is currently running.
    /// </summary>
    public bool IsRunning => _listener != null;

    /// <summary>
    /// Raised when an ident query is received.
    /// </summary>
    public event EventHandler<IdentQueryEventArgs>? QueryReceived;

    /// <summary>
    /// Initializes a new instance of the IdentServer.
    /// </summary>
    public IdentServer()
    {
        _logger = SerilogConfig.ForContext<IdentServer>();
    }

    /// <summary>
    /// Registers an active IRC connection for ident response.
    /// </summary>
    /// <param name="localPort">The local port of the IRC connection.</param>
    /// <param name="remotePort">The remote port of the IRC connection (usually 6667/6697).</param>
    /// <param name="username">The username to return for this connection.</param>
    public void RegisterConnection(int localPort, int remotePort, string username)
    {
        _activeConnections[(localPort, remotePort)] = username;
        _logger.Debug("Registered ident connection: {LocalPort},{RemotePort} = {Username}", 
            localPort, remotePort, username);
    }

    /// <summary>
    /// Unregisters an IRC connection.
    /// </summary>
    /// <param name="localPort">The local port of the IRC connection.</param>
    /// <param name="remotePort">The remote port of the IRC connection.</param>
    public void UnregisterConnection(int localPort, int remotePort)
    {
        _activeConnections.TryRemove((localPort, remotePort), out _);
        _logger.Debug("Unregistered ident connection: {LocalPort},{RemotePort}", localPort, remotePort);
    }

    /// <summary>
    /// Starts the ident server.
    /// </summary>
    /// <returns>True if the server started successfully, false otherwise.</returns>
    public bool Start()
    {
        if (!IsEnabled)
        {
            _logger.Information("Ident server is disabled");
            return false;
        }

        if (_listener != null)
        {
            _logger.Warning("Ident server is already running");
            return true;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            
            _cts = new CancellationTokenSource();
            _acceptTask = AcceptConnectionsAsync(_cts.Token);
            
            _logger.Information("Ident server started on port {Port}", Port);
            return true;
        }
        catch (SocketException ex)
        {
            _logger.Error(ex, "Failed to start ident server on port {Port}. Port may be in use or require admin rights.", Port);
            _listener = null;
            return false;
        }
    }

    /// <summary>
    /// Stops the ident server.
    /// </summary>
    public void Stop()
    {
        if (_listener == null)
            return;

        _cts?.Cancel();
        
        try
        {
            _listener.Stop();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error stopping ident server listener");
        }

        _listener = null;
        _logger.Information("Ident server stopped");
    }

    /// <summary>
    /// Accepts incoming connections asynchronously.
    /// </summary>
    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error accepting ident connection");
            }
        }
    }

    /// <summary>
    /// Handles a single client connection.
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remoteEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
        _logger.Debug("Ident connection from {RemoteAddress}", remoteEndpoint?.Address);

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(IdleTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            // Read query line
            var line = await reader.ReadLineAsync(linkedCts.Token);
            if (string.IsNullOrEmpty(line))
            {
                _logger.Debug("Empty ident query received");
                return;
            }

            _logger.Debug("Ident query: {Query}", line);

            // Parse query: <port-on-server> , <port-on-client>
            var response = ProcessQuery(line.Trim(), remoteEndpoint);
            
            _logger.Debug("Ident response: {Response}", response);
            await writer.WriteLineAsync(response);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Ident connection timed out");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error handling ident client");
        }
        finally
        {
            client.Close();
        }
    }

    /// <summary>
    /// Processes an ident query and returns the appropriate response.
    /// </summary>
    /// <param name="query">The query string in format "localPort,remotePort".</param>
    /// <param name="remoteEndpoint">The remote endpoint of the ident query.</param>
    /// <returns>The response string.</returns>
    private string ProcessQuery(string query, IPEndPoint? remoteEndpoint)
    {
        // Parse port pair
        var parts = query.Split(',');
        if (parts.Length != 2)
        {
            return $"{query} : ERROR : INVALID-PORT";
        }

        // The query format is: <port-on-server> , <port-on-client>
        // For IRC: server queries about connection from client
        // port-on-server = remote IRC port (e.g., 6667)
        // port-on-client = local client port
        if (!int.TryParse(parts[0].Trim(), out var serverPort) || 
            !int.TryParse(parts[1].Trim(), out var clientPort))
        {
            return $"{query} : ERROR : INVALID-PORT";
        }

        // Validate port range (1-65535)
        if (serverPort < 1 || serverPort > 65535 || clientPort < 1 || clientPort > 65535)
        {
            return $"{serverPort} , {clientPort} : ERROR : INVALID-PORT";
        }

        // Raise query event
        var eventArgs = new IdentQueryEventArgs(serverPort, clientPort, remoteEndpoint?.Address);
        QueryReceived?.Invoke(this, eventArgs);

        // Check if user should be hidden
        if (HideUser)
        {
            return $"{serverPort} , {clientPort} : ERROR : HIDDEN-USER";
        }

        // Look up registered connection
        // The IRC server asks about its port (serverPort) and our local port (clientPort)
        // We registered with (localPort, remotePort) = (clientPort, serverPort)
        if (_activeConnections.TryGetValue((clientPort, serverPort), out var username))
        {
            return $"{serverPort} , {clientPort} : USERID : {OperatingSystem} : {username}";
        }

        // No specific connection registered, use default
        return $"{serverPort} , {clientPort} : USERID : {OperatingSystem} : {DefaultUsername}";
    }

    /// <summary>
    /// Disposes the ident server.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts?.Dispose();
        _activeConnections.Clear();

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Event arguments for ident queries.
/// </summary>
public class IdentQueryEventArgs : EventArgs
{
    /// <summary>
    /// Gets the server port being queried.
    /// </summary>
    public int ServerPort { get; }

    /// <summary>
    /// Gets the client port being queried.
    /// </summary>
    public int ClientPort { get; }

    /// <summary>
    /// Gets the IP address of the querying host.
    /// </summary>
    public IPAddress? QueryingHost { get; }

    /// <summary>
    /// Gets the timestamp of the query.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of IdentQueryEventArgs.
    /// </summary>
    public IdentQueryEventArgs(int serverPort, int clientPort, IPAddress? queryingHost)
    {
        ServerPort = serverPort;
        ClientPort = clientPort;
        QueryingHost = queryingHost;
        Timestamp = DateTime.Now;
    }
}
