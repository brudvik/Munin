namespace Munin.Core.Models;

/// <summary>
/// Parsed IRC protocol message.
/// </summary>
public class ParsedIrcMessage
{
    public string? Prefix { get; set; }
    public string? Nick { get; set; }
    public string? User { get; set; }
    public string? Host { get; set; }
    public string Command { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = new();
    public string? Trailing { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public string RawMessage { get; set; } = string.Empty;

    public string? GetParameter(int index) =>
        index >= 0 && index < Parameters.Count ? Parameters[index] : null;

    /// <summary>
    /// Gets the server timestamp from the @time tag (IRCv3 server-time capability).
    /// Returns the local time if the tag is not present or cannot be parsed.
    /// </summary>
    public DateTime GetTimestamp()
    {
        if (Tags.TryGetValue("time", out var timeValue) && !string.IsNullOrEmpty(timeValue))
        {
            // IRCv3 time format: ISO 8601 (e.g., "2023-01-15T14:30:00.000Z")
            if (DateTime.TryParse(timeValue, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTime))
            {
                return parsedTime.ToLocalTime();
            }
        }
        return DateTime.Now;
    }

    /// <summary>
    /// Gets the account name from the @account tag (IRCv3 account-tag capability).
    /// Returns null if the tag is not present.
    /// </summary>
    public string? GetAccountName()
    {
        if (Tags.TryGetValue("account", out var account) && account != "*")
        {
            return account;
        }
        return null;
    }

    /// <summary>
    /// Gets the message ID from the @msgid tag (IRCv3 msgid capability).
    /// </summary>
    public string? GetMessageId() =>
        Tags.TryGetValue("msgid", out var msgid) ? msgid : null;

    /// <summary>
    /// Gets the label from the @label tag (IRCv3 labeled-response capability).
    /// </summary>
    public string? GetLabel() =>
        Tags.TryGetValue("label", out var label) ? label : null;

    /// <summary>
    /// Checks if this message is an echo of a message we sent (echo-message capability).
    /// </summary>
    public bool IsEcho => Tags.ContainsKey("echo-message");
}
