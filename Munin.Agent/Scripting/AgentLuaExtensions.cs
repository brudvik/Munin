using MoonSharp.Interpreter;
using Munin.Agent.Services;
using Munin.Agent.UserDatabase;
using Munin.Core.Scripting.Lua;
using Serilog;

namespace Munin.Agent.Scripting;

/// <summary>
/// Extends the Lua engine with agent-specific Eggdrop-style functions.
/// </summary>
public class AgentLuaExtensions
{
    private readonly ILogger _logger;
    private readonly AgentScriptContext _context;
    private readonly IrcBotService _botService;

    public AgentLuaExtensions(AgentScriptContext context, IrcBotService botService)
    {
        _logger = Log.ForContext<AgentLuaExtensions>();
        _context = context;
        _botService = botService;
    }

    /// <summary>
    /// Extends a Lua engine with agent-specific APIs.
    /// </summary>
    public void ExtendLuaEngine(LuaScriptEngine engine)
    {
        // Register agent-specific types
        UserData.RegisterType<LuaAgentApi>();
        UserData.RegisterType<LuaBindApi>();
        UserData.RegisterType<LuaUserDbApi>();
    }

    /// <summary>
    /// Creates agent-specific globals for a Lua script.
    /// Called during script initialization.
    /// </summary>
    public void SetupScriptGlobals(Script script)
    {
        // Add Eggdrop-style bind function
        script.Globals["bind"] = (Func<string, string, string, DynValue, string>)((type, flags, mask, callback) =>
        {
            if (callback.Type != DataType.Function)
                throw new ScriptRuntimeException("Callback must be a function");

            return _context.RegisterBind(type, flags, mask, async ctx =>
            {
                try
                {
                    var result = script.Call(callback, CreateBindContextTable(script, ctx));
                    return result.Type == DataType.Boolean && result.Boolean;
                }
                catch (ScriptRuntimeException ex)
                {
                    _logger.Error(ex, "Error in bind callback");
                    return false;
                }
            });
        });

        script.Globals["unbind"] = (Action<string>)(bindId => _context.UnregisterBind(bindId));

        // Eggdrop-style IRC functions
        script.Globals["putserv"] = (Action<string, string>)(async (serverId, raw) =>
            await _botService.SendRawAsync(serverId, raw));

        script.Globals["puthelp"] = (Action<string, string>)(async (serverId, raw) =>
            await _botService.SendRawAsync(serverId, raw)); // Same as putserv for now

        script.Globals["putquick"] = (Action<string, string>)(async (serverId, raw) =>
            await _botService.SendRawAsync(serverId, raw)); // Same as putserv for now

        script.Globals["putkick"] = (Action<string, string, string, string?>)(async (serverId, channel, nick, reason) =>
            await _botService.SendRawAsync(serverId, $"KICK {channel} {nick}" + (reason != null ? $" :{reason}" : "")));

        script.Globals["putmsg"] = (Action<string, string, string>)(async (serverId, target, message) =>
            await _botService.SendMessageAsync(serverId, target, message));

        script.Globals["putnotice"] = (Action<string, string, string>)(async (serverId, target, message) =>
            await _botService.SendRawAsync(serverId, $"NOTICE {target} :{message}"));

        // User database API
        script.Globals["users"] = UserData.Create(new LuaUserDbApi(_context));

        // Agent API
        script.Globals["agent"] = UserData.Create(new LuaAgentApi(_context, _botService));
    }

    private Table CreateBindContextTable(Script script, BindContext ctx)
    {
        var table = new Table(script);
        table["server"] = ctx.ServerId;
        table["nick"] = ctx.Nick ?? "";
        table["host"] = ctx.Hostmask ?? "";
        table["channel"] = ctx.Channel ?? "";
        table["text"] = ctx.Text ?? "";
        table["args"] = CreateArgsTable(script, ctx.Args);
        table["target"] = ctx.Target ?? "";
        table["command"] = ctx.Command ?? "";

        // Add user info if available
        if (ctx.User != null)
        {
            var userTable = new Table(script);
            userTable["handle"] = ctx.User.Handle;
            userTable["flags"] = ctx.User.GetFlagString();
            table["user"] = userTable;
        }

        // Add reply function
        table["reply"] = (Action<string>)(async msg =>
        {
            if (ctx.Reply != null)
                await ctx.Reply(msg);
        });

        return table;
    }

    private Table CreateArgsTable(Script script, string[] args)
    {
        var table = new Table(script);
        for (int i = 0; i < args.Length; i++)
        {
            table[i + 1] = args[i]; // Lua arrays are 1-indexed
        }
        return table;
    }
}

