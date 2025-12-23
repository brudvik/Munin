using System.Net;
using System.Net.Sockets;
using System.Text;
using IrcClient.Core.Models;

namespace IrcClient.Core.Services;

/// <summary>
/// Provides proxy connection support for IRC connections.
/// Supports SOCKS4, SOCKS5, and HTTP CONNECT proxies.
/// </summary>
/// <remarks>
/// <para>Proxy support allows connecting to IRC servers through intermediary servers,
/// useful for privacy, bypassing network restrictions, or when direct connections are blocked.</para>
/// <para>Supported proxy types:</para>
/// <list type="bullet">
///   <item><description>SOCKS4 - Basic proxy, no authentication, IPv4 only</description></item>
///   <item><description>SOCKS5 - Full-featured proxy with authentication and domain resolution</description></item>
///   <item><description>HTTP CONNECT - HTTP tunneling proxy</description></item>
/// </list>
/// </remarks>
public class ProxyConnector
{
    /// <summary>Gets or sets the proxy type.</summary>
    public ProxyType Type { get; set; } = ProxyType.None;

    /// <summary>Gets or sets the proxy server hostname or IP address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the proxy server port.</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the proxy authentication username (SOCKS5/HTTP only).</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the proxy authentication password (SOCKS5/HTTP only).</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Creates a socket connected through the proxy to the target host.
    /// </summary>
    /// <param name="targetHost">The destination server hostname.</param>
    /// <param name="targetPort">The destination server port.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connected socket tunneled through the proxy.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no proxy is configured or connection fails.</exception>
    public async Task<Socket> ConnectAsync(string targetHost, int targetPort, CancellationToken ct = default)
    {
        if (Type == ProxyType.None)
        {
            throw new InvalidOperationException("No proxy configured");
        }

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        try
        {
            // Connect to proxy
            await socket.ConnectAsync(Host, Port, ct);

            // Perform proxy handshake
            switch (Type)
            {
                case ProxyType.SOCKS4:
                    await ConnectSocks4Async(socket, targetHost, targetPort, ct);
                    break;
                case ProxyType.SOCKS5:
                    await ConnectSocks5Async(socket, targetHost, targetPort, ct);
                    break;
                case ProxyType.HTTP:
                    await ConnectHttpAsync(socket, targetHost, targetPort, ct);
                    break;
            }

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task ConnectSocks4Async(Socket socket, string targetHost, int targetPort, CancellationToken ct)
    {
        // Resolve hostname to IP (SOCKS4 doesn't support hostnames directly, SOCKS4a does)
        var addresses = await Dns.GetHostAddressesAsync(targetHost, ct);
        var ipAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new InvalidOperationException($"Could not resolve {targetHost} to IPv4");

        var request = new byte[9 + (Username?.Length ?? 0)];
        request[0] = 0x04; // SOCKS4 version
        request[1] = 0x01; // CONNECT command
        request[2] = (byte)(targetPort >> 8);
        request[3] = (byte)(targetPort & 0xFF);
        var ipBytes = ipAddress.GetAddressBytes();
        Array.Copy(ipBytes, 0, request, 4, 4);
        // User ID (empty or username)
        if (!string.IsNullOrEmpty(Username))
        {
            Encoding.ASCII.GetBytes(Username, 0, Username.Length, request, 8);
        }
        request[^1] = 0x00; // Null terminator

        await socket.SendAsync(request, SocketFlags.None, ct);

        var response = new byte[8];
        var received = await socket.ReceiveAsync(response, SocketFlags.None, ct);
        
        if (received < 8 || response[1] != 90)
        {
            var errorMessage = response[1] switch
            {
                91 => "Request rejected or failed",
                92 => "Request rejected: SOCKS server cannot connect to identd",
                93 => "Request rejected: identd reports different user-id",
                _ => $"Unknown SOCKS4 error: {response[1]}"
            };
            throw new InvalidOperationException($"SOCKS4 proxy error: {errorMessage}");
        }
    }

    private async Task ConnectSocks5Async(Socket socket, string targetHost, int targetPort, CancellationToken ct)
    {
        // Greeting
        var hasAuth = !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
        var greeting = hasAuth 
            ? new byte[] { 0x05, 0x02, 0x00, 0x02 } // Version 5, 2 methods: no-auth, username/password
            : new byte[] { 0x05, 0x01, 0x00 };      // Version 5, 1 method: no-auth

        await socket.SendAsync(greeting, SocketFlags.None, ct);

        var greetResponse = new byte[2];
        await socket.ReceiveAsync(greetResponse, SocketFlags.None, ct);

        if (greetResponse[0] != 0x05)
        {
            throw new InvalidOperationException("Invalid SOCKS5 version in response");
        }

        // Handle authentication
        if (greetResponse[1] == 0x02 && hasAuth)
        {
            // Username/password authentication (RFC 1929)
            var userBytes = Encoding.UTF8.GetBytes(Username!);
            var passBytes = Encoding.UTF8.GetBytes(Password!);
            var authRequest = new byte[3 + userBytes.Length + passBytes.Length];
            authRequest[0] = 0x01; // Version
            authRequest[1] = (byte)userBytes.Length;
            Array.Copy(userBytes, 0, authRequest, 2, userBytes.Length);
            authRequest[2 + userBytes.Length] = (byte)passBytes.Length;
            Array.Copy(passBytes, 0, authRequest, 3 + userBytes.Length, passBytes.Length);

            await socket.SendAsync(authRequest, SocketFlags.None, ct);

            var authResponse = new byte[2];
            await socket.ReceiveAsync(authResponse, SocketFlags.None, ct);

            if (authResponse[1] != 0x00)
            {
                throw new InvalidOperationException("SOCKS5 authentication failed");
            }
        }
        else if (greetResponse[1] == 0xFF)
        {
            throw new InvalidOperationException("SOCKS5 proxy requires authentication");
        }
        else if (greetResponse[1] != 0x00)
        {
            throw new InvalidOperationException($"Unsupported SOCKS5 auth method: {greetResponse[1]}");
        }

        // Connection request
        var hostBytes = Encoding.UTF8.GetBytes(targetHost);
        var request = new byte[7 + hostBytes.Length];
        request[0] = 0x05; // Version
        request[1] = 0x01; // CONNECT
        request[2] = 0x00; // Reserved
        request[3] = 0x03; // Domain name
        request[4] = (byte)hostBytes.Length;
        Array.Copy(hostBytes, 0, request, 5, hostBytes.Length);
        request[5 + hostBytes.Length] = (byte)(targetPort >> 8);
        request[6 + hostBytes.Length] = (byte)(targetPort & 0xFF);

        await socket.SendAsync(request, SocketFlags.None, ct);

        // Response: version, status, reserved, address type, address, port
        var response = new byte[10];
        var received = await socket.ReceiveAsync(response, SocketFlags.None, ct);

        if (received < 2 || response[1] != 0x00)
        {
            var errorMessage = response[1] switch
            {
                0x01 => "General SOCKS server failure",
                0x02 => "Connection not allowed by ruleset",
                0x03 => "Network unreachable",
                0x04 => "Host unreachable",
                0x05 => "Connection refused",
                0x06 => "TTL expired",
                0x07 => "Command not supported",
                0x08 => "Address type not supported",
                _ => $"Unknown error: {response[1]}"
            };
            throw new InvalidOperationException($"SOCKS5 proxy error: {errorMessage}");
        }

        // Read remaining response bytes based on address type
        if (response[3] == 0x03) // Domain name
        {
            var domainLen = response[4];
            var remaining = new byte[domainLen + 1]; // domain + port (2 bytes, but we got 1 already)
            await socket.ReceiveAsync(remaining, SocketFlags.None, ct);
        }
        else if (response[3] == 0x04) // IPv6
        {
            var remaining = new byte[12]; // 16 bytes total, we got 4
            await socket.ReceiveAsync(remaining, SocketFlags.None, ct);
        }
        // IPv4 (0x01) is already fully read in initial 10 bytes
    }

    private async Task ConnectHttpAsync(Socket socket, string targetHost, int targetPort, CancellationToken ct)
    {
        var request = $"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\n" +
                      $"Host: {targetHost}:{targetPort}\r\n";

        if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
            request += $"Proxy-Authorization: Basic {credentials}\r\n";
        }

        request += "\r\n";

        await socket.SendAsync(Encoding.UTF8.GetBytes(request), SocketFlags.None, ct);

        // Read response
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(buffer, SocketFlags.None, ct);
        var response = Encoding.UTF8.GetString(buffer, 0, received);

        // Check for 200 OK
        if (!response.StartsWith("HTTP/1.") || !response.Contains(" 200 "))
        {
            var firstLine = response.Split('\n').FirstOrDefault()?.Trim() ?? "Unknown error";
            throw new InvalidOperationException($"HTTP proxy error: {firstLine}");
        }
    }
}
