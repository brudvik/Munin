namespace IrcClient.Core.Models;

/// <summary>
/// Represents a channel list entry (ban, exception, or invite).
/// </summary>
public class ChannelListModeEntry
{
    /// <summary>
    /// The mode type (b=ban, e=exception, I=invite).
    /// </summary>
    public char Mode { get; set; }

    /// <summary>
    /// The mask (e.g., *!*@hostname).
    /// </summary>
    public string Mask { get; set; } = string.Empty;

    /// <summary>
    /// Who set this entry.
    /// </summary>
    public string? SetBy { get; set; }

    /// <summary>
    /// When this entry was set (Unix timestamp).
    /// </summary>
    public DateTime? SetAt { get; set; }
}

/// <summary>
/// Types of channel list modes.
/// </summary>
public enum ChannelListModeType
{
    Ban,        // +b
    Exception,  // +e
    Invite      // +I
}
