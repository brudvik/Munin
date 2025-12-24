using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Serilog;

namespace Munin.Core.Services;

/// <summary>
/// Provides secure file storage with optional encryption.
/// Manages reading and writing files that may be encrypted.
/// </summary>
/// <remarks>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Transparent encryption/decryption of files</description></item>
///   <item><description>Support for both encrypted and plaintext modes</description></item>
///   <item><description>Metadata storage for encryption state</description></item>
///   <item><description>Migration from plaintext to encrypted and back</description></item>
/// </list>
/// </remarks>
public class SecureStorageService
{
    private readonly ILogger _logger;
    private readonly EncryptionService _encryptionService;
    private readonly string _basePath;
    private readonly string _metadataPath;
    
    private EncryptionMetadata? _metadata;
    
    /// <summary>
    /// Gets whether encryption is enabled for this storage.
    /// </summary>
    public bool IsEncryptionEnabled => _metadata?.IsEncryptionEnabled ?? false;
    
    /// <summary>
    /// Gets whether the storage is currently unlocked (ready for use).
    /// </summary>
    public bool IsUnlocked => !IsEncryptionEnabled || _encryptionService.IsUnlocked;
    
    /// <summary>
    /// Gets the encryption service for advanced operations.
    /// </summary>
    public EncryptionService EncryptionService => _encryptionService;
    
    /// <summary>
    /// Gets the base path for storage.
    /// </summary>
    public string BasePath => _basePath;
    
    /// <summary>
    /// Initializes a new instance of the SecureStorageService.
    /// </summary>
    /// <param name="basePath">The base directory for storage.</param>
    public SecureStorageService(string basePath)
    {
        _logger = SerilogConfig.ForContext<SecureStorageService>();
        _encryptionService = new EncryptionService();
        _basePath = basePath;
        _metadataPath = Path.Combine(_basePath, "encryption.meta");
        
        Directory.CreateDirectory(_basePath);
        LoadMetadata();
    }
    
