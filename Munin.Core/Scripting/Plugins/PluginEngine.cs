using Munin.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Munin.Core.Scripting.Plugins;

/// <summary>
/// Loads and manages C# plugins from DLL files.
/// </summary>
[SuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Plugins are loaded dynamically and trimming is not used")]
[SuppressMessage("Trimming", "IL2072:RequiresDynamicallyAccessedMembers", Justification = "Plugins are loaded dynamically and trimming is not used")]
public class PluginEngine : IScriptEngine
{
    private ScriptContext _context = null!;
    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new();
    private readonly ConcurrentDictionary<string, PluginCommand> _commands = new();
    
    public string Name => "Plugins";
    public string FileExtension => ".dll";

    public void Initialize(ScriptContext context)
    {
        _context = context;
    }

    public async Task<ScriptResult> LoadScriptAsync(string filePath)
    {
        try
        {
            // Create an isolated load context for the plugin
            var loadContext = new PluginLoadContext(filePath);
            var assembly = loadContext.LoadFromAssemblyPath(filePath);
            
            // Find all types implementing IPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();
            
            if (!pluginTypes.Any())
            {
                loadContext.Unload();
                return ScriptResult.Fail("No plugin classes found in assembly");
            }
            
            var loadedCount = 0;
            
            foreach (var pluginType in pluginTypes)
            {
                try
                {
                    var plugin = (IPlugin)Activator.CreateInstance(pluginType)!;
                    var pluginContext = new PluginContextImpl(_context, plugin.Id, this);
                    
                    await plugin.InitializeAsync(pluginContext);
                    
                    _plugins[plugin.Id] = new LoadedPlugin
                    {
                        Plugin = plugin,
                        Context = pluginContext,
                        LoadContext = loadContext,
                        FilePath = filePath,
                        LoadedAt = DateTime.Now
                    };
                    
                    loadedCount++;
                    _context.Print($"Loaded plugin: {plugin.Name} v{plugin.Version} by {plugin.Author}");
                }
                catch (Exception ex)
                {
                    _context.RaiseError("Plugins", $"Failed to load plugin {pluginType.Name}: {ex.Message}");
                }
            }
            
            return loadedCount > 0 
                ? ScriptResult.Ok() 
                : ScriptResult.Fail("Failed to load any plugins from assembly");
        }
        catch (Exception ex)
        {
            return ScriptResult.Fail($"Failed to load plugin assembly: {ex.Message}");
        }
    }

    public Task<ScriptResult> ExecuteAsync(string code, string? scriptName = null)
    {
        // C# plugins don't support inline execution
        return Task.FromResult(ScriptResult.Fail("C# plugins must be compiled DLLs"));
    }

    public void UnloadScript(string scriptName)
    {
        // scriptName here is the plugin ID
        if (_plugins.TryRemove(scriptName, out var loaded))
        {
            try
            {
                loaded.Plugin.ShutdownAsync().Wait();
                loaded.Plugin.Dispose();
                
                // Remove commands registered by this plugin
                var commandsToRemove = _commands
                    .Where(kvp => kvp.Value.PluginId == scriptName)
                    .Select(kvp => kvp.Key)
                    .ToList();
                    
                foreach (var cmd in commandsToRemove)
                    _commands.TryRemove(cmd, out _);
                
                // Unload the assembly context
                loaded.LoadContext.Unload();
                
                _context.Print($"Unloaded plugin: {scriptName}");
            }
            catch (Exception ex)
            {
                _context.RaiseError("Plugins", $"Error unloading plugin {scriptName}: {ex.Message}");
            }
        }
    }

    public async Task DispatchEventAsync(ScriptEvent scriptEvent)
    {
        // Handle input events for custom commands
        if (scriptEvent is InputEvent inputEvent)
        {
            if (_commands.TryGetValue(inputEvent.Command.ToLowerInvariant(), out var command))
            {
                var args = new CommandArgs
                {
                    Server = scriptEvent.Server!,
                    Channel = inputEvent.Channel,
                    Command = inputEvent.Command,
                    ArgumentString = inputEvent.Arguments,
                    Arguments = string.IsNullOrEmpty(inputEvent.Arguments) 
                        ? Array.Empty<string>() 
                        : inputEvent.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                };
                
                try
                {
                    await command.Handler(args);
                    scriptEvent.Cancelled = true; // Prevent default handling
                }
                catch (Exception ex)
                {
                    _context.RaiseError("Plugins", $"Command error ({inputEvent.Command}): {ex.Message}");
                }
                
                return;
            }
        }
        
        // Dispatch to all plugins
        foreach (var loaded in _plugins.Values)
        {
            try
            {
                await loaded.Plugin.OnEventAsync(scriptEvent);
                
                if (scriptEvent.Cancelled)
                    return;
            }
            catch (Exception ex)
            {
                _context.RaiseError("Plugins", $"Plugin error ({loaded.Plugin.Name}): {ex.Message}");
            }
        }
    }

    internal void RegisterCommand(string pluginId, string name, string description, Func<CommandArgs, Task> handler)
    {
        _commands[name.ToLowerInvariant()] = new PluginCommand
        {
            PluginId = pluginId,
            Name = name,
            Description = description,
            Handler = handler
        };
    }

    internal void UnregisterCommand(string name)
    {
        _commands.TryRemove(name.ToLowerInvariant(), out _);
    }

    /// <summary>
    /// Gets all registered plugin commands.
    /// </summary>
    public IEnumerable<(string Name, string Description, string PluginId)> GetCommands()
    {
        return _commands.Values.Select(c => (c.Name, c.Description, c.PluginId));
    }

    /// <summary>
    /// Gets all loaded plugins.
    /// </summary>
    public IEnumerable<IPlugin> GetPlugins()
    {
        return _plugins.Values.Select(p => p.Plugin);
    }

    public void Dispose()
    {
        foreach (var plugin in _plugins.Values)
        {
            try
            {
                plugin.Plugin.ShutdownAsync().Wait();
                plugin.Plugin.Dispose();
                plugin.LoadContext.Unload();
            }
            catch { }
        }
        _plugins.Clear();
        _commands.Clear();
    }

    private class LoadedPlugin
    {
        public IPlugin Plugin { get; init; } = null!;
        public PluginContextImpl Context { get; init; } = null!;
        public PluginLoadContext LoadContext { get; init; } = null!;
        public string FilePath { get; init; } = string.Empty;
        public DateTime LoadedAt { get; init; }
    }

    private class PluginCommand
    {
        public string PluginId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public Func<CommandArgs, Task> Handler { get; init; } = null!;
    }
}

/// <summary>
/// Isolated assembly load context for plugins.
/// </summary>
[SuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Plugins are loaded dynamically and trimming is not used")]
internal class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
    }
}

