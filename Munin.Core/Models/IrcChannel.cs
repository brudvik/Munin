namespace Munin.Core.Models;

/// <summary>
/// Represents an IRC channel with its users, messages, and modes.
/// </summary>
/// <remarks>
/// This class maintains the runtime state of a channel including:
/// <list type="bullet">
///   <item><description>Topic and topic metadata</description></item>
///   <item><description>User list with modes/prefixes</description></item>
///   <item><description>Message history for display</description></item>
///   <item><description>Channel modes (moderated, invite-only, etc.)</description></item>
///   <item><description>Unread message tracking</description></item>
/// </list>
/// </remarks>
public class IrcChannel
{
    /// <summary>
    /// The channel name including prefix (e.g., "#general").
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The current channel topic.
    /// </summary>
    public string? Topic { get; set; }
    
    /// <summary>
    /// Nickname or hostmask of who set the topic.
    /// </summary>
    public string? TopicSetBy { get; set; }
    
    /// <summary>
    /// Timestamp when the topic was set.
    /// </summary>
    public DateTime? TopicSetAt { get; set; }
    
    /// <summary>
    /// List of users currently in the channel.
    /// </summary>
    public List<IrcUser> Users { get; set; } = new();
    
    /// <summary>
    /// Message history for display in the UI.
    /// </summary>
    public List<IrcMessage> Messages { get; set; } = new();
    
    /// <summary>
    /// Whether the local user has successfully joined this channel.
    /// </summary>
    public bool IsJoined { get; set; }
    
    /// <summary>
    /// Channel key if the channel is key-protected (+k).
    /// </summary>
    public string? Key { get; set; }
    
    /// <summary>
    /// Current channel modes.
    /// </summary>
    public ChannelModes Modes { get; set; } = new();
    
    /// <summary>
    /// Number of unread messages since last view.
    /// </summary>
    public int UnreadCount { get; set; }
    
    /// <summary>
    /// Whether there are unread messages that mention the user.
    /// </summary>
    public bool HasMention { get; set; }
}

/// <summary>
/// Represents channel mode flags.
/// </summary>
/// <remarks>
/// These modes correspond to common IRC channel modes:
/// <list type="bullet">
///   <item><term>+i</term><description>Invite only</description></item>
///   <item><term>+m</term><description>Moderated (only voiced users can speak)</description></item>
///   <item><term>+n</term><description>No external messages</description></item>
///   <item><term>+p</term><description>Private</description></item>
///   <item><term>+s</term><description>Secret</description></item>
///   <item><term>+t</term><description>Topic protected (ops only)</description></item>
///   <item><term>+l</term><description>User limit</description></item>
///   <item><term>+k</term><description>Channel key (password)</description></item>
/// </list>
/// </remarks>
public class ChannelModes
{
    /// <summary>Whether the channel is invite-only (+i).</summary>
    public bool InviteOnly { get; set; }
    
    /// <summary>Whether the channel is moderated (+m). Only voiced users can speak.</summary>
    public bool Moderated { get; set; }
    
    /// <summary>Whether external messages are blocked (+n).</summary>
    public bool NoExternalMessages { get; set; }
    
    /// <summary>Whether the channel is private (+p).</summary>
    public bool Private { get; set; }
    
    /// <summary>Whether the channel is secret (+s). Hidden from LIST.</summary>
    public bool Secret { get; set; }
    
    /// <summary>Whether only operators can change the topic (+t).</summary>
    public bool TopicProtected { get; set; }
    
    /// <summary>Maximum number of users allowed (+l). Null if no limit.</summary>
    public int? UserLimit { get; set; }
    
    /// <summary>Channel key/password (+k). Null if not set.</summary>
    public string? Key { get; set; }
}
