using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Munin.Agent.Configuration;
using Munin.Agent.Services;
using Munin.Agent.UserDatabase;
using Munin.Core.Models;
using Serilog;

namespace Munin.Agent.Protection;

/// <summary>
/// Provides channel protection features like flood detection, clone detection,
/// bad word filtering, and mass-kick protection.
/// </summary>
public class ChannelProtectionService
{
    private readonly ILogger _logger;
    private readonly AgentConfigurationService _configService;
    private readonly AgentUserDatabaseService _userDatabase;
    private readonly IrcBotService _botService;
    
    // Flood tracking
    private readonly ConcurrentDictionary<string, FloodTracker> _floodTrackers = new();
    
    // Clone tracking
    private readonly ConcurrentDictionary<string, HostTracker> _hostTrackers = new();
    
    // Mass-kick tracking
    private readonly ConcurrentDictionary<string, MassActionTracker> _kickTrackers = new();
    
    // Compiled bad word patterns
    private readonly List<CompiledBadWord> _badWordPatterns = new();
    
    // Cleanup timer
    private Timer? _cleanupTimer;

    /// <summary>
    /// Event raised when a protection action is taken.
    /// </summary>
    public event EventHandler<ProtectionActionEventArgs>? ActionTaken;

    public ChannelProtectionService(
        AgentConfigurationService configService,
        AgentUserDatabaseService userDatabase,
        IrcBotService botService)
    {
        _logger = Log.ForContext<ChannelProtectionService>();
        _configService = configService;
        _userDatabase = userDatabase;
        _botService = botService;
    }

    /// <summary>
    /// Initializes the protection service.
    /// </summary>
    public void Initialize()
    {
        var config = _configService.Configuration.ChannelProtection;

        // Compile bad word patterns
        _badWordPatterns.Clear();
        foreach (var badWord in config.BadWords)
        {
            try
            {
                var pattern = badWord.IsRegex 
                    ? new Regex(badWord.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)
                    : new Regex(Regex.Escape(badWord.Pattern), RegexOptions.IgnoreCase | RegexOptions.Compiled);
                
                _badWordPatterns.Add(new CompiledBadWord
                {
                    Pattern = pattern,
                    Action = badWord.Action,
                    Reason = badWord.Reason
                });
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Invalid bad word pattern: {Pattern}", badWord.Pattern);
            }
        }

        // Start cleanup timer (every minute)
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _logger.Information("Channel protection initialized with {BadWordCount} bad word patterns", 
            _badWordPatterns.Count);
    }

    /// <summary>
    /// Processes a channel message for protection checks.
    /// </summary>
    /// <returns>True if the message should be blocked/acted upon.</returns>
    public async Task<ProtectionResult> ProcessMessageAsync(
        string serverId, 
        string channel, 
        string nick, 
        string hostmask, 
        string message)
    {
        var config = _configService.Configuration.ChannelProtection;
        var result = new ProtectionResult();

        // Check if user is protected (has friend flag)
        var user = _userDatabase.MatchUser(hostmask);
        if (user?.HasFlag(UserFlags.Friend, channel) == true)
        {
            return result; // Friends are exempt
        }

        // Flood protection
        if (config.FloodProtection)
        {
            var floodResult = CheckFlood(serverId, channel, nick, hostmask);
            if (floodResult.IsFlooding)
            {
                result.Action = config.FloodAction;
                result.Reason = "Flood protection triggered";
                result.ActionTaken = true;

                await TakeActionAsync(serverId, channel, nick, hostmask, result.Action, result.Reason);
                ActionTaken?.Invoke(this, new ProtectionActionEventArgs(
                    ProtectionType.Flood, result.Action, serverId, channel, nick, hostmask, result.Reason));

                return result;
            }
        }

        // Bad word filter
        var badWordMatch = CheckBadWords(message);
        if (badWordMatch != null)
        {
            result.Action = badWordMatch.Action;
            result.Reason = badWordMatch.Reason;
            result.ActionTaken = true;

            await TakeActionAsync(serverId, channel, nick, hostmask, result.Action, result.Reason);
            ActionTaken?.Invoke(this, new ProtectionActionEventArgs(
                ProtectionType.BadWord, result.Action, serverId, channel, nick, hostmask, result.Reason));

            return result;
        }

        return result;
    }