    /// <summary>
    /// Enables encryption with a new master password.
    /// If there are existing plaintext files, they will be encrypted.
    /// </summary>
    /// <param name="password">The master password to use.</param>
    /// <returns>True if encryption was enabled successfully.</returns>
    public async Task<bool> EnableEncryptionAsync(string password)
    {
        if (IsEncryptionEnabled)
        {
            _logger.Warning("Encryption is already enabled");
            return false;
        }
        
        try
        {
            // Read all existing plaintext files before enabling encryption
            var existingFiles = await ReadAllPlaintextFilesAsync();
            
            // Initialize encryption with new password
            var salt = _encryptionService.InitializeNew(password);
            var verificationToken = _encryptionService.CreateVerificationToken();
            
            // Create and save metadata
            _metadata = new EncryptionMetadata
            {
                IsEncryptionEnabled = true,
                Salt = Convert.ToBase64String(salt),
                VerificationToken = Convert.ToBase64String(verificationToken),
                CreatedAt = DateTime.UtcNow,
                Version = 1
            };
            
            await SaveMetadataAsync();
            
            // Re-write all existing files (now they will be encrypted)
            foreach (var (relativePath, content) in existingFiles)
            {
                await WriteTextAsync(relativePath, content);
                _logger.Debug("Encrypted existing file: {Path}", relativePath);
            }
            
            _logger.Information("Encryption enabled successfully, encrypted {Count} existing files", existingFiles.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to enable encryption");
            return false;
        }
    }
    
    /// <summary>
    /// Reads all existing plaintext files in the storage directory.
    /// </summary>
    private async Task<List<(string relativePath, string content)>> ReadAllPlaintextFilesAsync()
    {
        var result = new List<(string, string)>();
        
        try
        {
            // Get all files in the base path, excluding metadata
            var files = Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("encryption.meta") && 
                           !f.EndsWith(".tmp") &&
                           Path.GetFileName(f) != "encryption.meta");
            
            foreach (var fullPath in files)
            {
                try
                {
                    var relativePath = Path.GetRelativePath(_basePath, fullPath);
                    
                    // Check if file is already encrypted (has magic bytes)
                    var bytes = await File.ReadAllBytesAsync(fullPath);
                    if (IsEncryptedFile(bytes))
                    {
                        _logger.Debug("Skipping already encrypted file: {Path}", relativePath);
                        continue;
                    }
                    
                    // Read as plaintext
                    var content = System.Text.Encoding.UTF8.GetString(bytes);
                    result.Add((relativePath, content));
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to read file for encryption: {Path}", fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to enumerate files for encryption");
        }
        
        return result;
    }
    
    /// <summary>
    /// Checks if a file appears to be encrypted (has magic bytes).
    /// </summary>
    private static bool IsEncryptedFile(byte[] data)
    {
        if (data.Length < 8)
            return false;
        
        // Check for "IRCENC01" magic bytes
        return data[0] == 'I' && data[1] == 'R' && data[2] == 'C' && data[3] == 'E' &&
               data[4] == 'N' && data[5] == 'C' && data[6] == '0' && data[7] == '1';
    }
    
    /// <summary>
    /// Unlocks the storage with the master password.
    /// </summary>
    /// <param name="password">The master password.</param>
    /// <returns>True if unlock was successful.</returns>
    public bool Unlock(string password)
    {
        if (!IsEncryptionEnabled)
        {
            _logger.Debug("Encryption not enabled, unlock not required");
            return true;
        }
        
        if (_metadata == null)
        {
            _logger.Error("No encryption metadata found");
            return false;
        }
        
        try
        {
            var salt = Convert.FromBase64String(_metadata.Salt);
            var verificationToken = Convert.FromBase64String(_metadata.VerificationToken);
            
            // Verify password
            if (!EncryptionService.VerifyPassword(password, salt, verificationToken))
            {
                _logger.Warning("Invalid password provided");
                return false;
            }
            
            // Initialize with correct password
            _encryptionService.Initialize(password, salt);
            _logger.Information("Storage unlocked successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to unlock storage");
            return false;
        }
    }
    
    /// <summary>
    /// Locks the storage, clearing encryption keys from memory.
    /// </summary>
    public void Lock()
    {
        _encryptionService.Lock();
        _logger.Information("Storage locked");
    }
    
    /// <summary>
    /// Disables encryption and decrypts all files.
    /// </summary>
    /// <param name="password">The current master password for verification.</param>
    /// <returns>True if encryption was disabled successfully.</returns>
    public async Task<bool> DisableEncryptionAsync(string password)
    {
        if (!IsEncryptionEnabled)
        {
            _logger.Warning("Encryption is not enabled");
            return false;
        }
        
        // Verify password first
        if (!Unlock(password))
        {
            _logger.Warning("Invalid password, cannot disable encryption");
            return false;
        }
        
        try
        {
            // Decrypt all encrypted files
            await DecryptAllFilesAsync();
            
            // Clear metadata
            _metadata = new EncryptionMetadata { IsEncryptionEnabled = false };
            await SaveMetadataAsync();
            
            // Lock the service
            Lock();
            
            _logger.Information("Encryption disabled successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to disable encryption");
            return false;
        }
    }
    
    /// <summary>
    /// Changes the master password.
    /// </summary>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <returns>True if password was changed successfully.</returns>
    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        if (!IsEncryptionEnabled)
        {
            _logger.Warning("Encryption is not enabled");
            return false;
        }
        
        // Verify current password
        if (!Unlock(currentPassword))
        {
            _logger.Warning("Invalid current password");
            return false;
        }
        
        try
        {
            // Read all encrypted files with old key
            var files = await ReadAllEncryptedFilesAsync();
            
            // Change password (generates new salt and key)
            var newSalt = _encryptionService.ChangePassword(newPassword);
            var newVerificationToken = _encryptionService.CreateVerificationToken();
            
            // Re-encrypt all files with new key
            foreach (var (path, content) in files)
            {
                await WriteFileInternalAsync(path, content, encrypt: true);
            }
            
            // Update metadata
            _metadata!.Salt = Convert.ToBase64String(newSalt);
            _metadata.VerificationToken = Convert.ToBase64String(newVerificationToken);
            _metadata.ModifiedAt = DateTime.UtcNow;
            await SaveMetadataAsync();
            
            _logger.Information("Password changed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to change password");
            return false;
        }
    }
    
    /// <summary>
    /// Writes text content to a file, encrypting if encryption is enabled.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <param name="content">The text content to write.</param>
    public async Task WriteTextAsync(string relativePath, string content)
    {
        var fullPath = GetFullPath(relativePath);
        await WriteFileInternalAsync(fullPath, System.Text.Encoding.UTF8.GetBytes(content), IsEncryptionEnabled);
    }
    
    /// <summary>
    /// Writes binary content to a file, encrypting if encryption is enabled.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <param name="content">The binary content to write.</param>
    public async Task WriteBytesAsync(string relativePath, byte[] content)
    {
        var fullPath = GetFullPath(relativePath);
        await WriteFileInternalAsync(fullPath, content, IsEncryptionEnabled);
    }
    
