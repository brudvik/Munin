using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Munin.Core.Models;
using Serilog;

namespace Munin.Core.Services;

/// <summary>
/// Client-side connector for MuninRelay.
/// Handles connection, authentication, and protocol handshake with the relay server.
/// </summary>
/// <remarks>
/// <para>This class implements the client side of the MuninRelay protocol:</para>
/// <list type="bullet">
///   <item><description>SSL/TLS connection to relay server</description></item>
///   <item><description>HMAC-SHA256 challenge-response authentication</description></item>
///   <item><description>Connection request to target IRC server</description></item>
/// </list>
/// </remarks>
public class RelayConnector
{
    private readonly ILogger _logger = SerilogConfig.ForContext<RelayConnector>();

    // Protocol constants (must match MuninRelay)
    private static readonly byte[] MagicBytes = "MUNIN"u8.ToArray();
    private const int ProtocolVersion = 1;
    private const byte MsgAuth = 0x01;
    private const byte MsgAuthOk = 0x02;
    private const byte MsgAuthFail = 0x03;
    private const byte MsgConnect = 0x04;
    private const byte MsgConnectOk = 0x05;
    private const byte MsgConnectFail = 0x06;

    /// <summary>
    /// Connects to the IRC server through the MuninRelay.
    /// </summary>
    /// <param name="relay">Relay configuration.</param>
    /// <param name="targetHostname">Target IRC server hostname.</param>
    /// <param name="targetPort">Target IRC server port.</param>
    /// <param name="targetUseSsl">Whether the target uses SSL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connected stream to the IRC server through the relay.</returns>
    public async Task<Stream> ConnectAsync(
        RelaySettings relay,
        string targetHostname,
        int targetPort,
        bool targetUseSsl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(relay.Host))
            throw new ArgumentException("Relay host is not configured", nameof(relay));

        if (string.IsNullOrEmpty(relay.AuthToken))
            throw new ArgumentException("Relay authentication token is not configured", nameof(relay));

        _logger.Information("Connecting to MuninRelay at {Host}:{Port}", relay.Host, relay.Port);

        // Connect to relay server
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(relay.Host, relay.Port, ct);

        Stream stream = tcpClient.GetStream();

        try
        {
            // Set up SSL if configured
            if (relay.UseSsl)
            {
                var sslStream = new SslStream(
                    stream,
                    false,
                    relay.AcceptInvalidCertificates
                        ? (sender, cert, chain, errors) => true
                        : null);

                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = relay.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, ct);

                stream = sslStream;
                _logger.Debug("SSL/TLS established with relay");
            }

            // Authenticate with relay
            await AuthenticateAsync(stream, relay.AuthToken, ct);
            _logger.Information("Authenticated with MuninRelay");

            // Request connection to target IRC server
            await RequestConnectionAsync(stream, targetHostname, targetPort, targetUseSsl, ct);
            _logger.Information("Connected to {Target}:{Port} through relay", targetHostname, targetPort);

            return stream;
        }
        catch
        {
            stream.Dispose();
            tcpClient.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Performs HMAC-SHA256 challenge-response authentication with the relay.
    /// </summary>
    private async Task AuthenticateAsync(Stream stream, string authToken, CancellationToken ct)
    {
        // Generate random challenge
        var challenge = RandomNumberGenerator.GetBytes(32);

        // Create auth request message
        using var requestMs = new MemoryStream();
        using var requestWriter = new BinaryWriter(requestMs);

        requestWriter.Write(MagicBytes);
        requestWriter.Write((byte)ProtocolVersion);
        requestWriter.Write(MsgAuth);
        requestWriter.Write((byte)challenge.Length);
        requestWriter.Write(challenge);

        // Calculate HMAC-SHA256 signature
        var tokenBytes = Encoding.UTF8.GetBytes(authToken);
        using var hmac = new HMACSHA256(tokenBytes);
        var signature = hmac.ComputeHash(challenge);

        requestWriter.Write((byte)signature.Length);
        requestWriter.Write(signature);

        // Send auth request
        var requestData = requestMs.ToArray();
        await stream.WriteAsync(requestData, ct);
        await stream.FlushAsync(ct);

        // Read response
        var responseBuffer = new byte[512];
        var bytesRead = await stream.ReadAsync(responseBuffer, ct);

        if (bytesRead == 0)
            throw new IOException("Connection closed during authentication");

        // Parse response
        if (bytesRead < MagicBytes.Length + 2)
            throw new InvalidOperationException("Invalid authentication response");

        var msgType = responseBuffer[MagicBytes.Length + 1];

        if (msgType == MsgAuthFail)
        {
            var errorMessage = ParseErrorMessage(responseBuffer, bytesRead);
            throw new AuthenticationException($"Relay authentication failed: {errorMessage}");
        }

        if (msgType != MsgAuthOk)
            throw new InvalidOperationException($"Unexpected response type: {msgType}");
    }

    /// <summary>
    /// Requests connection to the target IRC server through the relay.
    /// </summary>
    private async Task RequestConnectionAsync(
        Stream stream,
        string hostname,
        int port,
        bool useSsl,
        CancellationToken ct)
    {
        // Create connect request
        using var requestMs = new MemoryStream();
        using var requestWriter = new BinaryWriter(requestMs);

        requestWriter.Write(MagicBytes);
        requestWriter.Write((byte)ProtocolVersion);
        requestWriter.Write(MsgConnect);

        var hostnameBytes = Encoding.UTF8.GetBytes(hostname);
        requestWriter.Write((byte)hostnameBytes.Length);
        requestWriter.Write(hostnameBytes);
        requestWriter.Write((ushort)port);
        requestWriter.Write(useSsl);

        // Send request
        var requestData = requestMs.ToArray();
        await stream.WriteAsync(requestData, ct);
        await stream.FlushAsync(ct);

        // Read response
        var responseBuffer = new byte[512];
        var bytesRead = await stream.ReadAsync(responseBuffer, ct);

        if (bytesRead == 0)
            throw new IOException("Connection closed during connect request");

        if (bytesRead < MagicBytes.Length + 2)
            throw new InvalidOperationException("Invalid connect response");

        var msgType = responseBuffer[MagicBytes.Length + 1];

        if (msgType == MsgConnectFail)
        {
            var errorMessage = ParseErrorMessage(responseBuffer, bytesRead);
            throw new IOException($"Failed to connect to target: {errorMessage}");
        }

        if (msgType != MsgConnectOk)
            throw new InvalidOperationException($"Unexpected response type: {msgType}");
    }

    /// <summary>
    /// Parses an error message from a response buffer.
    /// </summary>
    private static string ParseErrorMessage(byte[] buffer, int length)
    {
        try
        {
            if (length < MagicBytes.Length + 4)
                return "Unknown error";

            using var ms = new MemoryStream(buffer, 0, length);
            using var reader = new BinaryReader(ms);

            // Skip magic, version, type
            reader.ReadBytes(MagicBytes.Length + 2);

            var msgLength = reader.ReadUInt16();
            if (msgLength == 0 || msgLength > length - ms.Position)
                return "Unknown error";

            var msgBytes = reader.ReadBytes(msgLength);
            return Encoding.UTF8.GetString(msgBytes);
        }
        catch
        {
            return "Unknown error";
        }
    }
}
