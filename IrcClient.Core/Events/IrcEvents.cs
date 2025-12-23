using IrcClient.Core.Models;

namespace IrcClient.Core.Events;

/// <summary>
/// Event arguments for IRC connection state changes.
/// </summary>
/// <remarks>
/// Raised when the connection to a server is established or lost.
/// </remarks>
public class IrcConnectionEventArgs : EventArgs
{
    /// <summary>The server that triggered the event.</summary>
    public IrcServer Server { get; }

    public IrcConnectionEventArgs(IrcServer server)
    {
        Server = server;
    }
}

/// <summary>
/// Event arguments for raw parsed IRC messages.
/// </summary>
/// <remarks>
/// Raised for every message received from the server, before processing.
/// Useful for debugging and raw message logging.
/// </remarks>
public class IrcMessageReceivedEventArgs : EventArgs
{
    /// <summary>The server the message was received from.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The parsed IRC message.</summary>
    public ParsedIrcMessage Message { get; }

    public IrcMessageReceivedEventArgs(IrcServer server, ParsedIrcMessage message)
    {
        Server = server;
        Message = message;
    }
}

/// <summary>
/// Event arguments for channel messages (PRIVMSG to a channel).
/// </summary>
public class IrcChannelMessageEventArgs : EventArgs
{
    /// <summary>The server the message was received from.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The channel the message was sent to.</summary>
    public IrcChannel Channel { get; }
    
    /// <summary>The formatted message for display.</summary>
    public IrcMessage Message { get; }

    public IrcChannelMessageEventArgs(IrcServer server, IrcChannel channel, IrcMessage message)
    {
        Server = server;
        Channel = channel;
        Message = message;
    }
}

/// <summary>
/// Event arguments for private messages (PRIVMSG to a user).
/// </summary>
public class IrcPrivateMessageEventArgs : EventArgs
{
    /// <summary>The server the message was received from.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The nickname of the sender.</summary>
    public string From { get; }
    
    /// <summary>The formatted message for display.</summary>
    public IrcMessage Message { get; }

    public IrcPrivateMessageEventArgs(IrcServer server, string from, IrcMessage message)
    {
        Server = server;
        From = from;
        Message = message;
    }
}

/// <summary>
/// Event arguments for channel-related events.
/// </summary>
public class IrcChannelEventArgs : EventArgs
{
    /// <summary>The server the event occurred on.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The channel involved in the event.</summary>
    public IrcChannel Channel { get; }

    public IrcChannelEventArgs(IrcServer server, IrcChannel channel)
    {
        Server = server;
        Channel = channel;
    }
}

/// <summary>
/// Event arguments for user-related events (join, part, quit).
/// </summary>
public class IrcUserEventArgs : EventArgs
{
    /// <summary>The server the event occurred on.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The channel involved (null for QUIT events).</summary>
    public IrcChannel? Channel { get; }
    
    /// <summary>The user involved in the event.</summary>
    public IrcUser User { get; }

    public IrcUserEventArgs(IrcServer server, IrcChannel? channel, IrcUser user)
    {
        Server = server;
        Channel = channel;
        User = user;
    }
}

/// <summary>
/// Event arguments for nickname changes.
/// </summary>
public class IrcNickChangedEventArgs : EventArgs
{
    /// <summary>The server the event occurred on.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The user's old nickname.</summary>
    public string OldNick { get; }
    
    /// <summary>The user's new nickname.</summary>
    public string NewNick { get; }

    public IrcNickChangedEventArgs(IrcServer server, string oldNick, string newNick)
    {
        Server = server;
        OldNick = oldNick;
        NewNick = newNick;
    }
}

/// <summary>
/// Event arguments for IRC errors.
/// </summary>
public class IrcErrorEventArgs : EventArgs
{
    /// <summary>The server the error occurred on.</summary>
    public IrcServer Server { get; }
    
    /// <summary>Human-readable error message.</summary>
    public string Message { get; }
    
    /// <summary>The underlying exception, if any.</summary>
    public Exception? Exception { get; }

    public IrcErrorEventArgs(IrcServer server, string message, Exception? exception = null)
    {
        Server = server;
        Message = message;
        Exception = exception;
    }
}

/// <summary>
/// Event arguments for raw IRC message logging.
/// </summary>
public class IrcRawMessageEventArgs : EventArgs
{
    /// <summary>The server involved.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The raw message text.</summary>
    public string Message { get; }
    
    /// <summary>True if we sent this message, false if received.</summary>
    public bool IsOutgoing { get; }

    public IrcRawMessageEventArgs(IrcServer server, string message, bool isOutgoing)
    {
        Server = server;
        Message = message;
        IsOutgoing = isOutgoing;
    }
}

