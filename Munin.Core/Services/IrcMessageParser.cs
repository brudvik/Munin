using System.Text.RegularExpressions;
using Munin.Core.Models;

namespace Munin.Core.Services;

/// <summary>
/// Parses raw IRC protocol messages into structured objects.
/// </summary>
/// <remarks>
/// <para>Implements parsing according to RFC 1459 and IRCv3 message format:</para>
/// <code>[@tags] [:prefix] &lt;command&gt; [params] [:trailing]</code>
/// <para>Supports:</para>
/// <list type="bullet">
///   <item><description>IRCv3 message tags (@key=value)</description></item>
///   <item><description>Source prefix (:nick!user@host)</description></item>
///   <item><description>Commands (alphabetic or 3-digit numeric)</description></item>
///   <item><description>Parameters and trailing message</description></item>
/// </list>
/// </remarks>
public partial class IrcMessageParser
{
    /// <summary>
    /// Regex pattern for parsing IRC messages.
    /// Groups: tags, prefix, command, params, trailing
    /// </summary>
    [GeneratedRegex(@"^(?:@(?<tags>[^ ]+) )?(?::(?<prefix>[^ ]+) )?(?<command>[A-Za-z]+|[0-9]{3})(?<params>(?: [^:][^ ]*)*)(?:(?: :(?<trailing>.*)))?$")]
    private static partial Regex MessageRegex();

    /// <summary>
    /// Regex pattern for parsing the prefix (nick!user@host).
    /// </summary>
    [GeneratedRegex(@"^(?<nick>[^!@]+)(?:!(?<user>[^@]+))?(?:@(?<host>.+))?$")]
    private static partial Regex PrefixRegex();

    /// <summary>
    /// Parses a raw IRC message string into a structured object.
    /// </summary>
    /// <param name="rawMessage">The raw IRC message line.</param>
    /// <returns>A parsed message object with extracted components.</returns>
    public ParsedIrcMessage Parse(string rawMessage)
    {
        var parsed = new ParsedIrcMessage
        {
            RawMessage = rawMessage
        };

        var match = MessageRegex().Match(rawMessage);
        if (!match.Success)
        {
            parsed.Command = "UNKNOWN";
            return parsed;
        }

        // Parse tags (IRCv3)
        if (match.Groups["tags"].Success)
        {
            var tagsStr = match.Groups["tags"].Value;
            foreach (var tag in tagsStr.Split(';'))
            {
                var parts = tag.Split('=', 2);
                parsed.Tags[parts[0]] = parts.Length > 1 ? UnescapeTagValue(parts[1]) : "";
            }
        }

        // Parse prefix
        if (match.Groups["prefix"].Success)
        {
            parsed.Prefix = match.Groups["prefix"].Value;
            var prefixMatch = PrefixRegex().Match(parsed.Prefix);
            if (prefixMatch.Success)
            {
                parsed.Nick = prefixMatch.Groups["nick"].Value;
                parsed.User = prefixMatch.Groups["user"].Success ? prefixMatch.Groups["user"].Value : null;
                parsed.Host = prefixMatch.Groups["host"].Success ? prefixMatch.Groups["host"].Value : null;
            }
        }

        parsed.Command = match.Groups["command"].Value.ToUpperInvariant();

        // Parse parameters
        if (match.Groups["params"].Success)
        {
            var paramsStr = match.Groups["params"].Value.Trim();
            if (!string.IsNullOrEmpty(paramsStr))
            {
                parsed.Parameters = paramsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        // Trailing (message content)
        if (match.Groups["trailing"].Success)
        {
            parsed.Trailing = match.Groups["trailing"].Value;
            parsed.Parameters.Add(parsed.Trailing);
        }

        return parsed;
    }

    /// <summary>
    /// Unescapes IRCv3 tag values according to the specification.
    /// </summary>
    /// <param name="value">The escaped tag value.</param>
    /// <returns>The unescaped tag value.</returns>
    /// <remarks>
    /// Escape sequences: \: -> ; | \s -> space | \\ -> \ | \r -> CR | \n -> LF
    /// </remarks>
    private static string UnescapeTagValue(string value)
    {
        return value
            .Replace("\\:", ";")
            .Replace("\\s", " ")
            .Replace("\\\\", "\\")
            .Replace("\\r", "\r")
            .Replace("\\n", "\n");
    }

    /// <summary>
    /// Checks if a message is a CTCP (Client-To-Client Protocol) message.
    /// </summary>
    /// <param name="message">The message content to check.</param>
    /// <returns>True if the message is wrapped in CTCP delimiters (0x01).</returns>
    public static bool IsCTCP(string message)
    {
        return message.StartsWith('\x01') && message.EndsWith('\x01');
    }

    /// <summary>
    /// Parses a CTCP message into command and parameter.
    /// </summary>
    /// <param name="message">The CTCP message (with 0x01 delimiters).</param>
    /// <returns>A tuple of (command, parameter). Parameter may be null.</returns>
    /// <example>
    /// ParseCTCP("\x01VERSION\x01") returns ("VERSION", null)
    /// ParseCTCP("\x01PING 12345\x01") returns ("PING", "12345")
    /// </example>
    public static (string Command, string? Parameter) ParseCTCP(string message)
    {
        var content = message.Trim('\x01');
        var spaceIndex = content.IndexOf(' ');
        if (spaceIndex == -1)
        {
            return (content.ToUpperInvariant(), null);
        }
        return (content[..spaceIndex].ToUpperInvariant(), content[(spaceIndex + 1)..]);
    }
}
