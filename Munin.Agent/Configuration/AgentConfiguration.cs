using System.Text.Json.Serialization;

namespace Munin.Agent.Configuration;

/// <summary>
/// Root configuration for the Munin Agent.
/// All sensitive fields are stored encrypted.
/// </summary>
public class AgentConfiguration
{
    /// <summary>
    /// Unique identifier for this agent instance.
    /// </summary>
    public string AgentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Human-readable name for this agent.
    /// </summary>
    public string Name { get; set; } = "MuninAgent";

    /// <summary>
    /// Port for the control server to listen on.
    /// </summary>
    public int ControlPort { get; set; } = 5550;

    /// <summary>
    /// Whether to require TLS for control connections.
    /// Should always be true in production.
    /// </summary>
    public bool RequireTls { get; set; } = true;

    /// <summary>
    /// Path to TLS certificate file (PFX format).
    /// </summary>
    public string? TlsCertificatePath { get; set; }

    /// <summary>
    /// Encrypted TLS certificate password.
    /// </summary>
    [JsonPropertyName("tlsCertificatePassword")]
    public EncryptedValue? TlsCertificatePasswordEncrypted { get; set; }

    /// <summary>
    /// Encrypted authentication token for control connections.
    /// </summary>
    [JsonPropertyName("authToken")]
    public EncryptedValue? AuthTokenEncrypted { get; set; }

    /// <summary>
    /// List of IP addresses/ranges allowed to connect.
    /// Empty or ["*"] allows all.
    /// </summary>
    public List<string> AllowedIPs { get; set; } = new() { "*" };

    /// <summary>
    /// IRC server configurations.
    /// </summary>
    public List<IrcServerConfiguration> Servers { get; set; } = new();

    /// <summary>
    /// Script configuration.
    /// </summary>
    public ScriptConfiguration Scripts { get; set; } = new();

    /// <summary>
    /// User database configuration.
    /// </summary>
    public UserDatabaseConfiguration Users { get; set; } = new();

    /// <summary>
    /// Logging configuration.
    /// </summary>
    public LoggingConfiguration Logging { get; set; } = new();

    /// <summary>
    /// Control server configuration.
    /// </summary>
    public ControlServerConfiguration ControlServer { get; set; } = new();

    /// <summary>
    /// Botnet configuration for linking with other agents.
    /// </summary>
    public BotnetConfiguration Botnet { get; set; } = new();

    /// <summary>
    /// Channel protection configuration.
    /// </summary>
    public ChannelProtectionConfiguration ChannelProtection { get; set; } = new();

    /// <summary>
    /// Encryption metadata - not user-configurable.
    /// </summary>
    public EncryptionMetadata? Encryption { get; set; }
}

/// <summary>
/// IRC server connection configuration.
/// </summary>
public class IrcServerConfiguration
{
    /// <summary>
    /// Unique identifier for this server configuration.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Human-readable name for this server.
    /// </summary>
    public string Name { get; set; } = "IRC Server";

    /// <summary>
    /// Server hostname or IP address.
    /// </summary>
    public string Address { get; set; } = "irc.libera.chat";

    /// <summary>
    /// Server port.
    /// </summary>
    public int Port { get; set; } = 6697;

    /// <summary>
    /// Whether to use SSL/TLS.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Whether to verify the server's SSL certificate.
    /// </summary>
    public bool VerifyCertificate { get; set; } = true;

    /// <summary>
    /// Bot's nickname.
    /// </summary>
    public string Nickname { get; set; } = "MuninBot";

    /// <summary>
    /// Alternative nicknames if primary is taken.
    /// </summary>
    public List<string> AlternativeNicknames { get; set; } = new() { "MuninBot_", "MuninBot__" };

    /// <summary>
    /// Bot's username (ident).
    /// </summary>
    public string Username { get; set; } = "munin";

    /// <summary>
    /// Bot's realname (GECOS).
    /// </summary>
    public string Realname { get; set; } = "Munin IRC Agent";

    /// <summary>
    /// Encrypted server password (if required).
    /// </summary>
    [JsonPropertyName("serverPassword")]
    public EncryptedValue? ServerPasswordEncrypted { get; set; }

    /// <summary>
    /// Encrypted NickServ password.
    /// </summary>
    [JsonPropertyName("nickservPassword")]
    public EncryptedValue? NickservPasswordEncrypted { get; set; }