/// <summary>
/// Agent API exposed to Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaAgentApi
{
    private readonly AgentScriptContext _context;
    private readonly IrcBotService _botService;

    public LuaAgentApi(AgentScriptContext context, IrcBotService botService)
    {
        _context = context;
        _botService = botService;
    }

    /// <summary>
    /// Gets the list of connected servers.
    /// </summary>
    public List<string> servers()
    {
        return _botService.GetConnectionStatus().Select(c => c.Id).ToList();
    }

    /// <summary>
    /// Gets connection status for a server.
    /// </summary>
    public Table? server(Script script, string serverId)
    {
        var status = _botService.GetConnectionStatus().FirstOrDefault(c => c.Id == serverId);
        if (status == null) return null;

        var table = new Table(script);
        table["id"] = status.Id;
        table["host"] = status.Host;
        table["port"] = status.Port;
        table["nick"] = status.Nickname;
        table["state"] = status.State;
        table["channels"] = CreateStringList(script, status.Channels);
        return table;
    }

    private Table CreateStringList(Script script, List<string> items)
    {
        var table = new Table(script);
        for (int i = 0; i < items.Count; i++)
        {
            table[i + 1] = items[i];
        }
        return table;
    }

    /// <summary>
    /// Logs a message.
    /// </summary>
    public void log(string level, string message)
    {
        var logger = Log.ForContext("Script", "Lua");
        switch (level.ToLowerInvariant())
        {
            case "debug": logger.Debug(message); break;
            case "info": logger.Information(message); break;
            case "warn": logger.Warning(message); break;
            case "error": logger.Error(message); break;
            default: logger.Information(message); break;
        }
    }
}

/// <summary>
/// Bind API for registering event handlers.
/// </summary>
[MoonSharpUserData]
public class LuaBindApi
{
    private readonly AgentScriptContext _context;

    public LuaBindApi(AgentScriptContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Gets all registered binds.
    /// </summary>
    public int count(string? type = null)
    {
        return _context.GetBinds(type).Count;
    }
}

/// <summary>
/// User database API for Lua scripts.
/// </summary>
[MoonSharpUserData]
public class LuaUserDbApi
{
    private readonly AgentScriptContext _context;

    public LuaUserDbApi(AgentScriptContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Gets a user by handle.
    /// </summary>
    public Table? get(Script script, string handle)
    {
        var user = _context.UserDatabase.GetUser(handle);
        if (user == null) return null;
        return CreateUserTable(script, user);
    }

    /// <summary>
    /// Matches a hostmask to a user.
    /// </summary>
    public Table? match(Script script, string hostmask)
    {
        var user = _context.UserDatabase.MatchUser(hostmask);
        if (user == null) return null;
        return CreateUserTable(script, user);
    }

    /// <summary>
    /// Checks if a user has specific flags.
    /// </summary>
    public bool hasflags(string handle, string flags, string? channel = null)
    {
        var user = _context.UserDatabase.GetUser(handle);
        if (user == null) return false;

        var required = AgentUser.ParseFlags(flags);
        return user.HasFlag(required, channel);
    }

    /// <summary>
    /// Adds flags to a user.
    /// </summary>
    public bool addflags(string handle, string flags, string? channel = null)
    {
        return _context.UserDatabase.AddFlags(handle, flags, channel);
    }

    /// <summary>
    /// Removes flags from a user.
    /// </summary>
    public bool delflags(string handle, string flags, string? channel = null)
    {
        return _context.UserDatabase.RemoveFlags(handle, flags, channel);
    }

    /// <summary>
    /// Gets all users.
    /// </summary>
    public Table list(Script script)
    {
        var table = new Table(script);
        var users = _context.UserDatabase.GetUsers();
        for (int i = 0; i < users.Count; i++)
        {
            table[i + 1] = CreateUserTable(script, users[i]);
        }
        return table;
    }

    private Table CreateUserTable(Script script, AgentUser user)
    {
        var table = new Table(script);
        table["handle"] = user.Handle;
        table["flags"] = user.GetFlagString();
        table["info"] = user.Info ?? "";
        table["lastseen"] = user.LastSeen?.ToString("o") ?? "";

        var hosts = new Table(script);
        for (int i = 0; i < user.Hostmasks.Count; i++)
        {
            hosts[i + 1] = user.Hostmasks[i];
        }
        table["hosts"] = hosts;

        return table;
    }
}
