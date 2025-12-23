namespace IrcClient.Core.Models;

/// <summary>
/// Represents an IRC server configuration and connection state.
/// Contains both persistent configuration settings and runtime state.
/// </summary>
/// <remarks>
/// <para>This class holds:</para>
/// <list type="bullet">
///   <item><description>Connection settings (hostname, port, SSL)</description></item>
///   <item><description>User identity (nickname, username, realname)</description></item>
///   <item><description>Authentication (password, NickServ, SASL)</description></item>
///   <item><description>Runtime state (connection status, channels, capabilities)</description></item>
/// </list>
/// </remarks>
public class IrcServer
{
    /// <summary>
    /// Unique identifier for this server configuration.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Display name for the server (e.g., "Libera.Chat").
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Server hostname or IP address.
    /// </summary>
    public string Hostname { get; set; } = string.Empty;
    
    /// <summary>
    /// Server port number. Default is 6667 (plaintext) or 6697 (SSL).
    /// </summary>
    public int Port { get; set; } = 6667;
    
    /// <summary>
    /// Whether to use SSL/TLS encryption for the connection.
    /// </summary>
    public bool UseSsl { get; set; } = false;
    
    /// <summary>
    /// Whether to accept invalid/self-signed SSL certificates.
    /// </summary>
    /// <remarks>
    /// Setting this to true is a security risk and should only be used
    /// for development or with known self-signed certificates.
    /// </remarks>
    public bool AcceptInvalidCertificates { get; set; } = false;
    
    /// <summary>
    /// The nickname to use when connecting.
    /// </summary>
    public string Nickname { get; set; } = string.Empty;
    
    /// <summary>
    /// The username (ident) to use when connecting.
    /// </summary>
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// The realname (GECOS) field to use when connecting.
    /// </summary>
    public string RealName { get; set; } = string.Empty;
    
    /// <summary>
    /// Server password (PASS command), if required.
    /// </summary>
    public string? Password { get; set; }
    
    /// <summary>
    /// Password for NickServ identification after connection.
    /// </summary>
    public string? NickServPassword { get; set; }
    
    /// <summary>
    /// List of channels to automatically join on connect.
    /// </summary>
    public List<string> AutoJoinChannels { get; set; } = new();
    
    /// <summary>
    /// Whether to automatically connect to this server on application startup.
    /// </summary>
    public bool AutoConnect { get; set; } = false;
    
    /// <summary>
    /// Current connection state (runtime only, not persisted).
    /// </summary>
    public ConnectionState State { get; set; } = ConnectionState.Disconnected;
    
    /// <summary>
    /// List of joined channels (runtime only, not persisted).
    /// </summary>
    public List<IrcChannel> Channels { get; set; } = new();
    
    /// <summary>
    /// Timestamp when the connection was established (runtime only).
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// Server ISUPPORT (005) configuration.
    /// Contains server capabilities, limits, and feature flags.
    /// </summary>
    public ISupport ISupport { get; } = new();

    /// <summary>
    /// IRCv3 capability manager.
    /// </summary>
    public CapabilityManager Capabilities { get; } = new();

    /// <summary>
    /// SASL username for authentication.
    /// </summary>
    public string? SaslUsername { get; set; }

    /// <summary>
    /// SASL password for authentication.
    /// </summary>
    public string? SaslPassword { get; set; }

    /// <summary>
    /// Whether to use a client certificate for authentication (SASL EXTERNAL).
    /// </summary>
    public bool UseClientCertificate { get; set; } = false;

    /// <summary>
    /// Path to the client certificate file (.pfx or .pem).
    /// </summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>
    /// Password for the client certificate, if encrypted.
    /// </summary>
    public string? ClientCertificatePassword { get; set; }

    /// <summary>
    /// Proxy settings for the connection.
    /// </summary>
    public ProxySettings? Proxy { get; set; }
}

/// <summary>
/// Proxy configuration for an IRC server connection.
/// </summary>
public class ProxySettings
{
    public ProxyType Type { get; set; } = ProxyType.None;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public enum ProxyType
{
    None,
    SOCKS4,
    SOCKS5,
    HTTP
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}
