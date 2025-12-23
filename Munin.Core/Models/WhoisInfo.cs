namespace Munin.Core.Models;

/// <summary>
/// Contains WHOIS information for a user.
/// </summary>
public class WhoisInfo
{
    public string Nickname { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Hostname { get; set; }
    public string? RealName { get; set; }
    public string? Server { get; set; }
    public string? ServerInfo { get; set; }
    public List<string> Channels { get; set; } = new();
    public bool IsAway { get; set; }
    public string? AwayMessage { get; set; }
    public bool IsOperator { get; set; }
    public DateTime? IdleTime { get; set; }
    public int IdleSeconds { get; set; }
    public DateTime? SignonTime { get; set; }
    public string? Account { get; set; }
    public bool IsSecure { get; set; }
    
    /// <summary>
    /// Formatted idle time string.
    /// </summary>
    public string FormattedIdleTime
    {
        get
        {
            if (IdleSeconds == 0) return "Active now";
            var span = TimeSpan.FromSeconds(IdleSeconds);
            if (span.TotalDays >= 1)
                return $"Idle {(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1)
                return $"Idle {(int)span.TotalHours}h {span.Minutes}m";
            if (span.TotalMinutes >= 1)
                return $"Idle {(int)span.TotalMinutes}m";
            return $"Idle {span.Seconds}s";
        }
    }
    
    /// <summary>
    /// User@host string.
    /// </summary>
    public string UserHost => $"{Username}@{Hostname}";
    
    /// <summary>
    /// Channels as comma-separated string.
    /// </summary>
    public string ChannelsString => string.Join(", ", Channels);
}
