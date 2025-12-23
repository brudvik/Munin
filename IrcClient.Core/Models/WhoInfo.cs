namespace IrcClient.Core.Models;

/// <summary>
/// Represents information from a WHO reply (352).
/// </summary>
public class WhoInfo
{
    /// <summary>
    /// Channel name (or * for no channel).
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// Username (ident).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Hostname.
    /// </summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Server the user is connected to.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// Nickname.
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// Status flags (H=here, G=gone/away, *=oper, @=op, +=voice, etc.).
    /// </summary>
    public string Flags { get; set; } = string.Empty;

    /// <summary>
    /// Hop count to the user's server.
    /// </summary>
    public int HopCount { get; set; }

    /// <summary>
    /// Real name (GECOS).
    /// </summary>
    public string RealName { get; set; } = string.Empty;

    /// <summary>
    /// Account name (from WHOX).
    /// </summary>
    public string? Account { get; set; }

    /// <summary>
    /// Whether the user is away.
    /// </summary>
    public bool IsAway => Flags.Contains('G');

    /// <summary>
    /// Whether the user is an IRC operator.
    /// </summary>
    public bool IsOper => Flags.Contains('*');

    /// <summary>
    /// Whether the user is a channel operator.
    /// </summary>
    public bool IsChannelOp => Flags.Contains('@');

    /// <summary>
    /// Whether the user has voice.
    /// </summary>
    public bool HasVoice => Flags.Contains('+');
}

/// <summary>
/// Represents information from a WHOWAS reply (314).
/// </summary>
public class WhowasInfo
{
    public string Nickname { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string RealName { get; set; } = string.Empty;
    public DateTime? LastSeen { get; set; }
}
