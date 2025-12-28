using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace MuninRelay;

/// <summary>
/// Handles individual client connections and relays data to target IRC servers.
/// </summary>
public class RelayConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly RelayConfiguration _config;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private Stream _clientStream = null!;
    private TcpClient? _targetClient;
    private Stream? _targetStream;
    private string? _targetHostname;
    private int _targetPort;

    /// <summary>
    /// Unique identifier for this connection.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Remote endpoint of the client.
    /// </summary>
    public EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;

    /// <summary>
    /// Whether the connection is active.
    /// </summary>
    public bool IsActive => _client.Connected && !_cts.IsCancellationRequested;

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    public event EventHandler? Closed;

    public RelayConnection(TcpClient client, RelayConfiguration config)
    {
        _client = client;
        _config = config;
        _logger = Log.ForContext<RelayConnection>()
            .ForContext("ConnectionId", Id.ToString()[..8]);
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Starts handling the client connection.
    /// </summary>
    public async Task HandleAsync()
    {
        try
        {
            _logger.Information("New connection from {Endpoint}", RemoteEndPoint);

            // Set up SSL if configured
            if (!string.IsNullOrEmpty(_config.CertificatePath))
            {
                await SetupSslAsync();
            }
            else
            {
                _clientStream = _client.GetStream();
            }

            // Authentication phase
            if (!await AuthenticateClientAsync())
            {
                _logger.Warning("Authentication failed for {Endpoint}", RemoteEndPoint);
                return;
            }

            _logger.Information("Client authenticated successfully");

            // Wait for connect request
            if (!await HandleConnectRequestAsync())
            {
                return;
            }

            // Start bidirectional relay
            await RelayDataAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Connection cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling connection");
        }
        finally
        {
            Closed?.Invoke(this, EventArgs.Empty);
            Dispose();
        }
    }

    /// <summary>
    /// Sets up SSL/TLS for the client connection.
    /// </summary>
    private async Task SetupSslAsync()
    {
        if (string.IsNullOrEmpty(_config.CertificatePath))
            throw new InvalidOperationException("Certificate path is not configured");

        var certificate = new X509Certificate2(_config.CertificatePath, _config.CertificatePassword);
        var sslStream = new SslStream(_client.GetStream(), false);

        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificateRequired = false
        });

        _clientStream = sslStream;
        _logger.Debug("SSL/TLS established with client");
    }

    /// <summary>
    /// Handles the authentication phase.
    /// </summary>
    private async Task<bool> AuthenticateClientAsync()
    {
        try
        {
            var buffer = new byte[512];
            var bytesRead = await _clientStream.ReadAsync(buffer, _cts.Token);

            if (bytesRead == 0)
            {
                return false;
            }

            var message = buffer[..bytesRead];

            if (!RelayProtocol.VerifyAuthRequest(message, _config.AuthToken, out var errorMessage))
            {
                _logger.Warning("Auth failed: {Error}", errorMessage);
                var failResponse = RelayProtocol.CreateAuthResponse(false, errorMessage);
                await _clientStream.WriteAsync(failResponse, _cts.Token);
                return false;
            }

            var successResponse = RelayProtocol.CreateAuthResponse(true, "Authenticated");
            await _clientStream.WriteAsync(successResponse, _cts.Token);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Authentication error");
            return false;
        }
    }

    /// <summary>
    /// Handles the connect request to a target IRC server.
    /// </summary>
    private async Task<bool> HandleConnectRequestAsync()
    {
        try
        {
            var buffer = new byte[512];
            var bytesRead = await _clientStream.ReadAsync(buffer, _cts.Token);

            if (bytesRead == 0)
            {
                return false;
            }

            var message = buffer[..bytesRead];
            var connectRequest = RelayProtocol.ParseConnectRequest(message);

            if (connectRequest == null)
            {
                _logger.Warning("Invalid connect request");
                var failResponse = RelayProtocol.CreateConnectResponse(false, "Invalid connect request");
                await _clientStream.WriteAsync(failResponse, _cts.Token);
                return false;
            }

            var (hostname, port, useSsl) = connectRequest.Value;
            _targetHostname = hostname;
            _targetPort = port;

            // Validate against allowed servers
            if (_config.AllowedServers.Count > 0)
            {
                var isAllowed = _config.AllowedServers.Any(s =>
                    s.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase) &&
                    s.Port == port);

                if (!isAllowed)
                {
                    _logger.Warning("Server not in allowed list: {Host}:{Port}", hostname, port);
                    var failResponse = RelayProtocol.CreateConnectResponse(false, "Server not in allowed list");
                    await _clientStream.WriteAsync(failResponse, _cts.Token);
                    return false;
                }
            }

            _logger.Information("Connecting to target: {Host}:{Port} (SSL: {Ssl})", hostname, port, useSsl);

            // Connect to target IRC server
            _targetClient = new TcpClient();
            await _targetClient.ConnectAsync(hostname, port, _cts.Token);

            if (useSsl)
            {
                var sslStream = new SslStream(_targetClient.GetStream(), false,
                    (sender, cert, chain, errors) =>
                    {
                        // In production, you might want to validate the certificate
                        if (errors != SslPolicyErrors.None)
                        {
                            _logger.Warning("Target server certificate errors: {Errors}", errors);
                        }
                        return true; // Accept for now, can be made configurable
                    });

                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = hostname,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });

                _targetStream = sslStream;
            }
            else
            {
                _targetStream = _targetClient.GetStream();
            }

            var successResponse = RelayProtocol.CreateConnectResponse(true, $"Connected to {hostname}:{port}");
            await _clientStream.WriteAsync(successResponse, _cts.Token);

            _logger.Information("Successfully connected to target");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to connect to target");
            var failResponse = RelayProtocol.CreateConnectResponse(false, $"Connection failed: {ex.Message}");
            await _clientStream.WriteAsync(failResponse, _cts.Token);
            return false;
        }
    }

    /// <summary>
    /// Relays data bidirectionally between client and target server.
    /// </summary>
    private async Task RelayDataAsync()
    {
        if (_targetStream == null)
            return;

        _logger.Debug("Starting bidirectional relay");

        // Start two tasks for bidirectional communication
        var clientToTarget = RelayDirectionAsync(_clientStream, _targetStream, "Client->Target");
        var targetToClient = RelayDirectionAsync(_targetStream, _clientStream, "Target->Client");

        // Wait for either direction to complete (usually means connection closed)
        await Task.WhenAny(clientToTarget, targetToClient);

        _logger.Debug("Relay ended");
        _cts.Cancel();
    }

    /// <summary>
    /// Relays data in one direction.
    /// </summary>
    private async Task RelayDirectionAsync(Stream source, Stream destination, string direction)
    {
        var buffer = new byte[8192];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var bytesRead = await source.ReadAsync(buffer, _cts.Token);

                if (bytesRead == 0)
                {
                    _logger.Debug("{Direction}: Connection closed", direction);
                    break;
                }

                if (_config.VerboseLogging)
                {
                    _logger.Verbose("{Direction}: Relaying {Bytes} bytes", direction, bytesRead);
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), _cts.Token);
                await destination.FlushAsync(_cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (IOException ex)
        {
            _logger.Debug("{Direction}: IO error: {Message}", direction, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{Direction}: Error during relay", direction);
        }
    }

    /// <summary>
    /// Closes the connection gracefully.
    /// </summary>
    public void Close()
    {
        _cts.Cancel();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        try { _clientStream?.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
        try { _targetStream?.Dispose(); } catch { }
        try { _targetClient?.Dispose(); } catch { }

        _logger.Debug("Connection disposed");
    }
}
