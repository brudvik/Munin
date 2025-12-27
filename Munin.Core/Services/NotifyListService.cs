using Serilog;
using System.Collections.Concurrent;

namespace Munin.Core.Services;

/// <summary>
/// Manages the notify list for tracking when users come online/offline.
/// </summary>
/// <remarks>
/// <para>Uses ISON polling to check user status on servers that don't support WATCH/MONITOR.</para>
/// <para>For servers with away-notify capability, uses real-time updates instead.</para>
/// </remarks>
public class NotifyListService
{
    private static readonly Lazy<NotifyListService> _instance = new(() => new NotifyListService());
    
    /// <summary>
    /// Gets the singleton instance of the notify list service.
    /// </summary>
    public static NotifyListService Instance => _instance.Value;
    
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _notifyLists = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _onlineUsers = new();
    
    /// <summary>
    /// Raised when a user comes online.
    /// </summary>
    public event EventHandler<NotifyEventArgs>? UserOnline;
    
    /// <summary>
    /// Raised when a user goes offline.
    /// </summary>
    public event EventHandler<NotifyEventArgs>? UserOffline;
    
    private NotifyListService()
    {
        _logger = SerilogConfig.ForContext<NotifyListService>();
    }
    
    /// <summary>
    /// Gets the notify list for a specific server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <returns>Set of nicknames on the notify list.</returns>
    public IReadOnlySet<string> GetNotifyList(string serverName)
    {
        return _notifyLists.GetOrAdd(serverName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Adds a user to the notify list.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="nickname">The nickname to add.</param>
    /// <returns>True if added, false if already exists.</returns>
    public bool AddToNotifyList(string serverName, string nickname)
    {
        var list = _notifyLists.GetOrAdd(serverName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var added = list.Add(nickname);
        
        if (added)
        {
            _logger.Information("Added {Nickname} to notify list for {Server}", nickname, serverName);
        }
        
        return added;
    }
    
    /// <summary>
    /// Removes a user from the notify list.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="nickname">The nickname to remove.</param>
    /// <returns>True if removed, false if not found.</returns>
    public bool RemoveFromNotifyList(string serverName, string nickname)
    {
        if (_notifyLists.TryGetValue(serverName, out var list))
        {
            var removed = list.Remove(nickname);
            if (removed)
            {
                _logger.Information("Removed {Nickname} from notify list for {Server}", nickname, serverName);
            }
            return removed;
        }
        return false;
    }
    
    /// <summary>
    /// Checks if a user is on the notify list.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="nickname">The nickname to check.</param>
    /// <returns>True if on notify list.</returns>
    public bool IsOnNotifyList(string serverName, string nickname)
    {
        return _notifyLists.TryGetValue(serverName, out var list) && list.Contains(nickname);
    }
    
    /// <summary>
    /// Updates online status for users from ISON response.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="onlineNicknames">Nicknames that are currently online.</param>
    public void UpdateOnlineStatus(string serverName, IEnumerable<string> onlineNicknames)
    {
        var currentOnline = _onlineUsers.GetOrAdd(serverName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var notifyList = GetNotifyList(serverName);
        var newOnline = new HashSet<string>(onlineNicknames, StringComparer.OrdinalIgnoreCase);
        
        // Check for users who just came online
        foreach (var nick in newOnline)
        {
            if (notifyList.Contains(nick) && !currentOnline.Contains(nick))
            {
                _logger.Information("Notify: {Nickname} is now online on {Server}", nick, serverName);
                UserOnline?.Invoke(this, new NotifyEventArgs(serverName, nick));
            }
        }
        
        // Check for users who just went offline
        foreach (var nick in currentOnline)
        {
            if (notifyList.Contains(nick) && !newOnline.Contains(nick))
            {
                _logger.Information("Notify: {Nickname} is now offline on {Server}", nick, serverName);
                UserOffline?.Invoke(this, new NotifyEventArgs(serverName, nick));
            }
        }
        
        // Update the online set
        _onlineUsers[serverName] = newOnline;
    }
    
    /// <summary>
    /// Handles a user quitting - marks them as offline.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="nickname">The nickname that quit.</param>
    public void HandleUserQuit(string serverName, string nickname)
    {
        if (_onlineUsers.TryGetValue(serverName, out var online) && online.Remove(nickname))
        {
            if (IsOnNotifyList(serverName, nickname))
            {
                _logger.Information("Notify: {Nickname} is now offline (quit) on {Server}", nickname, serverName);
                UserOffline?.Invoke(this, new NotifyEventArgs(serverName, nickname));
            }
        }
    }
    
    /// <summary>
    /// Handles a user joining - marks them as online.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="nickname">The nickname that joined.</param>
    public void HandleUserJoin(string serverName, string nickname)
    {
        var online = _onlineUsers.GetOrAdd(serverName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        
        if (online.Add(nickname) && IsOnNotifyList(serverName, nickname))
        {
            _logger.Information("Notify: {Nickname} is now online (join) on {Server}", nickname, serverName);
            UserOnline?.Invoke(this, new NotifyEventArgs(serverName, nickname));
        }
    }
    
    /// <summary>
    /// Gets the ISON command string for checking notify list status.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <returns>ISON command with nicknames, or null if list is empty.</returns>
    public string? GetIsonCommand(string serverName)
    {
        var list = GetNotifyList(serverName);
        if (list.Count == 0) return null;
        
        return $"ISON {string.Join(" ", list)}";
    }
    
    /// <summary>
    /// Clears the notify list and online status for a server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    public void ClearServer(string serverName)
    {
        _notifyLists.TryRemove(serverName, out _);
        _onlineUsers.TryRemove(serverName, out _);
    }
    
    /// <summary>
    /// Saves the notify lists to configuration.
    /// </summary>
    /// <returns>Dictionary of server names to nickname lists.</returns>
    public Dictionary<string, List<string>> ExportNotifyLists()
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var (server, list) in _notifyLists)
        {
            result[server] = list.ToList();
        }
        return result;
    }
    
    /// <summary>
    /// Loads notify lists from configuration.
    /// </summary>
    /// <param name="data">Dictionary of server names to nickname lists.</param>
    public void ImportNotifyLists(Dictionary<string, List<string>>? data)
    {
        if (data == null) return;
        
        foreach (var (server, nicknames) in data)
        {
            var list = _notifyLists.GetOrAdd(server, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var nick in nicknames)
            {
                list.Add(nick);
            }
        }
    }
}

/// <summary>
/// Event arguments for notify list events.
/// </summary>
public class NotifyEventArgs : EventArgs
{
    /// <summary>
    /// Gets the server name.
    /// </summary>
    public string ServerName { get; }
    
    /// <summary>
    /// Gets the nickname.
    /// </summary>
    public string Nickname { get; }
    
    /// <summary>
    /// Initializes a new instance of the NotifyEventArgs class.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="nickname">The nickname.</param>
    public NotifyEventArgs(string serverName, string nickname)
    {
        ServerName = serverName;
        Nickname = nickname;
    }
}
