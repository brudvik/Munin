using System.Text.RegularExpressions;
using Serilog;

namespace IrcClient.Core.Services;

/// <summary>
/// Handles command aliases and expansion.
/// Allows users to define custom shortcuts for commands.
/// </summary>
/// <remarks>
/// <para>Aliases support argument substitution:</para>
/// <list type="bullet">
///   <item><description>$1, $2, etc. - Individual arguments</description></item>
///   <item><description>$1- - Argument 1 and all following</description></item>
///   <item><description>$channel - Current channel name</description></item>
///   <item><description>$me, $nick - Current nickname</description></item>
/// </list>
/// <para>Multiple commands can be chained with semicolons (;).</para>
/// <example>
/// <code>
/// alias.SetAlias("kb", "/mode $channel +b $1; /kick $1 $2-");
/// </code>
/// </example>
/// </remarks>
public class AliasService
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, AliasDefinition> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Built-in default aliases.
    /// </summary>
    public static readonly Dictionary<string, string> DefaultAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "ns", "/msg NickServ $1-" },
        { "cs", "/msg ChanServ $1-" },
        { "ms", "/msg MemoServ $1-" },
        { "bs", "/msg BotServ $1-" },
        { "hs", "/msg HostServ $1-" },
        { "os", "/msg OperServ $1-" },
        { "j", "/join $1-" },
        { "p", "/part $1-" },
        { "q", "/quit $1-" },
        { "k", "/kick $1 $2-" },
        { "kb", "/mode $channel +b $1; /kick $1 $2-" },
        { "b", "/mode $channel +b $1" },
        { "ub", "/mode $channel -b $1" },
        { "op", "/mode $channel +o $1" },
        { "deop", "/mode $channel -o $1" },
        { "voice", "/mode $channel +v $1" },
        { "devoice", "/mode $channel -v $1" },
        { "hop", "/mode $channel +h $1" },
        { "dehop", "/mode $channel -h $1" },
        { "slap", "/me slaps $1 around a bit with a large trout" },
        { "afk", "/away $1-" },
        { "back", "/away" },
        { "w", "/whois $1" },
        { "ww", "/whowas $1" },
        { "n", "/names $1" },
        { "t", "/topic $1-" },
        { "i", "/invite $1 $channel" },
        { "cycle", "/part $channel; /join $channel" },
        { "rejoin", "/part $channel; /join $channel" }
    };

    public AliasService()
    {
        _logger = SerilogConfig.ForContext<AliasService>();
        
        // Load default aliases
        foreach (var (name, expansion) in DefaultAliases)
        {
            _aliases[name] = new AliasDefinition { Name = name, Expansion = expansion };
        }
        
        _logger.Debug("Loaded {Count} default aliases", _aliases.Count);
    }

    /// <summary>
    /// Adds or updates an alias.
    /// </summary>
    public void SetAlias(string name, string expansion)
    {
        _aliases[name.TrimStart('/')] = new AliasDefinition 
        { 
            Name = name.TrimStart('/'), 
            Expansion = expansion 
        };
    }

    /// <summary>
    /// Removes an alias.
    /// </summary>
    public bool RemoveAlias(string name)
    {
        return _aliases.Remove(name.TrimStart('/'));
    }

    /// <summary>
    /// Gets all defined aliases.
    /// </summary>
    public IReadOnlyDictionary<string, AliasDefinition> GetAliases() => _aliases;

    /// <summary>
    /// Expands a command if it matches an alias.
    /// Returns null if no alias matched.
    /// </summary>
    /// <param name="input">The command input (e.g., "/ns identify password")</param>
    /// <param name="currentChannel">Current channel for $channel variable</param>
    /// <param name="currentNick">Current nickname for $me variable</param>
    /// <returns>Expanded command(s) or null</returns>
    public List<string>? ExpandAlias(string input, string? currentChannel = null, string? currentNick = null)
    {
        if (!input.StartsWith("/")) return null;

        var parts = input[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var command = parts[0];
        var args = parts.Skip(1).ToArray();

        if (!_aliases.TryGetValue(command, out var alias))
            return null;

        var expanded = ExpandExpression(alias.Expansion, args, currentChannel, currentNick);
        
        // Handle multiple commands separated by ;
        return expanded.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(cmd => cmd.Trim())
            .ToList();
    }

    private static string ExpandExpression(string expansion, string[] args, string? channel, string? nick)
    {
        var result = expansion;

        // Replace $1, $2, etc. with arguments
        for (int i = 0; i < args.Length; i++)
        {
            result = result.Replace($"${i + 1}", args[i]);
        }

        // Replace $n- with argument n and all following
        for (int i = 1; i <= 9; i++)
        {
            var rangePattern = $"${i}-";
            if (result.Contains(rangePattern))
            {
                var remaining = i <= args.Length 
                    ? string.Join(" ", args.Skip(i - 1)) 
                    : "";
                result = result.Replace(rangePattern, remaining);
            }
        }

        // Replace special variables
        result = result.Replace("$channel", channel ?? "");
        result = result.Replace("$me", nick ?? "");
        result = result.Replace("$nick", nick ?? "");

        // Clean up unreplaced variables
        result = Regex.Replace(result, @"\$\d+(-)?", "");
        result = Regex.Replace(result, @"\s+", " ");

        return result.Trim();
    }

    /// <summary>
    /// Checks if a command is a built-in command (not an alias).
    /// </summary>
    public static bool IsBuiltInCommand(string command)
    {
        var builtIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "join", "part", "quit", "msg", "me", "nick", "topic", "kick", "ban",
            "mode", "whois", "who", "whowas", "names", "list", "ignore", "unignore",
            "clear", "query", "notice", "away", "back", "invite", "ctcp", "ping",
            "monitor", "raw", "quote", "alias", "unalias", "help", "server", "disconnect"
        };

        return builtIn.Contains(command.TrimStart('/').Split(' ')[0]);
    }
}

/// <summary>
/// Represents a command alias definition.
/// </summary>
public class AliasDefinition
{
    /// <summary>
    /// Gets or sets the alias name (e.g., "kb" for kick-ban).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expansion template with placeholders.
    /// </summary>
    public string Expansion { get; set; } = string.Empty;

    /// <summary>
    /// Gets whether this is a built-in default alias.
    /// </summary>
    public bool IsBuiltIn => AliasService.DefaultAliases.ContainsKey(Name);
}
