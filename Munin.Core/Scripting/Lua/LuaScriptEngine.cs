using MoonSharp.Interpreter;
using Munin.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Munin.Core.Scripting.Lua;

/// <summary>
/// Lua script engine using MoonSharp.
/// Provides a sandboxed environment for running Lua scripts.
/// </summary>
public class LuaScriptEngine : IScriptEngine
{
    private ScriptContext _context = null!;
    private readonly ConcurrentDictionary<string, Script> _scripts = new();
    private readonly ConcurrentDictionary<string, List<DynValue>> _eventHandlers = new();
    private readonly object _lock = new();
    
    public string Name => "Lua";
    public string FileExtension => ".lua";

    static LuaScriptEngine()
    {
        // Register types that scripts can access
        UserData.RegisterType<LuaIrcApi>();
        UserData.RegisterType<LuaStorageApi>();
        UserData.RegisterType<LuaTimerApi>();
        UserData.RegisterType<LuaServerProxy>();
        UserData.RegisterType<LuaChannelProxy>();
        UserData.RegisterType<LuaUserProxy>();
        UserData.RegisterType<LuaEventProxy>();
    }

    public void Initialize(ScriptContext context)
    {
        _context = context;
    }

    public async Task<ScriptResult> LoadScriptAsync(string filePath)
    {
        try
        {
            var code = await File.ReadAllTextAsync(filePath);
            var scriptName = Path.GetFileNameWithoutExtension(filePath);
            return await ExecuteAsync(code, scriptName);
        }
        catch (Exception ex)
        {
            return ScriptResult.Fail($"Failed to load script: {ex.Message}");
        }
    }

    public Task<ScriptResult> ExecuteAsync(string code, string? scriptName = null)
    {
        var sw = Stopwatch.StartNew();
        scriptName ??= $"inline_{Guid.NewGuid():N}";
        
        try
        {
            var script = CreateSandboxedScript();
            
            // Execute the script
            script.DoString(code);
            
            // Store the script for event handling
            _scripts[scriptName] = script;
            
            sw.Stop();
            return Task.FromResult(ScriptResult.Ok(time: sw.Elapsed));
        }
        catch (SyntaxErrorException ex)
        {
            sw.Stop();
            return Task.FromResult(ScriptResult.Fail($"Syntax error: {ex.Message}", sw.Elapsed));
        }
        catch (ScriptRuntimeException ex)
        {
            sw.Stop();
            return Task.FromResult(ScriptResult.Fail($"Runtime error: {ex.Message}", sw.Elapsed));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(ScriptResult.Fail($"Error: {ex.Message}", sw.Elapsed));
        }
    }

    public void UnloadScript(string scriptName)
    {
        _scripts.TryRemove(scriptName, out _);
        
        // Remove event handlers for this script
        lock (_lock)
        {
            foreach (var handlers in _eventHandlers.Values)
            {
                handlers.RemoveAll(h => h.Function?.OwnerScript != null && 
                    _scripts.Values.All(s => s != h.Function.OwnerScript));
            }
        }
    }

    public async Task DispatchEventAsync(ScriptEvent scriptEvent)
    {
        var eventType = scriptEvent.EventType;
        var eventProxy = new LuaEventProxy(scriptEvent, _context);
        
        foreach (var script in _scripts.Values)
        {
            try
            {
                // Call the global event table if it exists
                var onTable = script.Globals.Get("on");
                if (onTable.Type == DataType.Table)
                {
                    var handler = onTable.Table.Get(eventType);
                    if (handler.Type == DataType.Function)
                    {
                        await Task.Run(() => script.Call(handler, eventProxy));
                        
                        if (eventProxy.Cancelled)
                        {
                            scriptEvent.Cancelled = true;
                            return;
                        }
                    }
                }
            }
            catch (ScriptRuntimeException ex)
            {
                _context.RaiseError("Lua", $"Event handler error ({eventType}): {ex.Message}");
            }
        }
    }

