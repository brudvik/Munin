using Munin.Agent.Configuration;
using Munin.Agent.UserDatabase;
using Munin.Core.Models;
using Munin.Core.Scripting;
using Munin.Core.Scripting.Lua;
using Munin.Core.Scripting.Plugins;
using Munin.Core.Scripting.Triggers;
using Serilog;

namespace Munin.Agent.Scripting;

/// <summary>
/// Agent-specific script context that extends the core ScriptContext
/// with agent features like user database access and Eggdrop-style binds.
/// </summary>
public class AgentScriptContext : ScriptContext
{
    private readonly ILogger _logger;
    private readonly AgentUserDatabaseService _userDatabase;
    private readonly AgentConfigurationService _configService;
    
    // Eggdrop-style bind registrations
    private readonly Dictionary<string, List<ScriptBind>> _binds = new();
    private readonly object _bindLock = new();

    /// <summary>
    /// Gets the user database service.
    /// </summary>
    public AgentUserDatabaseService UserDatabase => _userDatabase;

    /// <summary>
    /// Event raised when a bind matches and should be executed.
    /// </summary>
    public event EventHandler<BindMatchEventArgs>? BindMatched;

    public AgentScriptContext(
        AgentConfigurationService configService,
        AgentUserDatabaseService userDatabase,
        string scriptsDirectory)
        : base(null!, scriptsDirectory) // Agent doesn't use IrcClientManager directly
    {
        _logger = Log.ForContext<AgentScriptContext>();
        _configService = configService;
        _userDatabase = userDatabase;
    }

    #region Eggdrop-style Binds

    /// <summary>
    /// Registers a bind (event handler).
    /// Eggdrop-compatible: bind type flags mask proc
    /// </summary>
    /// <param name="type">Bind type: pub, msg, join, part, kick, nick, mode, ctcp, raw, etc.</param>
    /// <param name="flags">Required user flags (e.g., "o" for ops, "n" for owner, "-" for anyone).</param>
    /// <param name="mask">Pattern to match (channel, command, etc.).</param>
    /// <param name="callback">The callback to invoke.</param>
    /// <returns>Bind ID for unbind.</returns>
    public string RegisterBind(string type, string flags, string mask, Func<BindContext, Task<bool>> callback)
    {
        var bind = new ScriptBind
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Type = type.ToLowerInvariant(),
            Flags = flags,
            Mask = mask,
            Callback = callback,
            CreatedAt = DateTime.UtcNow
        };

        lock (_bindLock)
        {
            if (!_binds.ContainsKey(bind.Type))
                _binds[bind.Type] = new List<ScriptBind>();
            
            _binds[bind.Type].Add(bind);
        }

