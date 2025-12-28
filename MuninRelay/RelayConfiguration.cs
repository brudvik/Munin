using System.Text.Json;
using System.Text.Json.Serialization;

namespace MuninRelay;

/// <summary>
/// Configuration for the MuninRelay service.
/// </summary>
public class RelayConfiguration
{
    /// <summary>
    /// Port the relay listens on for incoming Munin connections.
    /// </summary>
    public int ListenPort { get; set; } = 6900;

    /// <summary>
    /// Authentication token required from Munin clients.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Path to the SSL certificate (PFX) for client connections.
    /// If empty, a self-signed certificate will be generated.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Password for the SSL certificate.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Enable IP verification on startup and periodically.
    /// </summary>
    public bool EnableIpVerification { get; set; } = true;

    /// <summary>
    /// Interval in minutes for periodic IP checks.
    /// </summary>
    public int IpCheckIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Expected country code for GeoIP verification (e.g., "NL", "US").
    /// If empty, country is not verified.
    /// </summary>
    public string? ExpectedCountryCode { get; set; }

    /// <summary>
    /// Encrypted list of allowed IRC servers (master password protected).
    /// Use AllowedServers property to access the decrypted list at runtime.
    /// </summary>
    public string? EncryptedAllowedServers { get; set; }

    /// <summary>
    /// List of allowed IRC servers to relay to (runtime only, not serialized).
    /// Empty list means all servers are allowed.
    /// </summary>
    [JsonIgnore]
    public List<AllowedServer> AllowedServers { get; set; } = new();
    
    /// <summary>
    /// The configuration directory path (runtime only).
    /// </summary>
    [JsonIgnore]
    public string ConfigDirectory { get; set; } = string.Empty;
    
    /// <summary>
    /// The master password used for encryption (runtime only, never serialized).
    /// </summary>
    [JsonIgnore]
    internal string RuntimeMasterPassword { get; set; } = string.Empty;

    /// <summary>
    /// Maximum concurrent connections allowed.
    /// </summary>
    public int MaxConnections { get; set; } = 10;

    /// <summary>
    /// Log file path. If empty, logs to console only.
    /// </summary>
    public string? LogFilePath { get; set; }

    /// <summary>
    /// Enable verbose logging.
    /// </summary>
    public bool VerboseLogging { get; set; } = false;

