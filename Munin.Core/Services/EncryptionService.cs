using System.Security.Cryptography;
using System.Text;

namespace Munin.Core.Services;

/// <summary>
/// Provides AES-256-GCM encryption and decryption services with PBKDF2 key derivation.
/// </summary>
/// <remarks>
/// <para>Security features:</para>
/// <list type="bullet">
///   <item><description>AES-256-GCM authenticated encryption</description></item>
///   <item><description>PBKDF2 key derivation with 150,000 iterations</description></item>
///   <item><description>Unique salt per installation</description></item>
///   <item><description>Unique IV/nonce per encryption operation</description></item>
/// </list>
/// </remarks>
public class EncryptionService
{
    /// <summary>
    /// Number of PBKDF2 iterations for key derivation.
    /// Higher values increase security but slow down unlock time.
    /// </summary>
    private const int Pbkdf2Iterations = 150_000;
    
    /// <summary>
    /// AES key size in bytes (256 bits).
    /// </summary>
    private const int KeySizeBytes = 32;
    
    /// <summary>
    /// GCM nonce size in bytes (96 bits as recommended).
    /// </summary>
    private const int NonceSizeBytes = 12;
    
    /// <summary>
    /// GCM authentication tag size in bytes (128 bits).
    /// </summary>
    private const int TagSizeBytes = 16;
    
    /// <summary>
    /// Salt size in bytes for key derivation.
    /// </summary>
    private const int SaltSizeBytes = 32;
    
    /// <summary>
    /// Magic bytes to identify encrypted files.
    /// </summary>
    private static readonly byte[] MagicBytes = "IRCENC01"u8.ToArray();
    
    private byte[]? _derivedKey;
    private byte[]? _salt;
    
    /// <summary>
    /// Gets whether the service has been initialized with a password.
    /// </summary>
    public bool IsUnlocked => _derivedKey != null;
    
    /// <summary>
    /// Gets the current salt, or null if not initialized.
    /// </summary>
    public byte[]? Salt => _salt?.ToArray();
    
    /// <summary>
    /// Initializes the encryption service with a new password and generates a new salt.
    /// Used when setting up encryption for the first time.
    /// </summary>
    /// <param name="password">The master password chosen by the user.</param>
    /// <returns>The generated salt that must be stored for future use.</returns>
    public byte[] InitializeNew(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        
        _salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        _derivedKey = DeriveKey(password, _salt);
        
        return _salt.ToArray();
    }
    
    /// <summary>
    /// Initializes the encryption service with an existing password and salt.
    /// Used when unlocking an existing encrypted installation.
    /// </summary>
    /// <param name="password">The master password.</param>
    /// <param name="salt">The salt stored from initial setup.</param>
    public void Initialize(string password, byte[] salt)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        if (salt == null || salt.Length != SaltSizeBytes)
            throw new ArgumentException($"Salt must be {SaltSizeBytes} bytes.", nameof(salt));
        
