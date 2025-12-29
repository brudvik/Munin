using Munin.Agent.Configuration;
using Munin.Agent.Services;
using Munin.Agent.UserDatabase;
using Munin.Core.Scripting;
using Munin.Core.Scripting.Lua;
using Munin.Core.Scripting.Plugins;
using Munin.Core.Scripting.Triggers;
using Serilog;

namespace Munin.Agent.Scripting;

/// <summary>
/// Manages scripting for the agent, including Lua, triggers, and C# plugins.
/// </summary>
public class AgentScriptManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly AgentScriptContext _context;
    private readonly ScriptManager _scriptManager;
    private readonly AgentLuaExtensions _luaExtensions;
    private bool _disposed;

    /// <summary>
    /// Gets the script context.
    /// </summary>
    public AgentScriptContext Context => _context;

    /// <summary>
    /// Gets the underlying script manager.
    /// </summary>
    public ScriptManager ScriptManager => _scriptManager;

    /// <summary>
    /// Event raised when a script outputs a message.
    /// </summary>
    public event EventHandler<ScriptOutputEventArgs>? ScriptOutput;

    /// <summary>
    /// Event raised when a script error occurs.
    /// </summary>
    public event EventHandler<ScriptErrorEventArgs>? ScriptError;

    public AgentScriptManager(
        AgentConfigurationService configService,
        AgentUserDatabaseService userDatabase,
        IrcBotService botService)
    {
        _logger = Log.ForContext<AgentScriptManager>();

        var scriptsDir = configService.Configuration.Scripts.Directory;
        if (!Path.IsPathRooted(scriptsDir))
            scriptsDir = Path.Combine(AppContext.BaseDirectory, scriptsDir);

        _context = new AgentScriptContext(configService, userDatabase, scriptsDir);
        _scriptManager = new ScriptManager(_context);
        _luaExtensions = new AgentLuaExtensions(_context, botService);

        // Wire up events
        _scriptManager.ScriptOutput += (s, e) => ScriptOutput?.Invoke(this, e);
        _scriptManager.ScriptError += (s, e) => ScriptError?.Invoke(this, e);
    }

    /// <summary>
    /// Initializes the script engines and loads auto-load scripts.
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.Information("Initializing script engines...");

        // Register engines
        var luaEngine = new LuaScriptEngine();
        _luaExtensions.ExtendLuaEngine(luaEngine);
        _scriptManager.RegisterEngine(luaEngine);

        _scriptManager.RegisterEngine(new TriggerEngine());
        _scriptManager.RegisterEngine(new PluginEngine());

        // Load all scripts
        await _scriptManager.LoadAllScriptsAsync();

        _logger.Information("Script engines initialized");
    }

    /// <summary>
    /// Dispatches an IRC event to scripts.
    /// </summary>
    public async Task DispatchEventAsync(ScriptEvent scriptEvent)
    {
        await _scriptManager.DispatchEventAsync(scriptEvent);
    }

    /// <summary>
    /// Dispatches a bind event (Eggdrop-style).
    /// </summary>
    public async Task<bool> DispatchBindAsync(string type, BindContext context)
    {
        return await _context.DispatchBindAsync(type, context);
    }

    /// <summary>
    /// Loads a script file.
    /// </summary>
    public async Task<ScriptResult> LoadScriptAsync(string filePath)
    {
        return await _scriptManager.LoadScriptAsync(filePath);
    }

    /// <summary>
    /// Executes Lua code directly.
    /// </summary>
    public async Task<ScriptResult> ExecuteLuaAsync(string code)
    {
        var luaEngine = _scriptManager.GetEngines().FirstOrDefault(e => e.Name == "Lua");
        if (luaEngine == null)
            return ScriptResult.Fail("Lua engine not loaded");

        return await luaEngine.ExecuteAsync(code);
    }

    /// <summary>
    /// Reloads all scripts.
    /// </summary>
    public async Task ReloadAllScriptsAsync()
    {
        _logger.Information("Reloading all scripts...");
        await _scriptManager.LoadAllScriptsAsync();
        _logger.Information("Scripts reloaded");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _scriptManager.Dispose();
    }
}