/// <summary>
/// Implementation of IPluginContext for plugins.
/// </summary>
[SuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Plugins are loaded dynamically and trimming is not used")]
internal class PluginContextImpl : IPluginContext
{
    private readonly ScriptContext _context;
    private readonly string _pluginId;
    private readonly PluginEngine _engine;
    private readonly ConcurrentDictionary<string, object> _data = new();
    private string? _dataFilePath;

    public PluginContextImpl(ScriptContext context, string pluginId, PluginEngine engine)
    {
        _context = context;
        _pluginId = pluginId;
        _engine = engine;
        _dataFilePath = Path.Combine(context.ScriptsDirectory, "data", $"{pluginId}.json");
    }

    #region IRC Operations

    public Task SendMessageAsync(string server, string target, string message)
        => _context.SendMessageAsync(server, target, message);

    public Task SendActionAsync(string server, string target, string action)
        => _context.SendActionAsync(server, target, action);

    public Task SendNoticeAsync(string server, string target, string message)
        => _context.SendNoticeAsync(server, target, message);

    public Task SendRawAsync(string server, string command)
        => _context.SendRawAsync(server, command);

    public Task JoinChannelAsync(string server, string channel, string? key = null)
        => _context.JoinChannelAsync(server, channel, key);

    public Task PartChannelAsync(string server, string channel, string? reason = null)
        => _context.PartChannelAsync(server, channel, reason);

    public Task KickUserAsync(string server, string channel, string nick, string? reason = null)
        => _context.KickUserAsync(server, channel, nick, reason);

    public Task SetModeAsync(string server, string target, string modes)
        => _context.SetModeAsync(server, target, modes);

    #endregion

    #region Information

    public IEnumerable<IrcServer> GetServers() => _context.GetServers();
    public IrcServer? GetServer(string name) => _context.GetServer(name);
    public IrcChannel? GetChannel(string server, string channel) => _context.GetChannel(server, channel);
    public string? GetCurrentNick(string server) => _context.GetCurrentNick(server);

    #endregion

    #region Storage

    public void SetData(string key, object value) => _data[key] = value;
    
    public T? GetData<T>(string key)
    {
        if (_data.TryGetValue(key, out var value))
        {
            if (value is T typedValue) return typedValue;
            if (value is JsonElement element)
            {
                return JsonSerializer.Deserialize<T>(element.GetRawText());
            }
        }
        return default;
    }
    
    public bool RemoveData(string key) => _data.TryRemove(key, out _);

    public async Task SaveDataAsync()
    {
        if (_dataFilePath == null) return;
        
        var dir = Path.GetDirectoryName(_dataFilePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
            
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_dataFilePath, json);
    }

    public async Task LoadDataAsync()
    {
        if (_dataFilePath == null || !File.Exists(_dataFilePath)) return;
        
        try
        {
            var json = await File.ReadAllTextAsync(_dataFilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (data != null)
            {
                foreach (var kvp in data)
                    _data[kvp.Key] = kvp.Value;
            }
        }
        catch { }
    }

    #endregion

    #region Timers

    public int SetTimeout(Func<Task> callback, int delayMs) => _context.SetTimeout(callback, delayMs);
    public int SetInterval(Func<Task> callback, int intervalMs) => _context.SetInterval(callback, intervalMs);
    public bool ClearTimer(int timerId) => _context.ClearTimer(timerId);

    #endregion

    #region Logging

    public void LogInfo(string message) => _context.Print($"[{_pluginId}] {message}");
    public void LogWarning(string message) => _context.Print($"[{_pluginId}] WARNING: {message}");
    public void LogError(string message, Exception? ex = null) 
        => _context.RaiseError(_pluginId, ex != null ? $"{message}: {ex.Message}" : message);

    #endregion

    #region Commands

    public void RegisterCommand(string name, string description, Func<CommandArgs, Task> handler)
        => _engine.RegisterCommand(_pluginId, name, description, handler);

    public void UnregisterCommand(string name)
        => _engine.UnregisterCommand(name);

    #endregion
}