    private Script CreateSandboxedScript()
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);
        
        // Remove dangerous modules
        script.Globals["io"] = DynValue.Nil;
        script.Globals["os"] = DynValue.NewTable(script); // Provide limited os functions
        script.Globals["debug"] = DynValue.Nil;
        script.Globals["load"] = DynValue.Nil;
        script.Globals["loadfile"] = DynValue.Nil;
        script.Globals["dofile"] = DynValue.Nil;
        
        // Create the 'on' table for event handlers (must be before irc API is created)
        script.Globals["on"] = new Table(script);
        
        // Add our APIs
        script.Globals["irc"] = UserData.Create(new LuaIrcApi(_context, script));
        script.Globals["storage"] = UserData.Create(new LuaStorageApi(_context));
        script.Globals["timer"] = UserData.Create(new LuaTimerApi(_context, script));
        
        // Add print function that goes to our output
        script.Globals["print"] = (Action<string>)((msg) => _context.Print(msg));
        
        // Add utility functions
        script.Globals["sleep"] = (Func<int, Task>)(async (ms) => await Task.Delay(ms));
        
        return script;
    }

    public void Dispose()
    {
        _scripts.Clear();
        _eventHandlers.Clear();
    }
}

#region Lua API Proxies

/// <summary>
/// IRC API exposed to Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaIrcApi
{
    private readonly ScriptContext _context;
    private readonly Script _script;
    
    public LuaIrcApi(ScriptContext context, Script script)
    {
        _context = context;
        _script = script;
    }
    
    /// <summary>
    /// Register an event handler. Usage: irc.on("message", function(e) ... end)
    /// </summary>
    public void on(string eventType, DynValue callback)
    {
        if (callback.Type != DataType.Function)
            throw new ScriptRuntimeException("Second argument must be a function");
        
        // Add the handler to the global 'on' table
        var onTable = _script.Globals.Get("on");
        if (onTable.Type == DataType.Table)
        {
            onTable.Table.Set(eventType, callback);
        }
    }
    
    public void say(string server, string target, string message) 
        => _context.SendMessageAsync(server, target, message).Wait();
    
    public void msg(string server, string target, string message) 
        => say(server, target, message);
    
    public void action(string server, string target, string action) 
        => _context.SendActionAsync(server, target, action).Wait();
    
    public void notice(string server, string target, string message) 
        => _context.SendNoticeAsync(server, target, message).Wait();
    
    public void raw(string server, string command) 
        => _context.SendRawAsync(server, command).Wait();
    
    public void join(string server, string channel, string? key = null) 
        => _context.JoinChannelAsync(server, channel, key).Wait();
    
    public void part(string server, string channel, string? reason = null) 
        => _context.PartChannelAsync(server, channel, reason).Wait();
    
    public void kick(string server, string channel, string nick, string? reason = null) 
        => _context.KickUserAsync(server, channel, nick, reason).Wait();
    
    public void mode(string server, string target, string modes) 
        => _context.SetModeAsync(server, target, modes).Wait();
    
    public void nick(string server, string newNick) 
        => _context.SetNickAsync(server, newNick).Wait();
    
    public LuaServerProxy[] servers()
    {
        return _context.GetServers()
            .Select(s => new LuaServerProxy(s, _context))
            .ToArray();
    }
    
    public LuaServerProxy? server(string name)
    {
        var s = _context.GetServer(name);
        return s != null ? new LuaServerProxy(s, _context) : null;
    }
    
    public string? me(string server) => _context.GetCurrentNick(server);
}

/// <summary>
/// Storage API exposed to Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaStorageApi
{
    private readonly ScriptContext _context;
    
    public LuaStorageApi(ScriptContext context) => _context = context;
    
    public void set(string key, object value) => _context.SetGlobal(key, value);
    public object? get(string key) => _context.GetGlobal(key);
    public bool remove(string key) => _context.RemoveGlobal(key);
    public bool has(string key) => _context.HasGlobal(key);
    public string[] keys() => _context.GetGlobalKeys().ToArray();
}

