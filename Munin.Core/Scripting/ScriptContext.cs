using Munin.Core.Models;
using Munin.Core.Services;
using System.Collections.Concurrent;

namespace Munin.Core.Scripting;

/// <summary>
/// Shared context available to all scripts, providing access to IRC functionality and storage.
/// </summary>
public class ScriptContext
{
    private readonly ConcurrentDictionary<string, object> _globalStorage = new();
    private readonly List<ScriptTimer> _timers = new();
    private readonly object _timerLock = new();
    private int _nextTimerId = 1;

    /// <summary>
    /// Event raised when a script wants to send output to the user.
    /// </summary>
    public event EventHandler<ScriptOutputEventArgs>? Output;
    
    /// <summary>
    /// Event raised when a script encounters an error.
    /// </summary>
    public event EventHandler<ScriptErrorEventArgs>? Error;

    /// <summary>
    /// The IRC client manager for sending messages and commands.
    /// </summary>
    public IrcClientManager ClientManager { get; }
    
    /// <summary>
    /// Gets the scripts directory path.
    /// </summary>
    public string ScriptsDirectory { get; }

    public ScriptContext(IrcClientManager clientManager, string scriptsDirectory)
    {
        ClientManager = clientManager;
        ScriptsDirectory = scriptsDirectory;
        
        if (!Directory.Exists(scriptsDirectory))
        {
            Directory.CreateDirectory(scriptsDirectory);
        }
    }

    #region IRC API
    
