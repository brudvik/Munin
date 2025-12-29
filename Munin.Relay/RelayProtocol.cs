using System.Security.Cryptography;
using System.Text;

namespace Munin.Relay;

/// <summary>
/// Protocol for communication between Munin client and MuninRelay.
/// Handles authentication and connection setup.
/// </summary>
public static class RelayProtocol
{
    // Protocol version
    public const int Version = 1;

    // Magic bytes to identify MuninRelay protocol
    public static readonly byte[] MagicBytes = "MUNIN"u8.ToArray();

    // Message types
    public const byte MsgAuth = 0x01;          // Authentication request
    public const byte MsgAuthOk = 0x02;        // Authentication successful
    public const byte MsgAuthFail = 0x03;      // Authentication failed
    public const byte MsgConnect = 0x04;       // Connect to target server
    public const byte MsgConnectOk = 0x05;     // Connection established
    public const byte MsgConnectFail = 0x06;   // Connection failed
    public const byte MsgData = 0x07;          // Raw data
    public const byte MsgPing = 0x08;          // Keep-alive ping
    public const byte MsgPong = 0x09;          // Keep-alive pong
    public const byte MsgDisconnect = 0x0A;    // Disconnect notification
    public const byte MsgIpInfo = 0x0B;        // IP information response

    /// <summary>
    /// Creates an authentication request message.
    /// Uses challenge-response with HMAC-SHA256.
    /// </summary>
    /// <param name="authToken">The secret authentication token.</param>
    /// <param name="challenge">Random challenge bytes for replay protection.</param>
    /// <returns>Authentication message bytes.</returns>
    public static byte[] CreateAuthRequest(string authToken, byte[] challenge)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(MagicBytes);
        writer.Write((byte)Version);
        writer.Write(MsgAuth);

        // Write challenge
        writer.Write((byte)challenge.Length);
        writer.Write(challenge);

        // Calculate HMAC-SHA256 of challenge using auth token
        var tokenBytes = Encoding.UTF8.GetBytes(authToken);
        using var hmac = new HMACSHA256(tokenBytes);
        var signature = hmac.ComputeHash(challenge);

        writer.Write((byte)signature.Length);
        writer.Write(signature);