    /// <summary>
    /// Encrypted SASL username.
    /// </summary>
    [JsonPropertyName("saslUsername")]
    public EncryptedValue? SaslUsernameEncrypted { get; set; }

    /// <summary>
    /// Encrypted SASL password.
    /// </summary>
    [JsonPropertyName("saslPassword")]
    public EncryptedValue? SaslPasswordEncrypted { get; set; }

    /// <summary>
    /// Channels to join on connect.
    /// </summary>
    public List<ChannelConfiguration> Channels { get; set; } = new();

    /// <summary>
    /// Whether to automatically reconnect on disconnect.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Delay between reconnection attempts in seconds.
    /// </summary>
    public int ReconnectDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Maximum reconnection attempts (0 = infinite).
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 0;

    /// <summary>
    /// Whether this server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to automatically connect on agent startup.
    /// </summary>
    public bool AutoConnect { get; set; } = true;
}

/// <summary>
/// Channel configuration.
/// </summary>
public class ChannelConfiguration
{
    /// <summary>
    /// Channel name (including #).
    /// </summary>
    public string Name { get; set; } = "#channel";

    /// <summary>
    /// Encrypted channel key (if required).
    /// </summary>
    [JsonPropertyName("key")]
    public EncryptedValue? KeyEncrypted { get; set; }

    /// <summary>
    /// Whether to auto-join this channel.
    /// </summary>
    public bool AutoJoin { get; set; } = true;
}

/// <summary>
/// Script configuration.
/// </summary>
public class ScriptConfiguration
{
    /// <summary>
    /// Directory containing scripts.
    /// </summary>
    public string Directory { get; set; } = "scripts";

    /// <summary>
    /// Scripts to load automatically on startup.
    /// </summary>
    public List<string> AutoLoad { get; set; } = new();

    /// <summary>
    /// Whether to enable the Lua script engine.
    /// </summary>
    public bool EnableLua { get; set; } = true;

    /// <summary>
    /// Whether to enable the trigger engine.
    /// </summary>
    public bool EnableTriggers { get; set; } = true;
}

/// <summary>
/// User database configuration.
/// </summary>
public class UserDatabaseConfiguration
{
    /// <summary>
    /// Path to the user database file.
    /// </summary>
    public string FilePath { get; set; } = "users.json";

    /// <summary>
    /// Default flags for new users.
    /// </summary>
    public string DefaultFlags { get; set; } = "";

    /// <summary>
    /// Whether to allow users to add themselves via "hello" command.
    /// </summary>
    public bool AllowSelfRegister { get; set; } = false;
}

/// <summary>
/// Logging configuration.
/// </summary>
public class LoggingConfiguration
{
    /// <summary>
    /// Whether to log to console.
    /// </summary>
    public bool Console { get; set; } = true;

    /// <summary>
    /// Path to log file (relative or absolute).
    /// </summary>
    public string? FilePath { get; set; } = "logs/agent.log";

    /// <summary>
    /// Minimum log level.
    /// </summary>
    public string Level { get; set; } = "Information";

    /// <summary>
    /// Whether to log IRC messages (may contain sensitive data).
    /// </summary>
    public bool LogIrcMessages { get; set; } = false;
}

/// <summary>
/// Control server configuration for remote management.
/// </summary>
public class ControlServerConfiguration
{
    /// <summary>
    /// Whether the control server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// IP address to bind to (* for all interfaces).
    /// </summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// Port to listen on.
    /// </summary>
    public int Port { get; set; } = 5550;

    /// <summary>
    /// Path to TLS certificate file (PFX format).
    /// </summary>
    public string CertificatePath { get; set; } = "agent.pfx";

    /// <summary>
    /// Encrypted certificate password.
    /// </summary>
    [JsonPropertyName("certificatePassword")]
    public EncryptedValue? CertificatePassword { get; set; }

    /// <summary>
    /// Encrypted authentication token.
    /// </summary>
    [JsonPropertyName("authToken")]
    public EncryptedValue? AuthToken { get; set; }

    /// <summary>
    /// List of IP addresses allowed to connect.
    /// Empty or ["*"] allows all.
    /// </summary>
    public List<string> AllowedIps { get; set; } = new() { "127.0.0.1" };
}

