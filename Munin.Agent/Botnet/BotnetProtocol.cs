namespace Munin.Agent.Botnet;

/// <summary>
/// Botnet protocol messages for inter-agent communication.
/// Uses a similar approach to Eggdrop's botnet but with modern crypto.
/// </summary>
public enum BotnetMessageType
{
    // Handshake & Authentication
    Hello = 1,
    Challenge = 2,
    Response = 3,
    Welcome = 4,
    Goodbye = 5,
    
    // Heartbeat
    Ping = 10,
    Pong = 11,
    
    // Partyline
    Chat = 20,
    Action = 21,
    Join = 22,
    Part = 23,
    Who = 24,
    WhoReply = 25,
    
    // User Database
    UserSync = 30,
    UserAdd = 31,
    UserDel = 32,
    UserUpdate = 33,
    UserRequest = 34,
    
    // Channel Operations
    OpRequest = 40,
    OpGrant = 41,
    KickRequest = 42,
    BanSync = 43,
    
    // Information
    Info = 50,
    Status = 51,
    Channels = 52,
    
    // Error
    Error = 99
}

/// <summary>
/// Base class for botnet messages.
/// </summary>
public abstract class BotnetMessage
{
    /// <summary>
    /// Message type.
    /// </summary>
    public abstract BotnetMessageType Type { get; }
    
    /// <summary>
    /// Source agent ID.
    /// </summary>
    public string FromAgent { get; set; } = "";
    
    /// <summary>
    /// Target agent ID (empty for broadcast).
    /// </summary>
    public string ToAgent { get; set; } = "";
    
    /// <summary>
    /// Message timestamp.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Hop count (for mesh routing).
    /// </summary>
    public int Hops { get; set; }
}

/// <summary>
/// Hello message - first message in handshake.
/// </summary>
public class BotnetHello : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Hello;
    public string AgentName { get; set; } = "";
    public string Version { get; set; } = "";
    public string PublicKey { get; set; } = ""; // For key exchange
}

/// <summary>
/// Challenge message - server's response to Hello.
/// </summary>
public class BotnetChallenge : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Challenge;
    public string Challenge { get; set; } = ""; // Random bytes for challenge-response
    public string ServerPublicKey { get; set; } = "";
}

/// <summary>
/// Response to challenge.
/// </summary>
public class BotnetResponse : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Response;
    public string Response { get; set; } = ""; // Signed challenge
    public string Password { get; set; } = ""; // Encrypted bot password
}

/// <summary>
/// Welcome - authentication successful.
/// </summary>
public class BotnetWelcome : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Welcome;
    public string AgentName { get; set; } = "";
    public List<string> LinkedBots { get; set; } = new(); // Other bots in network
}

/// <summary>
/// Goodbye - graceful disconnect.
/// </summary>
public class BotnetGoodbye : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Goodbye;
    public string Reason { get; set; } = "";
}

/// <summary>
/// Ping for keepalive.
/// </summary>
public class BotnetPing : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Ping;
    public long PingId { get; set; }
}

/// <summary>
/// Pong response.
/// </summary>
public class BotnetPong : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Pong;
    public long PingId { get; set; }
}

/// <summary>
/// Chat message on partyline.
/// </summary>
public class BotnetChat : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Chat;
    public string FromNick { get; set; } = "";
    public string Channel { get; set; } = ""; // Partyline channel (empty = main)
    public string Text { get; set; } = "";
}

/// <summary>
/// Action message (/me) on partyline.
/// </summary>
public class BotnetAction : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Action;
    public string FromNick { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>
/// User joining partyline.
/// </summary>
public class BotnetJoin : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Join;
    public string Nick { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Flags { get; set; } = ""; // User flags
}

/// <summary>
/// User leaving partyline.
/// </summary>
public class BotnetPart : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Part;
    public string Nick { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>
/// Request partyline user list.
/// </summary>
public class BotnetWho : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Who;
    public string Channel { get; set; } = "";
}

/// <summary>
/// Partyline user list reply.
/// </summary>
public class BotnetWhoReply : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.WhoReply;
    public string Channel { get; set; } = "";
    public List<PartylineUser> Users { get; set; } = new();
}

/// <summary>
/// User sync - share user database entry.
/// </summary>
public class BotnetUserSync : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.UserSync;
    public List<SyncedUser> Users { get; set; } = new();
    public bool IsFullSync { get; set; }
}

/// <summary>
/// Op request to other bots.
/// </summary>
public class BotnetOpRequest : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.OpRequest;
    public string IrcServer { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Nick { get; set; } = "";
    public string Hostmask { get; set; } = "";
}

/// <summary>
/// Op granted confirmation.
/// </summary>
public class BotnetOpGrant : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.OpGrant;
    public string IrcServer { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Nick { get; set; } = "";
    public bool Success { get; set; }
}

/// <summary>
/// Kick request to other bots.
/// </summary>
public class BotnetKickRequest : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.KickRequest;
    public string IrcServer { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Nick { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>
/// Ban synchronization.
/// </summary>
public class BotnetBanSync : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.BanSync;
    public string IrcServer { get; set; } = "";
    public string Channel { get; set; } = "";
    public List<BanEntry> Bans { get; set; } = new();
}

/// <summary>
/// Status request/response.
/// </summary>
public class BotnetStatus : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Status;
    public string AgentName { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime Uptime { get; set; }
    public List<ServerStatus> Servers { get; set; } = new();
}

/// <summary>
/// Error message.
/// </summary>
public class BotnetError : BotnetMessage
{
    public override BotnetMessageType Type => BotnetMessageType.Error;
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

// Supporting types

public class PartylineUser
{
    public string Nick { get; set; } = "";
    public string Bot { get; set; } = ""; // Which bot they're connected through
    public string Flags { get; set; } = "";
    public DateTime JoinedAt { get; set; }
}

public class SyncedUser
{
    public string Handle { get; set; } = "";
    public List<string> Hostmasks { get; set; } = new();
    public string GlobalFlags { get; set; } = "";
    public Dictionary<string, string> ChannelFlags { get; set; } = new();
    public DateTime LastModified { get; set; }
}

public class BanEntry
{
    public string Mask { get; set; } = "";
    public string SetBy { get; set; } = "";
    public DateTime SetAt { get; set; }
    public string Reason { get; set; } = "";
}

public class ServerStatus
{
    public string Id { get; set; } = "";
    public string Host { get; set; } = "";
    public string Nick { get; set; } = "";
    public string State { get; set; } = "";
    public List<string> Channels { get; set; } = new();
}