    /// <summary>
    /// Writes an object as JSON to a file, encrypting if encryption is enabled.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <param name="obj">The object to serialize and write.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    [RequiresUnreferencedCode("Use WriteJsonAsync<T>(string, T, JsonTypeInfo<T>) for AOT compatibility.")]
    public async Task WriteJsonAsync<T>(string relativePath, T obj, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(obj, options ?? new JsonSerializerOptions { WriteIndented = true });
        await WriteTextAsync(relativePath, json);
    }
    
    /// <summary>
    /// Writes an object as JSON to a file, encrypting if encryption is enabled.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <param name="obj">The object to serialize and write.</param>
    /// <param name="jsonTypeInfo">The JSON type info for AOT-compatible serialization.</param>
    public async Task WriteJsonAsync<T>(string relativePath, T obj, JsonTypeInfo<T> jsonTypeInfo)
    {
        var json = JsonSerializer.Serialize(obj, jsonTypeInfo);
        await WriteTextAsync(relativePath, json);
    }
    
    /// <summary>
    /// Reads and deserializes JSON from a file, decrypting if necessary.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <param name="jsonTypeInfo">The JSON type info for AOT-compatible deserialization.</param>
    /// <returns>The deserialized object, or default if file doesn't exist.</returns>
    public async Task<T?> ReadJsonAsync<T>(string relativePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        var json = await ReadTextAsync(relativePath);
        if (json == null) return default;
        
        return JsonSerializer.Deserialize(json, jsonTypeInfo);
    }
    
    /// <summary>
    /// Reads text content from a file synchronously, decrypting if necessary.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <returns>The text content, or null if file doesn't exist.</returns>
    public string? ReadTextSync(string relativePath)
    {
        var bytes = ReadBytesSync(relativePath);
        return bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }
    
    /// <summary>
    /// Writes text content to a file synchronously, encrypting if encryption is enabled.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <param name="content">The text content to write.</param>
    public void WriteTextSync(string relativePath, string content)
    {
        var fullPath = GetFullPath(relativePath);
        WriteFileInternalSync(fullPath, System.Text.Encoding.UTF8.GetBytes(content), IsEncryptionEnabled);
    }
    
    /// <summary>
    /// Reads binary content from a file synchronously, decrypting if necessary.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <returns>The binary content, or null if file doesn't exist.</returns>
    public byte[]? ReadBytesSync(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        return ReadFileInternalSync(fullPath);
    }
    
    /// <summary>
    /// Reads text content from a file, decrypting if necessary.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <returns>The text content, or null if file doesn't exist.</returns>
    public async Task<string?> ReadTextAsync(string relativePath)
    {
        var bytes = await ReadBytesAsync(relativePath);
        return bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }
    
    /// <summary>
    /// Reads binary content from a file, decrypting if necessary.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <returns>The binary content, or null if file doesn't exist.</returns>
    public async Task<byte[]?> ReadBytesAsync(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        return await ReadFileInternalAsync(fullPath);
    }
    
    /// <summary>
    /// Reads and deserializes JSON from a file, decrypting if necessary.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>The deserialized object, or default if file doesn't exist.</returns>
    [RequiresUnreferencedCode("Use ReadJsonAsync<T>(string, JsonTypeInfo<T>) for AOT compatibility.")]
    public async Task<T?> ReadJsonAsync<T>(string relativePath, JsonSerializerOptions? options = null)
    {
        var json = await ReadTextAsync(relativePath);
        if (json == null) return default;
        
        return JsonSerializer.Deserialize<T>(json, options);
    }
    
    /// <summary>
    /// Checks if a file exists.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    /// <returns>True if the file exists.</returns>
    public bool FileExists(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        return File.Exists(fullPath);
    }
    
    /// <summary>
    /// Deletes a file if it exists.
    /// </summary>
    /// <param name="relativePath">The path relative to the base directory.</param>
    public void DeleteFile(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
    
    /// <summary>
    /// Gets the full path for a relative path.
    /// </summary>
    /// <param name="relativePath">The relative path.</param>
    /// <returns>The full path.</returns>
    public string GetFullPath(string relativePath)
    {
        var fullPath = Path.Combine(_basePath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return fullPath;
    }
    
    /// <summary>
    /// Resets all data, deleting everything including encryption settings.
    /// </summary>
    public void ResetAll()
    {
        Lock();
        
        try
        {
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
            Directory.CreateDirectory(_basePath);
            
            _metadata = null;
            
            _logger.Information("All data reset");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reset all data");
            throw;
        }
    }
    
    /// <summary>
    /// Encrypts all existing plaintext files after enabling encryption.
    /// </summary>
    public async Task EncryptAllFilesAsync()
    {
        if (!IsUnlocked)
            throw new InvalidOperationException("Storage must be unlocked to encrypt files.");
        
        var files = Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta") && !EncryptionService.IsFileEncrypted(f));
        
        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllBytesAsync(file);
                await WriteFileInternalAsync(file, content, encrypt: true);
                _logger.Debug("Encrypted file: {File}", file);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to encrypt file: {File}", file);
            }
        }
    }
    
