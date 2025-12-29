using System.Text.Json;
using System.Text.Json.Serialization;
using Munin.Core.Services;
using Serilog;

namespace Munin.Agent.Configuration;

/// <summary>
/// Service for loading, saving, and managing agent configuration.
/// Handles encryption/decryption of sensitive fields transparently.
/// </summary>
public class AgentConfigurationService
{
    private readonly ILogger _logger;
    private readonly EncryptionService _encryptionService;
    private readonly string _configPath;
    private AgentConfiguration? _configuration;
    private bool _isUnlocked;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Gets the current configuration. Throws if not loaded.
    /// </summary>
    public AgentConfiguration Configuration => _configuration 
        ?? throw new InvalidOperationException("Configuration not loaded");

    /// <summary>
    /// Gets whether the configuration has been loaded and decrypted.
    /// </summary>
    public bool IsLoaded => _configuration != null;

    /// <summary>
    /// Gets whether the configuration is encrypted.
    /// </summary>
    public bool IsEncrypted => _configuration?.Encryption?.IsEncrypted ?? false;

    /// <summary>
    /// Gets whether the configuration is unlocked (decrypted).
    /// </summary>
    public bool IsUnlocked => _isUnlocked;

    /// <summary>
    /// Gets the encryption service.
    /// </summary>
    public EncryptionService EncryptionService => _encryptionService;

    public AgentConfigurationService()
    {
        _logger = Log.ForContext<AgentConfigurationService>();
        _encryptionService = new EncryptionService();
        
        // Determine config path
        _configPath = Environment.GetEnvironmentVariable("MUNIN_AGENT_CONFIG")
            ?? Path.Combine(AppContext.BaseDirectory, "agent.json");
    }

    /// <summary>
    /// Creates a new configuration service with a specific config file path.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    public AgentConfigurationService(string configPath)
    {
        _logger = Log.ForContext<AgentConfigurationService>();
        _encryptionService = new EncryptionService();
        _configPath = configPath;
    }

