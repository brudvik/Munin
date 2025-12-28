namespace Munin.Core.Models;

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
    
    /// <summary>
    /// Whether to prefer IPv6 connections over IPv4.
    /// When enabled, the client will try IPv6 first and fall back to IPv4.
    /// </summary>
    public bool PreferIPv6 { get; set; } = false;
    
    /// <summary>
    /// Indicates whether the current connection is using IPv6 (runtime only).
    /// </summary>
    public bool IsIPv6Connected { get; set; } = false;
    
    /// <summary>
    /// The group/folder this server belongs to.
    /// Null means ungrouped (shown at top level).
    /// </summary>
    public string? Group { get; set; }
    
    /// <summary>
    /// Sort order within the group (or at top level if ungrouped).
    /// </summary>
    public int SortOrder { get; set; } = 0;

    #region Bouncer/ZNC Support

    /// <summary>
    /// Whether this is a bouncer connection (e.g., ZNC, soju).
    /// </summary>
    public bool IsBouncer { get; set; } = false;

    /// <summary>
    /// Whether the server was auto-detected as a bouncer (runtime only).
    /// </summary>
    public bool IsBouncerDetected { get; set; } = false;

    /// <summary>
    /// Whether to suppress notification sounds during playback buffer replay.
    /// </summary>
    public bool SuppressPlaybackNotifications { get; set; } = true;

    /// <summary>
    /// Whether currently receiving playback buffer (runtime only).
    /// </summary>
    public bool IsReceivingPlayback { get; set; } = false;

    #endregion

    #region MuninRelay Support

    /// <summary>
    /// Relay settings for routing connection through MuninRelay.
    /// When configured, traffic is routed through a relay server (e.g., for VPN routing).
    /// </summary>
    public RelaySettings? Relay { get; set; }

    #endregion
}

/// <summary>
/// Relay configuration for routing IRC connections through MuninRelay.
/// Allows routing traffic through a VPN server on another machine.
/// </summary>
public class RelaySettings
{
    /// <summary>
    /// Whether to use the relay for this server connection.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Hostname or IP address of the MuninRelay server.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Port number of the MuninRelay server (default 6900).
    /// </summary>
    public int Port { get; set; } = 6900;

    /// <summary>
    /// Authentication token for the relay server.
    /// Must match the token configured in MuninRelay.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Whether to use SSL/TLS to connect to the relay.
    /// Recommended for security.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Whether to accept invalid/self-signed certificates from the relay.
    /// </summary>
    public bool AcceptInvalidCertificates { get; set; } = true;
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
