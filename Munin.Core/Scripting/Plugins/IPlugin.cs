using Munin.Core.Models;

namespace Munin.Core.Scripting.Plugins;

/// <summary>
/// Interface for C# plugins. Plugins must implement this interface to be loaded.
/// </summary>
public interface IPlugin : IDisposable
{
    /// <summary>
    /// Unique identifier for the plugin.
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Display name of the plugin.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Plugin version.
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// Plugin author.
    /// </summary>
    string Author { get; }
    
    /// <summary>
    /// Brief description of what the plugin does.
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Called when the plugin is loaded.
    /// </summary>
    Task InitializeAsync(IPluginContext context);
    
    /// <summary>
    /// Called when an IRC event occurs.
    /// </summary>
    Task OnEventAsync(ScriptEvent evt);
    
    /// <summary>
    /// Called when the plugin is being unloaded.
    /// </summary>
    Task ShutdownAsync();
}

/// <summary>
/// Context provided to plugins for interacting with IRC and the application.
/// </summary>
public interface IPluginContext
{
    #region IRC Operations
    
    /// <summary>Sends a message to a channel or user.</summary>
    Task SendMessageAsync(string server, string target, string message);
    
    /// <summary>Sends an action (/me) to a channel or user.</summary>
    Task SendActionAsync(string server, string target, string action);
    
    /// <summary>Sends a notice to a channel or user.</summary>
    Task SendNoticeAsync(string server, string target, string message);
    
    /// <summary>Sends a raw IRC command.</summary>
    Task SendRawAsync(string server, string command);
    
    /// <summary>Joins a channel.</summary>
    Task JoinChannelAsync(string server, string channel, string? key = null);
    
    /// <summary>Parts a channel.</summary>
    Task PartChannelAsync(string server, string channel, string? reason = null);
    
    /// <summary>Kicks a user from a channel.</summary>
    Task KickUserAsync(string server, string channel, string nick, string? reason = null);
    
    /// <summary>Sets a mode on a channel or user.</summary>
    Task SetModeAsync(string server, string target, string modes);
    
    #endregion
    
    #region Information
    
    /// <summary>Gets all connected servers.</summary>
    IEnumerable<IrcServer> GetServers();
    
    /// <summary>Gets a server by name.</summary>
    IrcServer? GetServer(string name);
    
    /// <summary>Gets a channel from a server.</summary>
    IrcChannel? GetChannel(string server, string channel);
    
    /// <summary>Gets the current nickname on a server.</summary>
    string? GetCurrentNick(string server);
    
    #endregion
    
    #region Storage
    
    /// <summary>Stores a value in plugin storage.</summary>
    void SetData(string key, object value);
    
    /// <summary>Retrieves a value from plugin storage.</summary>
    T? GetData<T>(string key);
    
    /// <summary>Removes a value from plugin storage.</summary>
    bool RemoveData(string key);
    
    /// <summary>Saves plugin data to persistent storage.</summary>
    Task SaveDataAsync();
    
    /// <summary>Loads plugin data from persistent storage.</summary>
    Task LoadDataAsync();
    
    #endregion
    
    #region Timers
    
    /// <summary>Creates a one-shot timer.</summary>
    int SetTimeout(Func<Task> callback, int delayMs);
    
    /// <summary>Creates a repeating timer.</summary>
    int SetInterval(Func<Task> callback, int intervalMs);
    
    /// <summary>Cancels a timer.</summary>
    bool ClearTimer(int timerId);
    
    #endregion
    
    #region Logging
    
    /// <summary>Logs an informational message.</summary>
    void LogInfo(string message);
    
    /// <summary>Logs a warning message.</summary>
    void LogWarning(string message);
    
    /// <summary>Logs an error message.</summary>
    void LogError(string message, Exception? ex = null);
    
    #endregion
    
    #region Commands
    
    /// <summary>Registers a custom command that users can invoke.</summary>
    void RegisterCommand(string name, string description, Func<CommandArgs, Task> handler);
    
    /// <summary>Unregisters a custom command.</summary>
    void UnregisterCommand(string name);
    
    #endregion
}

/// <summary>
/// Arguments passed to a plugin command handler.
/// </summary>
public class CommandArgs
{
    /// <summary>The server where the command was invoked.</summary>
    public IrcServer Server { get; init; } = null!;
    
    /// <summary>The channel where the command was invoked (null for server console).</summary>
    public IrcChannel? Channel { get; init; }
    
    /// <summary>The command name without the prefix.</summary>
    public string Command { get; init; } = string.Empty;
    
    /// <summary>The full argument string.</summary>
    public string ArgumentString { get; init; } = string.Empty;
    
    /// <summary>Arguments split by whitespace.</summary>
    public string[] Arguments { get; init; } = Array.Empty<string>();
    
    /// <summary>Gets an argument by index, or null if not present.</summary>
    public string? GetArg(int index) => index < Arguments.Length ? Arguments[index] : null;
    
    /// <summary>Gets arguments from index onwards as a single string.</summary>
    public string GetArgsFrom(int index)
    {
        if (index >= Arguments.Length) return string.Empty;
        return string.Join(" ", Arguments.Skip(index));
    }
}

/// <summary>
/// Attribute to mark a plugin class for automatic discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PluginAttribute : Attribute
{
    public string Id { get; }
    
    public PluginAttribute(string id)
    {
        Id = id;
    }
}