/// <summary>
/// Represents an encrypted value with its metadata.
/// </summary>
public class EncryptedValue
{
    /// <summary>
    /// The encrypted data as Base64.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Encryption algorithm identifier.
    /// </summary>
    public string Algorithm { get; set; } = "AES-256-GCM";
}

/// <summary>
/// Encryption metadata stored in configuration.
/// </summary>
public class EncryptionMetadata
{
    /// <summary>
    /// Whether the configuration is encrypted.
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Salt used for key derivation (Base64).
    /// </summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// Verification token to check if password is correct (Base64).
    /// </summary>
    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>
    /// When encryption was set up.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Encryption schema version.
    /// </summary>
    public int Version { get; set; } = 1;
}

/// <summary>
/// Botnet configuration for linking agents together.
/// </summary>
public class BotnetConfiguration
{
    /// <summary>
    /// Whether botnet linking is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Port to listen for incoming bot connections.
    /// 0 = don't listen (only connect outbound).
    /// </summary>
    public int ListenPort { get; set; } = 0;

    /// <summary>
    /// Linked bot configurations.
    /// </summary>
    public List<LinkedBotConfiguration> LinkedBots { get; set; } = new();

    /// <summary>
    /// Whether to sync user database with linked bots.
    /// </summary>
    public bool SyncUsers { get; set; } = true;

    /// <summary>
    /// Whether to share ops across linked bots.
    /// </summary>
    public bool ShareOps { get; set; } = true;
}

/// <summary>
/// Configuration for a linked bot.
/// </summary>
public class LinkedBotConfiguration
{
    /// <summary>
    /// Name of the linked bot.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Hostname or IP address of the bot.
    /// </summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// Port to connect to.
    /// </summary>
    public int Port { get; set; } = 5551;

    /// <summary>
    /// Shared secret for authentication.
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// Whether to actively connect to this bot.
    /// False = only accept incoming connections.
    /// </summary>
    public bool Connect { get; set; } = true;

    /// <summary>
    /// Whether this is a hub bot (preferred for routing).
    /// </summary>
    public bool IsHub { get; set; } = false;
}

/// <summary>
/// Channel protection configuration.
/// </summary>
public class ChannelProtectionConfiguration
{
    /// <summary>
    /// Whether to enable flood protection.
    /// </summary>
    public bool FloodProtection { get; set; } = true;

    /// <summary>
    /// Maximum messages per interval before action.
    /// </summary>
    public int FloodMaxMessages { get; set; } = 5;

    /// <summary>
    /// Flood detection interval in seconds.
    /// </summary>
    public int FloodIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Action on flood: kick, ban, kickban, quiet
    /// </summary>
    public string FloodAction { get; set; } = "kick";

    /// <summary>
    /// Whether to detect clones.
    /// </summary>
    public bool CloneDetection { get; set; } = true;

    /// <summary>
    /// Maximum clones from same host.
    /// </summary>
    public int MaxClonesPerHost { get; set; } = 3;

    /// <summary>
    /// Bad words to filter.
    /// </summary>
    public List<BadWordEntry> BadWords { get; set; } = new();

    /// <summary>
    /// Whether to auto-invite +f users to +i channels.
    /// </summary>
    public bool InviteOnlyGuard { get; set; } = true;

    /// <summary>
    /// Whether to detect mass-kick attacks.
    /// </summary>
    public bool MassKickProtection { get; set; } = true;

    /// <summary>
    /// Kicks within this interval trigger mass-kick detection.
    /// </summary>
    public int MassKickIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Number of kicks to trigger mass-kick detection.
    /// </summary>
    public int MassKickThreshold { get; set; } = 3;
}

/// <summary>
/// Bad word filter entry.
/// </summary>
public class BadWordEntry
{
    /// <summary>
    /// The word or pattern to match.
    /// </summary>
    public string Pattern { get; set; } = "";

    /// <summary>
    /// Whether the pattern is a regex.
    /// </summary>
    public bool IsRegex { get; set; } = false;

    /// <summary>
    /// Action: warn, kick, ban, kickban
    /// </summary>
    public string Action { get; set; } = "kick";

    /// <summary>
    /// Reason shown when action is taken.
    /// </summary>
    public string Reason { get; set; } = "Bad language";
}
