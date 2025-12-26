namespace Munin.Core.Scripting;

/// <summary>
/// Interface for script engines (Lua, C# plugins, etc.).
/// </summary>
public interface IScriptEngine : IDisposable
{
    /// <summary>
    /// Gets the engine name for display purposes.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Gets the file extension this engine handles (e.g., ".lua", ".cs").
    /// </summary>
    string FileExtension { get; }
    
    /// <summary>
    /// Initializes the engine with the script context.
    /// </summary>
    void Initialize(ScriptContext context);
    
    /// <summary>
    /// Loads and executes a script from file.
    /// </summary>
    Task<ScriptResult> LoadScriptAsync(string filePath);
    
    /// <summary>
    /// Executes a script from string content.
    /// </summary>
    Task<ScriptResult> ExecuteAsync(string code, string? scriptName = null);
    
    /// <summary>
    /// Unloads a previously loaded script.
    /// </summary>
    void UnloadScript(string scriptName);
    
    /// <summary>
    /// Dispatches an event to all loaded scripts.
    /// </summary>
    Task DispatchEventAsync(ScriptEvent scriptEvent);
}

/// <summary>
/// Result of script execution.
/// </summary>
public class ScriptResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public object? ReturnValue { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    
    public static ScriptResult Ok(object? value = null, TimeSpan? time = null) 
        => new() { Success = true, ReturnValue = value, ExecutionTime = time ?? TimeSpan.Zero };
    
    public static ScriptResult Fail(string error, TimeSpan? time = null) 
        => new() { Success = false, Error = error, ExecutionTime = time ?? TimeSpan.Zero };
}
