using System.Security.Cryptography;
using System.Text;

namespace MuninRelay;

/// <summary>
/// Provides master password protection for sensitive configuration data.
/// </summary>
/// <remarks>
/// <para>Uses a two-layer encryption approach:</para>
/// <list type="bullet">
///   <item><description>Master password hash is stored encrypted with DPAPI (machine-bound)</description></item>
///   <item><description>Sensitive data is encrypted with AES using a key derived from the password</description></item>
/// </list>
/// <para>This means:</para>
/// <list type="bullet">
///   <item><description>Config file cannot be used on another machine (DPAPI)</description></item>
///   <item><description>Even on this machine, password is required to decrypt data</description></item>
/// </list>
/// </remarks>
public static class MasterPassword
{
    private const string MasterPasswordPrefix = "MASTER:";
    private const string HashPrefix = "HASH:";
    private const int SaltSize = 16;
    private const int KeySize = 32; // 256-bit AES key
    private const int IvSize = 16;
    private const int Iterations = 100000; // PBKDF2 iterations
    
    // File to store the password hash (DPAPI protected)
    private const string PasswordHashFile = "master.key";
    
    /// <summary>
    /// Checks if a master password has been set up.
    /// </summary>
    public static bool IsConfigured(string configDirectory)
    {
        var hashPath = Path.Combine(configDirectory, PasswordHashFile);
        return File.Exists(hashPath);
    }
    
    /// <summary>
    /// Checks if a value is encrypted with master password.
    /// </summary>
    public static bool IsEncrypted(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(MasterPasswordPrefix);
    }
    
    /// <summary>
    /// Sets up a new master password.
    /// </summary>
    /// <param name="configDirectory">Directory where the hash file will be stored.</param>
    /// <param name="password">The master password to set.</param>
    /// <exception cref="InvalidOperationException">If a password is already configured.</exception>
    public static void Setup(string configDirectory, string password)
    {
        if (IsConfigured(configDirectory))
        {
            throw new InvalidOperationException("Master password is already configured. Use ChangeMasterPassword to change it.");
        }
        
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }
        
        if (password.Length < 8)
        {
            throw new ArgumentException("Password must be at least 8 characters.", nameof(password));
        }
        
