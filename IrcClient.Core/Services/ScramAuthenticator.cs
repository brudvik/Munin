using System.Security.Cryptography;
using System.Text;

namespace IrcClient.Core.Services;

/// <summary>
/// Implements SCRAM-SHA-256 authentication for SASL.
/// RFC 5802 - Salted Challenge Response Authentication Mechanism (SCRAM)
/// </summary>
/// <remarks>
/// <para>SCRAM provides a secure authentication mechanism that:</para>
/// <list type="bullet">
///   <item><description>Never transmits the password in plain text</description></item>
///   <item><description>Protects against replay attacks</description></item>
///   <item><description>Provides mutual authentication</description></item>
/// </list>
/// <para>Usage flow:</para>
/// <list type="number">
///   <item><description>Call GetClientFirstMessage() and send to server</description></item>
///   <item><description>Call ProcessServerFirstMessage() with server response</description></item>
///   <item><description>Send client-final-message to server</description></item>
///   <item><description>Call VerifyServerFinalMessage() to complete</description></item>
/// </list>
/// </remarks>
public class ScramAuthenticator
{
    private readonly string _username;
    private readonly string _password;
    private string _clientNonce = string.Empty;
    private string _serverNonce = string.Empty;
    private byte[] _salt = Array.Empty<byte>();
    private int _iterations;
    private string _clientFirstMessageBare = string.Empty;
    private string _serverFirstMessage = string.Empty;

    /// <summary>
    /// Gets the current state of the SCRAM authentication process.
    /// </summary>
    public ScramAuthenticatorState State { get; private set; } = ScramAuthenticatorState.Initial;

    /// <summary>
    /// Initializes a new SCRAM authenticator.
    /// </summary>
    /// <param name="username">The username for authentication.</param>
    /// <param name="password">The password for authentication.</param>
    public ScramAuthenticator(string username, string password)
    {
        _username = SanitizeUsername(username);
        _password = password;
    }

    /// <summary>
    /// Generates the client-first message to send to the server.
    /// </summary>
    public string GetClientFirstMessage()
    {
        _clientNonce = GenerateNonce();
        _clientFirstMessageBare = $"n={_username},r={_clientNonce}";
        
        // gs2-header: n,, (no channel binding, no authzid)
        var clientFirstMessage = $"n,,{_clientFirstMessageBare}";
        
        State = ScramAuthenticatorState.WaitingForServerFirst;
        return clientFirstMessage;
    }

    /// <summary>
    /// Processes the server-first message and generates the client-final message.
    /// </summary>
    public string ProcessServerFirstMessage(string serverFirstMessage)
    {
        _serverFirstMessage = serverFirstMessage;
        
        // Parse server-first-message: r=nonce,s=salt,i=iterations
        var parts = ParseMessage(serverFirstMessage);
        
        if (!parts.TryGetValue("r", out var nonce) ||
            !parts.TryGetValue("s", out var saltBase64) ||
            !parts.TryGetValue("i", out var iterStr))
        {
            throw new InvalidOperationException("Invalid server-first-message format");
        }

        _serverNonce = nonce;
        _salt = Convert.FromBase64String(saltBase64);
        _iterations = int.Parse(iterStr);

        // Verify server nonce starts with client nonce
        if (!_serverNonce.StartsWith(_clientNonce))
        {
            throw new InvalidOperationException("Server nonce doesn't start with client nonce");
        }

        // Generate client-final-message
        // channel-binding: c=biws (base64 of "n,,")
        var channelBinding = Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,"));
        var clientFinalMessageWithoutProof = $"c={channelBinding},r={_serverNonce}";

        // Calculate client proof
        var saltedPassword = Hi(_password, _salt, _iterations);
        var clientKey = HMAC(saltedPassword, "Client Key");
        var storedKey = Hash(clientKey);
        
        var authMessage = $"{_clientFirstMessageBare},{_serverFirstMessage},{clientFinalMessageWithoutProof}";
        var clientSignature = HMAC(storedKey, authMessage);
        var clientProof = XOR(clientKey, clientSignature);
        
        var clientFinalMessage = $"{clientFinalMessageWithoutProof},p={Convert.ToBase64String(clientProof)}";
        
        State = ScramAuthenticatorState.WaitingForServerFinal;
        return clientFinalMessage;
    }

    /// <summary>
    /// Verifies the server-final message.
    /// </summary>
    public bool VerifyServerFinalMessage(string serverFinalMessage)
    {
        var parts = ParseMessage(serverFinalMessage);
        
        if (parts.TryGetValue("e", out var error))
        {
            throw new InvalidOperationException($"SCRAM authentication error: {error}");
        }

        if (!parts.TryGetValue("v", out var verifierBase64))
        {
            throw new InvalidOperationException("Invalid server-final-message format");
        }

        // Verify server signature
        var saltedPassword = Hi(_password, _salt, _iterations);
        var serverKey = HMAC(saltedPassword, "Server Key");
        
        var channelBinding = Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,"));
        var clientFinalMessageWithoutProof = $"c={channelBinding},r={_serverNonce}";
        var authMessage = $"{_clientFirstMessageBare},{_serverFirstMessage},{clientFinalMessageWithoutProof}";
        
        var expectedServerSignature = HMAC(serverKey, authMessage);
        var receivedServerSignature = Convert.FromBase64String(verifierBase64);

        State = ScramAuthenticatorState.Complete;
        return expectedServerSignature.SequenceEqual(receivedServerSignature);
    }

    /// <summary>
    /// PBKDF2 key derivation (Hi function in RFC 5802).
    /// </summary>
    private static byte[] Hi(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), 
            salt, 
            iterations, 
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // SHA-256 produces 32 bytes
    }

    /// <summary>
    /// HMAC-SHA-256.
    /// </summary>
    private static byte[] HMAC(byte[] key, string message)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    }

    /// <summary>
    /// SHA-256 hash.
    /// </summary>
    private static byte[] Hash(byte[] data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>
    /// XOR two byte arrays.
    /// </summary>
    private static byte[] XOR(byte[] a, byte[] b)
    {
        var result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = (byte)(a[i] ^ b[i]);
        }
        return result;
    }

    /// <summary>
    /// Generates a random nonce.
    /// </summary>
    private static string GenerateNonce()
    {
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        // Use printable characters only (base64 without special chars)
        return Convert.ToBase64String(bytes).Replace("+", "A").Replace("/", "B").Replace("=", "");
    }

    /// <summary>
    /// Sanitizes username according to RFC 5802 (SASLprep).
    /// </summary>
    private static string SanitizeUsername(string username)
    {
        // Basic escaping: = becomes =3D, , becomes =2C
        return username.Replace("=", "=3D").Replace(",", "=2C");
    }

    /// <summary>
    /// Parses a SCRAM message into key-value pairs.
    /// </summary>
    private static Dictionary<string, string> ParseMessage(string message)
    {
        var result = new Dictionary<string, string>();
        foreach (var part in message.Split(','))
        {
            var eqIndex = part.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = part[..eqIndex];
                var value = part[(eqIndex + 1)..];
                result[key] = value;
            }
        }
        return result;
    }
}

public enum ScramAuthenticatorState
{
    Initial,
    WaitingForServerFirst,
    WaitingForServerFinal,
    Complete
}
