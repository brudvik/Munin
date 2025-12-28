using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MuninRelay;

/// <summary>
/// Background service that listens for incoming relay connections
/// and manages the relay lifecycle.
/// </summary>
public class RelayService : BackgroundService
{
    private readonly RelayConfiguration _config;
    private readonly IpVerificationService _ipVerifier;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, RelayConnection> _connections;
    private TcpListener? _listener;
    private Timer? _ipCheckTimer;

    public RelayService(RelayConfiguration config)
    {
        _config = config;
        _ipVerifier = new IpVerificationService(config);
        _logger = Log.ForContext<RelayService>();
        _connections = new ConcurrentDictionary<Guid, RelayConnection>();

        _ipVerifier.IpChanged += OnIpChanged;
    }

    /// <summary>
    /// Starts the relay service.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("MuninRelay starting...");
        _logger.Information("Configuration: ListenPort={Port}, MaxConnections={Max}, IPVerification={IpCheck}",
            _config.ListenPort, _config.MaxConnections, _config.EnableIpVerification);

        // Initial IP verification
        if (_config.EnableIpVerification)
        {
            await PerformIpVerificationAsync();

            // Set up periodic IP checking
            _ipCheckTimer = new Timer(
                async _ => await PerformIpVerificationAsync(),
                null,
                TimeSpan.FromMinutes(_config.IpCheckIntervalMinutes),
                TimeSpan.FromMinutes(_config.IpCheckIntervalMinutes));
        }

        // Start listening
        _listener = new TcpListener(IPAddress.Any, _config.ListenPort);
        _listener.Start();

        _logger.Information("Listening on port {Port}", _config.ListenPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleNewConnectionAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Relay service stopping...");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in relay service");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>
    /// Performs IP verification and logs results.
    /// </summary>
    private async Task PerformIpVerificationAsync()
    {
        _logger.Information("Performing IP verification...");
        var result = await _ipVerifier.VerifyAsync();

        if (result.Success)
        {
            _logger.Information("IP Verification: {Result}", result);

            if (!result.CountryMatches && !string.IsNullOrEmpty(_config.ExpectedCountryCode))
            {
                _logger.Warning("WARNING: IP country does not match expected! VPN may not be active!");
            }

            if (!result.IsLikelyVpn)
            {
                _logger.Warning("WARNING: IP does not appear to be from a VPN provider");
            }
        }
        else
        {
            _logger.Error("IP Verification failed: {Error}", result.ErrorMessage);
        }
    }

    /// <summary>
    /// Handles IP change events.
    /// </summary>
    private void OnIpChanged(object? sender, IpChangedEventArgs e)
    {
        // Check if IPs are in the same subnet (VPN providers often rotate IPs within a range)
        var sameSubnet = AreIpsInSameSubnet(e.OldIp, e.NewIp, 16); // /16 = same class B network
        
        if (sameSubnet)
        {
            _logger.Information("IP rotated within same network: {Old} → {New} (normal VPN behavior)", e.OldIp, e.NewIp);
        }
        else
        {
            _logger.Warning("IP ADDRESS CHANGED! Old: {Old}, New: {New}", e.OldIp, e.NewIp);
            _logger.Warning("VPN connection may have dropped! Consider disconnecting all clients.");
        }

        // Optionally disconnect all clients on IP change
        // foreach (var conn in _connections.Values)
        // {
        //     conn.Close();
        // }
    }

    /// <summary>
    /// Checks if two IP addresses are in the same subnet.
    /// Used to detect VPN IP rotation vs actual VPN disconnection.
    /// </summary>
    /// <param name="ip1">First IP address.</param>
    /// <param name="ip2">Second IP address.</param>
    /// <param name="prefixLength">CIDR prefix length (e.g., 16 for /16).</param>
    /// <returns>True if both IPs are in the same subnet.</returns>
    private static bool AreIpsInSameSubnet(string ip1, string ip2, int prefixLength)
    {
        try
        {
            if (!IPAddress.TryParse(ip1, out var addr1) || !IPAddress.TryParse(ip2, out var addr2))
                return false;

            if (addr1.AddressFamily != addr2.AddressFamily)
                return false;

            var bytes1 = addr1.GetAddressBytes();
            var bytes2 = addr2.GetAddressBytes();

            // Create subnet mask
            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullBytes && i < bytes1.Length; i++)
            {
                if (bytes1[i] != bytes2[i])
                    return false;
            }

            if (remainingBits > 0 && fullBytes < bytes1.Length)
            {
                byte mask = (byte)(0xFF << (8 - remainingBits));
                if ((bytes1[fullBytes] & mask) != (bytes2[fullBytes] & mask))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Handles a new incoming connection.
    /// </summary>
    private async Task HandleNewConnectionAsync(TcpClient client, CancellationToken stoppingToken)
    {
        // Check connection limit
        if (_connections.Count >= _config.MaxConnections)
        {
            _logger.Warning("Connection limit reached ({Max}), rejecting connection from {Endpoint}",
                _config.MaxConnections, client.Client.RemoteEndPoint);
            client.Dispose();
            return;
        }

        var connection = new RelayConnection(client, _config);
        _connections.TryAdd(connection.Id, connection);

        connection.Closed += (s, e) =>
        {
            _connections.TryRemove(connection.Id, out _);
            _logger.Debug("Connection removed. Active connections: {Count}", _connections.Count);
        };

        _logger.Information("Active connections: {Count}/{Max}", _connections.Count, _config.MaxConnections);

        try
        {
            await connection.HandleAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling connection {Id}", connection.Id);
        }
    }

    /// <summary>
    /// Cleans up resources on shutdown.
    /// </summary>
    private async Task CleanupAsync()
    {
        _logger.Information("Cleaning up...");

        _ipCheckTimer?.Dispose();
        _listener?.Stop();

        // Close all active connections
        foreach (var conn in _connections.Values)
        {
            conn.Close();
        }

        // Wait a moment for connections to close gracefully
        await Task.Delay(500);

        _logger.Information("Cleanup complete");
    }

    /// <summary>
    /// Gets the current status of the relay service.
    /// </summary>
    public RelayStatus GetStatus()
    {
        return new RelayStatus
        {
            IsListening = _listener?.Server.IsBound ?? false,
            ListenPort = _config.ListenPort,
            ActiveConnections = _connections.Count,
            MaxConnections = _config.MaxConnections,
            CurrentIp = _ipVerifier.CurrentIp,
            CurrentCountry = _ipVerifier.CurrentCountry,
            CurrentOrganization = _ipVerifier.CurrentOrganization
        };
    }
}

/// <summary>
/// Status information for the relay service.
/// </summary>
public class RelayStatus
{
    public bool IsListening { get; set; }
    public int ListenPort { get; set; }
    public int ActiveConnections { get; set; }
    public int MaxConnections { get; set; }
    public string? CurrentIp { get; set; }
    public string? CurrentCountry { get; set; }
    public string? CurrentOrganization { get; set; }
}
