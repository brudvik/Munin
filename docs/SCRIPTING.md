# Munin Scripting System

Munin supports a powerful hybrid scripting system that allows you to automate and extend the client.

## Three Scripting Methods

### 1. Lua Scripts (Primary)

Lua is the recommended scripting language for most use cases. It's safe, fast, and easy to learn.

**File extension:** `.lua`  
**Location:** `scripts/` folder

```lua
-- Respond to !hello command
on.message = function(e)
    if e.text:match("^!hello") then
        e.reply("Hello, " .. e.nick .. "! 👋")
    end
end

-- Welcome users
on.join = function(e)
    if e.channel == "#welcome" then
        irc.say(e.server, e.channel, "Welcome, " .. e.nick .. "!")
    end
end

print("Script loaded!")
```

#### Lua API Reference

**IRC Operations:**
- `irc.say(server, target, message)` - Send a message
- `irc.msg(server, target, message)` - Alias for say
- `irc.action(server, target, message)` - Send /me action
- `irc.notice(server, target, message)` - Send notice
- `irc.raw(server, command)` - Send raw IRC command
- `irc.join(server, channel, [key])` - Join a channel
- `irc.part(server, channel, [reason])` - Leave a channel
- `irc.kick(server, channel, nick, [reason])` - Kick a user
- `irc.mode(server, target, modes)` - Set modes
- `irc.nick(server, newNick)` - Change nickname
- `irc.me(server)` - Get current nickname
- `irc.servers()` - Get all servers
- `irc.server(name)` - Get a server by name

**Server Object:**
- `server.name` - Server name
- `server.address` - Server address
- `server.port` - Server port
- `server.connected` - Connection status
- `server:channels()` - Get all channels
- `server:channel(name)` - Get a channel
- `server:say(target, message)` - Send message
- `server:raw(command)` - Send raw command

**Channel Object:**
- `channel.name` - Channel name
- `channel.topic` - Channel topic
- `channel.userCount` - Number of users
- `channel:users()` - Get all users
- `channel:user(nick)` - Get a user
- `channel:hasUser(nick)` - Check if user is in channel
- `channel:say(message)` - Send message
- `channel:action(message)` - Send action
- `channel:notice(message)` - Send notice
- `channel:part([reason])` - Leave channel
- `channel:kick(nick, [reason])` - Kick user
- `channel:mode(modes)` - Set modes

**User Object:**
- `user.nick` - Nickname
- `user.ident` - Username/ident
- `user.host` - Hostname
- `user.prefix` - Mode prefix (@, +, etc.)
- `user.isOp` - Is operator
- `user.isVoice` - Is voiced
- `user.isOwner` - Is owner
- `user.isAdmin` - Is admin
- `user.isHalfOp` - Is half-op

**Storage:**
- `storage.set(key, value)` - Store a value
- `storage.get(key)` - Retrieve a value
- `storage.remove(key)` - Remove a value
- `storage.has(key)` - Check if key exists
- `storage.keys()` - Get all keys

**Timers:**
- `timer.timeout(callback, delayMs)` - One-shot timer
- `timer.interval(callback, intervalMs)` - Repeating timer
- `timer.clear(timerId)` - Cancel a timer

**Utilities:**
- `print(message)` - Output to script console
- `sleep(ms)` - Async delay (use sparingly)

#### Event Object (e)

All event handlers receive an event object with these common properties:
- `e.type` - Event type name
- `e.server` - Server name
- `e.cancel()` - Cancel the event

**Message events:**
- `e.channel` - Channel name
- `e.nick` - Sender nickname
- `e.text` - Message text
- `e.isAction` - Is this a /me action
- `e.reply(message)` - Reply to the source

**Join/Part events:**
- `e.channel` - Channel name
- `e.nick` - User nickname
- `e.reason` - Part/quit reason (if any)

**Nick change:**
- `e.oldNick` - Old nickname
- `e.newNick` - New nickname

**Kick event:**
- `e.channel` - Channel name
- `e.kicker` - Who kicked
- `e.kicked` - Who was kicked
- `e.reason` - Kick reason

---

### 2. JSON Triggers (Simple Automation)

For simple automation without coding, use JSON trigger files.

**File extension:** `.triggers.json`  
**Location:** `scripts/` folder

```json
{
    "name": "My Triggers",
    "triggers": [
        {
            "on": "message",
            "match": "!ping",
            "action": "reply",
            "text": "Pong! 🏓"
        },
        {
            "on": "message",
            "match": "!slap",
            "action": "action",
            "text": "slaps {1} with a large trout 🐟"
        },
        {
            "on": "join",
            "channel": "#welcome",
            "action": "reply",
            "text": "Welcome {nick}!"
        }
    ]
}
```

#### Trigger Properties

