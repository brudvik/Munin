using System.Text;

namespace Munin.Core.Services;

/// <summary>
/// FiSH encryption service for IRC message encryption.
/// </summary>
/// <remarks>
/// <para>Provides Blowfish-based encryption compatible with mIRC FiSH,
/// HexChat FiSHLiM, and other IRC FiSH implementations.</para>
/// <para>Supports both ECB mode (+OK prefix) and CBC mode (*OK prefix).</para>
/// </remarks>
public class FishCryptService
{
    // FiSH uses a custom Base64 alphabet
    private const string FishBase64Alphabet = "./0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly char[] FishBase64Chars = FishBase64Alphabet.ToCharArray();
    private static readonly int[] FishBase64Lookup = BuildLookupTable();

    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _keysLock = new();

    /// <summary>
    /// Prefix for ECB-encrypted messages.
    /// </summary>
    public const string EcbPrefix = "+OK ";

    /// <summary>
    /// Prefix for CBC-encrypted messages.
    /// </summary>
    public const string CbcPrefix = "*OK ";

    /// <summary>
    /// Alternative prefix used by some clients.
    /// </summary>
    public const string McpsPrefix = "mcps ";

    /// <summary>
    /// Prefix for CBC keys (added to key when CBC mode should be used).
    /// </summary>
    /// <remarks>
    /// When a key starts with this prefix, CBC mode is used for encryption/decryption.
    /// This is compatible with mIRC Mircryption and FiSH 10.
    /// </remarks>
    public const string CbcKeyPrefix = "cbc:";

    /// <summary>
    /// Event raised when a key is set or removed.
    /// </summary>
    public event EventHandler<FishKeyEventArgs>? KeyChanged;

    /// <summary>
    /// Sets the encryption key for a target (channel or nick).
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="target">The channel name or nickname.</param>
    /// <param name="key">The encryption key, or null to remove.</param>
    public void SetKey(string serverId, string target, string? key)
    {
        var fullKey = GetFullKey(serverId, target);

        lock (_keysLock)
        {
            if (string.IsNullOrEmpty(key))
            {
                _keys.Remove(fullKey);
            }
            else
            {
                _keys[fullKey] = key;
            }
        }

        KeyChanged?.Invoke(this, new FishKeyEventArgs(serverId, target, !string.IsNullOrEmpty(key)));
    }

    /// <summary>
    /// Gets the encryption key for a target.
    /// </summary>
    public string? GetKey(string serverId, string target)
    {
        var fullKey = GetFullKey(serverId, target);
        lock (_keysLock)
        {
            return _keys.TryGetValue(fullKey, out var key) ? key : null;
        }
    }

    /// <summary>
    /// Checks if a target has an encryption key set.
    /// </summary>
    public bool HasKey(string serverId, string target)
    {
        return GetKey(serverId, target) != null;
    }