        return ms.ToArray();
    }

    /// <summary>
    /// Verifies an authentication request.
    /// </summary>
    /// <param name="message">The received message.</param>
    /// <param name="authToken">The expected auth token.</param>
    /// <param name="errorMessage">Error message if verification fails.</param>
    /// <returns>True if authentication is valid.</returns>
    public static bool VerifyAuthRequest(byte[] message, string authToken, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            using var ms = new MemoryStream(message);
            using var reader = new BinaryReader(ms);

            // Verify magic bytes
            var magic = reader.ReadBytes(MagicBytes.Length);
            if (!magic.SequenceEqual(MagicBytes))
            {
                errorMessage = "Invalid protocol magic bytes";
                return false;
            }

            // Verify version
            var version = reader.ReadByte();
            if (version != Version)
            {
                errorMessage = $"Unsupported protocol version: {version}";
                return false;
            }

            // Verify message type
            var msgType = reader.ReadByte();
            if (msgType != MsgAuth)
            {
                errorMessage = $"Expected auth message, got: {msgType}";
                return false;
            }

            // Read challenge
            var challengeLen = reader.ReadByte();
            var challenge = reader.ReadBytes(challengeLen);

            // Read signature
            var sigLen = reader.ReadByte();
            var receivedSignature = reader.ReadBytes(sigLen);

            // Compute expected signature
            var tokenBytes = Encoding.UTF8.GetBytes(authToken);
            using var hmac = new HMACSHA256(tokenBytes);
            var expectedSignature = hmac.ComputeHash(challenge);

            // Constant-time comparison
            if (!CryptographicOperations.FixedTimeEquals(receivedSignature, expectedSignature))
            {
                errorMessage = "Invalid authentication signature";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to parse auth message: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Creates an authentication response message.
    /// </summary>
    public static byte[] CreateAuthResponse(bool success, string? message = null)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(MagicBytes);
        writer.Write((byte)Version);
        writer.Write(success ? MsgAuthOk : MsgAuthFail);

        if (!string.IsNullOrEmpty(message))
        {
            var msgBytes = Encoding.UTF8.GetBytes(message);
            writer.Write((ushort)msgBytes.Length);
            writer.Write(msgBytes);
        }
        else
        {
            writer.Write((ushort)0);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a connect request to forward to a target server.
    /// </summary>
    /// <param name="hostname">Target IRC server hostname.</param>
    /// <param name="port">Target IRC server port.</param>
    /// <param name="useSsl">Whether to use SSL to the target.</param>
    public static byte[] CreateConnectRequest(string hostname, int port, bool useSsl)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(MagicBytes);
        writer.Write((byte)Version);
        writer.Write(MsgConnect);

        var hostnameBytes = Encoding.UTF8.GetBytes(hostname);
        writer.Write((byte)hostnameBytes.Length);
        writer.Write(hostnameBytes);
        writer.Write((ushort)port);
        writer.Write(useSsl);

        return ms.ToArray();
    }

    /// <summary>
    /// Parses a connect request message.
    /// </summary>
    public static (string hostname, int port, bool useSsl)? ParseConnectRequest(byte[] message)
    {
        try
        {
            using var ms = new MemoryStream(message);
            using var reader = new BinaryReader(ms);

            // Skip magic, version
            reader.ReadBytes(MagicBytes.Length);
            reader.ReadByte();

            var msgType = reader.ReadByte();
            if (msgType != MsgConnect)
                return null;

            var hostnameLen = reader.ReadByte();
            var hostnameBytes = reader.ReadBytes(hostnameLen);
            var hostname = Encoding.UTF8.GetString(hostnameBytes);
            var port = reader.ReadUInt16();
            var useSsl = reader.ReadBoolean();

            return (hostname, port, useSsl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a connect response message.
    /// </summary>
    public static byte[] CreateConnectResponse(bool success, string? message = null)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(MagicBytes);
        writer.Write((byte)Version);
        writer.Write(success ? MsgConnectOk : MsgConnectFail);

        if (!string.IsNullOrEmpty(message))
        {
            var msgBytes = Encoding.UTF8.GetBytes(message);
            writer.Write((ushort)msgBytes.Length);
            writer.Write(msgBytes);
        }
        else
        {
            writer.Write((ushort)0);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Creates an IP info message containing current VPN status.
    /// </summary>
    public static byte[] CreateIpInfoMessage(IpVerificationResult result)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(MagicBytes);
        writer.Write((byte)Version);
        writer.Write(MsgIpInfo);

        writer.Write(result.Success);
        WriteString(writer, result.IpAddress);
        WriteString(writer, result.Country);
        WriteString(writer, result.CountryCode);
        WriteString(writer, result.Organization);
        writer.Write(result.IsLikelyVpn);
        writer.Write(result.CountryMatches);

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a ping message for keep-alive.
    /// </summary>
    public static byte[] CreatePing()
    {
        return new byte[] { MagicBytes[0], MagicBytes[1], MagicBytes[2], MagicBytes[3], MagicBytes[4], Version, MsgPing };
    }

    /// <summary>
    /// Creates a pong response.
    /// </summary>
    public static byte[] CreatePong()
    {
        return new byte[] { MagicBytes[0], MagicBytes[1], MagicBytes[2], MagicBytes[3], MagicBytes[4], Version, MsgPong };
    }

    /// <summary>
    /// Creates a disconnect notification.
    /// </summary>
    public static byte[] CreateDisconnect(string? reason = null)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(MagicBytes);
        writer.Write((byte)Version);
        writer.Write(MsgDisconnect);

        if (!string.IsNullOrEmpty(reason))
        {
            var reasonBytes = Encoding.UTF8.GetBytes(reason);
            writer.Write((ushort)reasonBytes.Length);
            writer.Write(reasonBytes);
        }
        else
        {
            writer.Write((ushort)0);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Reads message type from a message buffer.
    /// </summary>
    public static byte GetMessageType(byte[] message)
    {
        if (message.Length < MagicBytes.Length + 2)
            return 0;

        return message[MagicBytes.Length + 1];
    }

    /// <summary>
    /// Generates a random challenge for authentication.
    /// </summary>
    public static byte[] GenerateChallenge(int length = 32)
    {
        return RandomNumberGenerator.GetBytes(length);
    }

    private static void WriteString(BinaryWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write((byte)0);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write((byte)Math.Min(bytes.Length, 255));
            writer.Write(bytes, 0, Math.Min(bytes.Length, 255));
        }
    }
}