    /// <summary>
    /// Processes a join event for clone detection.
    /// </summary>
    public async Task<ProtectionResult> ProcessJoinAsync(
        string serverId, 
        string channel, 
        string nick, 
        string hostmask)
    {
        var config = _configService.Configuration.ChannelProtection;
        var result = new ProtectionResult();

        if (!config.CloneDetection)
            return result;

        // Check if user is protected
        var user = _userDatabase.MatchUser(hostmask);
        if (user?.HasFlag(UserFlags.Friend, channel) == true)
            return result;

        // Extract host from hostmask
        var host = ExtractHost(hostmask);
        if (string.IsNullOrEmpty(host))
            return result;

        var key = $"{serverId}:{channel}:{host}";
        var tracker = _hostTrackers.GetOrAdd(key, _ => new HostTracker { Host = host });
        
        lock (tracker)
        {
            tracker.Nicks.Add(nick);
            tracker.LastSeen = DateTime.UtcNow;

            if (tracker.Nicks.Count > config.MaxClonesPerHost)
            {
                result.Action = "kickban";
                result.Reason = $"Clone limit exceeded ({tracker.Nicks.Count} from {host})";
                result.ActionTaken = true;
            }
        }

        if (result.ActionTaken)
        {
            await TakeActionAsync(serverId, channel, nick, hostmask, result.Action, result.Reason);
            ActionTaken?.Invoke(this, new ProtectionActionEventArgs(
                ProtectionType.Clone, result.Action, serverId, channel, nick, hostmask, result.Reason));
        }

        return result;
    }

    /// <summary>
    /// Processes a kick event for mass-kick detection.
    /// </summary>
    public async Task<ProtectionResult> ProcessKickAsync(
        string serverId,
        string channel,
        string kickerNick,
        string kickerHostmask,
        string kickedNick)
    {
        var config = _configService.Configuration.ChannelProtection;
        var result = new ProtectionResult();

        if (!config.MassKickProtection)
            return result;

        // Track kicks by this person
        var key = $"{serverId}:{channel}:{kickerNick}";
        var tracker = _kickTrackers.GetOrAdd(key, _ => new MassActionTracker());
        
        var now = DateTime.UtcNow;
        var threshold = TimeSpan.FromSeconds(config.MassKickIntervalSeconds);

        lock (tracker)
        {
            // Remove old entries
            tracker.Times.RemoveAll(t => now - t > threshold);
            tracker.Times.Add(now);

            if (tracker.Times.Count >= config.MassKickThreshold)
            {
                result.Action = "kickban";
                result.Reason = "Mass-kick detected";
                result.ActionTaken = true;
                tracker.Times.Clear(); // Reset after action
            }
        }

        if (result.ActionTaken)
        {
            // Deop and kickban the mass-kicker
            await _botService.SendRawAsync(serverId, $"MODE {channel} -o {kickerNick}");
            await TakeActionAsync(serverId, channel, kickerNick, kickerHostmask, result.Action, result.Reason);
            
            ActionTaken?.Invoke(this, new ProtectionActionEventArgs(
                ProtectionType.MassKick, result.Action, serverId, channel, kickerNick, kickerHostmask, result.Reason));
        }

        return result;
    }

    /// <summary>
    /// Invites a user to a +i channel if they have the friend flag.
    /// </summary>
    public async Task<bool> TryAutoInviteAsync(string serverId, string channel, string nick, string hostmask)
    {
        var config = _configService.Configuration.ChannelProtection;
        
        if (!config.InviteOnlyGuard)
            return false;

        var user = _userDatabase.MatchUser(hostmask);
        if (user?.HasFlag(UserFlags.Friend, channel) == true)
        {
            await _botService.SendRawAsync(serverId, $"INVITE {nick} {channel}");
            _logger.Information("Auto-invited {Nick} to {Channel}", nick, channel);
            return true;
        }

        return false;
    }

    #region Private Methods

    private FloodCheckResult CheckFlood(string serverId, string channel, string nick, string hostmask)
    {
        var config = _configService.Configuration.ChannelProtection;
        var key = $"{serverId}:{channel}:{nick}";
        var tracker = _floodTrackers.GetOrAdd(key, _ => new FloodTracker
        {
            Nick = nick,
            Hostmask = hostmask
        });

        var now = DateTime.UtcNow;
        var interval = TimeSpan.FromSeconds(config.FloodIntervalSeconds);

        lock (tracker)
        {
            // Remove old messages
            tracker.MessageTimes.RemoveAll(t => now - t > interval);
            tracker.MessageTimes.Add(now);

            return new FloodCheckResult
            {
                IsFlooding = tracker.MessageTimes.Count > config.FloodMaxMessages,
                MessageCount = tracker.MessageTimes.Count
            };
        }
    }

    private CompiledBadWord? CheckBadWords(string message)
    {
        foreach (var badWord in _badWordPatterns)
        {
            if (badWord.Pattern.IsMatch(message))
            {
                return badWord;
            }
        }
        return null;
    }

