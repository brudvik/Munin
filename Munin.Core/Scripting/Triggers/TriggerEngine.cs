using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Munin.Core.Scripting.Triggers;

/// <summary>
/// Simple YAML/JSON-based trigger engine for automation without coding.
/// </summary>
[SuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Triggers use JSON deserialization and trimming is not used")]
public class TriggerEngine : IScriptEngine
{
    private ScriptContext _context = null!;
    private readonly List<TriggerDefinition> _triggers = new();
    private readonly Dictionary<string, List<TriggerDefinition>> _triggersByFile = new();
    private readonly object _lock = new();
    
    public string Name => "Triggers";
    public string FileExtension => ".triggers.json";

    public void Initialize(ScriptContext context)
    {
        _context = context;
    }

    public async Task<ScriptResult> LoadScriptAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var triggers = JsonSerializer.Deserialize<TriggerFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (triggers?.Triggers == null)
            {
                return ScriptResult.Fail("Invalid trigger file format");
            }
            
            var scriptName = Path.GetFileNameWithoutExtension(filePath);
            
            lock (_lock)
            {
                // Remove old triggers from this file
                if (_triggersByFile.TryGetValue(scriptName, out var oldTriggers))
                {
                    foreach (var t in oldTriggers)
                        _triggers.Remove(t);
                }
                
                // Add new triggers
                var newTriggers = triggers.Triggers.ToList();
                _triggers.AddRange(newTriggers);
                _triggersByFile[scriptName] = newTriggers;
            }
            
            return ScriptResult.Ok();
        }
        catch (JsonException ex)
        {
            return ScriptResult.Fail($"JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ScriptResult.Fail($"Failed to load triggers: {ex.Message}");
        }
    }

    public Task<ScriptResult> ExecuteAsync(string code, string? scriptName = null)
    {
        // Triggers don't support inline execution
        return Task.FromResult(ScriptResult.Fail("Triggers must be loaded from files"));
    }

    public void UnloadScript(string scriptName)
    {
        lock (_lock)
        {
            if (_triggersByFile.TryGetValue(scriptName, out var triggers))
            {
                foreach (var t in triggers)
                    _triggers.Remove(t);
                _triggersByFile.Remove(scriptName);
            }
        }
    }

    public async Task DispatchEventAsync(ScriptEvent scriptEvent)
    {
        List<TriggerDefinition> matchingTriggers;
        
        lock (_lock)
        {
            matchingTriggers = _triggers
                .Where(t => t.On.Equals(scriptEvent.EventType, StringComparison.OrdinalIgnoreCase))
                .Where(t => MatchesTrigger(t, scriptEvent))
                .ToList();
        }

        foreach (var trigger in matchingTriggers)
        {
            try
            {
                await ExecuteTriggerAction(trigger, scriptEvent);
                
                if (trigger.Cancel)
                {
                    scriptEvent.Cancelled = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                _context.RaiseError("Triggers", $"Error executing trigger: {ex.Message}");
            }
        }
    }

    private bool MatchesTrigger(TriggerDefinition trigger, ScriptEvent scriptEvent)
    {
        // Check server filter
        if (!string.IsNullOrEmpty(trigger.Server))
        {
            if (!scriptEvent.ServerName.Equals(trigger.Server, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        
        // Check channel filter
        if (!string.IsNullOrEmpty(trigger.Channel))
        {
            var eventChannel = GetEventChannel(scriptEvent);
            if (eventChannel == null) return false;
            
            // Support wildcards
            if (trigger.Channel.Contains('*'))
            {
                var pattern = "^" + Regex.Escape(trigger.Channel).Replace("\\*", ".*") + "$";
                if (!Regex.IsMatch(eventChannel, pattern, RegexOptions.IgnoreCase))
                    return false;
            }
            else if (!eventChannel.Equals(trigger.Channel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        
        // Check nick filter
        if (!string.IsNullOrEmpty(trigger.Nick))
        {
            var eventNick = GetEventNick(scriptEvent);
            if (eventNick == null) return false;
            
            if (trigger.Nick.Contains('*'))
            {
                var pattern = "^" + Regex.Escape(trigger.Nick).Replace("\\*", ".*") + "$";
                if (!Regex.IsMatch(eventNick, pattern, RegexOptions.IgnoreCase))
                    return false;
            }
            else if (!eventNick.Equals(trigger.Nick, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        
        // Check text match
        if (!string.IsNullOrEmpty(trigger.Match))
        {
            var text = GetEventText(scriptEvent);
            if (text == null) return false;
            
            if (trigger.IsRegex)
            {
                try
                {
                    if (!Regex.IsMatch(text, trigger.Match, RegexOptions.IgnoreCase))
                        return false;
                }
                catch
                {
                    return false; // Invalid regex
                }
            }
            else if (trigger.MatchExact)
            {
                if (!text.Equals(trigger.Match, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                // Contains match (supports simple prefix like "!command")
                if (trigger.Match.StartsWith("!") || trigger.Match.StartsWith("."))
                {
                    // Command match - check if text starts with command
                    var parts = text.Split(' ', 2);
                    if (!parts[0].Equals(trigger.Match, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else if (!text.Contains(trigger.Match, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        
        return true;
    }

    private async Task ExecuteTriggerAction(TriggerDefinition trigger, ScriptEvent scriptEvent)
    {
        var serverName = scriptEvent.ServerName;
        var target = ResolveTarget(trigger, scriptEvent);
        
        switch (trigger.Action?.ToLowerInvariant())
        {
            case "reply":
            case "say":
                if (!string.IsNullOrEmpty(trigger.Text) && target != null)
                {
                    var text = ExpandVariables(trigger.Text, scriptEvent);
                    await _context.SendMessageAsync(serverName, target, text);
                }
                break;
                
            case "action":
            case "me":
                if (!string.IsNullOrEmpty(trigger.Text) && target != null)
                {
                    var text = ExpandVariables(trigger.Text, scriptEvent);
                    await _context.SendActionAsync(serverName, target, text);
                }
                break;
                
            case "notice":
                if (!string.IsNullOrEmpty(trigger.Text) && target != null)
                {
                    var text = ExpandVariables(trigger.Text, scriptEvent);
                    await _context.SendNoticeAsync(serverName, target, text);
                }
                break;
                
            case "raw":
                if (!string.IsNullOrEmpty(trigger.Text))
                {
                    var text = ExpandVariables(trigger.Text, scriptEvent);
                    await _context.SendRawAsync(serverName, text);
                }
                break;
                
            case "join":
                if (!string.IsNullOrEmpty(trigger.Target))
                {
                    await _context.JoinChannelAsync(serverName, trigger.Target, trigger.Key);
                }
                break;
                
            case "part":
                if (target != null)
                {
                    await _context.PartChannelAsync(serverName, target, trigger.Text);
                }
                break;
                
            case "kick":
                var kickTarget = GetEventNick(scriptEvent);
                var channel = GetEventChannel(scriptEvent);
                if (kickTarget != null && channel != null)
                {
                    await _context.KickUserAsync(serverName, channel, kickTarget, trigger.Text);
                }
                break;
                
            case "ban":
                var banTarget = GetEventNick(scriptEvent);
                var banChannel = GetEventChannel(scriptEvent);
                if (banTarget != null && banChannel != null)
                {
                    await _context.SetModeAsync(serverName, banChannel, $"+b {banTarget}!*@*");
                }
                break;
                
            case "print":
            case "log":
                if (!string.IsNullOrEmpty(trigger.Text))
                {
                    var text = ExpandVariables(trigger.Text, scriptEvent);
                    _context.Print(text);
                }
                break;
        }
        
        // Delay if specified
        if (trigger.Delay > 0)
        {
            await Task.Delay(trigger.Delay);
        }
    }

    private string? ResolveTarget(TriggerDefinition trigger, ScriptEvent scriptEvent)
    {
        if (!string.IsNullOrEmpty(trigger.Target))
            return trigger.Target;
            
        return GetEventChannel(scriptEvent) ?? GetEventNick(scriptEvent);
    }

    private string ExpandVariables(string text, ScriptEvent scriptEvent)
    {
        var result = text;
        
        // Basic variables
        result = result.Replace("{server}", scriptEvent.ServerName);
        result = result.Replace("{nick}", GetEventNick(scriptEvent) ?? "");
        result = result.Replace("{channel}", GetEventChannel(scriptEvent) ?? "");
        result = result.Replace("{text}", GetEventText(scriptEvent) ?? "");
        result = result.Replace("{me}", _context.GetCurrentNick(scriptEvent.ServerName) ?? "");
        result = result.Replace("{time}", DateTime.Now.ToString("HH:mm:ss"));
        result = result.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"));
        
        // Event-specific variables
        if (scriptEvent is MessageEvent msgEvent)
        {
            // Extract command arguments
            var parts = msgEvent.Text.Split(' ', 2);
            result = result.Replace("{args}", parts.Length > 1 ? parts[1] : "");
            result = result.Replace("{1}", parts.Length > 1 ? parts[1].Split(' ')[0] : "");
            
            var args = parts.Length > 1 ? parts[1].Split(' ') : Array.Empty<string>();
            for (int i = 0; i < 10; i++)
            {
                result = result.Replace($"{{{i + 1}}}", i < args.Length ? args[i] : "");
            }
        }
        
        if (scriptEvent is NickChangeEvent nickEvent)
        {
            result = result.Replace("{oldnick}", nickEvent.OldNick);
            result = result.Replace("{newnick}", nickEvent.NewNick);
        }
        
        if (scriptEvent is TopicEvent topicEvent)
        {
            result = result.Replace("{topic}", topicEvent.Topic);
        }
        
        if (scriptEvent is KickEvent kickEvent)
        {
            result = result.Replace("{kicker}", kickEvent.Kicker);
            result = result.Replace("{kicked}", kickEvent.Kicked);
            result = result.Replace("{reason}", kickEvent.Reason ?? "");
        }
        
        return result;
    }

    private static string? GetEventChannel(ScriptEvent evt) => evt switch
    {
        MessageEvent e => e.ChannelName,
        JoinEvent e => e.ChannelName,
        PartEvent e => e.ChannelName,
        KickEvent e => e.ChannelName,
        TopicEvent e => e.ChannelName,
        NoticeEvent e => e.ChannelName,
        InputEvent e => e.ChannelName,
        InviteEvent e => e.ChannelName,
        _ => null
    };

    private static string? GetEventNick(ScriptEvent evt) => evt switch
    {
        MessageEvent e => e.Nickname,
        PrivateMessageEvent e => e.Nickname,
        JoinEvent e => e.Nickname,
        PartEvent e => e.Nickname,
        QuitEvent e => e.Nickname,
        KickEvent e => e.Kicked,
        NickChangeEvent e => e.OldNick,
        NoticeEvent e => e.Nickname,
        InviteEvent e => e.Nickname,
        _ => null
    };

    private static string? GetEventText(ScriptEvent evt) => evt switch
    {
        MessageEvent e => e.Text,
        PrivateMessageEvent e => e.Text,
        NoticeEvent e => e.Text,
        TopicEvent e => e.Topic,
        _ => null
    };

    public void Dispose()
    {
        _triggers.Clear();
        _triggersByFile.Clear();
    }
}

/// <summary>
/// Represents a trigger file structure.
/// </summary>
public class TriggerFile
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public List<TriggerDefinition> Triggers { get; set; } = new();
}

/// <summary>
/// Represents a single trigger definition.
/// </summary>
public class TriggerDefinition
{
    /// <summary>
    /// Event type to trigger on (message, join, part, quit, nick, topic, etc.)
    /// </summary>
    public string On { get; set; } = "message";
    
    /// <summary>
    /// Text pattern to match (for message events)
    /// </summary>
    public string? Match { get; set; }
    
    /// <summary>
    /// Match type: contains, exact, startsWith, endsWith, regex, wildcard
    /// </summary>
    public string? MatchType { get; set; }
    
    /// <summary>
    /// Whether Match is a regex pattern (legacy, use MatchType instead)
    /// </summary>
    public bool IsRegex { get; set; }
    
    /// <summary>
    /// Whether Match must be an exact match (legacy, use MatchType instead)
    /// </summary>
    public bool MatchExact { get; set; }
    
    /// <summary>
    /// Filter by server name
    /// </summary>
    public string? Server { get; set; }
    
    /// <summary>
    /// Filter by channel name (supports * wildcard)
    /// </summary>
    public string? Channel { get; set; }
    
    /// <summary>
    /// Filter by nickname (supports * wildcard)
    /// </summary>
    public string? Nick { get; set; }
    
    /// <summary>
    /// Action to perform (reply, send, msg, notify, sound, highlight, log, command, say, action, notice, raw, join, part, kick, ban, print)
    /// </summary>
    public string? Action { get; set; }
    
    /// <summary>
    /// Target for the action (channel or nick). If not set, replies to source.
    /// </summary>
    public string? Target { get; set; }
    
    /// <summary>
    /// Text to send. Supports variables: {nick}, {channel}, {text}, {server}, {me}, {args}, {1}...{9}
    /// </summary>
    public string? Text { get; set; }
    
    /// <summary>
    /// Message to send/display (alias for Text, used by trigger builder).
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// Command to execute (for command action).
    /// </summary>
    public string? Command { get; set; }
    
    /// <summary>
    /// Sound to play (for sound action).
    /// </summary>
    public string? Sound { get; set; }
    
    /// <summary>
    /// Channel key for join action
    /// </summary>
    public string? Key { get; set; }
    
    /// <summary>
    /// Whether to cancel the event (prevent default handling)
    /// </summary>
    public bool Cancel { get; set; }
    
    /// <summary>
    /// Delay in milliseconds before executing
    /// </summary>
    public int Delay { get; set; }
}
