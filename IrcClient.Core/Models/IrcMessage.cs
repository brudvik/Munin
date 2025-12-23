namespace IrcClient.Core.Models;

/// <summary>
/// Represents a displayable IRC message in the UI.
/// </summary>
/// <remarks>
/// <para>This class is used for rendering messages in the chat window.</para>
/// <para>It includes factory methods for creating common message types.</para>
/// </remarks>
public class IrcMessage
{
    /// <summary>
    /// Unique identifier for this message (for reactions, read markers, etc.).
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// When the message was received or sent.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// The type of message (normal, action, notice, etc.).
    /// </summary>
    public MessageType Type { get; set; } = MessageType.Normal;
    
    /// <summary>
    /// The source nickname or server name.
    /// </summary>
    public string? Source { get; set; }
    
    /// <summary>
    /// The target channel or nickname.
    /// </summary>
    public string? Target { get; set; }
    
    /// <summary>
    /// The message content/text.
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this message should be highlighted (mentions user's nick).
    /// </summary>
    public bool IsHighlight { get; set; }
    
    /// <summary>
    /// The original raw IRC message, if available.
    /// </summary>
    public string? RawMessage { get; set; }

    /// <summary>
    /// Gets the timestamp formatted for display.
    /// </summary>
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    public static IrcMessage CreateSystem(string content) => new()
    {
        Type = MessageType.System,
        Content = content
    };

    public static IrcMessage CreateError(string content) => new()
    {
        Type = MessageType.Error,
        Content = content
    };

    public static IrcMessage CreateAction(string nickname, string action) => new()
    {
        Type = MessageType.Action,
        Source = nickname,
        Content = action
    };

    public static IrcMessage CreateNotice(string source, string content) => new()
    {
        Type = MessageType.Notice,
        Source = source,
        Content = content
    };

    public static IrcMessage CreateJoin(string nickname, string channel) => new()
    {
        Type = MessageType.Join,
        Source = nickname,
        Target = channel,
        Content = $"→ {nickname} has joined {channel}"
    };

    public static IrcMessage CreatePart(string nickname, string channel, string? reason = null) => new()
    {
        Type = MessageType.Part,
        Source = nickname,
        Target = channel,
        Content = string.IsNullOrEmpty(reason) 
            ? $"← {nickname} has left {channel}"
            : $"← {nickname} has left {channel} ({reason})"
    };

    public static IrcMessage CreateQuit(string nickname, string? reason = null) => new()
    {
        Type = MessageType.Quit,
        Source = nickname,
        Content = string.IsNullOrEmpty(reason)
            ? $"⚡ {nickname} has quit"
            : $"⚡ {nickname} has quit ({reason})"
    };

    public static IrcMessage CreateKick(string kicker, string target, string channel, string? reason = null) => new()
    {
        Type = MessageType.Kick,
        Source = kicker,
        Target = channel,
        Content = string.IsNullOrEmpty(reason)
            ? $"⛔ {target} was kicked by {kicker}"
            : $"⛔ {target} was kicked by {kicker} ({reason})"
    };

    public static IrcMessage CreateNickChange(string oldNick, string newNick) => new()
    {
        Type = MessageType.Nick,
        Source = oldNick,
        Content = $"✎ {oldNick} is now known as {newNick}"
    };

    public static IrcMessage CreateMode(string setter, string target, string modes) => new()
    {
        Type = MessageType.Mode,
        Source = setter,
        Target = target,
        Content = $"⚙ {setter} sets mode {modes} on {target}"
    };

    public static IrcMessage CreateTopic(string? setter, string channel, string topic) => new()
    {
        Type = MessageType.Topic,
        Source = setter,
        Target = channel,
        Content = string.IsNullOrEmpty(setter)
            ? $"Topic for {channel}: {topic}"
            : $"✎ {setter} changed the topic to: {topic}"
    };
}

/// <summary>
/// Types of IRC messages for display formatting.
/// </summary>
public enum MessageType
{
    /// <summary>Normal chat message (PRIVMSG).</summary>
    Normal,
    
    /// <summary>Action message (/me).</summary>
    Action,
    
    /// <summary>Notice message (NOTICE).</summary>
    Notice,
    
    /// <summary>User joined a channel.</summary>
    Join,
    
    /// <summary>User left a channel.</summary>
    Part,
    
    /// <summary>User disconnected from the server.</summary>
    Quit,
    
    /// <summary>User was kicked from a channel.</summary>
    Kick,
    
    /// <summary>User changed their nickname.</summary>
    Nick,
    
    /// <summary>Channel or user mode change.</summary>
    Mode,
    
    /// <summary>Channel topic change.</summary>
    Topic,
    
    /// <summary>System/informational message.</summary>
    System,
    
    /// <summary>Error message.</summary>
    Error,
    
    /// <summary>CTCP request or response.</summary>
    CTCP
}
