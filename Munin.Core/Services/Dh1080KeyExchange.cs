using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Munin.Core.Services;

/// <summary>
/// Implements DH1080 key exchange for FiSH encryption.
/// </summary>
/// <remarks>
/// <para>DH1080 is a Diffie-Hellman key exchange protocol used by FiSH
/// to automatically establish shared encryption keys between users.</para>
/// <para>Protocol flow:</para>
/// <list type="number">
///   <item>Initiator sends: DH1080_INIT [public_key_base64]</item>
///   <item>Responder replies: DH1080_FINISH [public_key_base64]</item>
///   <item>Both compute shared secret, hash with SHA256, and use as Blowfish key</item>
/// </list>
/// </remarks>
public class Dh1080KeyExchange
{
    // DH1080 prime (1080 bits) - Sophie Germain prime from FiSH specification
    // Hex: FBE1022E23D213E8ACFA9AE8B9DFADA3EA6B7AC7A7B7E95AB5EB2DF858921FEADE95E6AC7BE7DE6ADBAB8A783E7AF7A7FA6A2B7BEB1E72EAE2B72F9FA2BFB2A2EFBEFAC868BADB3E828FA8BADFADA3E4CC1BE7E8AFE85E9698A783EB68FA07A77AB6AD7BEB618ACF9CA2897EB28A6189EFA07AB99A8A7FA9AE299EFA7BA66DEAFEFBEFBF0B7D8B
    private static readonly byte[] Prime1080Bytes = new byte[]
    {
        0xFB, 0xE1, 0x02, 0x2E, 0x23, 0xD2, 0x13, 0xE8, 0xAC, 0xFA, 0x9A, 0xE8,
        0xB9, 0xDF, 0xAD, 0xA3, 0xEA, 0x6B, 0x7A, 0xC7, 0xA7, 0xB7, 0xE9, 0x5A,
        0xB5, 0xEB, 0x2D, 0xF8, 0x58, 0x92, 0x1F, 0xEA, 0xDE, 0x95, 0xE6, 0xAC,
        0x7B, 0xE7, 0xDE, 0x6A, 0xDB, 0xAB, 0x8A, 0x78, 0x3E, 0x7A, 0xF7, 0xA7,
        0xFA, 0x6A, 0x2B, 0x7B, 0xEB, 0x1E, 0x72, 0xEA, 0xE2, 0xB7, 0x2F, 0x9F,
        0xA2, 0xBF, 0xB2, 0xA2, 0xEF, 0xBE, 0xFA, 0xC8, 0x68, 0xBA, 0xDB, 0x3E,
        0x82, 0x8F, 0xA8, 0xBA, 0xDF, 0xAD, 0xA3, 0xE4, 0xCC, 0x1B, 0xE7, 0xE8,
        0xAF, 0xE8, 0x5E, 0x96, 0x98, 0xA7, 0x83, 0xEB, 0x68, 0xFA, 0x07, 0xA7,
        0x7A, 0xB6, 0xAD, 0x7B, 0xEB, 0x61, 0x8A, 0xCF, 0x9C, 0xA2, 0x89, 0x7E,
        0xB2, 0x8A, 0x61, 0x89, 0xEF, 0xA0, 0x7A, 0xB9, 0x9A, 0x8A, 0x7F, 0xA9,
        0xAE, 0x29, 0x9E, 0xFA, 0x7B, 0xA6, 0x6D, 0xEA, 0xFE, 0xFB, 0xEF, 0xBF,
        0x0B, 0x7D, 0x8B
    };

    private static readonly BigInteger DhPrime = new BigInteger(Prime1080Bytes, isUnsigned: true, isBigEndian: true);

    // Generator
    private const int Generator = 2;

    // FiSH Base64 alphabet for key encoding
    private const string FishBase64Alphabet = "./0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private BigInteger? _privateKey;
    private BigInteger? _publicKey;
    private readonly object _lock = new();

    /// <summary>
    /// CTCP command for initiating key exchange.
    /// </summary>
    public const string InitCommand = "DH1080_INIT";

