namespace Munin.Core.Models;

/// <summary>
/// Represents an IRC user in a channel.
/// </summary>
/// <remarks>
/// This class tracks user information including:
/// <list type="bullet">
///   <item><description>Identity (nickname, username, hostname)</description></item>
///   <item><description>Channel privileges (operator, voice, etc.)</description></item>
///   <item><description>Account status (from IRCv3 account-tag)</description></item>
///   <item><description>Away status</description></item>
/// </list>
/// </remarks>
public class IrcUser
{
    /// <summary>
    /// The user's current nickname.
    /// </summary>
    public string Nickname { get; set; } = string.Empty;
    
    /// <summary>
    /// The user's ident/username.
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// The user's hostname or cloaked host.
    /// </summary>
    public string? Hostname { get; set; }
    
    /// <summary>
    /// The user's realname/GECOS.
    /// </summary>
    public string? RealName { get; set; }
    
    /// <summary>
    /// The user's services account name (from IRCv3 extended-join or account-tag).
    /// </summary>
    /// <remarks>
    /// <para>Null if not available. "*" treated as not logged in.</para>
    /// <para>Requires <c>account-tag</c> or <c>extended-join</c> capability.</para>
    /// </remarks>
    public string? Account { get; set; }
    
    /// <summary>
    /// The user's channel privilege mode.
    /// </summary>
    public UserMode Mode { get; set; } = UserMode.Normal;
    
    /// <summary>
    /// Whether the user is currently marked as away.
    /// </summary>
    public bool IsAway { get; set; }
    
    /// <summary>
    /// The user's away message, if set.
    /// </summary>
    public string? AwayMessage { get; set; }
    
    /// <summary>
    /// Whether the user is logged in to a services account.
    /// </summary>
    public bool IsLoggedIn => !string.IsNullOrEmpty(Account) && Account != "*";
    
    /// <summary>
    /// Gets the prefix character for the user's mode (e.g., "@" for operator).
    /// </summary>
    public string Prefix => Mode switch
    {
        UserMode.Owner => "~",
        UserMode.Admin => "&",
        UserMode.Operator => "@",
        UserMode.HalfOperator => "%",
        UserMode.Voice => "+",
        _ => ""
    };

    /// <summary>
    /// Gets the display name with prefix (e.g., "@Nickname").
    /// </summary>
    public string DisplayName => $"{Prefix}{Nickname}";

    /// <summary>
    /// Gets the sort order for user list display (higher privileges first).
    /// </summary>
    public int SortOrder => Mode switch
    {
        UserMode.Owner => 0,
        UserMode.Admin => 1,
        UserMode.Operator => 2,
        UserMode.HalfOperator => 3,
        UserMode.Voice => 4,
        _ => 5
    };
}

/// <summary>
/// User privilege modes in a channel.
/// </summary>
/// <remarks>
/// Modes are listed in ascending order of privilege.
/// Different networks may support different subsets of these modes.
/// </remarks>
public enum UserMode
{
    /// <summary>Regular user with no special privileges.</summary>
    Normal,
    
    /// <summary>Voiced user (+v). Can speak in moderated channels.</summary>
    Voice,
    
    /// <summary>Half-operator (+h). Limited operator privileges.</summary>
    HalfOperator,
    
    /// <summary>Channel operator (+o). Full channel control.</summary>
    Operator,
    
    /// <summary>Channel admin (+a). Above operator on some networks.</summary>
    Admin,
    
    /// <summary>Channel owner (+q). Highest privilege level.</summary>
    Owner
}