| Property | Description |
|----------|-------------|
| `on` | Event type: `message`, `join`, `part`, `quit`, `nick`, `topic`, `invite` |
| `match` | Text pattern to match (for message events) |
| `isRegex` | If true, `match` is a regex pattern |
| `matchExact` | If true, requires exact match |
| `server` | Filter by server name |
| `channel` | Filter by channel (supports * wildcard) |
| `nick` | Filter by nickname (supports * wildcard) |
| `action` | Action: `reply`, `say`, `action`, `notice`, `raw`, `join`, `part`, `kick`, `ban`, `print` |
| `target` | Target for action (defaults to source) |
| `text` | Text to send (supports variables) |
| `key` | Channel key for join action |
| `cancel` | If true, prevents default handling |
| `delay` | Delay in ms before executing |

#### Variables

Use these in the `text` field:
- `{server}` - Server name
- `{channel}` - Channel name
- `{nick}` - User nickname
- `{text}` - Full message text
- `{me}` - Your current nickname
- `{time}` - Current time (HH:mm:ss)
- `{date}` - Current date (yyyy-MM-dd)
- `{args}` - Everything after the command
- `{1}`, `{2}`, etc. - Individual arguments
- `{oldnick}`, `{newnick}` - For nick changes
- `{topic}` - For topic events
- `{kicker}`, `{kicked}`, `{reason}` - For kick events

---

### 3. C# Plugins (Advanced)

For maximum power, create compiled C# plugins.

**File extension:** `.dll`  
**Location:** `scripts/` folder

```csharp
using Munin.Core.Scripting;
using Munin.Core.Scripting.Plugins;

[Plugin("my-plugin")]
public class MyPlugin : IPlugin
{
    public string Id => "my-plugin";
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Author => "Your Name";
    public string Description => "Does awesome things";
    
    private IPluginContext _context = null!;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        _context = context;
        
        // Register a custom command
        context.RegisterCommand("mycommand", "Description", HandleMyCommand);
        
        // Load saved data
        await context.LoadDataAsync();
        
        context.LogInfo("Plugin initialized!");
    }
    
    private async Task HandleMyCommand(CommandArgs args)
    {
        await _context.SendMessageAsync(
            args.Server.Name, 
            args.Channel?.Name ?? "", 
            $"Hello from plugin! Args: {args.ArgumentString}");
    }
    
    public async Task OnEventAsync(ScriptEvent evt)
    {
        if (evt is MessageEvent msg)
        {
            // Handle messages
            if (msg.Text.Contains("hello"))
            {
                await _context.SendMessageAsync(
                    msg.Server.Name, 
                    msg.Channel.Name, 
                    $"Hi {msg.Nickname}!");
            }
        }
    }
    
    public async Task ShutdownAsync()
    {
        await _context.SaveDataAsync();
        _context.LogInfo("Plugin shutting down");
    }
    
    public void Dispose() { }
}
```

#### Plugin API

The `IPluginContext` provides:

**IRC Operations:**
- `SendMessageAsync(server, target, message)`
- `SendActionAsync(server, target, action)`
- `SendNoticeAsync(server, target, message)`
- `SendRawAsync(server, command)`
- `JoinChannelAsync(server, channel, [key])`
- `PartChannelAsync(server, channel, [reason])`
- `KickUserAsync(server, channel, nick, [reason])`
- `SetModeAsync(server, target, modes)`

**Information:**
- `GetServers()` - All servers
- `GetServer(name)` - Server by name
- `GetChannel(server, channel)` - Channel info
- `GetCurrentNick(server)` - Current nickname

**Storage:**
- `SetData(key, value)` - Store value
- `GetData<T>(key)` - Get typed value
- `RemoveData(key)` - Remove value
- `SaveDataAsync()` - Save to disk
- `LoadDataAsync()` - Load from disk

**Timers:**
- `SetTimeout(callback, delayMs)` - One-shot
- `SetInterval(callback, intervalMs)` - Repeating
- `ClearTimer(id)` - Cancel timer

**Commands:**
- `RegisterCommand(name, description, handler)`
- `UnregisterCommand(name)`

**Logging:**
- `LogInfo(message)`
- `LogWarning(message)`
- `LogError(message, [exception])`

---

## Events

| Event | Description | Properties |
|-------|-------------|------------|
| `message` | Channel message | channel, nick, text, isAction |
| `privmsg` | Private message | nick, text, isAction |
| `notice` | Notice received | channel, nick, text |
| `join` | User joined | channel, nick, ident, host |
| `part` | User left | channel, nick, reason |
| `quit` | User quit IRC | nick, reason, channels |
| `kick` | User kicked | channel, kicker, kicked, reason |
| `nick` | Nick change | oldNick, newNick |
| `topic` | Topic changed | channel, setBy, topic |
| `mode` | Mode changed | target, modes, setBy |
| `connect` | Connected to server | |
| `disconnect` | Disconnected | reason, wasError |
| `raw` | Raw IRC line | line, command, prefix, parameters |
| `input` | User typed /command | channel, command, arguments |
| `ctcp` | CTCP request | nick, channel, ctcp, ctcpArgs |
| `invite` | Invited to channel | nick, channel |

---

## Tips

1. **Start simple**: Use JSON triggers for basic automation
2. **Use Lua for logic**: When you need conditionals or loops
3. **Use plugins for power**: When you need complex features or external libraries
4. **Test safely**: Use a test server/channel when developing scripts
5. **Be respectful**: Don't create spam bots or abusive scripts
