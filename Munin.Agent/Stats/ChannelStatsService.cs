using System.Collections.Concurrent;
using System.Text.Json;
using Munin.Agent.Configuration;
using Serilog;

namespace Munin.Agent.Stats;

/// <summary>
/// Tracks channel statistics like top talkers, activity times, and word counts.
/// Similar to Eggdrop's stats1.4 script.
/// </summary>
public class ChannelStatsService
{
    private readonly ILogger _logger;
    private readonly AgentConfigurationService _configService;
    private readonly string _statsDirectory;
    
    // Stats per server/channel
    private readonly ConcurrentDictionary<string, ChannelStats> _channelStats = new();
    
    // Timer for periodic saves
    private Timer? _saveTimer;

    public ChannelStatsService(AgentConfigurationService configService)
    {
        _logger = Log.ForContext<ChannelStatsService>();
        _configService = configService;
        _statsDirectory = Path.Combine(AppContext.BaseDirectory, "stats");
    }

    /// <summary>
    /// Initializes the stats service.
    /// </summary>
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_statsDirectory);
        
        // Load existing stats
        await LoadAllStatsAsync();
        
        // Save stats every 5 minutes
        _saveTimer = new Timer(async _ => await SaveAllStatsAsync(), null, 
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        _logger.Information("Channel stats service initialized");
    }

    /// <summary>
    /// Records a message for statistics.
    /// </summary>
    public void RecordMessage(string serverId, string channel, string nick, string message)
    {
        var stats = GetOrCreateStats(serverId, channel);
        var now = DateTime.UtcNow;
        var hour = now.Hour;
        var dayOfWeek = (int)now.DayOfWeek;

        lock (stats)
        {
            // Update total message count
            stats.TotalMessages++;
            stats.LastActivity = now;

            // Update hourly activity
            if (!stats.HourlyActivity.ContainsKey(hour))
                stats.HourlyActivity[hour] = 0;
            stats.HourlyActivity[hour]++;

            // Update daily activity
            if (!stats.DailyActivity.ContainsKey(dayOfWeek))
                stats.DailyActivity[dayOfWeek] = 0;
            stats.DailyActivity[dayOfWeek]++;

            // Update user stats
            if (!stats.UserStats.ContainsKey(nick))
            {
                stats.UserStats[nick] = new UserStats { Nick = nick };
            }

            var userStats = stats.UserStats[nick];
            userStats.MessageCount++;
            userStats.LastSeen = now;
            userStats.CharacterCount += message.Length;
            userStats.WordCount += message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            // Track actions (/me)
            if (message.StartsWith("\x01ACTION"))
            {
                userStats.ActionCount++;
            }

            // Track questions
            if (message.EndsWith("?"))
            {
                userStats.QuestionCount++;
            }

            // Track exclamations
            if (message.EndsWith("!"))
            {
                userStats.ExclamationCount++;
            }

            // Track all caps
            if (message.Length > 5 && message.ToUpper() == message && message.Any(char.IsLetter))
            {
                userStats.AllCapsCount++;
            }

            // Track URLs
            if (message.Contains("http://") || message.Contains("https://"))
            {
                userStats.UrlCount++;
            }

            // Update unique days
            var today = now.Date.ToString("yyyy-MM-dd");
            if (!userStats.UniqueDays.Contains(today))
            {
                userStats.UniqueDays.Add(today);
                // Keep only last 365 days
                if (userStats.UniqueDays.Count > 365)
                {
                    userStats.UniqueDays.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>
    /// Records a join event.
    /// </summary>
    public void RecordJoin(string serverId, string channel, string nick)
    {
        var stats = GetOrCreateStats(serverId, channel);
        
        lock (stats)
        {
            stats.TotalJoins++;
            
            if (!stats.UserStats.ContainsKey(nick))
            {
                stats.UserStats[nick] = new UserStats { Nick = nick };
            }
            stats.UserStats[nick].JoinCount++;
            stats.UserStats[nick].LastSeen = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Records a part event.
    /// </summary>
    public void RecordPart(string serverId, string channel, string nick)
    {
        var stats = GetOrCreateStats(serverId, channel);
        
        lock (stats)
        {
            stats.TotalParts++;
            
            if (stats.UserStats.TryGetValue(nick, out var userStats))
            {
                userStats.PartCount++;
            }
        }
    }

    /// <summary>
    /// Records a kick event.
    /// </summary>
    public void RecordKick(string serverId, string channel, string kickerNick, string kickedNick)
    {
        var stats = GetOrCreateStats(serverId, channel);
        
        lock (stats)
        {
            stats.TotalKicks++;
            
            if (stats.UserStats.TryGetValue(kickerNick, out var kickerStats))
            {
                kickerStats.KicksGiven++;
            }
            
            if (stats.UserStats.TryGetValue(kickedNick, out var kickedStats))
            {
                kickedStats.KicksReceived++;
            }
        }
    }

    /// <summary>
    /// Records a topic change.
    /// </summary>
    public void RecordTopicChange(string serverId, string channel, string nick, string newTopic)
    {
        var stats = GetOrCreateStats(serverId, channel);
        
        lock (stats)
        {
            stats.TotalTopicChanges++;
            stats.LastTopic = newTopic;
            stats.LastTopicSetBy = nick;
            stats.LastTopicSetAt = DateTime.UtcNow;
            
            if (stats.UserStats.TryGetValue(nick, out var userStats))
            {
                userStats.TopicChanges++;
            }
        }
    }

    /// <summary>
    /// Gets statistics for a channel.
    /// </summary>
    public ChannelStats? GetStats(string serverId, string channel)
    {
        var key = $"{serverId}:{channel.ToLowerInvariant()}";
        return _channelStats.TryGetValue(key, out var stats) ? stats : null;
    }

    /// <summary>
    /// Gets the top N talkers in a channel.
    /// </summary>
    public List<UserStats> GetTopTalkers(string serverId, string channel, int count = 10)
    {
        var stats = GetStats(serverId, channel);
        if (stats == null) return new List<UserStats>();

        lock (stats)
        {
            return stats.UserStats.Values
                .OrderByDescending(u => u.MessageCount)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Gets peak activity hours.
    /// </summary>
    public Dictionary<int, long> GetPeakHours(string serverId, string channel)
    {
        var stats = GetStats(serverId, channel);
        if (stats == null) return new Dictionary<int, long>();

        lock (stats)
        {
            return new Dictionary<int, long>(stats.HourlyActivity);
        }
    }

    /// <summary>
    /// Generates a text-based stats report.
    /// </summary>
    public string GenerateReport(string serverId, string channel)
    {
        var stats = GetStats(serverId, channel);
        if (stats == null)
            return $"No statistics available for {channel}";

        var lines = new List<string>();
        
        lock (stats)
        {
            lines.Add($"=== Statistics for {channel} ===");
            lines.Add($"Total messages: {stats.TotalMessages:N0}");
            lines.Add($"Total joins: {stats.TotalJoins:N0} | Parts: {stats.TotalParts:N0} | Kicks: {stats.TotalKicks:N0}");
            lines.Add($"Unique users: {stats.UserStats.Count:N0}");
            lines.Add($"Last activity: {stats.LastActivity:u}");
            lines.Add("");
            lines.Add("Top 5 talkers:");
            
            var topTalkers = stats.UserStats.Values
                .OrderByDescending(u => u.MessageCount)
                .Take(5)
                .ToList();

            for (int i = 0; i < topTalkers.Count; i++)
            {
                var user = topTalkers[i];
                var avgWords = user.MessageCount > 0 ? user.WordCount / user.MessageCount : 0;
                lines.Add($"  {i + 1}. {user.Nick}: {user.MessageCount:N0} messages ({avgWords:F1} words/msg)");
            }

            lines.Add("");
            lines.Add("Peak hours (UTC):");
            var peakHours = stats.HourlyActivity
                .OrderByDescending(h => h.Value)
                .Take(3)
                .ToList();
            
            foreach (var hour in peakHours)
            {
                lines.Add($"  {hour.Key:D2}:00 - {hour.Value:N0} messages");
            }
        }

        return string.Join("\n", lines);
    }

    #region Private Methods

    private ChannelStats GetOrCreateStats(string serverId, string channel)
    {
        var key = $"{serverId}:{channel.ToLowerInvariant()}";
        return _channelStats.GetOrAdd(key, _ => new ChannelStats
        {
            ServerId = serverId,
            Channel = channel,
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task LoadAllStatsAsync()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_statsDirectory, "*.stats.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var stats = JsonSerializer.Deserialize<ChannelStats>(json);
                    if (stats != null)
                    {
                        var key = $"{stats.ServerId}:{stats.Channel.ToLowerInvariant()}";
                        _channelStats[key] = stats;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to load stats file: {File}", file);
                }
            }

            _logger.Information("Loaded stats for {Count} channels", _channelStats.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load channel stats");
        }
    }

    private async Task SaveAllStatsAsync()
    {
        foreach (var kvp in _channelStats)
        {
            try
            {
                var stats = kvp.Value;
                var fileName = $"{stats.ServerId}_{stats.Channel.TrimStart('#')}.stats.json";
                var filePath = Path.Combine(_statsDirectory, fileName);

                string json;
                lock (stats)
                {
                    json = JsonSerializer.Serialize(stats, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }

                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to save stats for {Key}", kvp.Key);
            }
        }
    }

    #endregion
}

/// <summary>
/// Statistics for a single channel.
/// </summary>
public class ChannelStats
{
    public string ServerId { get; set; } = "";
    public string Channel { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivity { get; set; }
    
    public long TotalMessages { get; set; }
    public long TotalJoins { get; set; }
    public long TotalParts { get; set; }
    public long TotalKicks { get; set; }
    public long TotalTopicChanges { get; set; }
    
    public string? LastTopic { get; set; }
    public string? LastTopicSetBy { get; set; }
    public DateTime? LastTopicSetAt { get; set; }
    
    /// <summary>
    /// Messages per hour (0-23).
    /// </summary>
    public Dictionary<int, long> HourlyActivity { get; set; } = new();
    
    /// <summary>
    /// Messages per day of week (0=Sunday).
    /// </summary>
    public Dictionary<int, long> DailyActivity { get; set; } = new();
    
    /// <summary>
    /// Per-user statistics.
    /// </summary>
    public Dictionary<string, UserStats> UserStats { get; set; } = new();
}

/// <summary>
/// Statistics for a single user.
/// </summary>
public class UserStats
{
    public string Nick { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    
    public long MessageCount { get; set; }
    public long WordCount { get; set; }
    public long CharacterCount { get; set; }
    public long ActionCount { get; set; }
    public long QuestionCount { get; set; }
    public long ExclamationCount { get; set; }
    public long AllCapsCount { get; set; }
    public long UrlCount { get; set; }
    
    public long JoinCount { get; set; }
    public long PartCount { get; set; }
    public long KicksGiven { get; set; }
    public long KicksReceived { get; set; }
    public long TopicChanges { get; set; }
    
    /// <summary>
    /// Unique days the user has been active.
    /// </summary>
    public List<string> UniqueDays { get; set; } = new();
}