    /// <summary>
    /// CTCP command for completing key exchange.
    /// </summary>
    public const string FinishCommand = "DH1080_FINISH";

    /// <summary>
    /// Suffix appended to indicate CBC mode.
    /// </summary>
    public const string CbcSuffix = " CBC";

    /// <summary>
    /// Generates a new key pair and returns the public key for sending.
    /// </summary>
    /// <returns>Base64-encoded public key.</returns>
    public string GeneratePublicKey()
    {
        lock (_lock)
        {
            // Generate random private key (1080 bits)
            // Ensure it's in valid range [2, p-2]
            do
            {
                var privateBytes = new byte[135]; // 1080 bits
                RandomNumberGenerator.Fill(privateBytes);
                _privateKey = new BigInteger(privateBytes, isUnsigned: true);
            } while (_privateKey >= DhPrime || _privateKey <= 1);

            // Compute public key: g^private mod p
            _publicKey = BigInteger.ModPow(Generator, _privateKey.Value, DhPrime);

            // Validate public key is in valid range
            if (_publicKey <= 1 || _publicKey >= DhPrime)
            {
                throw new InvalidOperationException($"Generated invalid public key: {_publicKey}");
            }

            return EncodePublicKey(_publicKey.Value);
        }
    }

    /// <summary>
    /// Computes the shared secret from the received public key.
    /// </summary>
    /// <param name="theirPublicKeyBase64">The other party's Base64-encoded public key.</param>
    /// <returns>The shared secret key for Blowfish encryption.</returns>
    public string? ComputeSharedSecret(string theirPublicKeyBase64)
    {
        lock (_lock)
        {
            if (_privateKey == null)
            {
                return null;
            }

            try
            {
                var theirPublicKey = DecodePublicKey(theirPublicKeyBase64);
                
                // Validate public key (must be in range [2, p-1])
                if (theirPublicKey <= 1 || theirPublicKey >= DhPrime)
                {
                    Log.Warning("Received invalid DH1080 public key (out of range): {Key}", theirPublicKeyBase64);
                    return null;
                }
                
                // Compute shared secret: their_public^our_private mod p
                var sharedSecret = BigInteger.ModPow(theirPublicKey, _privateKey.Value, DhPrime);

                // Validate shared secret is not zero or one
                if (sharedSecret <= 1)
                {
                    Log.Warning("Computed invalid DH1080 shared secret (too small)");
                    return null;
                }

                // Convert to bytes (big-endian, unsigned) - pad/trim to 135 bytes
                var secretBytes = sharedSecret.ToByteArray(isUnsigned: true, isBigEndian: true);
                
                // Ensure exactly 135 bytes (pad if shorter, trim if longer)
                if (secretBytes.Length != 135)
                {
                    var normalized = new byte[135];
                    if (secretBytes.Length < 135)
                    {
                        // Pad with leading zeros
                        Array.Copy(secretBytes, 0, normalized, 135 - secretBytes.Length, secretBytes.Length);
                    }
                    else
                    {
                        // Trim extra bytes (should not happen with mod p, but be safe)
                        Array.Copy(secretBytes, secretBytes.Length - 135, normalized, 0, 135);
                    }
                    secretBytes = normalized;
                }
                
                // FiSH uses SHA256 hash of the raw bytes
                var hash = SHA256.HashData(secretBytes);
                
                // Encode using DH1080 Base64 (standard Base64, not FiSH Base64!)
                // Same format as public key encoding
                var b64 = Convert.ToBase64String(hash);
                
                // DH1080 format: remove '=' padding, add 'A' if no padding was needed
                if (!b64.Contains('='))
                {
                    return b64 + "A";
                }
                else
                {
                    return b64.Replace("=", "");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to compute DH1080 shared secret from key: {Key}", theirPublicKeyBase64);
                return null;
            }
        }
    }

    /// <summary>
    /// Creates a DH1080_INIT message for FiSH key exchange.
    /// </summary>
    /// <param name="useCbc">Whether to use CBC mode (default: true, more secure).</param>
    /// <param name="useMircFormat">Whether to use mIRC format (DH1080_INIT_cbc) instead of FiSH-irssi format (DH1080_INIT ... CBC).</param>
    public string CreateInitMessage(bool useCbc = true, bool useMircFormat = true)
    {
        var publicKey = GeneratePublicKey();
        
        if (useCbc && useMircFormat)
        {
            // mIRC format: DH1080_INIT_cbc <pubkey>
            return $"{InitCommand}_cbc {publicKey}";
        }
        else if (useCbc)
        {
            // FiSH-irssi format: DH1080_INIT <pubkey> CBC
            return $"{InitCommand} {publicKey}{CbcSuffix}";
        }
        else
        {
            // ECB format: DH1080_INIT <pubkey>
            return $"{InitCommand} {publicKey}";
        }
    }

    /// <summary>
    /// Creates a DH1080_FINISH message for FiSH key exchange.
    /// </summary>
    /// <param name="theirPublicKey">The initiator's public key.</param>
    /// <param name="useCbc">Whether to use CBC mode.</param>
    /// <param name="useMircFormat">Whether to use mIRC format (DH1080_FINISH_cbc) instead of FiSH-irssi format (DH1080_FINISH ... CBC).</param>
    /// <returns>Tuple of (message, shared secret key).</returns>
    public (string Message, string? SharedKey) CreateFinishMessage(string theirPublicKey, bool useCbc = false, bool useMircFormat = false)
    {
        var ourPublicKey = GeneratePublicKey();
        var sharedKey = ComputeSharedSecret(theirPublicKey);
        
        string message;
        if (useCbc && useMircFormat)
        {
            // mIRC format: DH1080_FINISH_cbc <pubkey>
            message = $"{FinishCommand}_cbc {ourPublicKey}";
        }
        else if (useCbc)
        {
            // FiSH-irssi format: DH1080_FINISH <pubkey> CBC
            message = $"{FinishCommand} {ourPublicKey}{CbcSuffix}";
        }
        else
        {
            // ECB format: DH1080_FINISH <pubkey>
            message = $"{FinishCommand} {ourPublicKey}";
        }
        
        return (message, sharedKey);
    }

    /// <summary>
    /// Parses a DH1080 message.
    /// Supports both formats:
    /// - mIRC style: "DH1080_INIT_cbc [pubkey]"
    /// - FiSH-irssi style: "DH1080_INIT [pubkey] CBC"
    /// </summary>
    /// <param name="message">The raw message content.</param>
    /// <returns>Tuple of (command, public key, isCbc, useMircFormat) or null if not a DH1080 message.</returns>
    public static (string Command, string PublicKey, bool IsCbc, bool UseMircFormat)? ParseMessage(string message)
    {
        var parts = message.Split(' ');
        if (parts.Length < 2) return null;

        var command = parts[0];
        bool isCbc = false;
        bool useMircFormat = false;
        string baseCommand;

        // Check for mIRC style: DH1080_INIT_cbc or DH1080_FINISH_cbc
        if (command.EndsWith("_cbc", StringComparison.OrdinalIgnoreCase))
        {
            isCbc = true;
            useMircFormat = true;
            baseCommand = command.Substring(0, command.Length - 4); // Remove "_cbc"
        }
        else
        {
            baseCommand = command;
            // Check for FiSH-irssi style: CBC suffix at end
            isCbc = parts.Length >= 3 && parts[^1].Equals("CBC", StringComparison.OrdinalIgnoreCase);
        }

        if (baseCommand != InitCommand && baseCommand != FinishCommand) return null;

        // Public key is the second part
        var publicKey = parts[1];

        return (baseCommand, publicKey, isCbc, useMircFormat);
    }

    /// <summary>
    /// Resets the key exchange state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _privateKey = null;
            _publicKey = null;
        }
    }

    /// <summary>
    /// Encodes a public key using DH1080 Base64 (standard Base64 with padding handling).
    /// </summary>
    private static string EncodePublicKey(BigInteger key)
    {
        var bytes = key.ToByteArray(isUnsigned: true, isBigEndian: true);
        
        // Ensure exactly 135 bytes (1080 bits) - pad if shorter, trim if longer
        if (bytes.Length != 135)
        {
            var normalized = new byte[135];
            if (bytes.Length < 135)
            {
                // Pad with leading zeros
                Array.Copy(bytes, 0, normalized, 135 - bytes.Length, bytes.Length);
            }
            else
            {
                // Trim to 135 bytes (take rightmost bytes)
                Array.Copy(bytes, bytes.Length - 135, normalized, 0, 135);
            }
            bytes = normalized;
        }
        
        // Use standard Base64
        var b64 = Convert.ToBase64String(bytes);
        
        // DH1080 format: remove '=' padding, add 'A' if no padding was needed
        if (!b64.Contains('='))
        {
            return b64 + "A";
        }
        else
        {
            return b64.Replace("=", "");
        }
    }

    /// <summary>
    /// Decodes a public key from DH1080 Base64.
    /// </summary>
    private static BigInteger DecodePublicKey(string encoded)
    {
        // Remove any trailing characters
        encoded = encoded.TrimEnd('\x01', ' ', '\r', '\n');
        
        // DH1080 format: if ends with 'A' and length % 4 == 1, remove it
        if (encoded.Length % 4 == 1 && encoded.EndsWith("A"))
        {
            encoded = encoded.Substring(0, encoded.Length - 1);
        }
        
        // Add '=' padding to make valid Base64
        while (encoded.Length % 4 != 0)
        {
            encoded += "=";
        }
        
        var bytes = Convert.FromBase64String(encoded);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// FiSH Base64 encoding.
    /// </summary>
    /// <remarks>
    /// Uses big-endian for bytes↔uint32, encodes right half first, then left.
    /// </remarks>
    private static string FishBase64Encode(byte[] data)
    {
        var sb = new StringBuilder();

        // Pad to 8-byte boundary
        int paddedLength = ((data.Length + 7) / 8) * 8;
        byte[] padded = new byte[paddedLength];
        Array.Copy(data, padded, data.Length);

        // Process 8 bytes at a time
        for (int i = 0; i < padded.Length; i += 8)
        {
            // Big-endian: bytes[0..3] → left, bytes[4..7] → right
            uint left = ((uint)padded[i] << 24) | ((uint)padded[i + 1] << 16) |
                       ((uint)padded[i + 2] << 8) | padded[i + 3];
            uint right = ((uint)padded[i + 4] << 24) | ((uint)padded[i + 5] << 16) |
                        ((uint)padded[i + 6] << 8) | padded[i + 7];

            // FiSH encodes RIGHT first (6 characters)
            for (int j = 0; j < 6; j++)
            {
                sb.Append(FishBase64Alphabet[(int)(right & 0x3F)]);
                right >>= 6;
            }

            // Then LEFT (6 characters)
            for (int j = 0; j < 6; j++)
            {
                sb.Append(FishBase64Alphabet[(int)(left & 0x3F)]);
                left >>= 6;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// FiSH Base64 decoding.
    /// </summary>
    /// <remarks>
    /// First 6 chars → right, next 6 chars → left. Big-endian for uint32→bytes.
    /// </remarks>
    private static byte[] FishBase64Decode(string encoded)
    {
        // Build lookup table
        var lookup = new int[256];
        Array.Fill(lookup, -1);
        for (int i = 0; i < FishBase64Alphabet.Length; i++)
        {
            lookup[FishBase64Alphabet[i]] = i;
        }

        // Must be multiple of 12 characters
        if (encoded.Length % 12 != 0)
        {
            encoded = encoded.PadRight(((encoded.Length + 11) / 12) * 12, '.');
        }

        var result = new List<byte>();

        for (int i = 0; i < encoded.Length; i += 12)
        {
            // First 6 characters decode to RIGHT half
            uint right = 0;
            for (int j = 0; j < 6; j++)
            {
                int idx = lookup[encoded[i + j]];
                if (idx < 0) idx = 0;
                right |= (uint)idx << (j * 6);
            }

            // Next 6 characters decode to LEFT half
            uint left = 0;
            for (int j = 0; j < 6; j++)
            {
                int idx = lookup[encoded[i + 6 + j]];
                if (idx < 0) idx = 0;
                left |= (uint)idx << (j * 6);
            }

            // Convert to bytes in big-endian order: left first (bytes 0-3), then right (bytes 4-7)
            result.Add((byte)(left >> 24));
            result.Add((byte)(left >> 16));
            result.Add((byte)(left >> 8));
            result.Add((byte)left);
            result.Add((byte)(right >> 24));
            result.Add((byte)(right >> 16));
            result.Add((byte)(right >> 8));
            result.Add((byte)right);
        }

        // Remove trailing zeros that were padding
        while (result.Count > 0 && result[result.Count - 1] == 0)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result.ToArray();
    }
}

/// <summary>
/// Manages DH1080 key exchanges with multiple users.
/// </summary>
public class Dh1080Manager
{
    private static readonly ILogger _logger = Log.ForContext<Dh1080Manager>();
    private readonly Dictionary<string, (Dh1080KeyExchange Exchange, bool UseCbc)> _pendingExchanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly FishCryptService _fishCrypt;
    private readonly object _lock = new();

    /// <summary>
    /// Event raised when a key exchange completes successfully.
    /// </summary>
    public event EventHandler<Dh1080CompleteEventArgs>? KeyExchangeComplete;

    /// <summary>
    /// Event raised when a key exchange fails.
    /// </summary>
    public event EventHandler<Dh1080FailEventArgs>? KeyExchangeFailed;

    public Dh1080Manager(FishCryptService fishCrypt)
    {
        _fishCrypt = fishCrypt;
    }

    /// <summary>
    /// Initiates a key exchange with another user.
    /// </summary>
    /// <param name="serverId">Server identifier.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="useCbc">Whether to use CBC mode (default: true, more secure).</param>
    /// <returns>The CTCP NOTICE to send to the user.</returns>
    public string InitiateKeyExchange(string serverId, string nick, bool useCbc = true)
    {
        var key = GetKey(serverId, nick);
        var exchange = new Dh1080KeyExchange();

        lock (_lock)
        {
            _pendingExchanges[key] = (exchange, useCbc);
        }

        return exchange.CreateInitMessage(useCbc);
    }

    /// <summary>
    /// Handles an incoming DH1080 message.
    /// </summary>
    /// <param name="serverId">Server identifier.</param>
    /// <param name="fromNick">The nick who sent the message.</param>
    /// <param name="ctcpMessage">The CTCP message content (without \x01).</param>
    /// <returns>Response CTCP message to send, or null.</returns>
    public string? HandleMessage(string serverId, string fromNick, string ctcpMessage)
    {
        _logger.Debug("HandleMessage: serverId={ServerId}, fromNick={FromNick}, message={Message}", 
            serverId, fromNick, ctcpMessage);
        
        var parsed = Dh1080KeyExchange.ParseMessage(ctcpMessage);
        if (parsed == null)
        {
            // Don't log the actual message content as it may contain key material
            _logger.Warning("HandleMessage: ParseMessage returned null");
            return null;
        }

        var (command, theirPublicKey, isCbc, useMircFormat) = parsed.Value;
        var key = GetKey(serverId, fromNick);
        
        // Only log non-sensitive metadata, NOT the public key length which could reveal crypto info
        _logger.Debug("HandleMessage: parsed command={Command}, isCbc={IsCbc}, useMircFormat={UseMircFormat}",
            command, isCbc, useMircFormat);

        if (command == Dh1080KeyExchange.InitCommand)
        {
            // Someone is initiating a key exchange with us
            // Reply using the same format they used (mIRC or FiSH-irssi)
            var exchange = new Dh1080KeyExchange();
            var (response, sharedKey) = exchange.CreateFinishMessage(theirPublicKey, isCbc, useMircFormat);

            if (sharedKey != null)
            {
                _logger.Information("DH1080 INIT handled successfully, shared key established with {Nick}", fromNick);
                // Add cbc: prefix if CBC mode is used (compatible with Mircryption/FiSH 10)
                var keyToStore = isCbc ? FishCryptService.CbcKeyPrefix + sharedKey : sharedKey;
                _fishCrypt.SetKey(serverId, fromNick, keyToStore);
                KeyExchangeComplete?.Invoke(this, new Dh1080CompleteEventArgs(serverId, fromNick, sharedKey, isCbc));
            }
            else
            {
                KeyExchangeFailed?.Invoke(this, new Dh1080FailEventArgs(serverId, fromNick, "Failed to compute shared secret"));
            }

            return response;
        }
        else if (command == Dh1080KeyExchange.FinishCommand)
        {
            _logger.Debug("HandleMessage: Processing FINISH command from {Nick}", fromNick);
            
            // Response to our initiated exchange
            Dh1080KeyExchange? exchange;
            bool initiatedWithCbc;
            lock (_lock)
            {
                _logger.Debug("HandleMessage: Looking for pending exchange with key={Key}, pendingCount={Count}", 
                    key, _pendingExchanges.Count);
                
                if (!_pendingExchanges.TryGetValue(key, out var pending))
                {
                    _logger.Warning("HandleMessage: No pending key exchange found for {Nick} (key={Key})", fromNick, key);
                    KeyExchangeFailed?.Invoke(this, new Dh1080FailEventArgs(serverId, fromNick, "No pending key exchange"));
                    return null;
                }
                exchange = pending.Exchange;
                initiatedWithCbc = pending.UseCbc;
                _pendingExchanges.Remove(key);
            }

            // Use the CBC mode we initiated with, or what the response indicates
            var useCbcMode = initiatedWithCbc || isCbc;
            
            // Don't log key length or other crypto-revealing metadata
            _logger.Debug("HandleMessage: Computing shared secret, initiatedWithCbc={InitCbc}, responseCbc={RespCbc}", 
                initiatedWithCbc, isCbc);
            var sharedKey = exchange.ComputeSharedSecret(theirPublicKey);
            if (sharedKey != null)
            {
                _logger.Information("DH1080 FINISH handled successfully, shared key established with {Nick}, useCbc={UseCbc}", fromNick, useCbcMode);
                // Add cbc: prefix if CBC mode is used (compatible with Mircryption/FiSH 10)
                var keyToStore = useCbcMode ? FishCryptService.CbcKeyPrefix + sharedKey : sharedKey;
                _fishCrypt.SetKey(serverId, fromNick, keyToStore);
                KeyExchangeComplete?.Invoke(this, new Dh1080CompleteEventArgs(serverId, fromNick, sharedKey, useCbcMode));
            }
            else
            {
                _logger.Warning("HandleMessage: Failed to compute shared secret with {Nick}", fromNick);
                KeyExchangeFailed?.Invoke(this, new Dh1080FailEventArgs(serverId, fromNick, "Failed to compute shared secret"));
            }

            return null; // No response needed
        }

        return null;
    }

    private static string GetKey(string serverId, string nick)
    {
        return $"{serverId}:{nick.ToLowerInvariant()}";
    }
}

/// <summary>
/// Event args for successful key exchange.
/// </summary>
public class Dh1080CompleteEventArgs : EventArgs
{
    public string ServerId { get; }
    public string Nick { get; }
    public string SharedKey { get; }
    public bool IsCbc { get; }

    public Dh1080CompleteEventArgs(string serverId, string nick, string sharedKey, bool isCbc = false)
    {
        ServerId = serverId;
        Nick = nick;
        SharedKey = sharedKey;
        IsCbc = isCbc;
    }
}

/// <summary>
/// Event args for failed key exchange.
/// </summary>
public class Dh1080FailEventArgs : EventArgs
{
    public string ServerId { get; }
    public string Nick { get; }
    public string Reason { get; }

    public Dh1080FailEventArgs(string serverId, string nick, string reason)
    {
        ServerId = serverId;
        Nick = nick;
        Reason = reason;
    }
}