    /// <summary>
    /// Loads configuration from a JSON file.
    /// Requires master password to decrypt sensitive data.
    /// </summary>
    /// <param name="path">Path to the config file.</param>
    /// <param name="masterPassword">The master password to decrypt sensitive data.</param>
    /// <param name="isNewSetup">Output: true if this is a new configuration that was just created.</param>
    /// <returns>The loaded configuration with decrypted values.</returns>
    public static RelayConfiguration Load(string path, string masterPassword, out bool isNewSetup)
    {
        var directory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
        isNewSetup = !File.Exists(path);
        
        if (isNewSetup)
        {
            // Create default config with encrypted token and servers
            var plainToken = GenerateSecureToken();
            var defaultServers = new List<AllowedServer>
            {
                new() { Hostname = "irc.libera.chat", Port = 6697, UseSsl = true },
                new() { Hostname = "irc.efnet.org", Port = 6697, UseSsl = true }
            };
            
            var defaultConfig = new RelayConfiguration
            {
                AuthToken = plainToken, // Will be encrypted on save
                AllowedServers = defaultServers,
                ConfigDirectory = directory,
                RuntimeMasterPassword = masterPassword
            };
            defaultConfig.Save(path, masterPassword);
            
            return defaultConfig;
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<RelayConfiguration>(json, JsonOptions) ?? new RelayConfiguration();
        config.ConfigDirectory = directory;
        config.RuntimeMasterPassword = masterPassword;
        
        // Decrypt token using master password
        if (MasterPassword.IsEncrypted(config.AuthToken))
        {
            if (!MasterPassword.TryDecrypt(masterPassword, config.AuthToken, out var plainToken))
            {
                throw new InvalidOperationException(
                    "Failed to decrypt AuthToken. Wrong password or corrupted data.");
            }
            config.AuthToken = plainToken!;;
        }
        else if (TokenProtection.IsEncrypted(config.AuthToken))
        {
            // Migration from old DPAPI-only format
            if (!TokenProtection.TryDecrypt(config.AuthToken, out var plainToken))
            {
                throw new InvalidOperationException(
                    "Failed to decrypt AuthToken (legacy format). " +
                    "This config file was created on a different machine.");
            }
            config.AuthToken = plainToken!;
            
            // Re-save with new master password encryption
            config.Save(path, masterPassword);
        }
        
        // Decrypt server list using master password
        if (MasterPassword.IsEncrypted(config.EncryptedAllowedServers))
        {
            config.AllowedServers = DecryptServerList(masterPassword, config.EncryptedAllowedServers!);
        }
        else if (TokenProtection.IsEncrypted(config.EncryptedAllowedServers))
        {
            // Migration from old DPAPI-only format
            config.AllowedServers = DecryptServerListLegacy(config.EncryptedAllowedServers!);
            
            // Re-save with new master password encryption
            config.Save(path, masterPassword);
        }
        
        return config;
    }

    /// <summary>
    /// Saves configuration to a JSON file using the runtime master password.
    /// Use this overload when the config was loaded with Load() and the password is cached.
    /// </summary>
    /// <param name="path">Path to the config file.</param>
    /// <exception cref="InvalidOperationException">If RuntimeMasterPassword is not set.</exception>
    public void Save(string path)
    {
        if (string.IsNullOrEmpty(RuntimeMasterPassword))
        {
            throw new InvalidOperationException(
                "RuntimeMasterPassword is not set. Use Save(path, masterPassword) instead, " +
                "or ensure the config was loaded with Load().");
        }
        
        Save(path, RuntimeMasterPassword);
    }

    /// <summary>
    /// Saves configuration to a JSON file.
    /// Encrypts sensitive data using the master password.
    /// </summary>
    /// <param name="path">Path to the config file.</param>
    /// <param name="masterPassword">The master password to encrypt sensitive data.</param>
    public void Save(string path, string masterPassword)
    {
        // Cache the password for future saves
        RuntimeMasterPassword = masterPassword;
        var directory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
        
        // Create a copy for serialization with encrypted values
        var configToSave = new RelayConfiguration
        {
            ListenPort = ListenPort,
            AuthToken = MasterPassword.Encrypt(directory, masterPassword, AuthToken),
            CertificatePath = CertificatePath,
            CertificatePassword = CertificatePassword,
            EnableIpVerification = EnableIpVerification,
            IpCheckIntervalMinutes = IpCheckIntervalMinutes,
            ExpectedCountryCode = ExpectedCountryCode,
            EncryptedAllowedServers = AllowedServers.Count > 0 
                ? EncryptServerList(directory, masterPassword, AllowedServers) 
                : null,
            MaxConnections = MaxConnections,
            LogFilePath = LogFilePath,
            VerboseLogging = VerboseLogging
        };
        
        var json = JsonSerializer.Serialize(configToSave, JsonOptions);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Encrypts the server list using master password.
    /// </summary>
    private static string EncryptServerList(string configDir, string masterPassword, List<AllowedServer> servers)
    {
        var json = JsonSerializer.Serialize(servers, JsonOptions);
        return MasterPassword.Encrypt(configDir, masterPassword, json);
    }

    /// <summary>
    /// Decrypts the server list using master password.
    /// </summary>
    private static List<AllowedServer> DecryptServerList(string masterPassword, string encrypted)
    {
        try
        {
            if (!MasterPassword.TryDecrypt(masterPassword, encrypted, out var json) || string.IsNullOrEmpty(json))
            {
                return new List<AllowedServer>();
            }
            return JsonSerializer.Deserialize<List<AllowedServer>>(json, JsonOptions) ?? new List<AllowedServer>();
        }
        catch
        {
            return new List<AllowedServer>();
        }
    }
    
    /// <summary>
    /// Decrypts the server list using legacy DPAPI (for migration).
    /// </summary>
    private static List<AllowedServer> DecryptServerListLegacy(string encrypted)
    {
        try
        {
            if (!TokenProtection.TryDecrypt(encrypted, out var json) || string.IsNullOrEmpty(json))
            {
                return new List<AllowedServer>();
            }
            return JsonSerializer.Deserialize<List<AllowedServer>>(json, JsonOptions) ?? new List<AllowedServer>();
        }
        catch
        {
            return new List<AllowedServer>();
        }
    }

    /// <summary>
    /// Generates a cryptographically secure token.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Represents an allowed IRC server destination.
/// </summary>
public class AllowedServer
{
    /// <summary>
    /// IRC server hostname.
    /// </summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// IRC server port.
    /// </summary>
    public int Port { get; set; } = 6667;

    /// <summary>
    /// Whether to use SSL/TLS for the IRC connection.
    /// </summary>
    public bool UseSsl { get; set; } = true;
}