    private async Task DecryptAllFilesAsync()
    {
        if (!IsUnlocked)
            throw new InvalidOperationException("Storage must be unlocked to decrypt files.");
        
        var files = Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta") && EncryptionService.IsFileEncrypted(f));
        
        foreach (var file in files)
        {
            try
            {
                var content = await ReadFileInternalAsync(file);
                if (content != null)
                {
                    await File.WriteAllBytesAsync(file, content);
                    _logger.Debug("Decrypted file: {File}", file);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to decrypt file: {File}", file);
            }
        }
    }
    
    private async Task<List<(string Path, byte[] Content)>> ReadAllEncryptedFilesAsync()
    {
        var result = new List<(string, byte[])>();
        
        var files = Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta"));
        
        foreach (var file in files)
        {
            try
            {
                var content = await ReadFileInternalAsync(file);
                if (content != null)
                {
                    result.Add((file, content));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to read file: {File}", file);
            }
        }
        
        return result;
    }
    
    private async Task WriteFileInternalAsync(string fullPath, byte[] content, bool encrypt)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        if (encrypt && IsUnlocked)
        {
            var encrypted = _encryptionService.Encrypt(content);
            await File.WriteAllBytesAsync(fullPath, encrypted);
        }
        else
        {
            await File.WriteAllBytesAsync(fullPath, content);
        }
    }
    
    private void WriteFileInternalSync(string fullPath, byte[] content, bool encrypt)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        if (encrypt && IsUnlocked)
        {
            var encrypted = _encryptionService.Encrypt(content);
            File.WriteAllBytes(fullPath, encrypted);
        }
        else
        {
            File.WriteAllBytes(fullPath, content);
        }
    }
    
    private byte[]? ReadFileInternalSync(string fullPath)
    {
        if (!File.Exists(fullPath))
            return null;
        
        var data = File.ReadAllBytes(fullPath);
        
        // Check if file is encrypted
        if (EncryptionService.IsEncrypted(data))
        {
            if (!_encryptionService.IsUnlocked)
                throw new InvalidOperationException("Storage is locked. Call Unlock first.");
            
            return _encryptionService.Decrypt(data);
        }
        
        return data;
    }
    
    private async Task<byte[]?> ReadFileInternalAsync(string fullPath)
    {
        if (!File.Exists(fullPath))
            return null;
        
        var data = await File.ReadAllBytesAsync(fullPath);
        
        // Check if file is encrypted
        if (EncryptionService.IsEncrypted(data))
        {
            if (!_encryptionService.IsUnlocked)
                throw new InvalidOperationException("Storage is locked. Call Unlock first.");
            
            return _encryptionService.Decrypt(data);
        }
        
        return data;
    }
    
    private void LoadMetadata()
    {
        if (File.Exists(_metadataPath))
        {
            try
            {
                var json = File.ReadAllText(_metadataPath);
                _metadata = JsonSerializer.Deserialize(json, JsonSourceGenerationContext.Default.EncryptionMetadata);
                _logger.Debug("Encryption metadata loaded. Enabled: {Enabled}", _metadata?.IsEncryptionEnabled);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load encryption metadata");
                _metadata = new EncryptionMetadata();
            }
        }
        else
        {
            _metadata = new EncryptionMetadata();
        }
    }
    
    private async Task SaveMetadataAsync()
    {
        if (_metadata == null) return;
        var json = JsonSerializer.Serialize(_metadata, JsonSourceGenerationContext.Default.EncryptionMetadata);
        await File.WriteAllTextAsync(_metadataPath, json);
    }
}

/// <summary>
/// Metadata about the encryption state of the storage.
/// This file is NOT encrypted.
/// </summary>
public class EncryptionMetadata
{
    /// <summary>
    /// Whether encryption is enabled.
    /// </summary>
    public bool IsEncryptionEnabled { get; set; }
    
    /// <summary>
    /// Base64-encoded salt for key derivation.
    /// </summary>
    public string Salt { get; set; } = string.Empty;
    
    /// <summary>
    /// Base64-encoded verification token for password validation.
    /// </summary>
    public string VerificationToken { get; set; } = string.Empty;
    
    /// <summary>
    /// When encryption was first enabled.
    /// </summary>
    public DateTime? CreatedAt { get; set; }
    
    /// <summary>
    /// When the password was last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
    
    /// <summary>
    /// Version of the encryption format.
    /// </summary>
    public int Version { get; set; } = 1;
}
