namespace Munin.Agent.Protocol;

/// <summary>
/// Defines the protocol for communication between Munin UI and Agent.
/// All communication is encrypted via TLS.
/// </summary>
public static class AgentProtocol
{
    /// <summary>
    /// Protocol version. Increment when making breaking changes.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Magic bytes to identify Munin Agent protocol.
    /// </summary>
    public static readonly byte[] MagicBytes = "MAGT"u8.ToArray();

    /// <summary>
    /// Maximum message size (1 MB).
    /// </summary>
    public const int MaxMessageSize = 1024 * 1024;

    /// <summary>
    /// Timeout for authentication in seconds.
    /// </summary>
    public const int AuthTimeoutSeconds = 30;

    /// <summary>
    /// Heartbeat interval in seconds.
    /// </summary>
    public const int HeartbeatIntervalSeconds = 30;
}

/// <summary>
/// Message types for Agent protocol.
/// </summary>
public enum AgentMessageType : byte
{
    // ======== Authentication (0x01-0x0F) ========
    
    /// <summary>
    /// Server sends challenge to client.
    /// Payload: [8 bytes nonce][32 bytes challenge]
    /// </summary>
    AuthChallenge = 0x01,

    /// <summary>
    /// Client responds to challenge.
    /// Payload: [8 bytes nonce][32 bytes HMAC response]
    /// </summary>
    AuthResponse = 0x02,

    /// <summary>
    /// Authentication successful.
    /// Payload: [Agent info JSON]
    /// </summary>
    AuthSuccess = 0x03,

    /// <summary>
    /// Authentication failed.
    /// Payload: [Error message]
    /// </summary>
    AuthFailure = 0x04,

    // ======== Heartbeat (0x10-0x1F) ========

    /// <summary>
    /// Ping request.
    /// Payload: [8 bytes timestamp]
    /// </summary>
    Ping = 0x10,

    /// <summary>
    /// Pong response.
    /// Payload: [8 bytes timestamp from ping]
    /// </summary>
    Pong = 0x11,

    // ======== Status (0x20-0x2F) ========

    /// <summary>
    /// Request agent status.
    /// Payload: none
    /// </summary>
    GetStatus = 0x20,

    /// <summary>
    /// Agent status response.
    /// Payload: [Status JSON]
    /// </summary>
    Status = 0x21,

    /// <summary>
    /// Request list of IRC connections.
    /// Payload: none
    /// </summary>
    GetConnections = 0x22,

    /// <summary>
    /// List of IRC connections.
    /// Payload: [Connections JSON]
    /// </summary>
    Connections = 0x23,

    /// <summary>
    /// Request list of channels for a connection.
    /// Payload: [Server ID]
    /// </summary>
    GetChannels = 0x24,

    /// <summary>
    /// List of channels.
    /// Payload: [Channels JSON]
    /// </summary>
    Channels = 0x25,

    /// <summary>
    /// Request list of users in a channel.
    /// Payload: [Server ID][Channel name]
    /// </summary>
    GetUsers = 0x26,

    /// <summary>
    /// List of users.
    /// Payload: [Users JSON]
    /// </summary>
    Users = 0x27,

    // ======== IRC Control (0x30-0x4F) ========

    /// <summary>
    /// Join a channel.
    /// Payload: [Server ID][Channel name][Optional key]
    /// </summary>
    JoinChannel = 0x30,

    /// <summary>
    /// Part a channel.
    /// Payload: [Server ID][Channel name][Optional message]
    /// </summary>
    PartChannel = 0x31,

    /// <summary>
    /// Send a message.
    /// Payload: [Server ID][Target][Message]
    /// </summary>
    SendMessage = 0x32,

    /// <summary>
    /// Send an action (/me).
    /// Payload: [Server ID][Target][Action]
    /// </summary>
    SendAction = 0x33,

    /// <summary>
    /// Send a notice.
    /// Payload: [Server ID][Target][Message]
    /// </summary>
    SendNotice = 0x34,

    /// <summary>
    /// Change nickname.
    /// Payload: [Server ID][New nickname]
    /// </summary>
    ChangeNick = 0x35,

    /// <summary>
    /// Send raw IRC command.
    /// Payload: [Server ID][Raw command]
    /// </summary>
    SendRaw = 0x36,

    /// <summary>
    /// Connect to a server.
    /// Payload: [Server ID]
    /// </summary>
    Connect = 0x37,

    /// <summary>
    /// Disconnect from a server.
    /// Payload: [Server ID][Optional quit message]
    /// </summary>
    Disconnect = 0x38,

    /// <summary>
    /// Reconnect to a server.
    /// Payload: [Server ID]
    /// </summary>
    Reconnect = 0x39,

    // ======== Script Management (0x50-0x5F) ========

    /// <summary>
    /// Request list of loaded scripts.
    /// Payload: none
    /// </summary>
    GetScripts = 0x50,

    /// <summary>
    /// List of scripts.
    /// Payload: [Scripts JSON]
    /// </summary>
    Scripts = 0x51,

    /// <summary>
    /// Load a script.
    /// Payload: [Script path]
    /// </summary>
    LoadScript = 0x52,

    /// <summary>
    /// Unload a script.
    /// Payload: [Script path]
    /// </summary>
    UnloadScript = 0x53,