/// <summary>
/// Timer API exposed to Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaTimerApi
{
    private readonly ScriptContext _context;
    private readonly Script _script;
    
    public LuaTimerApi(ScriptContext context, Script script)
    {
        _context = context;
        _script = script;
    }
    
    public int timeout(DynValue callback, int delayMs)
    {
        if (callback.Type != DataType.Function)
            throw new ScriptRuntimeException("First argument must be a function");
            
        return _context.SetTimeout(async () =>
        {
            try { _script.Call(callback); }
            catch (Exception ex) { _context.RaiseError("Timer", ex.Message); }
            await Task.CompletedTask;
        }, delayMs);
    }
    
    public int interval(DynValue callback, int intervalMs)
    {
        if (callback.Type != DataType.Function)
            throw new ScriptRuntimeException("First argument must be a function");
            
        return _context.SetInterval(async () =>
        {
            try { _script.Call(callback); }
            catch (Exception ex) { _context.RaiseError("Timer", ex.Message); }
            await Task.CompletedTask;
        }, intervalMs);
    }
    
    public bool clear(int timerId) => _context.ClearTimer(timerId);
}

/// <summary>
/// Server proxy for Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaServerProxy
{
    private readonly IrcServer _server;
    private readonly ScriptContext _context;
    
    public LuaServerProxy(IrcServer server, ScriptContext context)
    {
        _server = server;
        _context = context;
    }
    
    public string name => _server.Name;
    public string address => _server.Hostname;
    public int port => _server.Port;
    public bool connected => _context.ClientManager.GetConnection(_server.Name)?.IsConnected ?? false;
    
    public LuaChannelProxy[] channels()
    {
        return _server.Channels
            .Select(c => new LuaChannelProxy(c, _server.Name, _context))
            .ToArray();
    }
    
    public LuaChannelProxy? channel(string name)
    {
        var c = _server.Channels.FirstOrDefault(ch => 
            ch.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return c != null ? new LuaChannelProxy(c, _server.Name, _context) : null;
    }
    
    public void say(string target, string message) 
        => _context.SendMessageAsync(_server.Name, target, message).Wait();
    
    public void raw(string command) 
        => _context.SendRawAsync(_server.Name, command).Wait();
}

/// <summary>
/// Channel proxy for Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaChannelProxy
{
    private readonly IrcChannel _channel;
    private readonly string _serverName;
    private readonly ScriptContext _context;
    
    public LuaChannelProxy(IrcChannel channel, string serverName, ScriptContext context)
    {
        _channel = channel;
        _serverName = serverName;
        _context = context;
    }
    
    public string name => _channel.Name;
    public string? topic => _channel.Topic;
    public int userCount => _channel.Users.Count;
    
    public LuaUserProxy[] users()
    {
        return _channel.Users
            .Select(u => new LuaUserProxy(u))
            .ToArray();
    }
    
    public LuaUserProxy? user(string nick)
    {
        var u = _channel.Users.FirstOrDefault(user => 
            user.Nickname.Equals(nick, StringComparison.OrdinalIgnoreCase));
        return u != null ? new LuaUserProxy(u) : null;
    }
    
    public bool hasUser(string nick)
    {
        return _channel.Users.Any(u => 
            u.Nickname.Equals(nick, StringComparison.OrdinalIgnoreCase));
    }
    
    public void say(string message) 
        => _context.SendMessageAsync(_serverName, _channel.Name, message).Wait();
    
    public void action(string message) 
        => _context.SendActionAsync(_serverName, _channel.Name, message).Wait();
    
    public void notice(string message) 
        => _context.SendNoticeAsync(_serverName, _channel.Name, message).Wait();
    
    public void part(string? reason = null) 
        => _context.PartChannelAsync(_serverName, _channel.Name, reason).Wait();
    
    public void kick(string nick, string? reason = null) 
        => _context.KickUserAsync(_serverName, _channel.Name, nick, reason).Wait();
    
    public void mode(string modes) 
        => _context.SetModeAsync(_serverName, _channel.Name, modes).Wait();
}

/// <summary>
/// User proxy for Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaUserProxy
{
    private readonly IrcUser _user;
    
    public LuaUserProxy(IrcUser user) => _user = user;
    
    public string nick => _user.Nickname;
    public string? ident => _user.Username;
    public string? host => _user.Hostname;
    public string prefix => _user.Prefix ?? "";
    public bool isOp => _user.Mode == UserMode.Operator || _user.Mode == UserMode.Owner || _user.Mode == UserMode.Admin;
    public bool isVoice => _user.Mode == UserMode.Voice;
    public bool isOwner => _user.Mode == UserMode.Owner;
    public bool isAdmin => _user.Mode == UserMode.Admin;
    public bool isHalfOp => _user.Mode == UserMode.HalfOperator;
}

/// <summary>
/// Event proxy for Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaEventProxy
{
    private readonly ScriptEvent _event;
    private readonly ScriptContext _context;
    
    public LuaEventProxy(ScriptEvent evt, ScriptContext context)
    {
        _event = evt;
        _context = context;
    }
    
    public string type => _event.EventType;
    public string server => _event.ServerName;
    public bool Cancelled { get; private set; }
    
    public void cancel() => Cancelled = true;
    
    // Message event properties - use ChannelName
    public string? channel => (_event as MessageEvent)?.ChannelName 
                           ?? (_event as JoinEvent)?.ChannelName
                           ?? (_event as PartEvent)?.ChannelName
                           ?? (_event as KickEvent)?.ChannelName
                           ?? (_event as TopicEvent)?.ChannelName
                           ?? (_event as InputEvent)?.ChannelName;
    
    public string? nick => (_event as MessageEvent)?.Nickname
                        ?? (_event as PrivateMessageEvent)?.Nickname
                        ?? (_event as JoinEvent)?.Nickname
                        ?? (_event as PartEvent)?.Nickname
                        ?? (_event as QuitEvent)?.Nickname
                        ?? (_event as NickChangeEvent)?.OldNick
                        ?? (_event as NoticeEvent)?.Nickname;
    
    public string? text => (_event as MessageEvent)?.Text
                        ?? (_event as PrivateMessageEvent)?.Text
                        ?? (_event as NoticeEvent)?.Text;
    
    public string? reason => (_event as PartEvent)?.Reason
                          ?? (_event as QuitEvent)?.Reason
                          ?? (_event as KickEvent)?.Reason
                          ?? (_event as DisconnectEvent)?.Reason;
    
    public bool isAction => (_event as MessageEvent)?.IsAction 
                         ?? (_event as PrivateMessageEvent)?.IsAction 
                         ?? false;
    
    // Nick change specific
    public string? oldNick => (_event as NickChangeEvent)?.OldNick;
    public string? newNick => (_event as NickChangeEvent)?.NewNick;
    
    // Kick specific
    public string? kicker => (_event as KickEvent)?.Kicker;
    public string? kicked => (_event as KickEvent)?.Kicked;
    
    // Topic specific
    public string? topic => (_event as TopicEvent)?.Topic;
    public string? setBy => (_event as TopicEvent)?.SetBy;
    
    // Mode specific
    public string? target => (_event as ModeEvent)?.Target;
    public string? modes => (_event as ModeEvent)?.Modes;
    
    // Raw specific
    public string? line => (_event as RawEvent)?.Line;
    public string? command => (_event as RawEvent)?.Command 
                           ?? (_event as InputEvent)?.Command;
    
    // Input specific
    public string? args => (_event as InputEvent)?.Arguments;
    
    // CTCP specific
    public string? ctcp => (_event as CtcpEvent)?.CtcpCommand;
    public string? ctcpArgs => (_event as CtcpEvent)?.Arguments;
    
    // Invite specific
    public string? inviteChannel => (_event as InviteEvent)?.ChannelName;
    
    // Helper: reply to the source
    public void reply(string message)
    {
        if (_event is MessageEvent msgEvent)
        {
            _context.SendMessageAsync(_event.ServerName, msgEvent.ChannelName, message).Wait();
        }
        else if (_event is PrivateMessageEvent pmEvent)
        {
            _context.SendMessageAsync(_event.ServerName, pmEvent.Nickname, message).Wait();
        }
    }
}

#endregion
