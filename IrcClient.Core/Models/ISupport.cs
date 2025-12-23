namespace IrcClient.Core.Models;

/// <summary>
/// Represents the ISUPPORT (005) configuration received from an IRC server.
/// This defines server capabilities and limits.
/// </summary>
public class ISupport
{
    /// <summary>
    /// Network name (e.g., "Libera.Chat").
    /// </summary>
    public string? Network { get; set; }

    /// <summary>
    /// Channel types supported (default: #&amp;).
    /// </summary>
    public string ChanTypes { get; set; } = "#&";

    /// <summary>
    /// User prefix modes and their symbols.
    /// Key: mode char (o, v, h, etc.), Value: prefix char (@ + % etc.)
    /// </summary>
    public Dictionary<char, char> Prefix { get; } = new()
    {
        ['o'] = '@',  // Operator
        ['v'] = '+'   // Voice
    };

    /// <summary>
    /// Prefix characters in order of priority (e.g., ~&amp;%+ etc.).
    /// </summary>
    public string PrefixChars { get; set; } = "@+";

    /// <summary>
    /// Mode characters in order of priority (e.g., "qaohv").
    /// </summary>
    public string PrefixModes { get; set; } = "ov";

    /// <summary>
    /// Channel modes categorized by type.
    /// A: List modes (ban, except, invite)
    /// B: Modes with parameter always (key)
    /// C: Modes with parameter on set only (limit)
    /// D: Modes without parameter (moderated, secret, etc.)
    /// </summary>
    public Dictionary<char, char[]> ChanModes { get; } = new()
    {
        ['A'] = new[] { 'b', 'e', 'I' },
        ['B'] = new[] { 'k' },
        ['C'] = new[] { 'l' },
        ['D'] = new[] { 'i', 'm', 'n', 'p', 's', 't' }
    };

    /// <summary>
    /// Case mapping for nick/channel comparison.
    /// Values: "ascii", "rfc1459", "strict-rfc1459"
    /// </summary>
    public string CaseMapping { get; set; } = "rfc1459";

    /// <summary>
    /// Maximum nickname length.
    /// </summary>
    public int NickLen { get; set; } = 9;

    /// <summary>
    /// Maximum channel name length.
    /// </summary>
    public int ChannelLen { get; set; } = 50;

    /// <summary>
    /// Maximum topic length.
    /// </summary>
    public int TopicLen { get; set; } = 307;

    /// <summary>
    /// Maximum kick reason length.
    /// </summary>
    public int KickLen { get; set; } = 255;

    /// <summary>
    /// Maximum away message length.
    /// </summary>
    public int AwayLen { get; set; } = 200;

    /// <summary>
    /// Maximum number of modes per MODE command.
    /// </summary>
    public int Modes { get; set; } = 3;

    /// <summary>
    /// Maximum number of channels a user can join.
    /// </summary>
    public int MaxChannels { get; set; } = 20;

    /// <summary>
    /// Channel limits by type (e.g., "#" -> 25).
    /// </summary>
    public Dictionary<string, int> ChanLimit { get; } = new();

    /// <summary>
    /// Maximum targets for commands.
    /// </summary>
    public Dictionary<string, int> TargMax { get; } = new();

    /// <summary>
    /// Maximum list sizes (bans, excepts, invites).
    /// </summary>
    public Dictionary<char, int> MaxList { get; } = new();

    /// <summary>
    /// Whether the server supports ban exceptions (+e).
    /// </summary>
    public bool SupportsExcepts { get; set; }
    public char ExceptsMode { get; set; } = 'e';

    /// <summary>
    /// Whether the server supports invite exceptions (+I).
    /// </summary>
    public bool SupportsInvex { get; set; }
    public char InvexMode { get; set; } = 'I';

    /// <summary>
    /// Status message prefixes (e.g., "@+" for STATUSMSG).
    /// </summary>
    public string StatusMsg { get; set; } = string.Empty;

    /// <summary>
    /// Whether WHOX is supported.
    /// </summary>
    public bool SupportsWhox { get; set; }

    /// <summary>
    /// Whether MONITOR is supported (and its limit).
    /// </summary>
    public int MonitorLimit { get; set; }