    private async Task TakeActionAsync(
        string serverId, 
        string channel, 
        string nick, 
        string hostmask, 
        string action, 
        string reason)
    {
        var banMask = CreateBanMask(hostmask);

        switch (action.ToLowerInvariant())
        {
            case "kick":
                await _botService.SendRawAsync(serverId, $"KICK {channel} {nick} :{reason}");
                break;

            case "ban":
                await _botService.SendRawAsync(serverId, $"MODE {channel} +b {banMask}");
                break;

            case "kickban":
                await _botService.SendRawAsync(serverId, $"MODE {channel} +b {banMask}");
                await _botService.SendRawAsync(serverId, $"KICK {channel} {nick} :{reason}");
                break;

            case "quiet":
                // +q mode (server-dependent)
                await _botService.SendRawAsync(serverId, $"MODE {channel} +q {banMask}");
                break;

            case "warn":
                await _botService.SendMessageAsync(serverId, channel, $"{nick}: Warning - {reason}");
                break;
        }

        _logger.Information("Protection action: {Action} on {Nick} in {Channel} - {Reason}",
            action, nick, channel, reason);
    }

    private static string ExtractHost(string hostmask)
    {
        var atIndex = hostmask.LastIndexOf('@');
        return atIndex >= 0 ? hostmask[(atIndex + 1)..] : "";
    }

    private static string CreateBanMask(string hostmask)
    {
        // Create *!*@host style ban mask
        var atIndex = hostmask.LastIndexOf('@');
        if (atIndex >= 0)
        {
            var host = hostmask[(atIndex + 1)..];
            return $"*!*@{host}";
        }
        return hostmask;
    }

    private void Cleanup()
    {
        var now = DateTime.UtcNow;
        var expiry = TimeSpan.FromMinutes(5);

        // Clean up old flood trackers
        foreach (var key in _floodTrackers.Keys.ToList())
        {
            if (_floodTrackers.TryGetValue(key, out var tracker))
            {
                lock (tracker)
                {
                    tracker.MessageTimes.RemoveAll(t => now - t > expiry);
                    if (tracker.MessageTimes.Count == 0)
                    {
                        _floodTrackers.TryRemove(key, out _);
                    }
                }
            }
        }

        // Clean up old host trackers
        foreach (var key in _hostTrackers.Keys.ToList())
        {
            if (_hostTrackers.TryGetValue(key, out var tracker))
            {
                if (now - tracker.LastSeen > expiry)
                {
                    _hostTrackers.TryRemove(key, out _);
                }
            }
        }

        // Clean up old kick trackers
        foreach (var key in _kickTrackers.Keys.ToList())
        {
            if (_kickTrackers.TryGetValue(key, out var tracker))
            {
                lock (tracker)
                {
                    tracker.Times.RemoveAll(t => now - t > expiry);
                    if (tracker.Times.Count == 0)
                    {
                        _kickTrackers.TryRemove(key, out _);
                    }
                }
            }
        }
    }

    #endregion
}

// Internal tracking classes

internal class FloodTracker
{
    public string Nick { get; set; } = "";
    public string Hostmask { get; set; } = "";
    public List<DateTime> MessageTimes { get; } = new();
}

internal class FloodCheckResult
{
    public bool IsFlooding { get; set; }
    public int MessageCount { get; set; }
}

internal class HostTracker
{
    public string Host { get; set; } = "";
    public HashSet<string> Nicks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime LastSeen { get; set; }
}

internal class MassActionTracker
{
    public List<DateTime> Times { get; } = new();
}

internal class CompiledBadWord
{
    public Regex Pattern { get; set; } = null!;
    public string Action { get; set; } = "kick";
    public string Reason { get; set; } = "";
}

// Public result types

public class ProtectionResult
{
    public bool ActionTaken { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
}

public enum ProtectionType
{
    Flood,
    Clone,
    BadWord,
    MassKick,
    InviteOnly
}

public class ProtectionActionEventArgs : EventArgs
{
    public ProtectionType Type { get; }
    public string Action { get; }
    public string ServerId { get; }
    public string Channel { get; }
    public string Nick { get; }
    public string Hostmask { get; }
    public string Reason { get; }

    public ProtectionActionEventArgs(
        ProtectionType type,
        string action,
        string serverId,
        string channel,
        string nick,
        string hostmask,
        string reason)
    {
        Type = type;
        Action = action;
        ServerId = serverId;
        Channel = channel;
        Nick = nick;
        Hostmask = hostmask;
        Reason = reason;
    }
}