        // Generate salt and hash the password
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);
        
        // Store salt + hash, encrypted with DPAPI (so the hash file can't be copied)
        var combined = new byte[SaltSize + hash.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, combined, SaltSize, hash.Length);
        
        var dpapiProtected = ProtectedData.Protect(
            combined,
            Encoding.UTF8.GetBytes("MuninRelay.MasterPassword.v1"),
            DataProtectionScope.LocalMachine);
        
        var hashPath = Path.Combine(configDirectory, PasswordHashFile);
        File.WriteAllBytes(hashPath, dpapiProtected);
    }
    
    /// <summary>
    /// Verifies that the provided password is correct.
    /// </summary>
    /// <param name="configDirectory">Directory where the hash file is stored.</param>
    /// <param name="password">The password to verify.</param>
    /// <returns>True if the password is correct.</returns>
    public static bool Verify(string configDirectory, string password)
    {
        if (!IsConfigured(configDirectory))
        {
            return false;
        }
        
        try
        {
            var hashPath = Path.Combine(configDirectory, PasswordHashFile);
            var dpapiProtected = File.ReadAllBytes(hashPath);
            
            var combined = ProtectedData.Unprotect(
                dpapiProtected,
                Encoding.UTF8.GetBytes("MuninRelay.MasterPassword.v1"),
                DataProtectionScope.LocalMachine);
            
            var salt = new byte[SaltSize];
            var storedHash = new byte[combined.Length - SaltSize];
            Buffer.BlockCopy(combined, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(combined, SaltSize, storedHash, 0, storedHash.Length);
            
            var providedHash = HashPassword(password, salt);
            
            return CryptographicOperations.FixedTimeEquals(storedHash, providedHash);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Changes the master password and re-encrypts all sensitive data.
    /// </summary>
    /// <param name="configDirectory">Directory where files are stored.</param>
    /// <param name="oldPassword">The current password.</param>
    /// <param name="newPassword">The new password to set.</param>
    /// <param name="reEncryptCallback">Callback to re-encrypt configuration with new password.</param>
    /// <exception cref="UnauthorizedAccessException">If the old password is incorrect.</exception>
    public static void ChangePassword(string configDirectory, string oldPassword, string newPassword, Action<string, string> reEncryptCallback)
    {
        if (!Verify(configDirectory, oldPassword))
        {
            throw new UnauthorizedAccessException("Current password is incorrect.");
        }
        
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new ArgumentException("New password cannot be empty.", nameof(newPassword));
        }
        
        if (newPassword.Length < 8)
        {
            throw new ArgumentException("New password must be at least 8 characters.", nameof(newPassword));
        }
        
        // Call the re-encryption callback with old and new passwords
        reEncryptCallback(oldPassword, newPassword);
        
        // Delete old hash file
        var hashPath = Path.Combine(configDirectory, PasswordHashFile);
        File.Delete(hashPath);
        
        // Set up with new password
        Setup(configDirectory, newPassword);
    }
    
    /// <summary>
    /// Encrypts a value using the master password.
    /// </summary>
    /// <param name="configDirectory">Directory where the hash file is stored.</param>
    /// <param name="password">The master password.</param>
    /// <param name="plainText">The value to encrypt.</param>
    /// <returns>The encrypted value with MASTER: prefix.</returns>
    /// <exception cref="UnauthorizedAccessException">If the password is incorrect.</exception>
    public static string Encrypt(string configDirectory, string password, string plainText)
    {
        if (!Verify(configDirectory, password))
        {
            throw new UnauthorizedAccessException("Invalid master password.");
        }
        
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }
        
        // Don't double-encrypt
        if (IsEncrypted(plainText))
        {
            return plainText;
        }
        
        // Generate a random salt for key derivation (unique per encryption)
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        
        // Combine: salt + iv + encrypted data
        var result = new byte[SaltSize + IvSize + encrypted.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, result, SaltSize, IvSize);
        Buffer.BlockCopy(encrypted, 0, result, SaltSize + IvSize, encrypted.Length);
        
        return MasterPasswordPrefix + Convert.ToBase64String(result);
    }
    
    /// <summary>
    /// Decrypts a value using the master password.
    /// </summary>
    /// <param name="password">The master password.</param>
    /// <param name="encryptedValue">The encrypted value (with MASTER: prefix).</param>
    /// <returns>The decrypted value.</returns>
    /// <exception cref="CryptographicException">If decryption fails.</exception>
    public static string Decrypt(string password, string encryptedValue)
    {
        if (string.IsNullOrEmpty(encryptedValue))
        {
            return string.Empty;
        }
        
        // If not encrypted with master password, return as-is (for migration)
        if (!IsEncrypted(encryptedValue))
        {
            return encryptedValue;
        }
        
        var base64 = encryptedValue[MasterPasswordPrefix.Length..];
        var combined = Convert.FromBase64String(base64);
        
        if (combined.Length < SaltSize + IvSize + 16) // Minimum: salt + iv + one AES block
        {
            throw new CryptographicException("Invalid encrypted data format.");
        }
        
        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        var encrypted = new byte[combined.Length - SaltSize - IvSize];
        
        Buffer.BlockCopy(combined, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(combined, SaltSize, iv, 0, IvSize);
        Buffer.BlockCopy(combined, SaltSize + IvSize, encrypted, 0, encrypted.Length);
        
        var key = DeriveKey(password, salt);
        
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        
        return Encoding.UTF8.GetString(decrypted);
    }
    
    /// <summary>
    /// Attempts to decrypt a value, returning null on failure.
    /// </summary>
    public static bool TryDecrypt(string password, string encryptedValue, out string? decrypted)
    {
        decrypted = null;
        
        try
        {
            decrypted = Decrypt(password, encryptedValue);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Prompts for password input from the console (with masking).
    /// </summary>
    /// <param name="prompt">The prompt to display.</param>
    /// <returns>The entered password.</returns>
    public static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        
        var password = new StringBuilder();
        ConsoleKeyInfo key;
        
        do
        {
            key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b"); // Erase the asterisk
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        while (key.Key != ConsoleKey.Enter);
        
        Console.WriteLine();
        return password.ToString();
    }
    
    /// <summary>
    /// Hashes a password with the given salt using PBKDF2.
    /// </summary>
    private static byte[] HashPassword(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);
        
        return pbkdf2.GetBytes(KeySize);
    }
    
    /// <summary>
    /// Derives an encryption key from the password and salt.
    /// </summary>
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);
        
        return pbkdf2.GetBytes(KeySize);
    }
}
