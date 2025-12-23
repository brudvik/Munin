using System.Text.Json;
using System.Text.Json.Serialization;
using IrcClient.Core.Models;
using Serilog;

namespace IrcClient.Core.Services;

/// <summary>
/// Handles saving and loading IRC client configuration.
/// </summary>
/// <remarks>
/// <para>Persists server configurations, user preferences, and application settings.</para>
/// <para>Supports optional encryption via SecureStorageService.</para>
/// <para>Default location: %APPDATA%\IrcClient\</para>
/// </remarks>
public class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string ConfigFileName = "config.json";
    
    private readonly ILogger _logger;
    private readonly SecureStorageService _storage;
    private ClientConfiguration _configuration = new();

    /// <summary>
    /// Gets the current client configuration.
    /// </summary>
    public ClientConfiguration Configuration => _configuration;
    
    /// <summary>
    /// Gets the secure storage service used by this configuration service.
    /// </summary>
    public SecureStorageService Storage => _storage;
    
    /// <summary>
    /// Gets whether encryption is enabled.
    /// </summary>
    public bool IsEncryptionEnabled => _storage.IsEncryptionEnabled;
    
    /// <summary>
    /// Gets whether the storage is unlocked and ready for use.
    /// </summary>
    public bool IsUnlocked => _storage.IsUnlocked;

    /// <summary>
    /// Initializes a new instance of the ConfigurationService.
    /// </summary>
    /// <param name="basePath">Optional custom path for the configuration directory. Uses default path if null.</param>
    public ConfigurationService(string? basePath = null)
    {
        var path = basePath ?? GetDefaultBasePath();
        _storage = new SecureStorageService(path);
        _logger = SerilogConfig.ForContext<ConfigurationService>();
    }
    
    /// <summary>
    /// Initializes a new instance of the ConfigurationService with a shared storage service.
    /// </summary>
    /// <param name="storage">The secure storage service to use.</param>
    public ConfigurationService(SecureStorageService storage)
    {
        _storage = storage;
        _logger = SerilogConfig.ForContext<ConfigurationService>();
    }

    private static string GetDefaultBasePath()
    {
        return PortableMode.BasePath;
    }

    /// <summary>
    /// Loads the configuration from disk asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if encryption is enabled but storage is locked.</exception>
    public async Task LoadAsync()
    {
        try
        {
            if (!_storage.IsUnlocked)
            {
                throw new InvalidOperationException("Storage is locked. Unlock with password first.");
            }
            
            if (_storage.FileExists(ConfigFileName))
            {
                _configuration = await _storage.ReadJsonAsync<ClientConfiguration>(ConfigFileName, JsonOptions) 
                    ?? new ClientConfiguration();
                _logger.Information("Configuration loaded");
            }
            else
            {
                _logger.Information("No configuration file found, using defaults");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load configuration");
            _configuration = new ClientConfiguration();
        }
    }

    /// <summary>
    /// Saves the current configuration to disk asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SaveAsync()
    {
        try
        {
            if (!_storage.IsUnlocked)
            {
                _logger.Warning("Cannot save configuration: storage is locked");
                return;
            }
            
            await _storage.WriteJsonAsync(ConfigFileName, _configuration, JsonOptions);
            _logger.Debug("Configuration saved");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configuration");
        }
    }

    /// <summary>
    /// Adds or updates a server in the configuration.
    /// </summary>
    /// <param name="server">The server to add or update.</param>
    public void AddServer(IrcServer server)
    {
        var existing = _configuration.Servers.FirstOrDefault(s => s.Id == server.Id);
        if (existing != null)
        {
            _configuration.Servers.Remove(existing);
        }
        
        // Create a clean copy for storage (without runtime state)
        var serverConfig = new ServerConfiguration
        {
            Id = server.Id,
            Name = server.Name,
            Hostname = server.Hostname,
            Port = server.Port,
            UseSsl = server.UseSsl,
            AcceptInvalidCertificates = server.AcceptInvalidCertificates,
            Nickname = server.Nickname,
            Username = server.Username,
            RealName = server.RealName,
            Password = server.Password,
            NickServPassword = server.NickServPassword,
            AutoJoinChannels = new List<string>(server.AutoJoinChannels),
            AutoConnect = server.AutoConnect
        };
        
        _configuration.Servers.Add(serverConfig);
    }

    /// <summary>
    /// Removes a server from the configuration.
    /// </summary>
    /// <param name="serverId">The unique identifier of the server to remove.</param>
    public void RemoveServer(string serverId)
    {
        var server = _configuration.Servers.FirstOrDefault(s => s.Id == serverId);
        if (server != null)
        {
            _configuration.Servers.Remove(server);
        }
    }

    /// <summary>
    /// Updates the auto-join channels for a server.
    /// </summary>
    /// <param name="serverId">The unique identifier of the server.</param>
    /// <param name="channels">The list of channels to auto-join.</param>
    public void UpdateServerChannels(string serverId, List<string> channels)
    {
        var server = _configuration.Servers.FirstOrDefault(s => s.Id == serverId);
        if (server != null)
        {
            server.AutoJoinChannels = channels;
        }
    }

    /// <summary>
    /// Adds a channel to the server's auto-join list.
    /// </summary>
    /// <param name="serverId">The unique identifier of the server.</param>
    /// <param name="channelName">The channel name to add (e.g., #general).</param>
    public void AddChannelToServer(string serverId, string channelName)
    {
        var server = _configuration.Servers.FirstOrDefault(s => s.Id == serverId);
        if (server != null && !server.AutoJoinChannels.Contains(channelName, StringComparer.OrdinalIgnoreCase))
        {
            server.AutoJoinChannels.Add(channelName);
        }
    }

    /// <summary>
    /// Removes a channel from the server's auto-join list.
    /// </summary>
    /// <param name="serverId">The unique identifier of the server.</param>
    /// <param name="channelName">The channel name to remove.</param>
    public void RemoveChannelFromServer(string serverId, string channelName)
    {
        var server = _configuration.Servers.FirstOrDefault(s => s.Id == serverId);
        if (server != null)
        {
            server.AutoJoinChannels.RemoveAll(c => c.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Gets a server by its unique identifier.
    /// </summary>
    /// <param name="serverId">The unique identifier of the server.</param>
    /// <returns>The server if found; otherwise, null.</returns>
    public IrcServer? GetServerById(string serverId)
    {
        var config = _configuration.Servers.FirstOrDefault(s => s.Id == serverId);
        return config?.ToIrcServer();
    }

    /// <summary>
    /// Gets all configured servers.
    /// </summary>
    /// <returns>A list of all server configurations converted to IrcServer instances.</returns>
    public List<IrcServer> GetAllServers()
    {
        return _configuration.Servers.Select(s => s.ToIrcServer()).ToList();
    }
}

/// <summary>
/// Root configuration object containing all client settings.
/// </summary>
public class ClientConfiguration
{
    /// <summary>
    /// Gets or sets the list of configured IRC servers.
    /// </summary>
    public List<ServerConfiguration> Servers { get; set; } = new();

    /// <summary>
    /// Gets or sets the general application settings.
    /// </summary>
    public GeneralSettings Settings { get; set; } = new();
}

/// <summary>
/// Server configuration for JSON serialization.
/// </summary>
public class ServerConfiguration
{
    /// <summary>Unique identifier for the server.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name for the server.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Server hostname or IP address.</summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>Server port (default: 6667 for non-SSL, 6697 for SSL).</summary>
    public int Port { get; set; } = 6667;

    /// <summary>Whether to use SSL/TLS encryption.</summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>Whether to accept invalid SSL certificates (for self-signed certs).</summary>
    public bool AcceptInvalidCertificates { get; set; } = false;

    /// <summary>IRC nickname to use.</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>IRC username (ident).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>IRC real name (GECOS).</summary>
    public string RealName { get; set; } = string.Empty;

    /// <summary>Server password for password-protected servers.</summary>
    public string? Password { get; set; }

    /// <summary>NickServ password for automatic identification.</summary>
    public string? NickServPassword { get; set; }

    /// <summary>List of channels to automatically join on connect.</summary>
    public List<string> AutoJoinChannels { get; set; } = new();

    /// <summary>Whether to automatically connect when the application starts.</summary>
    public bool AutoConnect { get; set; } = false;
    
    /// <summary>SASL username for authentication.</summary>
    public string? SaslUsername { get; set; }

    /// <summary>SASL password for authentication.</summary>
    public string? SaslPassword { get; set; }
    
    /// <summary>Whether to use a client certificate for authentication.</summary>
    public bool UseClientCertificate { get; set; } = false;

    /// <summary>Path to the client certificate file (PFX/P12).</summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>Password for the client certificate.</summary>
    public string? ClientCertificatePassword { get; set; }
    
    /// <summary>Proxy configuration for this server.</summary>
    public ProxyConfiguration? Proxy { get; set; }

    /// <summary>
    /// Converts this configuration to an IrcServer runtime instance.
    /// </summary>
    /// <returns>A new IrcServer with the configured values.</returns>
    public IrcServer ToIrcServer() => new()
    {
        Id = Id,
        Name = Name,
        Hostname = Hostname,
        Port = Port,
        UseSsl = UseSsl,
        AcceptInvalidCertificates = AcceptInvalidCertificates,
        Nickname = Nickname,
        Username = Username,
        RealName = RealName,
        Password = Password,
        NickServPassword = NickServPassword,
        AutoJoinChannels = new List<string>(AutoJoinChannels),
        AutoConnect = AutoConnect,
        SaslUsername = SaslUsername,
        SaslPassword = SaslPassword,
        UseClientCertificate = UseClientCertificate,
        ClientCertificatePath = ClientCertificatePath,
        ClientCertificatePassword = ClientCertificatePassword,
        Proxy = Proxy?.ToProxySettings()
    };
}

/// <summary>
/// Proxy configuration for JSON serialization.
/// </summary>
public class ProxyConfiguration
{
    /// <summary>Proxy type: None, SOCKS4, SOCKS5, or HTTP.</summary>
    public string Type { get; set; } = "None";

    /// <summary>Proxy server hostname or IP address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Proxy server port.</summary>
    public int Port { get; set; }

    /// <summary>Proxy username for authentication.</summary>
    public string? Username { get; set; }

    /// <summary>Proxy password for authentication.</summary>
    public string? Password { get; set; }
    
    /// <summary>
    /// Converts this configuration to a ProxySettings instance.
    /// </summary>
    /// <returns>A new ProxySettings instance.</returns>
    public ProxySettings ToProxySettings() => new()
    {
        Type = Enum.TryParse<ProxyType>(Type, out var t) ? t : ProxyType.None,
        Host = Host,
        Port = Port,
        Username = Username,
        Password = Password
    };
}

/// <summary>
/// General application settings.
/// </summary>
public class GeneralSettings
{
    /// <summary>Whether to automatically reconnect when disconnected.</summary>
    public bool ReconnectOnDisconnect { get; set; } = true;

    /// <summary>Delay in seconds between reconnection attempts.</summary>
    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>Whether to minimize to system tray instead of taskbar.</summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>Whether to show timestamps in messages.</summary>
    public bool ShowTimestamps { get; set; } = true;

    /// <summary>Format string for message timestamps (e.g., "HH:mm:ss").</summary>
    public string TimestampFormat { get; set; } = "HH:mm:ss";
    
    /// <summary>
    /// Custom words that trigger highlight in addition to nickname.
    /// </summary>
    public List<string> HighlightWords { get; set; } = new();
    
    /// <summary>
    /// List of nicknames to ignore (hide messages from).
    /// </summary>
    public List<string> IgnoredUsers { get; set; } = new();
    
    /// <summary>
    /// Enable sound notifications.
    /// </summary>
    public bool EnableSoundNotifications { get; set; } = true;
    
    /// <summary>
    /// Enable taskbar flash notifications.
    /// </summary>
    public bool EnableFlashNotifications { get; set; } = true;
    
    /// <summary>
    /// Only notify when window is minimized or not focused.
    /// </summary>
    public bool OnlyNotifyWhenInactive { get; set; } = true;
    
    /// <summary>
    /// Enable flood protection.
    /// </summary>
    public bool EnableFloodProtection { get; set; } = true;
    
    /// <summary>
    /// Flood protection max burst.
    /// </summary>
    public int FloodProtectionBurst { get; set; } = 5;
    
    /// <summary>
    /// Flood protection refill interval in milliseconds.
    /// </summary>
    public int FloodProtectionIntervalMs { get; set; } = 1000;
    
    /// <summary>
    /// Enable logging to file.
    /// </summary>
    public bool EnableLogging { get; set; } = true;
    
    /// <summary>
    /// Log format (Text, JSON, SQLite).
    /// </summary>
    public string LogFormat { get; set; } = "Text";
    
    /// <summary>
    /// Custom log directory path.
    /// </summary>
    public string? LogDirectory { get; set; }
    
    /// <summary>
    /// Custom command aliases.
    /// </summary>
    public Dictionary<string, string> Aliases { get; set; } = new();
    
    /// <summary>
    /// Global auto-perform commands (run on all server connections).
    /// </summary>
    public List<string> GlobalAutoPerform { get; set; } = new();
    
    /// <summary>
    /// Per-server auto-perform commands.
    /// </summary>
    public Dictionary<string, List<string>> ServerAutoPerform { get; set; } = new();
    
    /// <summary>
    /// Per-channel auto-perform commands (serverId -> channelName -> commands).
    /// </summary>
    public Dictionary<string, Dictionary<string, List<string>>> ChannelAutoPerform { get; set; } = new();
    
    // === SECURITY SETTINGS ===
    
    /// <summary>
    /// Enable automatic lock after inactivity.
    /// </summary>
    public bool AutoLockEnabled { get; set; } = false;
    
    /// <summary>
    /// Minutes of inactivity before auto-lock (default: 5 minutes).
    /// </summary>
    public int AutoLockMinutes { get; set; } = 5;
    
    /// <summary>
    /// Enable automatic deletion of old log files.
    /// </summary>
    public bool AutoDeleteLogsEnabled { get; set; } = false;
    
    /// <summary>
    /// Days to retain logs before auto-deletion (default: 30 days).
    /// </summary>
    public int AutoDeleteLogsDays { get; set; } = 30;
    
    /// <summary>
    /// Enable secure deletion (overwrite files before deleting).
    /// </summary>
    public bool SecureDeleteEnabled { get; set; } = false;
}