    /// <summary>
    /// Extra tokens not specifically parsed.
    /// </summary>
    public Dictionary<string, string?> RawTokens { get; } = new();

    /// <summary>
    /// Parses ISUPPORT tokens from a 005 message.
    /// </summary>
    public void ParseTokens(IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;

            // Handle negation (e.g., "-FEATURE")
            if (token.StartsWith('-'))
            {
                var feature = token[1..];
                RawTokens.Remove(feature);
                continue;
            }

            // Split key=value
            var eqIndex = token.IndexOf('=');
            string key, value;
            if (eqIndex > 0)
            {
                key = token[..eqIndex].ToUpperInvariant();
                value = token[(eqIndex + 1)..];
            }
            else
            {
                key = token.ToUpperInvariant();
                value = string.Empty;
            }

            RawTokens[key] = string.IsNullOrEmpty(value) ? null : value;

            // Parse specific tokens
            switch (key)
            {
                case "NETWORK":
                    Network = value;
                    break;

                case "CHANTYPES":
                    if (!string.IsNullOrEmpty(value))
                        ChanTypes = value;
                    break;

                case "PREFIX":
                    ParsePrefix(value);
                    break;

                case "CHANMODES":
                    ParseChanModes(value);
                    break;

                case "CASEMAPPING":
                    CaseMapping = value.ToLowerInvariant();
                    break;

                case "NICKLEN":
                    if (int.TryParse(value, out var nickLen))
                        NickLen = nickLen;
                    break;

                case "CHANNELLEN":
                    if (int.TryParse(value, out var chanLen))
                        ChannelLen = chanLen;
                    break;

                case "TOPICLEN":
                    if (int.TryParse(value, out var topicLen))
                        TopicLen = topicLen;
                    break;

                case "KICKLEN":
                    if (int.TryParse(value, out var kickLen))
                        KickLen = kickLen;
                    break;

                case "AWAYLEN":
                    if (int.TryParse(value, out var awayLen))
                        AwayLen = awayLen;
                    break;

                case "MODES":
                    if (int.TryParse(value, out var modes))
                        Modes = modes;
                    break;

                case "MAXCHANNELS":
                    if (int.TryParse(value, out var maxChan))
                        MaxChannels = maxChan;
                    break;

                case "CHANLIMIT":
                    ParseChanLimit(value);
                    break;

                case "TARGMAX":
                    ParseTargMax(value);
                    break;

                case "MAXLIST":
                    ParseMaxList(value);
                    break;

                case "EXCEPTS":
                    SupportsExcepts = true;
                    if (!string.IsNullOrEmpty(value) && value.Length == 1)
                        ExceptsMode = value[0];
                    break;

                case "INVEX":
                    SupportsInvex = true;
                    if (!string.IsNullOrEmpty(value) && value.Length == 1)
                        InvexMode = value[0];
                    break;

                case "STATUSMSG":
                    StatusMsg = value;
                    break;

                case "WHOX":
                    SupportsWhox = true;
                    break;

                case "MONITOR":
                    if (int.TryParse(value, out var monLimit))
                        MonitorLimit = monLimit;
                    break;
            }
        }
    }

    /// <summary>
    /// Parses PREFIX token (e.g., (qaohv)~&amp;@%+).
    /// </summary>
    private void ParsePrefix(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith('('))
            return;

        var closeIndex = value.IndexOf(')');
        if (closeIndex < 2)
            return;

        var modes = value[1..closeIndex];
        var prefixes = value[(closeIndex + 1)..];

        if (modes.Length != prefixes.Length)
            return;

        Prefix.Clear();
        var modeBuilder = new System.Text.StringBuilder();
        var prefixBuilder = new System.Text.StringBuilder();

        for (int i = 0; i < modes.Length; i++)
        {
            Prefix[modes[i]] = prefixes[i];
            modeBuilder.Append(modes[i]);
            prefixBuilder.Append(prefixes[i]);
        }

        PrefixModes = modeBuilder.ToString();
        PrefixChars = prefixBuilder.ToString();
    }

    /// <summary>
    /// Parses CHANMODES token, e.g., "beI,k,l,imnpst"
    /// </summary>
    private void ParseChanModes(string value)
    {
        var parts = value.Split(',');
        if (parts.Length < 4)
            return;

        ChanModes.Clear();
        ChanModes['A'] = parts[0].ToCharArray();
        ChanModes['B'] = parts[1].ToCharArray();
        ChanModes['C'] = parts[2].ToCharArray();
        ChanModes['D'] = parts.Length > 3 ? parts[3].ToCharArray() : Array.Empty<char>();
    }

    /// <summary>
    /// Parses CHANLIMIT token (e.g., #=25,&amp;=10).
    /// </summary>
    private void ParseChanLimit(string value)
    {
        foreach (var part in value.Split(','))
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0 && int.TryParse(part[(colonIndex + 1)..], out var limit))
            {
                var types = part[..colonIndex];
                ChanLimit[types] = limit;
                // Also update MaxChannels to the highest limit
                if (limit > MaxChannels)
                    MaxChannels = limit;
            }
        }
    }

    /// <summary>
    /// Parses TARGMAX token, e.g., "PRIVMSG:4,NOTICE:4,JOIN:1"
    /// </summary>
    private void ParseTargMax(string value)
    {
        foreach (var part in value.Split(','))
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0)
            {
                var cmd = part[..colonIndex];
                if (int.TryParse(part[(colonIndex + 1)..], out var max))
                {
                    TargMax[cmd] = max;
                }
                else
                {
                    TargMax[cmd] = int.MaxValue; // No limit
                }
            }
        }
    }

    /// <summary>
    /// Parses MAXLIST token (e.g., beI=100).
    /// </summary>
    private void ParseMaxList(string value)
    {
        foreach (var part in value.Split(','))
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0 && int.TryParse(part[(colonIndex + 1)..], out var limit))
            {
                var modes = part[..colonIndex];
                foreach (var mode in modes)
                {
                    MaxList[mode] = limit;
                }
            }
        }
    }

    /// <summary>
    /// Checks if a character is a channel prefix (# or &amp;).
    /// </summary>
    public bool IsChannelPrefix(char c) => ChanTypes.Contains(c);

    /// <summary>
    /// Checks if a string is a channel name.
    /// </summary>
    public bool IsChannel(string name) => !string.IsNullOrEmpty(name) && IsChannelPrefix(name[0]);

    /// <summary>
    /// Gets the mode character for a prefix symbol.
    /// </summary>
    public char? GetModeForPrefix(char prefix)
    {
        foreach (var kvp in Prefix)
        {
            if (kvp.Value == prefix)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>
    /// Gets the prefix symbol for a mode character.
    /// </summary>
    public char? GetPrefixForMode(char mode)
    {
        return Prefix.TryGetValue(mode, out var prefix) ? prefix : null;
    }

    /// <summary>
    /// Gets the sort order for a prefix (lower = higher rank).
    /// </summary>
    public int GetPrefixOrder(char prefix)
    {
        var idx = PrefixChars.IndexOf(prefix);
        return idx >= 0 ? idx : int.MaxValue;
    }

    /// <summary>
    /// Compares two strings according to the server's case mapping.
    /// </summary>
    public int Compare(string? a, string? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        return string.Compare(
            NormalizeCase(a),
            NormalizeCase(b),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if two strings are equal according to the server's case mapping.
    /// </summary>
    public bool Equals(string? a, string? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        
        return NormalizeCase(a) == NormalizeCase(b);
    }

    /// <summary>
    /// Normalizes a string for case-insensitive comparison.
    /// </summary>
    public string NormalizeCase(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        var chars = s.ToLowerInvariant().ToCharArray();

        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = CaseMapping switch
            {
                "ascii" => chars[i], // Only A-Z -> a-z (already done by ToLowerInvariant)
                "strict-rfc1459" => chars[i] switch
                {
                    '[' => '{',
                    ']' => '}',
                    '\\' => '|',
                    _ => chars[i]
                },
                _ => chars[i] switch // rfc1459 (default)
                {
                    '[' => '{',
                    ']' => '}',
                    '\\' => '|',
                    '^' => '~',
                    _ => chars[i]
                }
            };
        }

        return new string(chars);
    }
}
