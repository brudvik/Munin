using System.Collections.Concurrent;

namespace Munin.Core.Scripting;

/// <summary>
/// Manages multiple script engines and dispatches events to all loaded scripts.
/// </summary>
public class ScriptManager : IDisposable
{
    private readonly ScriptContext _context;
    private readonly List<IScriptEngine> _engines = new();
    private readonly ConcurrentDictionary<string, LoadedScript> _loadedScripts = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when a script is loaded.
    /// </summary>
    public event EventHandler<ScriptLoadedEventArgs>? ScriptLoaded;
    
    /// <summary>
    /// Event raised when a script is unloaded.
    /// </summary>
    public event EventHandler<ScriptUnloadedEventArgs>? ScriptUnloaded;
    
    /// <summary>
    /// Event raised when a script error occurs.
    /// </summary>
    public event EventHandler<ScriptErrorEventArgs>? ScriptError;
    
    /// <summary>
    /// Event raised when a script outputs a message.
    /// </summary>
    public event EventHandler<ScriptOutputEventArgs>? ScriptOutput;

    /// <summary>
    /// Gets the script context.
    /// </summary>
    public ScriptContext Context => _context;

    public ScriptManager(ScriptContext context)
    {
        _context = context;
        _context.Output += (s, e) => ScriptOutput?.Invoke(this, e);
        _context.Error += (s, e) => ScriptError?.Invoke(this, e);
    }

    /// <summary>
    /// Registers a script engine.
    /// </summary>
    public void RegisterEngine(IScriptEngine engine)
    {
        lock (_lock)
        {
            engine.Initialize(_context);
            _engines.Add(engine);
        }
    }

    /// <summary>
    /// Gets all registered engines.
    /// </summary>
    public IEnumerable<IScriptEngine> GetEngines()
    {
        lock (_lock)
        {
            return _engines.ToList();
        }
    }

    /// <summary>
    /// Loads all scripts from the scripts directory.
    /// </summary>
    public async Task LoadAllScriptsAsync()
    {
        if (!Directory.Exists(_context.ScriptsDirectory))
            return;

        foreach (var engine in _engines)
        {
            var pattern = $"*{engine.FileExtension}";
            var files = Directory.GetFiles(_context.ScriptsDirectory, pattern, SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                await LoadScriptAsync(file);
            }
        }
    }

    /// <summary>
    /// Loads a script from a file.
    /// </summary>
    public async Task<ScriptResult> LoadScriptAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        var engine = _engines.FirstOrDefault(e => 
            e.FileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase));
        
        if (engine == null)
        {
            return ScriptResult.Fail($"No engine registered for extension '{extension}'");
        }

        var scriptName = Path.GetFileNameWithoutExtension(filePath);
        
        // Unload existing script with same name
        if (_loadedScripts.ContainsKey(scriptName))
        {
            UnloadScript(scriptName);
        }

        var result = await engine.LoadScriptAsync(filePath);
        
        if (result.Success)
        {
            _loadedScripts[scriptName] = new LoadedScript
            {
                Name = scriptName,
                FilePath = filePath,
                Engine = engine,
                LoadedAt = DateTime.Now
            };
            
            ScriptLoaded?.Invoke(this, new ScriptLoadedEventArgs(scriptName, filePath));
        }
        else
        {
            ScriptError?.Invoke(this, new ScriptErrorEventArgs(scriptName, result.Error ?? "Unknown error"));
        }

        return result;
    }

    /// <summary>
    /// Executes a script from string content.
    /// </summary>
    public async Task<ScriptResult> ExecuteAsync(string code, string engineName = "lua")
    {
        var engine = _engines.FirstOrDefault(e => 
            e.Name.Equals(engineName, StringComparison.OrdinalIgnoreCase));
        
        if (engine == null)
        {
            return ScriptResult.Fail($"Engine '{engineName}' not found");
        }

        return await engine.ExecuteAsync(code);
    }

    /// <summary>
    /// Unloads a script by name.
    /// </summary>
    public bool UnloadScript(string scriptName)
    {
        if (_loadedScripts.TryRemove(scriptName, out var script))
        {
            script.Engine.UnloadScript(scriptName);
            ScriptUnloaded?.Invoke(this, new ScriptUnloadedEventArgs(scriptName));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reloads a script by name.
    /// </summary>
    public async Task<ScriptResult> ReloadScriptAsync(string scriptName)
    {
        if (_loadedScripts.TryGetValue(scriptName, out var script))
        {
            return await LoadScriptAsync(script.FilePath);
        }
        return ScriptResult.Fail($"Script '{scriptName}' not found");
    }

    /// <summary>
    /// Gets all loaded scripts.
    /// </summary>
    public IEnumerable<LoadedScript> GetLoadedScripts()
    {
        return _loadedScripts.Values.ToList();
    }

    /// <summary>
    /// Dispatches an event to all loaded scripts.
    /// </summary>
    public async Task DispatchEventAsync(ScriptEvent scriptEvent)
    {
        foreach (var engine in _engines)
        {
            try
            {
                await engine.DispatchEventAsync(scriptEvent);
            }
            catch (Exception ex)
            {
                ScriptError?.Invoke(this, new ScriptErrorEventArgs(
                    engine.Name, 
                    $"Error dispatching {scriptEvent.EventType}: {ex.Message}"));
            }
            
            // If event was cancelled, stop dispatching
            if (scriptEvent.Cancelled)
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var engine in _engines)
        {
            engine.Dispose();
        }
        _engines.Clear();
        _loadedScripts.Clear();
    }
}

/// <summary>
/// Information about a loaded script.
/// </summary>
public class LoadedScript
{
    public string Name { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public IScriptEngine Engine { get; init; } = null!;
    public DateTime LoadedAt { get; init; }
}

public class ScriptLoadedEventArgs : EventArgs
{
    public string ScriptName { get; }
    public string FilePath { get; }
    public ScriptLoadedEventArgs(string scriptName, string filePath)
    {
        ScriptName = scriptName;
        FilePath = filePath;
    }
}

public class ScriptUnloadedEventArgs : EventArgs
{
    public string ScriptName { get; }
    public ScriptUnloadedEventArgs(string scriptName) => ScriptName = scriptName;
}