        _logger.Debug("Registered bind: {Type} {Flags} {Mask} -> {Id}", type, flags, mask, bind.Id);
        return bind.Id;
    }

    /// <summary>
    /// Unregisters a bind by ID.
    /// </summary>
    public bool UnregisterBind(string bindId)
    {
        lock (_bindLock)
        {
            foreach (var bindList in _binds.Values)
            {
                var bind = bindList.FirstOrDefault(b => b.Id == bindId);
                if (bind != null)
                {
                    bindList.Remove(bind);
                    _logger.Debug("Unregistered bind: {Id}", bindId);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Gets all registered binds.
    /// </summary>
    public IReadOnlyList<ScriptBind> GetBinds(string? type = null)
    {
        lock (_bindLock)
        {
            if (type == null)
                return _binds.Values.SelectMany(b => b).ToList();
            
            return _binds.TryGetValue(type.ToLowerInvariant(), out var binds) 
                ? binds.ToList() 
                : new List<ScriptBind>();
        }
    }

    /// <summary>
    /// Dispatches an event to matching binds.
    /// </summary>
    public async Task<bool> DispatchBindAsync(string type, BindContext context)
    {
        List<ScriptBind> matchingBinds;
        
        lock (_bindLock)
        {
            if (!_binds.TryGetValue(type.ToLowerInvariant(), out var binds))
                return false;
            
            matchingBinds = binds.Where(b => MatchesBind(b, context)).ToList();
        }

        foreach (var bind in matchingBinds)
        {
            try
            {
                // Check user flags
                if (!CheckUserFlags(bind.Flags, context.Hostmask))
                    continue;

                var handled = await bind.Callback(context);
                BindMatched?.Invoke(this, new BindMatchEventArgs(bind, context, handled));

                if (handled)
                    return true; // Stop processing if bind returns true
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing bind {Id}", bind.Id);
            }
        }

        return false;
    }

    private bool MatchesBind(ScriptBind bind, BindContext context)
    {
        // Match the mask against the context
        return bind.Type switch
        {
            "pub" => MatchesPattern(bind.Mask, context.Text?.Split(' ').FirstOrDefault() ?? ""),
            "pubm" => MatchesPattern(bind.Mask, context.Text ?? ""),
            "msg" => MatchesPattern(bind.Mask, context.Text?.Split(' ').FirstOrDefault() ?? ""),
            "msgm" => MatchesPattern(bind.Mask, context.Text ?? ""),
            "join" => MatchesPattern(bind.Mask, $"{context.Channel} {context.Hostmask}"),
            "part" => MatchesPattern(bind.Mask, $"{context.Channel} {context.Hostmask}"),
            "kick" => MatchesPattern(bind.Mask, $"{context.Channel} {context.Nick}"),
            "nick" => MatchesPattern(bind.Mask, context.Nick ?? "*"),
            "mode" => MatchesPattern(bind.Mask, $"{context.Channel} {context.Text}"),
            "ctcp" => MatchesPattern(bind.Mask, context.Text?.Split(' ').FirstOrDefault() ?? ""),
            "raw" => MatchesPattern(bind.Mask, context.Command ?? "*"),
            _ => MatchesPattern(bind.Mask, context.Text ?? "")
        };
    }

    private bool MatchesPattern(string pattern, string text)
    {
        if (pattern == "*")
            return true;

        // Convert Eggdrop-style wildcards to regex
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool CheckUserFlags(string requiredFlags, string? hostmask)
    {
        // "-" means anyone can use it
        if (requiredFlags == "-" || string.IsNullOrEmpty(requiredFlags))
            return true;

        if (string.IsNullOrEmpty(hostmask))
            return false;

        // Look up user by hostmask
        var user = _userDatabase.MatchUser(hostmask);
        if (user == null)
            return requiredFlags == "-"; // No user found, only allow if no flags required

        // Check if user has any of the required flags
        var required = AgentUser.ParseFlags(requiredFlags);
        return user.HasFlag(required);
    }

    #endregion
}

/// <summary>
/// Represents a registered script bind.
/// </summary>
public class ScriptBind
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Flags { get; set; } = "-";
    public string Mask { get; set; } = "*";
    public Func<BindContext, Task<bool>> Callback { get; set; } = null!;
    public string? ScriptName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Context passed to bind callbacks.
/// </summary>
public class BindContext
{
    /// <summary>Server ID.</summary>
    public string ServerId { get; set; } = "";
    
    /// <summary>Source nickname.</summary>
    public string? Nick { get; set; }
    
    /// <summary>Full hostmask (nick!user@host).</summary>
    public string? Hostmask { get; set; }
    
    /// <summary>Channel name (if applicable).</summary>
    public string? Channel { get; set; }
    
    /// <summary>Message text or parameters.</summary>
    public string? Text { get; set; }
    
    /// <summary>IRC command (for raw binds).</summary>
    public string? Command { get; set; }
    
    /// <summary>Target of the message.</summary>
    public string? Target { get; set; }
    
    /// <summary>Additional arguments.</summary>
    public string[] Args { get; set; } = Array.Empty<string>();
    
    /// <summary>The matched user from database (if found).</summary>
    public AgentUser? User { get; set; }
    
    /// <summary>Function to send a reply.</summary>
    public Func<string, Task>? Reply { get; set; }
}

/// <summary>
/// Event args for bind matches.
/// </summary>
public class BindMatchEventArgs : EventArgs
{
    public ScriptBind Bind { get; }
    public BindContext Context { get; }
    public bool Handled { get; }

    public BindMatchEventArgs(ScriptBind bind, BindContext context, bool handled)
    {
        Bind = bind;
        Context = context;
        Handled = handled;
    }
}