    /// <summary>
    /// Gets all stored keys as a dictionary for persistence.
    /// </summary>
    /// <returns>Dictionary of fullKey (serverId:target) to encryption key.</returns>
    public Dictionary<string, string> GetAllKeys()
    {
        lock (_keysLock)
        {
            return new Dictionary<string, string>(_keys, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Loads keys from a dictionary (for persistence).
    /// </summary>
    /// <param name="keys">Dictionary of fullKey (serverId:target) to encryption key.</param>
    public void LoadKeys(Dictionary<string, string>? keys)
    {
        if (keys == null) return;
        
        lock (_keysLock)
        {
            foreach (var kvp in keys)
            {
                _keys[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Encrypts a message using FiSH encryption.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="target">The channel or nick.</param>
    /// <param name="message">The plaintext message.</param>
    /// <param name="useCbc">Whether to use CBC mode. If null, defaults to CBC (more secure).</param>
    /// <returns>The encrypted message with prefix, or null if no key is set.</returns>
    public string? Encrypt(string serverId, string target, string message, bool? useCbc = null)
    {
        var key = GetKey(serverId, target);
        if (key == null) return null;

        // Check if key has CBC prefix (strip it for actual encryption)
        string actualKey;
        if (key.StartsWith(CbcKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            actualKey = key[CbcKeyPrefix.Length..];
        }
        else
        {
            actualKey = key;
        }

        // Default to CBC mode (more secure) unless explicitly set to ECB
        var actualUseCbc = useCbc ?? true;

        return Encrypt(message, actualKey, actualUseCbc);
    }

    /// <summary>
    /// Encrypts a message using the specified key.
    /// </summary>
    /// <param name="message">The plaintext message.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="useCbc">Whether to use CBC mode.</param>
    /// <param name="useMircryptionFormat">Whether to use Mircryption CBC format (+OK *base64) instead of *OK format.</param>
    public static string Encrypt(string message, string key, bool useCbc = true, bool useMircryptionFormat = true)
    {
        var blowfish = Blowfish.FromKey(key);
        var data = Encoding.UTF8.GetBytes(message);

        if (useCbc)
        {
            if (useMircryptionFormat)
            {
                // Mircryption CBC format: +OK *<standard_base64>
                // Uses zero IV, prepends 8 random bytes to plaintext
                var randomBytes = new byte[8];
                Random.Shared.NextBytes(randomBytes);
                
                // Prepend random bytes to plaintext
                var plaintextWithRandom = new byte[randomBytes.Length + data.Length];
                Array.Copy(randomBytes, plaintextWithRandom, randomBytes.Length);
                Array.Copy(data, 0, plaintextWithRandom, randomBytes.Length, data.Length);
                
                var zeroIv = new byte[8];
                var encrypted = blowfish.EncryptCBC(plaintextWithRandom, zeroIv);
                
                return EcbPrefix + "*" + Convert.ToBase64String(encrypted);
            }
            else
            {
                // FiSHLiM CBC format: *OK <fish_base64>
                // Uses random IV prepended to ciphertext
                var iv = new byte[8];
                Random.Shared.NextBytes(iv);
                var encrypted = blowfish.EncryptCBC(data, iv);
                
                // Prepend IV to encrypted data
                var result = new byte[iv.Length + encrypted.Length];
                Array.Copy(iv, result, iv.Length);
                Array.Copy(encrypted, 0, result, iv.Length, encrypted.Length);
                
                return CbcPrefix + FishBase64Encode(result);
            }
        }
        else
        {
            var encrypted = blowfish.EncryptECB(data);
            return EcbPrefix + FishBase64Encode(encrypted);
        }
    }

    /// <summary>
    /// Decrypts a FiSH-encrypted message.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="target">The channel or nick.</param>
    /// <param name="message">The encrypted message (with prefix).</param>
    /// <returns>The decrypted message, or null if decryption fails.</returns>
    public string? Decrypt(string serverId, string target, string message)
    {
        var key = GetKey(serverId, target);
        if (key == null) return null;

        // Strip cbc: prefix from key if present (the prefix indicates mode for encryption,
        // but decryption mode is determined by the message prefix +OK vs +OK *)
        var actualKey = key.StartsWith(CbcKeyPrefix, StringComparison.OrdinalIgnoreCase) 
            ? key[CbcKeyPrefix.Length..] 
            : key;

        return Decrypt(message, actualKey);
    }

    /// <summary>
    /// Decrypts a message using the specified key.
    /// </summary>
    public static string? Decrypt(string message, string key)
    {
        bool isCbc;
        bool isMircryptionCbc = false;
        string encoded;

        if (message.StartsWith(CbcPrefix, StringComparison.Ordinal))
        {
            isCbc = true;
            encoded = message[CbcPrefix.Length..];
        }
        else if (message.StartsWith(EcbPrefix, StringComparison.Ordinal))
        {
            encoded = message[EcbPrefix.Length..];
            
            // Check for Mircryption CBC mode: +OK *...
            if (encoded.StartsWith("*"))
            {
                isCbc = true;
                isMircryptionCbc = true;
                encoded = encoded[1..]; // Remove the *
            }
            else
            {
                isCbc = false;
            }
        }
        else if (message.StartsWith(McpsPrefix, StringComparison.Ordinal))
        {
            isCbc = false;
            encoded = message[McpsPrefix.Length..];
        }
        else
        {
            return null; // Not a FiSH message
        }

        try
        {
            var blowfish = Blowfish.FromKey(key);
            
            byte[] data;
            byte[] decrypted;
            
            if (isMircryptionCbc)
            {
                // Mircryption CBC uses standard Base64, not FiSH Base64
                data = Convert.FromBase64String(encoded);
                
                // Mircryption CBC uses zero IV but prepends 8 random bytes to message
                var zeroIv = new byte[8];
                decrypted = blowfish.DecryptCBC(data, zeroIv);
                
                // Remove the 8 prepended random bytes
                if (decrypted.Length > 8)
                {
                    decrypted = decrypted[8..];
                }
                else
                {
                    return null;
                }
            }
            else if (isCbc)
            {
                data = FishBase64Decode(encoded);
                
                if (data.Length < 8) return null;
                var iv = new byte[8];
                Array.Copy(data, iv, 8);
                var ciphertext = new byte[data.Length - 8];
                Array.Copy(data, 8, ciphertext, 0, ciphertext.Length);
                decrypted = blowfish.DecryptCBC(ciphertext, iv);
            }
            else
            {
                data = FishBase64Decode(encoded);
                decrypted = blowfish.DecryptECB(data);
            }

            // Remove null padding
            int nullIndex = Array.IndexOf(decrypted, (byte)0);
            if (nullIndex >= 0)
            {
                decrypted = decrypted[..nullIndex];
            }

            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null; // Decryption failed
        }
    }

    /// <summary>
    /// Checks if a message is FiSH-encrypted.
    /// </summary>
    public static bool IsEncrypted(string message)
    {
        return message.StartsWith(EcbPrefix, StringComparison.Ordinal) ||
               message.StartsWith(CbcPrefix, StringComparison.Ordinal) ||
               message.StartsWith(McpsPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets all configured keys.
    /// </summary>
    public Dictionary<string, string> ExportKeys()
    {
        lock (_keysLock)
        {
            return new Dictionary<string, string>(_keys);
        }
    }

    /// <summary>
    /// Imports keys from a dictionary.
    /// </summary>
    public void ImportKeys(Dictionary<string, string> keys)
    {
        lock (_keysLock)
        {
            _keys.Clear();
            foreach (var kvp in keys)
            {
                _keys[kvp.Key] = kvp.Value;
            }
        }
    }

    private static string GetFullKey(string serverId, string target)
    {
        return $"{serverId}:{target.ToLowerInvariant()}";
    }

    /// <summary>
    /// Encodes data using FiSH's custom Base64 variant.
    /// </summary>
    /// <remarks>
    /// FiSH Base64 processes 8 bytes at a time (64 bits) and produces
    /// 12 characters using a custom alphabet. 
    /// Uses big-endian for bytes↔uint32, and encodes right half first, then left.
    /// </remarks>
    public static string FishBase64Encode(byte[] data)
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

            // FiSH encodes RIGHT first (6 characters, 6 bits each, LSB first)
            for (int j = 0; j < 6; j++)
            {
                sb.Append(FishBase64Chars[right & 0x3F]);
                right >>= 6;
            }

            // Then LEFT (6 characters, 6 bits each, LSB first)
            for (int j = 0; j < 6; j++)
            {
                sb.Append(FishBase64Chars[left & 0x3F]);
                left >>= 6;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Decodes FiSH's custom Base64 variant.
    /// </summary>
    /// <remarks>
    /// FiSH Base64 decodes right half first (chars 0-5), then left (chars 6-11).
    /// Uses big-endian for uint32→bytes conversion.
    /// </remarks>
    public static byte[] FishBase64Decode(string encoded)
    {
        // Must be multiple of 12 characters
        if (encoded.Length % 12 != 0)
        {
            // Pad with dots (which represents 0 in FiSH Base64)
            encoded = encoded.PadRight(((encoded.Length + 11) / 12) * 12, '.');
        }

        var result = new List<byte>();

        for (int i = 0; i < encoded.Length; i += 12)
        {
            // First 6 characters decode to RIGHT half
            uint right = 0;
            for (int j = 0; j < 6; j++)
            {
                int idx = FishBase64Lookup[encoded[i + j]];
                if (idx < 0) idx = 0;
                right |= (uint)idx << (j * 6);
            }

            // Next 6 characters decode to LEFT half
            uint left = 0;
            for (int j = 0; j < 6; j++)
            {
                int idx = FishBase64Lookup[encoded[i + 6 + j]];
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

        return result.ToArray();
    }

    private static int[] BuildLookupTable()
    {
        var table = new int[256];
        Array.Fill(table, -1);
        for (int i = 0; i < FishBase64Alphabet.Length; i++)
        {
            table[FishBase64Alphabet[i]] = i;
        }
        return table;
    }
}

/// <summary>
/// Event arguments for FiSH key changes.
/// </summary>
public class FishKeyEventArgs : EventArgs
{
    /// <summary>
    /// The server identifier.
    /// </summary>
    public string ServerId { get; }

    /// <summary>
    /// The channel or nick.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Whether a key is now set (false = removed).
    /// </summary>
    public bool HasKey { get; }

    public FishKeyEventArgs(string serverId, string target, bool hasKey)
    {
        ServerId = serverId;
        Target = target;
        HasKey = hasKey;
    }
}