    /// <summary>
    /// Reload a script.
    /// Payload: [Script path]
    /// </summary>
    ReloadScript = 0x54,

    /// <summary>
    /// Execute Lua code directly.
    /// Payload: [Lua code]
    /// </summary>
    ExecuteCode = 0x55,

    /// <summary>
    /// Script execution result.
    /// Payload: [Result JSON]
    /// </summary>
    ExecuteResult = 0x56,

    // ======== User Management (0x60-0x6F) ========

    /// <summary>
    /// Request list of users in database.
    /// Payload: none
    /// </summary>
    GetUserDatabase = 0x60,

    /// <summary>
    /// User database contents.
    /// Payload: [Users JSON]
    /// </summary>
    UserDatabase = 0x61,

    /// <summary>
    /// Add or update a user.
    /// Payload: [User JSON]
    /// </summary>
    SetUser = 0x62,

    /// <summary>
    /// Delete a user.
    /// Payload: [Handle]
    /// </summary>
    DeleteUser = 0x63,

    /// <summary>
    /// Set user flags.
    /// Payload: [Handle][Flags][Optional channel]
    /// </summary>
    SetUserFlags = 0x64,

    // ======== Agent Control (0x70-0x7F) ========

    /// <summary>
    /// Reload agent configuration.
    /// Payload: none
    /// </summary>
    ReloadConfig = 0x70,

    /// <summary>
    /// Request agent logs.
    /// Payload: [Number of lines][Optional filter]
    /// </summary>
    GetLogs = 0x71,

    /// <summary>
    /// Log entries.
    /// Payload: [Logs JSON]
    /// </summary>
    Logs = 0x72,

    /// <summary>
    /// Shutdown the agent.
    /// Payload: none
    /// </summary>
    Shutdown = 0x7E,

    /// <summary>
    /// Restart the agent.
    /// Payload: none
    /// </summary>
    Restart = 0x7F,

    // ======== Events (0x80-0x9F) - Agent -> Client ========

    /// <summary>
    /// IRC message received.
    /// Payload: [Message JSON]
    /// </summary>
    IrcMessage = 0x80,

    /// <summary>
    /// User joined channel.
    /// Payload: [Event JSON]
    /// </summary>
    IrcJoin = 0x81,

    /// <summary>
    /// User parted channel.
    /// Payload: [Event JSON]
    /// </summary>
    IrcPart = 0x82,

    /// <summary>
    /// User quit.
    /// Payload: [Event JSON]
    /// </summary>
    IrcQuit = 0x83,

    /// <summary>
    /// User was kicked.
    /// Payload: [Event JSON]
    /// </summary>
    IrcKick = 0x84,

    /// <summary>
    /// Mode change.
    /// Payload: [Event JSON]
    /// </summary>
    IrcMode = 0x85,

    /// <summary>
    /// Topic change.
    /// Payload: [Event JSON]
    /// </summary>
    IrcTopic = 0x86,

    /// <summary>
    /// Nick change.
    /// Payload: [Event JSON]
    /// </summary>
    IrcNick = 0x87,

    /// <summary>
    /// Connection state changed.
    /// Payload: [Event JSON]
    /// </summary>
    ConnectionStateChanged = 0x88,

    /// <summary>
    /// Script output.
    /// Payload: [Output text]
    /// </summary>
    ScriptOutput = 0x90,

    /// <summary>
    /// Script error.
    /// Payload: [Error JSON]
    /// </summary>
    ScriptError = 0x91,

    // ======== Responses (0xF0-0xFF) ========

    /// <summary>
    /// Command executed successfully.
    /// Payload: [Optional result]
    /// </summary>
    Success = 0xF0,

    /// <summary>
    /// Command failed.
    /// Payload: [Error message]
    /// </summary>
    Error = 0xF1,

    /// <summary>
    /// Command not supported.
    /// Payload: [Message type that was not recognized]
    /// </summary>
    NotSupported = 0xFE,

    /// <summary>
    /// Protocol error.
    /// Payload: [Error description]
    /// </summary>
    ProtocolError = 0xFF
}

/// <summary>
/// Represents an Agent protocol message.
/// </summary>
public class AgentMessage
{
    /// <summary>
    /// Message type.
    /// </summary>
    public AgentMessageType Type { get; set; }

    /// <summary>
    /// Message payload.
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Sequence number for request/response correlation.
    /// </summary>
    public uint SequenceNumber { get; set; }

    /// <summary>
    /// Creates a new message with the specified type.
    /// </summary>
    public static AgentMessage Create(AgentMessageType type, byte[]? payload = null)
    {
        return new AgentMessage
        {
            Type = type,
            Payload = payload ?? Array.Empty<byte>()
        };
    }

    /// <summary>
    /// Creates a new message with string payload.
    /// </summary>
    public static AgentMessage Create(AgentMessageType type, string payload)
    {
        return new AgentMessage
        {
            Type = type,
            Payload = System.Text.Encoding.UTF8.GetBytes(payload)
        };
    }

    /// <summary>
    /// Gets the payload as a string.
    /// </summary>
    public string GetPayloadString()
    {
        return System.Text.Encoding.UTF8.GetString(Payload);
    }
}
