using Serilog;

namespace IrcClient.Core.Services;

/// <summary>
/// Manages auto-perform commands that run on connection events.
/// </summary>
/// <remarks>
/// <para>Auto-perform commands are executed automatically when:</para>
/// <list type="bullet">
///   <item><description>Connecting to any server (global commands)</description></item>
///   <item><description>Connecting to a specific server (server-specific commands)</description></item>
///   <item><description>Joining a specific channel (channel-specific commands)</description></item>
/// </list>
/// <para>Common uses: NickServ identification, channel mode changes, joining hidden channels.</para>
/// </remarks>
public class AutoPerformService
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, List<string>> _serverCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, List<string>>> _channelCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _globalCommands = new();

    public AutoPerformService()
    {
        _logger = SerilogConfig.ForContext<AutoPerformService>();
    }

    /// <summary>
    /// Commands to run on any server connection.
    /// </summary>
    public IReadOnlyList<string> GlobalCommands => _globalCommands;

    /// <summary>
    /// Adds a global command to run on all server connections.
    /// </summary>
    public void AddGlobalCommand(string command)
    {
        if (!_globalCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
            _globalCommands.Add(command);
    }

    /// <summary>
    /// Removes a global command.
    /// </summary>
    public bool RemoveGlobalCommand(string command)
    {
        return _globalCommands.Remove(command);
    }

    /// <summary>
    /// Adds a command to run when connecting to a specific server.
    /// </summary>
    public void AddServerCommand(string serverId, string command)
    {
        if (!_serverCommands.TryGetValue(serverId, out var commands))
        {
            commands = new List<string>();
            _serverCommands[serverId] = commands;
        }
        if (!commands.Contains(command, StringComparer.OrdinalIgnoreCase))
            commands.Add(command);
    }

    /// <summary>
    /// Removes a server-specific command.
    /// </summary>
    public bool RemoveServerCommand(string serverId, string command)
    {
        if (_serverCommands.TryGetValue(serverId, out var commands))
            return commands.Remove(command);
        return false;
    }

    /// <summary>
    /// Gets commands for a specific server.
    /// </summary>
    public IReadOnlyList<string> GetServerCommands(string serverId)
    {
        if (_serverCommands.TryGetValue(serverId, out var commands))
            return commands;
        return Array.Empty<string>();
    }

    /// <summary>
    /// Adds a command to run when joining a specific channel.
    /// </summary>
    public void AddChannelCommand(string serverId, string channelName, string command)
    {
        if (!_channelCommands.TryGetValue(serverId, out var serverChannels))
        {
            serverChannels = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _channelCommands[serverId] = serverChannels;
        }
        if (!serverChannels.TryGetValue(channelName, out var commands))
        {
            commands = new List<string>();
            serverChannels[channelName] = commands;
        }
        if (!commands.Contains(command, StringComparer.OrdinalIgnoreCase))
            commands.Add(command);
    }

    /// <summary>
    /// Removes a channel-specific command.
    /// </summary>
    public bool RemoveChannelCommand(string serverId, string channelName, string command)
    {
        if (_channelCommands.TryGetValue(serverId, out var serverChannels) &&
            serverChannels.TryGetValue(channelName, out var commands))
        {
            return commands.Remove(command);
        }
        return false;
    }

    /// <summary>
    /// Gets commands for a specific channel.
    /// </summary>
    public IReadOnlyList<string> GetChannelCommands(string serverId, string channelName)
    {
        if (_channelCommands.TryGetValue(serverId, out var serverChannels) &&
            serverChannels.TryGetValue(channelName, out var commands))
        {
            return commands;
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// Gets all commands to run on server connection.
    /// </summary>
    public IEnumerable<string> GetConnectionCommands(string serverId)
    {
        // Global commands first, then server-specific
        foreach (var cmd in _globalCommands)
            yield return cmd;
        
        if (_serverCommands.TryGetValue(serverId, out var commands))
        {
            foreach (var cmd in commands)
                yield return cmd;
        }
    }

    /// <summary>
    /// Gets all commands to run on channel join.
    /// </summary>
    public IEnumerable<string> GetJoinCommands(string serverId, string channelName)
    {
        if (_channelCommands.TryGetValue(serverId, out var serverChannels) &&
            serverChannels.TryGetValue(channelName, out var commands))
        {
            foreach (var cmd in commands)
                yield return cmd;
        }
    }

    /// <summary>
    /// Clears all commands.
    /// </summary>
    public void Clear()
    {
        _globalCommands.Clear();
        _serverCommands.Clear();
        _channelCommands.Clear();
    }

    /// <summary>
    /// Serializes to a dictionary for saving.
    /// </summary>
    public Dictionary<string, object> Serialize()
    {
        return new Dictionary<string, object>
        {
            ["global"] = _globalCommands.ToList(),
            ["servers"] = _serverCommands.ToDictionary(x => x.Key, x => x.Value.ToList()),
            ["channels"] = _channelCommands.ToDictionary(
                x => x.Key, 
                x => x.Value.ToDictionary(y => y.Key, y => y.Value.ToList()))
        };
    }

    /// <summary>
    /// Deserializes from a dictionary.
    /// </summary>
    public void Deserialize(Dictionary<string, object>? data)
    {
        if (data == null) return;

        if (data.TryGetValue("global", out var globalObj) && globalObj is List<object> globalList)
        {
            _globalCommands.Clear();
            _globalCommands.AddRange(globalList.Cast<string>());
        }

        if (data.TryGetValue("servers", out var serversObj) && serversObj is Dictionary<string, object> servers)
        {
            _serverCommands.Clear();
            foreach (var (serverId, cmdsObj) in servers)
            {
                if (cmdsObj is List<object> cmds)
                    _serverCommands[serverId] = cmds.Cast<string>().ToList();
            }
        }

        if (data.TryGetValue("channels", out var channelsObj) && channelsObj is Dictionary<string, object> channels)
        {
            _channelCommands.Clear();
            foreach (var (serverId, serverChannelsObj) in channels)
            {
                if (serverChannelsObj is Dictionary<string, object> serverChannels)
                {
                    var channelDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (channel, cmdsObj) in serverChannels)
                    {
                        if (cmdsObj is List<object> cmds)
                            channelDict[channel] = cmds.Cast<string>().ToList();
                    }
                    _channelCommands[serverId] = channelDict;
                }
            }
        }
    }
}
