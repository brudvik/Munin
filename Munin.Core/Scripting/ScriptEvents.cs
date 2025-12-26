using Munin.Core.Models;

namespace Munin.Core.Scripting;

/// <summary>
/// Represents an event that can be dispatched to scripts.
/// </summary>
public abstract class ScriptEvent
{
    /// <summary>
    /// The event type name (e.g., "message", "join", "part").
    /// </summary>
    public abstract string EventType { get; }
    
    /// <summary>
    /// The server where the event occurred.
    /// </summary>
    public IrcServer? Server { get; set; }
    
    /// <summary>
    /// The server name (for when full IrcServer object isn't available).
    /// </summary>
    public string ServerName { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
    
    /// <summary>
    /// Whether the event should be cancelled (prevents default handling).
    /// </summary>
    public bool Cancelled { get; set; }
}

/// <summary>
/// Event fired when a message is received in a channel.
/// </summary>
public class MessageEvent : ScriptEvent
{
    public override string EventType => "message";
    public IrcChannel? Channel { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsAction { get; set; }
    
    public MessageEvent() { }
    
    public MessageEvent(string serverName, string channelName, string nickname, string text, bool isAction = false)
    {
        ServerName = serverName;
        ChannelName = channelName;
        Nickname = nickname;
        Text = text;
        IsAction = isAction;
    }
}

/// <summary>
/// Event fired when a private message is received.
/// </summary>
public class PrivateMessageEvent : ScriptEvent
{
    public override string EventType => "privmsg";
    public string Nickname { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsAction { get; set; }
    
    public PrivateMessageEvent() { }
    
    public PrivateMessageEvent(string serverName, string nickname, string text, bool isAction = false)
    {
        ServerName = serverName;
        Nickname = nickname;
        Text = text;
        IsAction = isAction;
    }
}

/// <summary>
/// Event fired when a notice is received.
/// </summary>
public class NoticeEvent : ScriptEvent
{
    public override string EventType => "notice";
    public string? ChannelName { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    
    public NoticeEvent() { }
    
    public NoticeEvent(string serverName, string? channelName, string nickname, string text)
    {
        ServerName = serverName;
        ChannelName = channelName;
        Nickname = nickname;
        Text = text;
    }
}

/// <summary>
/// Event fired when a user joins a channel.
/// </summary>
public class JoinEvent : ScriptEvent
{
    public override string EventType => "join";
    public IrcChannel? Channel { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string? Ident { get; set; }
    public string? Host { get; set; }
    
    public JoinEvent() { }
    
    public JoinEvent(string serverName, string channelName, string nickname, string? ident = null, string? host = null)
    {
        ServerName = serverName;
        ChannelName = channelName;
        Nickname = nickname;
        Ident = ident;
        Host = host;
    }
}

/// <summary>
/// Event fired when a user parts a channel.
/// </summary>
public class PartEvent : ScriptEvent
{
    public override string EventType => "part";
    public IrcChannel? Channel { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string? Reason { get; set; }
    
    public PartEvent() { }
    
    public PartEvent(string serverName, string channelName, string nickname, string? reason = null)
    {
        ServerName = serverName;
        ChannelName = channelName;
        Nickname = nickname;
        Reason = reason;
    }
}

/// <summary>
/// Event fired when a user quits IRC.
/// </summary>
public class QuitEvent : ScriptEvent
{
    public override string EventType => "quit";
    public string Nickname { get; set; } = string.Empty;
    public string? Reason { get; set; }
    /// <summary>
    /// List of channel names the user was in.
    /// </summary>
    public IEnumerable<string> ChannelNames { get; set; } = Array.Empty<string>();
    
    public QuitEvent() { }
    
    public QuitEvent(string serverName, string nickname, string? reason, IEnumerable<string> channelNames)
    {
        ServerName = serverName;
        Nickname = nickname;
        Reason = reason;
        ChannelNames = channelNames;
    }
}

/// <summary>
/// Event fired when a user is kicked from a channel.
/// </summary>
public class KickEvent : ScriptEvent
{
    public override string EventType => "kick";
    public IrcChannel? Channel { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Kicker { get; set; } = string.Empty;
    public string Kicked { get; set; } = string.Empty;
    public string? Reason { get; set; }
    
    public KickEvent() { }
    
    public KickEvent(string serverName, string channelName, string kicker, string kicked, string? reason = null)
    {
        ServerName = serverName;
        ChannelName = channelName;
        Kicker = kicker;
        Kicked = kicked;
        Reason = reason;
    }
}

/// <summary>
/// Event fired when a user changes their nickname.
/// </summary>
public class NickChangeEvent : ScriptEvent
{
    public override string EventType => "nick";
    public string OldNick { get; set; } = string.Empty;
    public string NewNick { get; set; } = string.Empty;
    