/// <summary>
/// Event arguments for server informational messages.
/// </summary>
public class IrcServerMessageEventArgs : EventArgs
{
    /// <summary>The server that sent the message.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The message content.</summary>
    public string Message { get; }
    
    /// <summary>The numeric code (e.g., "001") if applicable.</summary>
    public string? NumericCode { get; }

    public IrcServerMessageEventArgs(IrcServer server, string message, string? numericCode = null)
    {
        Server = server;
        Message = message;
        NumericCode = numericCode;
    }
}

/// <summary>
/// Event arguments for channel list entries (LIST command).
/// </summary>
public class IrcChannelListEventArgs : EventArgs
{
    /// <summary>The server providing the list.</summary>
    public IrcServer Server { get; }
    
    /// <summary>The channel list entry.</summary>
    public Models.ChannelListEntry Entry { get; }

    public IrcChannelListEventArgs(IrcServer server, Models.ChannelListEntry entry)
    {
        Server = server;
        Entry = entry;
    }
}

public class IrcServerEventArgs : EventArgs
{
    public IrcServer Server { get; }

    public IrcServerEventArgs(IrcServer server)
    {
        Server = server;
    }
}

public class IrcReconnectEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public int Attempt { get; }
    public int MaxAttempts { get; }
    public int DelaySeconds { get; }

    public IrcReconnectEventArgs(IrcServer server, int attempt, int maxAttempts, int delaySeconds)
    {
        Server = server;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        DelaySeconds = delaySeconds;
    }
}

public class IrcWhoisEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public Models.WhoisInfo Info { get; }

    public IrcWhoisEventArgs(IrcServer server, Models.WhoisInfo info)
    {
        Server = server;
        Info = info;
    }
}

public class IrcWhoEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public Models.WhoInfo Info { get; }

    public IrcWhoEventArgs(IrcServer server, Models.WhoInfo info)
    {
        Server = server;
        Info = info;
    }
}

public class IrcWhowasEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public Models.WhowasInfo Info { get; }

    public IrcWhowasEventArgs(IrcServer server, Models.WhowasInfo info)
    {
        Server = server;
        Info = info;
    }
}

public class IrcChannelModeListEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public string Channel { get; }
    public Models.ChannelListModeEntry Entry { get; }

    public IrcChannelModeListEventArgs(IrcServer server, string channel, Models.ChannelListModeEntry entry)
    {
        Server = server;
        Channel = channel;
        Entry = entry;
    }
}

public class IrcBatchEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public Models.IrcBatch Batch { get; }

    public IrcBatchEventArgs(IrcServer server, Models.IrcBatch batch)
    {
        Server = server;
        Batch = batch;
    }
}

public class IrcMonitorEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public string Nickname { get; }
    public bool IsOnline { get; }
    public string? Username { get; }
    public string? Hostname { get; }

    public IrcMonitorEventArgs(IrcServer server, string nickname, bool isOnline, string? username = null, string? hostname = null)
    {
        Server = server;
        Nickname = nickname;
        IsOnline = isOnline;
        Username = username;
        Hostname = hostname;
    }
}

/// <summary>
/// Event args for IRCv3 typing notification (+typing capability).
/// </summary>
public class IrcTypingEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public string Target { get; }
    public string Nickname { get; }
    public TypingState State { get; }

    public IrcTypingEventArgs(IrcServer server, string target, string nickname, TypingState state)
    {
        Server = server;
        Target = target;
        Nickname = nickname;
        State = state;
    }
}

public enum TypingState
{
    Active,
    Paused,
    Done
}

/// <summary>
/// Event args for IRCv3 reactions (+draft/react capability).
/// </summary>
public class IrcReactionEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public string Target { get; }
    public string Nickname { get; }
    public string MessageId { get; }
    public string Emoji { get; }
    public bool IsAdding { get; }

    public IrcReactionEventArgs(IrcServer server, string target, string nickname, string messageId, string emoji, bool isAdding)
    {
        Server = server;
        Target = target;
        Nickname = nickname;
        MessageId = messageId;
        Emoji = emoji;
        IsAdding = isAdding;
    }
}

/// <summary>
/// Event args for IRCv3 read markers.
/// </summary>
public class IrcReadMarkerEventArgs : EventArgs
{
    public IrcServer Server { get; }
    public string Target { get; }
    public string? MessageId { get; }
    public DateTime? Timestamp { get; }

    public IrcReadMarkerEventArgs(IrcServer server, string target, string? messageId, DateTime? timestamp)
    {
        Server = server;
        Target = target;
        MessageId = messageId;
        Timestamp = timestamp;
    }
}
