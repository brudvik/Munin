namespace IrcClient.UI.ViewModels;

/// <summary>
/// Statistics for a channel.
/// </summary>
public class ChannelStats
{
    public string ChannelName { get; set; } = string.Empty;
    public int TotalMessages { get; set; }
    public int UniqueUsers { get; set; }
    public double MessagesPerMinute { get; set; }
    public DateTime JoinedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public List<(string Nickname, int Count)> TopChatters { get; set; } = new();
    
    public string FormattedDuration
    {
        get
        {
            if (Duration.TotalDays >= 1)
                return $"{(int)Duration.TotalDays}d {Duration.Hours}h {Duration.Minutes}m";
            if (Duration.TotalHours >= 1)
                return $"{(int)Duration.TotalHours}h {Duration.Minutes}m";
            return $"{(int)Duration.TotalMinutes}m";
        }
    }
    
    public string FormattedMessagesPerMinute => MessagesPerMinute.ToString("F1");
}