    /// <summary>
    /// Loads the configuration from disk.
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            _logger.Warning("Configuration file not found at {Path}, creating default", _configPath);
            _configuration = new AgentConfiguration();
            await SaveAsync();
            return;
        }

        var json = await File.ReadAllTextAsync(_configPath);
        _configuration = JsonSerializer.Deserialize<AgentConfiguration>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize configuration");

        _logger.Information("Configuration loaded from {Path}", _configPath);

        // Check if encryption is enabled
        if (_configuration.Encryption?.IsEncrypted == true)
        {
            _logger.Information("Configuration is encrypted - unlock required");
            _isUnlocked = false;
        }
        else
        {
            _isUnlocked = true;
        }
    }

    /// <summary>
    /// Unlocks the configuration with the master password.
    /// </summary>
    /// <param name="password">The master password.</param>
    /// <returns>True if unlock successful.</returns>
    public bool Unlock(string password)
    {
        if (_configuration == null)
            throw new InvalidOperationException("Configuration not loaded");

        if (!IsEncrypted)
        {
            _isUnlocked = true;
            return true;
        }

        var metadata = _configuration.Encryption!;
        
        try
        {
            // Initialize encryption service with stored salt
            var salt = Convert.FromBase64String(metadata.Salt);
            _encryptionService.Initialize(password, salt);

            // Verify password by decrypting verification token
            var verificationToken = Convert.FromBase64String(metadata.VerificationToken);
            var decrypted = _encryptionService.Decrypt(verificationToken);
            
            // Token should decrypt to "MUNIN_AGENT_VERIFIED"
            var expected = System.Text.Encoding.UTF8.GetBytes("MUNIN_AGENT_VERIFIED");
            if (!decrypted.SequenceEqual(expected))
            {
                _encryptionService.Lock();
                return false;
            }

            _isUnlocked = true;
            _logger.Information("Configuration unlocked successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to unlock configuration - wrong password?");
            _encryptionService.Lock();
            return false;
        }
    }

    /// <summary>
    /// Gets a decrypted value from an encrypted field.
    /// </summary>
    public string? GetDecryptedValue(EncryptedValue? encrypted)
    {
        if (encrypted == null || string.IsNullOrEmpty(encrypted.Data))
            return null;

        if (!_isUnlocked)
            throw new InvalidOperationException("Configuration is locked");

        if (!IsEncrypted)
        {
            // Not encrypted, data is stored as plain Base64
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encrypted.Data));
        }

        var encryptedBytes = Convert.FromBase64String(encrypted.Data);
        var decrypted = _encryptionService.Decrypt(encryptedBytes);
        return System.Text.Encoding.UTF8.GetString(decrypted);
    }

    /// <summary>
    /// Sets an encrypted value.
    /// </summary>
    public EncryptedValue SetEncryptedValue(string plaintext)
    {
        if (!_isUnlocked)
            throw new InvalidOperationException("Configuration is locked");

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

        if (!IsEncrypted)
        {
            // Store as plain Base64 if encryption not enabled
            return new EncryptedValue
            {
                Data = Convert.ToBase64String(plaintextBytes),
                Algorithm = "PLAIN"
            };
        }

        var encrypted = _encryptionService.Encrypt(plaintextBytes);
        return new EncryptedValue
        {
            Data = Convert.ToBase64String(encrypted),
            Algorithm = "AES-256-GCM"
        };
    }

    /// <summary>
    /// Enables encryption for the configuration with a new master password.
    /// </summary>
    public async Task EnableEncryptionAsync(string password)
    {
        if (_configuration == null)
            throw new InvalidOperationException("Configuration not loaded");

        if (IsEncrypted)
            throw new InvalidOperationException("Configuration is already encrypted");

        // Collect all current plaintext values
        var authToken = GetDecryptedValue(_configuration.AuthTokenEncrypted);
        var tlsPassword = GetDecryptedValue(_configuration.TlsCertificatePasswordEncrypted);
        
        var serverSecrets = _configuration.Servers.Select(s => new
        {
            Server = s,
            ServerPassword = GetDecryptedValue(s.ServerPasswordEncrypted),
            NickservPassword = GetDecryptedValue(s.NickservPasswordEncrypted),
            SaslUsername = GetDecryptedValue(s.SaslUsernameEncrypted),
            SaslPassword = GetDecryptedValue(s.SaslPasswordEncrypted),
            ChannelKeys = s.Channels.ToDictionary(c => c.Name, c => GetDecryptedValue(c.KeyEncrypted))
        }).ToList();

        // Initialize encryption
        var salt = _encryptionService.InitializeNew(password);
        
        // Create verification token
        var verificationData = System.Text.Encoding.UTF8.GetBytes("MUNIN_AGENT_VERIFIED");
        var verificationToken = _encryptionService.Encrypt(verificationData);

        // Set metadata
        _configuration.Encryption = new EncryptionMetadata
        {
            IsEncrypted = true,
            Salt = Convert.ToBase64String(salt),
            VerificationToken = Convert.ToBase64String(verificationToken),
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };

        _isUnlocked = true;

        // Re-encrypt all values
        if (!string.IsNullOrEmpty(authToken))
            _configuration.AuthTokenEncrypted = SetEncryptedValue(authToken);
        
        if (!string.IsNullOrEmpty(tlsPassword))
            _configuration.TlsCertificatePasswordEncrypted = SetEncryptedValue(tlsPassword);

        foreach (var item in serverSecrets)
        {
            if (!string.IsNullOrEmpty(item.ServerPassword))
                item.Server.ServerPasswordEncrypted = SetEncryptedValue(item.ServerPassword);
            
            if (!string.IsNullOrEmpty(item.NickservPassword))
                item.Server.NickservPasswordEncrypted = SetEncryptedValue(item.NickservPassword);
            
            if (!string.IsNullOrEmpty(item.SaslUsername))
                item.Server.SaslUsernameEncrypted = SetEncryptedValue(item.SaslUsername);
            
            if (!string.IsNullOrEmpty(item.SaslPassword))
                item.Server.SaslPasswordEncrypted = SetEncryptedValue(item.SaslPassword);

            foreach (var channel in item.Server.Channels)
            {
                if (item.ChannelKeys.TryGetValue(channel.Name, out var key) && !string.IsNullOrEmpty(key))
                    channel.KeyEncrypted = SetEncryptedValue(key);
            }
        }

        await SaveAsync();
        _logger.Information("Configuration encryption enabled");
    }

    /// <summary>
    /// Saves the current configuration to disk.
    /// </summary>
    public async Task SaveAsync()
    {
        if (_configuration == null)
            throw new InvalidOperationException("No configuration to save");

        var json = JsonSerializer.Serialize(_configuration, JsonOptions);
        
        // Ensure directory exists
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(_configPath, json);
        _logger.Debug("Configuration saved to {Path}", _configPath);
    }

    /// <summary>
    /// Gets the auth token for control connections.
    /// </summary>
    public string GetAuthToken()
    {
        return GetDecryptedValue(_configuration?.AuthTokenEncrypted) 
            ?? throw new InvalidOperationException("Auth token not configured");
    }

    /// <summary>
    /// Gets the TLS certificate password.
    /// </summary>
    public string? GetTlsCertificatePassword()
    {
        return GetDecryptedValue(_configuration?.TlsCertificatePasswordEncrypted);
    }

    /// <summary>
    /// Gets the server password for a specific server.
    /// </summary>
    public string? GetServerPassword(IrcServerConfiguration server)
    {
        return GetDecryptedValue(server.ServerPasswordEncrypted);
    }

    /// <summary>
    /// Gets the NickServ password for a specific server.
    /// </summary>
    public string? GetNickservPassword(IrcServerConfiguration server)
    {
        return GetDecryptedValue(server.NickservPasswordEncrypted);
    }

    /// <summary>
    /// Gets the SASL credentials for a specific server.
    /// </summary>
    public (string? username, string? password) GetSaslCredentials(IrcServerConfiguration server)
    {
        return (
            GetDecryptedValue(server.SaslUsernameEncrypted),
            GetDecryptedValue(server.SaslPasswordEncrypted)
        );
    }

    /// <summary>
    /// Gets the channel key for a specific channel.
    /// </summary>
    public string? GetChannelKey(ChannelConfiguration channel)
    {
        return GetDecryptedValue(channel.KeyEncrypted);
    }
}