    /// <summary>
    /// Sends a message to a channel or user.
    /// </summary>
    public async Task SendMessageAsync(string serverName, string target, string message)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.SendMessageAsync(target, message);
        }
    }
    
    /// <summary>
    /// Sends a CTCP ACTION (/me) to a channel or user.
    /// </summary>
    public async Task SendActionAsync(string serverName, string target, string action)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.SendActionAsync(target, action);
        }
    }
    
    /// <summary>
    /// Sends a notice to a channel or user.
    /// </summary>
    public async Task SendNoticeAsync(string serverName, string target, string message)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.SendNoticeAsync(target, message);
        }
    }
    
    /// <summary>
    /// Sends a raw IRC command.
    /// </summary>
    public async Task SendRawAsync(string serverName, string command)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.SendRawAsync(command);
        }
    }
    
    /// <summary>
    /// Joins a channel.
    /// </summary>
    public async Task JoinChannelAsync(string serverName, string channel, string? key = null)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.JoinChannelAsync(channel, key);
        }
    }
    
    /// <summary>
    /// Parts (leaves) a channel.
    /// </summary>
    public async Task PartChannelAsync(string serverName, string channel, string? reason = null)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.PartChannelAsync(channel, reason);
        }
    }
    
    /// <summary>
    /// Kicks a user from a channel.
    /// </summary>
    public async Task KickUserAsync(string serverName, string channel, string nickname, string? reason = null)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.SendRawAsync($"KICK {channel} {nickname}" + (reason != null ? $" :{reason}" : ""));
        }
    }
    
    /// <summary>
    /// Sets a channel or user mode.
    /// </summary>
    public async Task SetModeAsync(string serverName, string target, string modes)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.SendRawAsync($"MODE {target} {modes}");
        }
    }
    
    /// <summary>
    /// Changes the user's nickname.
    /// </summary>
    public async Task SetNickAsync(string serverName, string newNick)
    {
        var connection = FindConnection(serverName);
        if (connection != null)
        {
            await connection.SendRawAsync($"NICK {newNick}");
        }
    }
    
    /// <summary>
    /// Gets the list of connected servers.
    /// </summary>
    public IEnumerable<IrcServer> GetServers()
    {
        return ClientManager.GetServers();
    }
    
    /// <summary>
    /// Gets a server by name.
    /// </summary>
    public IrcServer? GetServer(string name)
    {
        return ClientManager.GetServers()
            .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Gets a channel from a server.
    /// </summary>
    public IrcChannel? GetChannel(string serverName, string channelName)
    {
        var server = GetServer(serverName);
        return server?.Channels.FirstOrDefault(c => 
            c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Gets the current user's nickname on a server.
    /// </summary>
    public string? GetCurrentNick(string serverName)
    {
        var connection = FindConnection(serverName);
        return connection?.CurrentNickname;
    }
    
    private IrcConnection? FindConnection(string serverName)
    {
        return ClientManager.GetConnection(serverName);
    }
    
    #endregion
    
    #region Storage API
    
    /// <summary>
    /// Stores a value in global script storage.
    /// </summary>
    public void SetGlobal(string key, object value)
    {
        _globalStorage[key] = value;
    }
    
    /// <summary>
    /// Retrieves a value from global script storage.
    /// </summary>
    public object? GetGlobal(string key)
    {
        return _globalStorage.TryGetValue(key, out var value) ? value : null;
    }
    
    /// <summary>
    /// Removes a value from global script storage.
    /// </summary>
    public bool RemoveGlobal(string key)
    {
        return _globalStorage.TryRemove(key, out _);
    }
    
    /// <summary>
    /// Checks if a key exists in global storage.
    /// </summary>
    public bool HasGlobal(string key)
    {
        return _globalStorage.ContainsKey(key);
    }
    
    /// <summary>
    /// Gets all keys in global storage.
    /// </summary>
    public IEnumerable<string> GetGlobalKeys()
    {
        return _globalStorage.Keys;
    }
    
    #endregion
    
    #region Timer API
    
    /// <summary>
    /// Creates a new timer that fires after the specified delay.
    /// </summary>
    public int SetTimeout(Func<Task> callback, int delayMs)
    {
        var timer = new ScriptTimer
        {
            Id = Interlocked.Increment(ref _nextTimerId),
            Callback = callback,
            Interval = delayMs,
            IsRepeating = false,
            NextFireTime = DateTime.UtcNow.AddMilliseconds(delayMs)
        };
        
        lock (_timerLock)
        {
            _timers.Add(timer);
        }
        
        _ = RunTimerAsync(timer);
        return timer.Id;
    }
    
    /// <summary>
    /// Creates a new repeating timer.
    /// </summary>
    public int SetInterval(Func<Task> callback, int intervalMs)
    {
        var timer = new ScriptTimer
        {
            Id = Interlocked.Increment(ref _nextTimerId),
            Callback = callback,
            Interval = intervalMs,
            IsRepeating = true,
            NextFireTime = DateTime.UtcNow.AddMilliseconds(intervalMs)
        };
        
        lock (_timerLock)
        {
            _timers.Add(timer);
        }
        
        _ = RunTimerAsync(timer);
        return timer.Id;
    }
    
    /// <summary>
    /// Cancels a timer.
    /// </summary>
    public bool ClearTimer(int timerId)
    {
        lock (_timerLock)
        {
            var timer = _timers.FirstOrDefault(t => t.Id == timerId);
            if (timer != null)
            {
                timer.IsCancelled = true;
                _timers.Remove(timer);
                return true;
            }
        }
        return false;
    }
    
    private async Task RunTimerAsync(ScriptTimer timer)
    {
        while (!timer.IsCancelled)
        {
            var delay = timer.NextFireTime - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }
            
            if (timer.IsCancelled) break;
            
            try
            {
                await timer.Callback();
            }
            catch (Exception ex)
            {
                RaiseError("Timer", $"Timer {timer.Id} error: {ex.Message}");
            }
            
            if (!timer.IsRepeating)
            {
                lock (_timerLock)
                {
                    _timers.Remove(timer);
                }
                break;
            }
            
            timer.NextFireTime = DateTime.UtcNow.AddMilliseconds(timer.Interval);
        }
    }
    
    #endregion
    
    #region Output
    
    /// <summary>
    /// Outputs a message to the script console/log.
    /// </summary>
    public void Print(string message)
    {
        Output?.Invoke(this, new ScriptOutputEventArgs(message));
    }
    
    /// <summary>
    /// Raises an error from a script.
    /// </summary>
    public void RaiseError(string source, string message)
    {
        Error?.Invoke(this, new ScriptErrorEventArgs(source, message));
    }
    
    #endregion
    
    private class ScriptTimer
    {
        public int Id { get; init; }
        public Func<Task> Callback { get; init; } = null!;
        public int Interval { get; init; }
        public bool IsRepeating { get; init; }
        public DateTime NextFireTime { get; set; }
        public bool IsCancelled { get; set; }
    }
}

public class ScriptOutputEventArgs : EventArgs
{
    public string Source { get; }
    public string Message { get; }
    public ScriptOutputEventArgs(string message) : this("Script", message) { }
    public ScriptOutputEventArgs(string source, string message)
    {
        Source = source;
        Message = message;
    }
}

public class ScriptErrorEventArgs : EventArgs
{
    public string Source { get; }
    public string Message { get; }
    public ScriptErrorEventArgs(string source, string message)
    {
        Source = source;
        Message = message;
    }
}