    public NickChangeEvent() { }
    
    public NickChangeEvent(string serverName, string oldNick, string newNick)
    {
        ServerName = serverName;
        OldNick = oldNick;
        NewNick = newNick;
    }
}

/// <summary>
/// Event fired when a channel topic changes.
/// </summary>
public class TopicEvent : ScriptEvent
{
    public override string EventType => "topic";
    public IrcChannel? Channel { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string? SetBy { get; set; }
    public string Topic { get; set; } = string.Empty;
    
    public TopicEvent() { }
    
    public TopicEvent(string serverName, string channelName, string? setBy, string topic)
    {
        ServerName = serverName;
        ChannelName = channelName;
        SetBy = setBy;
        Topic = topic;
    }
}

/// <summary>
/// Event fired when a mode is changed.
/// </summary>
public class ModeEvent : ScriptEvent
{
    public override string EventType => "mode";
    public string Target { get; set; } = string.Empty;
    public string Modes { get; set; } = string.Empty;
    public string? SetBy { get; set; }
    
    public ModeEvent() { }
    
    public ModeEvent(string serverName, string target, string modes, string? setBy = null)
    {
        ServerName = serverName;
        Target = target;
        Modes = modes;
        SetBy = setBy;
    }
}

/// <summary>
/// Event fired when connecting to a server.
/// </summary>
public class ConnectEvent : ScriptEvent
{
    public override string EventType => "connect";
    
    public ConnectEvent() { }
    
    public ConnectEvent(string serverName)
    {
        ServerName = serverName;
    }
}

/// <summary>
/// Event fired when disconnecting from a server.
/// </summary>
public class DisconnectEvent : ScriptEvent
{
    public override string EventType => "disconnect";
    public string? Reason { get; set; }
    public bool WasError { get; set; }
    
    public DisconnectEvent() { }
    
    public DisconnectEvent(string serverName, string? reason = null, bool wasError = false)
    {
        ServerName = serverName;
        Reason = reason;
        WasError = wasError;
    }
}

/// <summary>
/// Event fired when a raw IRC message is received.
/// </summary>
public class RawEvent : ScriptEvent
{
    public override string EventType => "raw";
    public string Line { get; set; } = string.Empty;
    public string? Command { get; set; }
    public string? Prefix { get; set; }
    public IEnumerable<string> Parameters { get; set; } = Array.Empty<string>();
    
    public RawEvent() { }
    
    public RawEvent(string serverName, string line, string? command = null, string? prefix = null, IEnumerable<string>? parameters = null)
    {
        ServerName = serverName;
        Line = line;
        Command = command;
        Prefix = prefix;
        Parameters = parameters ?? Array.Empty<string>();
    }
}

/// <summary>
/// Event fired when the user types a command (starts with /).
/// </summary>
public class InputEvent : ScriptEvent
{
    public override string EventType => "input";
    public IrcChannel? Channel { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    
    public InputEvent() { }
    
    public InputEvent(string serverName, string channelName, string command, string arguments, string fullText)
    {
        ServerName = serverName;
        ChannelName = channelName;
        Command = command;
        Arguments = arguments;
        FullText = fullText;
    }
}

/// <summary>
/// Event fired when a CTCP request is received.
/// </summary>
public class CtcpEvent : ScriptEvent
{
    public override string EventType => "ctcp";
    public string Nickname { get; set; } = string.Empty;
    public string? ChannelName { get; set; }
    public string CtcpCommand { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    
    public CtcpEvent() { }
    
    public CtcpEvent(string serverName, string nickname, string? channelName, string ctcpCommand, string? arguments = null)
    {
        ServerName = serverName;
        Nickname = nickname;
        ChannelName = channelName;
        CtcpCommand = ctcpCommand;
        Arguments = arguments;
    }
}

/// <summary>
/// Event fired when invited to a channel.
/// </summary>
public class InviteEvent : ScriptEvent
{
    public override string EventType => "invite";
    public string Nickname { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    
    public InviteEvent() { }
    
    public InviteEvent(string serverName, string nickname, string channelName)
    {
        ServerName = serverName;
        Nickname = nickname;
        ChannelName = channelName;
    }
}
