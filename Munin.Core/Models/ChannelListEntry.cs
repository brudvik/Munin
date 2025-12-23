namespace Munin.Core.Models;

/// <summary>
/// Represents a channel entry from the LIST command.
/// </summary>
public class ChannelListEntry
{
    public string Name { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public string Topic { get; set; } = string.Empty;
}