        _salt = salt.ToArray();
        _derivedKey = DeriveKey(password, _salt);
    }
    
    /// <summary>
    /// Clears the derived key from memory, locking the service.
    /// </summary>
    public void Lock()
    {
        if (_derivedKey != null)
        {
            CryptographicOperations.ZeroMemory(_derivedKey);
            _derivedKey = null;
        }
    }
    
    /// <summary>
    /// Securely wipes all sensitive data from memory.
    /// Should be called on application exit.
    /// </summary>
    public void WipeMemory()
    {
        Lock();
        
        if (_salt != null)
        {
            CryptographicOperations.ZeroMemory(_salt);
            _salt = null;
        }
    }
    
    /// <summary>
    /// Encrypts plaintext data using AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <returns>Encrypted data with format: [Magic][Nonce][Tag][Ciphertext]</returns>
    /// <exception cref="InvalidOperationException">Thrown if service is not unlocked.</exception>
    public byte[] Encrypt(byte[] plaintext)
    {
        EnsureUnlocked();
        
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        
        using var aes = new AesGcm(_derivedKey!, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        
        // Format: [Magic 8][Nonce 12][Tag 16][Ciphertext N]
        var result = new byte[MagicBytes.Length + NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        var offset = 0;
        
        Buffer.BlockCopy(MagicBytes, 0, result, offset, MagicBytes.Length);
        offset += MagicBytes.Length;
        
        Buffer.BlockCopy(nonce, 0, result, offset, NonceSizeBytes);
        offset += NonceSizeBytes;
        
        Buffer.BlockCopy(tag, 0, result, offset, TagSizeBytes);
        offset += TagSizeBytes;
        
        Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);
        
        return result;
    }
    
    /// <summary>
    /// Encrypts a string using AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">The string to encrypt.</param>
    /// <returns>Encrypted data.</returns>
    public byte[] EncryptString(string plaintext)
    {
        return Encrypt(Encoding.UTF8.GetBytes(plaintext));
    }
    
    /// <summary>
    /// Decrypts data that was encrypted with <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="encryptedData">The encrypted data.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="InvalidOperationException">Thrown if service is not unlocked.</exception>
    /// <exception cref="CryptographicException">Thrown if decryption fails (wrong password or corrupted data).</exception>
    public byte[] Decrypt(byte[] encryptedData)
    {
        EnsureUnlocked();
        
        var minLength = MagicBytes.Length + NonceSizeBytes + TagSizeBytes;
        if (encryptedData.Length < minLength)
            throw new CryptographicException("Invalid encrypted data format.");
        
        // Verify magic bytes
        for (int i = 0; i < MagicBytes.Length; i++)
        {
            if (encryptedData[i] != MagicBytes[i])
                throw new CryptographicException("Invalid encrypted data format.");
        }
        
        var offset = MagicBytes.Length;
        
        var nonce = new byte[NonceSizeBytes];
        Buffer.BlockCopy(encryptedData, offset, nonce, 0, NonceSizeBytes);
        offset += NonceSizeBytes;
        
        var tag = new byte[TagSizeBytes];
        Buffer.BlockCopy(encryptedData, offset, tag, 0, TagSizeBytes);
        offset += TagSizeBytes;
        
        var ciphertextLength = encryptedData.Length - offset;
        var ciphertext = new byte[ciphertextLength];
        Buffer.BlockCopy(encryptedData, offset, ciphertext, 0, ciphertextLength);
        
        var plaintext = new byte[ciphertextLength];
        
        using var aes = new AesGcm(_derivedKey!, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        
        return plaintext;
    }
    
    /// <summary>
    /// Decrypts data to a string.
    /// </summary>
    /// <param name="encryptedData">The encrypted data.</param>
    /// <returns>The decrypted string.</returns>
    public string DecryptString(byte[] encryptedData)
    {
        return Encoding.UTF8.GetString(Decrypt(encryptedData));
    }
    
    /// <summary>
    /// Verifies that a password is correct by attempting to decrypt a test value.
    /// </summary>
    /// <param name="password">The password to verify.</param>
    /// <param name="salt">The stored salt.</param>
    /// <param name="encryptedTestValue">An encrypted test value created during setup.</param>
    /// <returns>True if the password is correct.</returns>
    public static bool VerifyPassword(string password, byte[] salt, byte[] encryptedTestValue)
    {
        try
        {
            var service = new EncryptionService();
            service.Initialize(password, salt);
            service.Decrypt(encryptedTestValue);
            service.Lock();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
    
    /// <summary>
    /// Creates an encrypted test value for password verification.
    /// </summary>
    /// <returns>An encrypted test value that can be used with <see cref="VerifyPassword"/>.</returns>
    public byte[] CreateVerificationToken()
    {
        EnsureUnlocked();
        // Encrypt a known value that we can try to decrypt to verify the password
        return EncryptString("IRC_ENCRYPTION_VERIFICATION_TOKEN_V1");
    }
    
    /// <summary>
    /// Checks if data appears to be encrypted by this service.
    /// </summary>
    /// <param name="data">The data to check.</param>
    /// <returns>True if the data has the encryption magic bytes.</returns>
    public static bool IsEncrypted(byte[] data)
    {
        if (data.Length < MagicBytes.Length)
            return false;
        
        for (int i = 0; i < MagicBytes.Length; i++)
        {
            if (data[i] != MagicBytes[i])
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Checks if a file appears to be encrypted.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if the file exists and has encryption magic bytes.</returns>
    public static bool IsFileEncrypted(string filePath)
    {
        if (!File.Exists(filePath))
            return false;
        
        try
        {
            using var stream = File.OpenRead(filePath);
            var header = new byte[MagicBytes.Length];
            if (stream.Read(header, 0, header.Length) < header.Length)
                return false;
            
            return IsEncrypted(header);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Changes the master password by re-deriving the key.
    /// Note: All encrypted data must be re-encrypted with the new key.
    /// </summary>
    /// <param name="newPassword">The new master password.</param>
    /// <returns>The new salt (same as old, key derivation changes).</returns>
    public byte[] ChangePassword(string newPassword)
    {
        if (_salt == null)
            throw new InvalidOperationException("Service not initialized.");
        
        // Generate new salt for the new password
        _salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        _derivedKey = DeriveKey(newPassword, _salt);
        
        return _salt.ToArray();
    }
    
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);
        
        return pbkdf2.GetBytes(KeySizeBytes);
    }
    
    private void EnsureUnlocked()
    {
        if (_derivedKey == null)
            throw new InvalidOperationException("Encryption service is locked. Call Initialize first.");
    }
}
