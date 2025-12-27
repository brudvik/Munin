using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Munin.Core.Models;
using Munin.Core.Scripting;
using Munin.Core.Scripting.Lua;
using Munin.Core.Scripting.Plugins;
using Munin.Core.Scripting.Triggers;
using Munin.Core.Services;
using Munin.UI.Resources;
using Munin.UI.Services;
using Munin.UI.Views;
using Serilog;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace Munin.UI.ViewModels;

/// <summary>
/// The main ViewModel for the Munin application.
/// Manages server connections, channel navigation, message handling, and UI state.
/// </summary>
/// <remarks>
/// <para>Responsibilities:</para>
/// <list type="bullet">
///   <item><description>Server connection management (connect, disconnect, add, remove)</description></item>
///   <item><description>Channel and message handling</description></item>
///   <item><description>Command processing and alias expansion</description></item>
///   <item><description>Message search and history</description></item>
///   <item><description>Configuration persistence</description></item>
/// </list>
/// </remarks>
public partial class MainViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly IrcClientManager _clientManager;
    private readonly ConfigurationService _configService;
    private readonly DispatcherTimer _latencyTimer;
    private readonly ScriptManager _scriptManager;
    private readonly FishCryptService _fishCrypt;
    private ScriptConsoleWindow? _scriptConsoleWindow;
    private ScriptManagerWindow? _scriptManagerWindow;

    [ObservableProperty]
    private ObservableCollection<ServerViewModel> _servers = new();

    [ObservableProperty]
    private ServerViewModel? _selectedServer;
    
    /// <summary>
    /// Updates IsSelected on all servers when the selected server changes.
    /// </summary>
    partial void OnSelectedServerChanged(ServerViewModel? oldValue, ServerViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.IsSelected = false;
        if (newValue != null)
            newValue.IsSelected = true;
    }

    [ObservableProperty]
    private ChannelViewModel? _selectedChannel;

    [ObservableProperty]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _searchResultCount;

    [ObservableProperty]
    private ObservableCollection<string> _searchResults = new();

    [ObservableProperty]
    private bool _isUserListVisible = true;

    /// <summary>
    /// Returns true if no servers are configured, used to show welcome screen.
    /// </summary>
    public bool HasNoServers => Servers.Count == 0;

    private readonly HashSet<string> _loadedHistoryChannels = new();
    private RawIrcLogWindow? _rawIrcLogWindow;

    partial void OnServersChanged(ObservableCollection<ServerViewModel> value)
    {
        OnPropertyChanged(nameof(HasNoServers));
    }

    partial void OnSelectedChannelChanged(ChannelViewModel? oldValue, ChannelViewModel? newValue)
    {
        _logger.Information("OnSelectedChannelChanged: value={Name}", newValue?.Channel?.Name ?? "null");
        
        // Deselect old channel
        if (oldValue != null)
        {
            oldValue.IsSelected = false;
        }
        
        if (newValue != null && SelectedServer != null)
        {
            // Mark new channel as selected
            newValue.IsSelected = true;
            
            // Clear unread when selecting a channel
            newValue.UnreadCount = 0;
            newValue.HasMention = false;
            
            _logger.Information("OnSelectedChannelChanged: About to load channel history");
            // Load history if not already loaded
            LoadChannelHistory(newValue);
            _logger.Information("OnSelectedChannelChanged: Channel history loaded");
        }
    }

    private void LoadChannelHistory(ChannelViewModel channel)
    {
        _logger.Information("LoadChannelHistory: Starting for {Name}", channel.Channel?.Name ?? "null");
        if (SelectedServer == null || channel.Channel == null) return;
        
        var historyKey = $"{SelectedServer.Server.Name}|{channel.Channel.Name}";
        if (_loadedHistoryChannels.Contains(historyKey))
        {
            _logger.Information("LoadChannelHistory: Already loaded, skipping");
            return;
        }
        
        _loadedHistoryChannels.Add(historyKey);
        
        _logger.Information("LoadChannelHistory: Reading last lines from log");
        // Load messages from log based on configuration
        var linesToLoad = _configService.Configuration.Settings.HistoryLinesToLoad;
        var historyLines = LoggingService.Instance.ReadLastLines(
            SelectedServer.Server.Name,
            channel.Channel.Name,
            linesToLoad);

        _logger.Information("LoadChannelHistory: Got history lines, parsing");
        var historyMessages = new List<MessageViewModel>();
        foreach (var line in historyLines)
        {
            var message = ParseLogLine(line);
            if (message != null)
            {
                historyMessages.Add(new MessageViewModel(message));
            }
        }

        _logger.Information("LoadChannelHistory: Parsed {Count} messages, inserting", historyMessages.Count);
        // Insert at the beginning
        if (historyMessages.Count > 0)
        {
            // Find the oldest message's timestamp to show "last updated" info
            var oldestMessage = historyMessages.FirstOrDefault();
            var newestMessage = historyMessages.LastOrDefault();
            
            for (int i = historyMessages.Count - 1; i >= 0; i--)
            {
                channel.Messages.Insert(0, historyMessages[i]);
            }
            
            // Add a separator message showing when this history is from
            if (oldestMessage?.Message?.Timestamp != null && newestMessage?.Message?.Timestamp != null)
            {
                var separatorMessage = new IrcMessage
                {
                    Type = MessageType.System,
                    Content = $"--- History loaded: {historyMessages.Count} messages from {oldestMessage.Message.Timestamp:g} to {newestMessage.Message.Timestamp:g} ---",
                    Timestamp = DateTime.Now
                };
                channel.Messages.Add(new MessageViewModel(separatorMessage));
            }
        }
        _logger.Information("LoadChannelHistory: Complete");
    }

    private IrcMessage? ParseLogLine(string line)
    {
        // Log format: [HH:mm:ss] <nickname> message
        // or: [HH:mm:ss] * nickname action
        // or: [HH:mm:ss] *** system message
        try
        {
            if (line.Length < 11) return null;
            
            var timeStr = line[1..9]; // HH:mm:ss
            if (!TimeSpan.TryParse(timeStr, out var time)) return null;
            
            var rest = line[11..]; // Skip "[HH:mm:ss] "
            
            if (rest.StartsWith("<"))
            {
                var endNick = rest.IndexOf('>');
                if (endNick > 1)
                {
                    var nick = rest[1..endNick];
                    var content = rest[(endNick + 2)..];
                    return new IrcMessage
                    {
                        Type = MessageType.Normal,
                        Content = content,
                        Source = nick,
                        Timestamp = DateTime.Today.Add(time)
                    };
                }
            }
            else if (rest.StartsWith("* "))
            {
                var parts = rest[2..].Split(' ', 2);
                if (parts.Length == 2)
                {
                    return new IrcMessage
                    {
                        Type = MessageType.Action,
                        Content = parts[1],
                        Source = parts[0],
                        Timestamp = DateTime.Today.Add(time)
                    };
                }
            }
            else if (rest.StartsWith("***"))
            {
                return new IrcMessage
                {
                    Type = MessageType.System,
                    Content = rest[4..],
                    Timestamp = DateTime.Today.Add(time)
                };
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse log line: {Line}", line);
            return null;
        }
    }

    public MainViewModel()
    {
        _logger = SerilogConfig.ForContext<MainViewModel>();
        _clientManager = new IrcClientManager();
        
        // Get storage from App if available
        var storage = (System.Windows.Application.Current as App)?.Storage;
        _configService = storage != null 
            ? new ConfigurationService(storage) 
            : new ConfigurationService();
        
        // Initialize scripting system
        var scriptsDir = Path.Combine(
            PortableMode.IsPortable 
                ? Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            PortableMode.IsPortable ? "scripts" : "Munin\\scripts");
        var scriptContext = new ScriptContext(_clientManager, scriptsDir);
        _scriptManager = new ScriptManager(scriptContext);
        
        // Initialize FiSH encryption
        _fishCrypt = new FishCryptService();
        _fishCrypt.KeyChanged += async (s, e) =>
        {
            var status = e.HasKey ? "Key set for" : "Key removed from";
            _logger.Information("FiSH: {Status} {Target}", status, e.Target);
            
            // Save keys to configuration
            _configService.Configuration.FishKeys = _fishCrypt.GetAllKeys();
            await _configService.SaveAsync();
            
            // If a key was set, try to decrypt the topic for that channel
            if (e.HasKey)
            {
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    var server = Servers.FirstOrDefault(srv => srv.Server.Id == e.ServerId);
                    var channel = server?.Channels.FirstOrDefault(ch => 
                        ch.Channel.Name.Equals(e.Target, StringComparison.OrdinalIgnoreCase));
                    
                    if (channel?.Topic != null && FishCryptService.IsEncrypted(channel.Topic))
                    {
                        var decrypted = _fishCrypt.Decrypt(e.ServerId, e.Target, channel.Topic);
                        if (decrypted != null)
                        {
                            channel.Topic = $"🔐 {decrypted}";
                            channel.Channel.Topic = channel.Topic;
                            _logger.Debug("FiSH: Decrypted topic for {Channel}", e.Target);
                        }
                    }
                });
            }
        };
        
        // Register script engines
        _scriptManager.RegisterEngine(new LuaScriptEngine());
        _scriptManager.RegisterEngine(new TriggerEngine());
        _scriptManager.RegisterEngine(new PluginEngine());
        
        // Handle script output
        _scriptManager.ScriptOutput += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _scriptConsoleWindow?.AddOutput("Script", e.Message);
            });
        };
        
        _scriptManager.ScriptError += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _scriptConsoleWindow?.AddError(e.Source, e.Message);
                _logger.Warning("Script error in {Source}: {Error}", e.Source, e.Message);
            });
        };
        
        _scriptManager.ScriptLoaded += (s, e) =>
        {
            _logger.Information("Script loaded: {ScriptName} from {FilePath}", e.ScriptName, e.FilePath);
        };
        
        _logger.Information("MainViewModel initialized");
        
        // Subscribe to collection changes to update HasNoServers
        Servers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoServers));
        
        // Setup latency measurement timer (every 30 seconds)
        _latencyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _latencyTimer.Tick += async (s, e) =>
        {
            foreach (var server in Servers.Where(s => s.IsConnected && s.Connection != null))
            {
                await server.Connection!.MeasureLatencyAsync();
            }
        };
        _latencyTimer.Start();
        
        SetupEventHandlers();
        _ = LoadConfigurationAsync();
        _ = LoadScriptsAsync();
    }

    /// <summary>
    /// Loads all scripts from the scripts directory.
    /// </summary>
    private async Task LoadScriptsAsync()
    {
        try
        {
            _logger.Information("Loading scripts from: {ScriptsDir}", _scriptManager.Context.ScriptsDirectory);
            
            await _scriptManager.LoadAllScriptsAsync();
            var scripts = _scriptManager.GetLoadedScripts().ToList();
            
            if (scripts.Count > 0)
            {
                _logger.Information("Loaded {Count} scripts:", scripts.Count);
                foreach (var script in scripts)
                {
                    _logger.Information("  - {Name} ({Engine})", script.Name, script.Engine.Name);
                }
                
                // Show in status bar
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = $"Loaded {scripts.Count} script(s)";
                });
            }
            else
            {
                _logger.Information("No scripts found in {ScriptsDir}", _scriptManager.Context.ScriptsDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load scripts");
        }
    }

    private async Task LoadConfigurationAsync()
    {
        _logger.Information("LoadConfigurationAsync: Starting");
        await _configService.LoadAsync();
        _logger.Information("LoadConfigurationAsync: Configuration loaded");
        
        // Load FiSH keys from configuration
        var fishKeys = _configService.Configuration.FishKeys;
        if (fishKeys != null && fishKeys.Count > 0)
        {
            _fishCrypt.LoadKeys(fishKeys);
            _logger.Information("LoadConfigurationAsync: Loaded {Count} FiSH key(s)", fishKeys.Count);
        }
        
        // Must run UI operations on the dispatcher thread
        await App.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var server in _configService.GetAllServers())
            {
                _logger.Information("LoadConfigurationAsync: Creating VM for {Name}", server.Name);
                var serverVm = CreateServerViewModel(server);
                _logger.Information("LoadConfigurationAsync: Adding server to collection");
                Servers.Add(serverVm);
                _logger.Information("LoadConfigurationAsync: Server added to Servers collection");
                
                // Auto-connect if configured
                if (server.AutoConnect)
                {
                    _logger.Information("LoadConfigurationAsync: Auto-connecting to {Name}", server.Name);
                    _ = ConnectToServerAsync(serverVm);
                }
            }
            
            _logger.Information("LoadConfigurationAsync: Setting SelectedServer");
            if (Servers.Count > 0)
            {
                SelectedServer = Servers[0];
                _logger.Information("LoadConfigurationAsync: SelectedServer set, getting FirstOrDefault channel");
                var firstChannel = SelectedServer.Channels.FirstOrDefault();
                _logger.Information("LoadConfigurationAsync: Got first channel: {Name}, Count: {Count}", 
                    firstChannel?.Channel?.Name ?? "null", SelectedServer.Channels.Count);
                SelectedChannel = firstChannel;
                _logger.Information("LoadConfigurationAsync: SelectedChannel set");
                StatusText = $"Loaded {Servers.Count} server(s)";
            }
            _logger.Information("LoadConfigurationAsync: Complete");
        });
    }

    private ServerViewModel CreateServerViewModel(IrcServer server)
    {
        var serverVm = new ServerViewModel(server);
        var connection = _clientManager.AddServer(server);
        serverVm.Connection = connection;
        
        // Apply FiSH encryption service
        connection.FishCrypt = _fishCrypt;
        
        // Apply highlight words from settings
        connection.HighlightWords = _configService.Configuration.Settings.HighlightWords;
        
        // Apply reconnect settings
        connection.AutoReconnect = _configService.Configuration.Settings.ReconnectOnDisconnect;
        connection.ReconnectDelaySeconds = _configService.Configuration.Settings.ReconnectDelaySeconds;
        
        // Update notification settings
        NotificationService.Instance.EnableSoundNotifications = _configService.Configuration.Settings.EnableSoundNotifications;
        NotificationService.Instance.EnableToastNotifications = _configService.Configuration.Settings.EnableFlashNotifications;
        NotificationService.Instance.OnlyWhenMinimized = _configService.Configuration.Settings.OnlyNotifyWhenInactive;
        
        // Create server console immediately
        var consoleChannel = new IrcChannel
        {
            Name = "(Server)",
            IsJoined = true
        };
        var console = new ChannelViewModel(consoleChannel, serverVm) { IsServerConsole = true };
        serverVm.Channels.Add(console);
        
        return serverVm;
    }

    private async Task SaveConfigurationAsync()
    {
        await _configService.SaveAsync();
    }

    private void SetupEventHandlers()
    {
        _clientManager.ServerConnected += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    server.IsConnecting = false;
                    server.IsConnected = true;
                    IsConnecting = false;
                    StatusText = $"Connected to {e.Server.Name}";
                    
                    // Update the current nickname for message coloring
                    MessageViewModel.CurrentNickname = server.Connection?.CurrentNickname;
                    
                    // Dispatch to scripts
                    _ = _scriptManager.DispatchEventAsync(new ConnectEvent(e.Server.Name));
                }
            });
        };

        _clientManager.ServerDisconnected += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    server.IsConnecting = false;
                    server.IsConnected = false;
                    IsConnecting = false;
                    StatusText = $"Disconnected from {e.Server.Name}";
                    
                    // Dispatch to scripts
                    _ = _scriptManager.DispatchEventAsync(new DisconnectEvent(e.Server.Name, "Disconnected", false));
                }
            });
        };
        
        _clientManager.Reconnecting += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    StatusText = $"Reconnecting to {e.Server.Name} (attempt {e.Attempt}/{e.MaxAttempts})...";
                    
                    // Add system message to server console
                    var console = server.Channels.FirstOrDefault(c => c.IsServerConsole);
                    console?.AddSystemMessage($"Reconnecting in {e.DelaySeconds} seconds (attempt {e.Attempt}/{e.MaxAttempts})...");
                }
            });
        };
        
        _clientManager.LatencyUpdated += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    server.LatencyMs = e.LatencyMs;
                }
            });
        };

        _clientManager.ChannelJoined += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    var existing = server.Channels.FirstOrDefault(c => c.Channel.Name == e.Channel.Name);
                    if (existing == null)
                    {
                        var channelVm = new ChannelViewModel(e.Channel, server);
                        server.Channels.Add(channelVm);
                        
                        // Always select the newly joined channel
                        SelectedChannel = channelVm;
                        
                        // Save channel to auto-join list
                        _configService.AddChannelToServer(e.Server.Id, e.Channel.Name);
                        _ = SaveConfigurationAsync();
                    }
                    else
                    {
                        // Channel already exists, just select it
                        SelectedChannel = existing;
                    }
                }
            });
        };

        _clientManager.ChannelParted += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    // Find the channel to remove
                    var channelVm = server.Channels.FirstOrDefault(c => c.Channel?.Name == e.Channel.Name);
                    if (channelVm != null)
                    {
                        // If we were viewing this channel, switch to another before removing
                        if (SelectedChannel == channelVm)
                        {
                            var channelIndex = server.Channels.IndexOf(channelVm);
                            
                            // Try to select the previous channel, or the next one, or the server console
                            if (channelIndex > 0)
                            {
                                // Select the channel before this one
                                SelectedChannel = server.Channels[channelIndex - 1];
                            }
                            else if (server.Channels.Count > 1)
                            {
                                // Select the next channel (index 1 after removal will be at index 0)
                                SelectedChannel = server.Channels.FirstOrDefault(c => c != channelVm);
                            }
                            else
                            {
                                // No more channels, select server console
                                SelectedChannel = server.Channels.FirstOrDefault(c => c.IsServerConsole);
                            }
                        }
                        
                        // Remove channel from UI
                        server.Channels.Remove(channelVm);
                    }
                    
                    // Remove channel from auto-join list
                    _configService.RemoveChannelFromServer(e.Server.Id, e.Channel.Name);
                    _ = SaveConfigurationAsync();
                }
            });
        };

        _clientManager.ChannelMessage += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                // Skip messages from ignored users
                if (!string.IsNullOrEmpty(e.Message.Source) && IsUserIgnored(e.Message.Source))
                    return;

                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                var channel = server?.Channels.FirstOrDefault(c => c.Channel.Name == e.Channel.Name);
                if (channel != null)
                {
                    channel.AddMessage(new MessageViewModel(e.Message));
                    
                    // Log the message
                    LoggingService.Instance.LogMessage(e.Server.Name, e.Channel.Name, e.Message);
                    
                    if (channel != SelectedChannel)
                    {
                        channel.UnreadCount++;
                        if (e.Message.IsHighlight)
                        {
                            channel.HasMention = true;
                            // Show notification for mention
                            NotificationService.Instance.NotifyMention(
                                e.Server.Name,
                                e.Channel.Name,
                                e.Message.Source ?? "Unknown",
                                e.Message.Content);
                        }
                    }
                    
                    // Dispatch to scripts
                    var scriptEvent = new MessageEvent(
                        e.Server.Name,
                        e.Channel.Name,
                        e.Message.Source ?? "",
                        e.Message.Content,
                        e.Message.Type == MessageType.Action);
                    _ = _scriptManager.DispatchEventAsync(scriptEvent);
                }
            });
        };

        _clientManager.UserListUpdated += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                var channel = server?.Channels.FirstOrDefault(c => c.Channel.Name == e.Channel.Name);
                channel?.RefreshUsers();
            });
        };

        _clientManager.UserJoined += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                var channel = server?.Channels.FirstOrDefault(c => c.Channel?.Name == e.Channel?.Name);
                if (channel != null)
                {
                    channel.RefreshUsers();
                    // Add join message to channel
                    var joinMsg = IrcMessage.CreateJoin(e.User.Nickname, e.Channel?.Name ?? "");
                    channel.AddMessage(new MessageViewModel(joinMsg), trackStats: false);
                    
                    // Dispatch to scripts
                    var scriptEvent = new JoinEvent(
                        e.Server.Name,
                        e.Channel?.Name ?? "",
                        e.User.Nickname,
                        e.User.Username,
                        e.User.Hostname);
                    _ = _scriptManager.DispatchEventAsync(scriptEvent);
                }
            });
        };

        _clientManager.UserParted += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                var channel = server?.Channels.FirstOrDefault(c => c.Channel?.Name == e.Channel?.Name);
                if (channel != null)
                {
                    channel.RefreshUsers();
                    // Add part message to channel
                    var partMsg = IrcMessage.CreatePart(e.User.Nickname, e.Channel?.Name ?? "");
                    channel.AddMessage(new MessageViewModel(partMsg), trackStats: false);
                    
                    // Dispatch to scripts
                    var scriptEvent = new PartEvent(
                        e.Server.Name,
                        e.Channel?.Name ?? "",
                        e.User.Nickname,
                        null);
                    _ = _scriptManager.DispatchEventAsync(scriptEvent);
                }
            });
        };

        _clientManager.UserQuit += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                // Add quit message to all channels where this user was present
                var channelNames = new List<string>();
                foreach (var server in Servers.Where(sv => sv.Server.Id == e.Server.Id))
                {
                    foreach (var channel in server.Channels.Where(c => !c.IsServerConsole && !c.IsPrivateMessage))
                    {
                        // Check if user was in this channel (they should still be in the list before refresh)
                        if (channel.Users.Any(u => u.User.Nickname.Equals(e.User.Nickname, StringComparison.OrdinalIgnoreCase)))
                        {
                            var quitMsg = IrcMessage.CreateQuit(e.User.Nickname);
                            channel.AddMessage(new MessageViewModel(quitMsg), trackStats: false);
                            channelNames.Add(channel.Channel.Name);
                        }
                        channel.RefreshUsers();
                    }
                    
                    // Dispatch to scripts
                    var scriptEvent = new QuitEvent(e.Server.Name, e.User.Nickname, null, channelNames);
                    _ = _scriptManager.DispatchEventAsync(scriptEvent);
                }
            });
        };

        _clientManager.NickChanged += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    // Update CurrentNickname if it's our own nick change
                    if (server.Connection?.CurrentNickname == e.NewNick)
                    {
                        MessageViewModel.CurrentNickname = e.NewNick;
                    }
                    
                    // Add nick change message to all channels
                    var nickMsg = IrcMessage.CreateNickChange(e.OldNick, e.NewNick);
                    foreach (var channel in server.Channels.Where(c => !c.IsServerConsole))
                    {
                        channel.AddMessage(new MessageViewModel(nickMsg), trackStats: false);
                        channel.RefreshUsers();
                    }
                    
                    // Dispatch to scripts
                    var scriptEvent = new NickChangeEvent(e.Server.Name, e.OldNick, e.NewNick);
                    _ = _scriptManager.DispatchEventAsync(scriptEvent);
                }
            });
        };

        _clientManager.Error += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                StatusText = $"Error: {e.Message}";
                
                // Also show in server console if available
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                var console = server?.Channels.FirstOrDefault(c => c.IsServerConsole);
                if (console != null)
                {
                    console.AddMessage(new MessageViewModel(IrcMessage.CreateError(e.Message)), trackStats: false);
                }
            });
        };

        _clientManager.ServerMessage += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    // Get server console channel (should always exist)
                    var console = server.Channels.FirstOrDefault(c => c.IsServerConsole);
                    if (console != null)
                    {
                        var message = new IrcMessage
                        {
                            Type = MessageType.System,
                            Content = e.Message
                        };
                        console.AddMessage(new MessageViewModel(message), trackStats: false);
                        
                        if (console != SelectedChannel)
                        {
                            console.UnreadCount++;
                        }
                    }
                }
            });
        };

        _clientManager.PrivateMessage += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                if (server != null)
                {
                    var dmChannel = GetOrCreatePrivateMessageChannel(server, e.From);
                    dmChannel.AddMessage(new MessageViewModel(e.Message));
                    
                    // Log the private message
                    LoggingService.Instance.LogMessage(e.Server.Name, $"PM-{e.From}", e.Message);
                    
                    if (dmChannel != SelectedChannel)
                    {
                        dmChannel.UnreadCount++;
                        dmChannel.HasMention = true;
                        
                        // Show notification for DM
                        NotificationService.Instance.NotifyPrivateMessage(
                            e.Server.Name,
                            e.From,
                            e.Message.Content);
                    }
                    
                    // Dispatch to scripts
                    var scriptEvent = new PrivateMessageEvent(
                        e.Server.Name,
                        e.From,
                        e.Message.Content,
                        e.Message.Type == MessageType.Action);
                    _ = _scriptManager.DispatchEventAsync(scriptEvent);
                }
            });
        };

        _clientManager.RawMessage += (s, e) =>
        {
            _rawIrcLogWindow?.AddEntry(e.IsOutgoing, e.Message);
            
            // Dispatch to scripts (only incoming messages)
            if (!e.IsOutgoing)
            {
                // Parse minimal info from raw message
                var parts = e.Message.Split(' ');
                var prefix = parts.Length > 0 && parts[0].StartsWith(':') ? parts[0][1..] : "";
                var command = parts.Length > 1 ? parts[1] : (parts.Length > 0 ? parts[0] : "");
                var parameters = new List<string>();
                for (int i = 2; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith(':'))
                    {
                        parameters.Add(string.Join(' ', parts.Skip(i))[1..]);
                        break;
                    }
                    parameters.Add(parts[i]);
                }
                
                var scriptEvent = new RawEvent(e.Server.Name, e.Message, command, prefix, parameters);
                _ = _scriptManager.DispatchEventAsync(scriptEvent);
            }
        };
        
        _clientManager.TopicChanged += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.Server.Id);
                var channelVm = server?.Channels.FirstOrDefault(c => 
                    c.Channel.Name.Equals(e.Channel.Name, StringComparison.OrdinalIgnoreCase));
                if (channelVm != null)
                {
                    channelVm.Topic = e.Channel.Topic;
                    
                    // Dispatch to scripts
                    var scriptEvent = new TopicEvent(e.Server.Name, e.Channel.Name, "", e.Channel.Topic ?? "");
                    _ = _scriptManager.DispatchEventAsync(scriptEvent);
                }
            });
        };
        
        _clientManager.Dh1080KeyExchangeComplete += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var server = Servers.FirstOrDefault(sv => sv.Server.Id == e.ServerId);
                if (server != null)
                {
                    var dmChannel = GetOrCreatePrivateMessageChannel(server, e.Nick);
                    var modeText = e.IsCbc ? "CBC" : "ECB";
                    dmChannel.AddSystemMessage($"🔐 DH1080 key exchange complete! Encryption enabled ({modeText} mode).");
                    
                    // Also add to server console
                    var serverConsole = server.Channels.FirstOrDefault();
                    if (serverConsole != null && serverConsole != dmChannel)
                    {
                        serverConsole.AddSystemMessage($"🔐 DH1080 key exchange complete with {e.Nick}. FiSH encryption enabled ({modeText}).");
                    }
                }
            });
        };
    }

    /// <summary>
    /// Finds an existing private message channel or creates a new one.
    /// </summary>
    private ChannelViewModel GetOrCreatePrivateMessageChannel(ServerViewModel server, string nickname)
    {
        // Look for existing PM channel with this user
        var existingDm = server.Channels.FirstOrDefault(c => 
            c.IsPrivateMessage && 
            c.PrivateMessageTarget.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        
        if (existingDm != null)
        {
            return existingDm;
        }

        // Create a new PM channel
        var pmChannel = new IrcChannel
        {
            Name = nickname,
            IsJoined = true
        };
        var dmVm = new ChannelViewModel(pmChannel, server) 
        { 
            IsPrivateMessage = true,
            PrivateMessageTarget = nickname
        };
        
        // Add at the end (after regular channels)
        server.Channels.Add(dmVm);
        
        return dmVm;
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        var dialog = new AddServerDialog
        {
            Owner = App.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Server != null)
        {
            var serverVm = CreateServerViewModel(dialog.Server);
            Servers.Add(serverVm);
            SelectedServer = serverVm;
            SelectedChannel = serverVm.Channels.FirstOrDefault();
            
            // Save server configuration
            _configService.AddServer(dialog.Server);
            await SaveConfigurationAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveServerAsync(ServerViewModel? server)
    {
        if (server == null) return;

        // Disconnect if connected
        if (server.Connection?.IsConnected == true)
        {
            await server.Connection.DisconnectAsync();
        }

        // Remove from client manager
        await _clientManager.RemoveServerAsync(server.Server.Id);

        // Remove from config
        _configService.RemoveServer(server.Server.Id);
        await SaveConfigurationAsync();

        // Remove from UI
        Servers.Remove(server);
        
        if (SelectedServer == server)
        {
            SelectedServer = Servers.FirstOrDefault();
            SelectedChannel = SelectedServer?.Channels.FirstOrDefault();
        }

        StatusText = $"Removed server {server.Server.Name}";
    }

    [RelayCommand]
    private async Task ConnectToServerAsync(ServerViewModel? server)
    {
        if (server?.Connection == null) return;

        try
        {
            IsConnecting = true;
            server.IsConnecting = true;
            StatusText = $"Connecting to {server.Server.Name}...";
            _logger.Information("Connecting to server {ServerName} ({Hostname}:{Port})", 
                server.Server.Name, server.Server.Hostname, server.Server.Port);
            await server.Connection.ConnectAsync();
            // Note: IsConnecting is set to false in ServerConnected event handler
            // This allows the spinner to show until we receive RPL_WELCOME (001)
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to connect to {ServerName}", server.Server.Name);
            StatusText = $"Failed to connect: {ex.Message}";
            IsConnecting = false;
            server.IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectFromServerAsync(ServerViewModel? server)
    {
        if (server?.Connection == null) return;

        // Save current channel list before disconnecting
        var joinedChannels = server.Channels
            .Where(c => !c.IsServerConsole && c.Channel.IsJoined)
            .Select(c => c.Channel.Name)
            .ToList();
        
        _configService.UpdateServerChannels(server.Server.Id, joinedChannels);
        await SaveConfigurationAsync();

        await server.Connection.DisconnectAsync();
        StatusText = $"Disconnected from {server.Server.Name}";
    }

    [RelayCommand]
    private async Task EditServerAsync(ServerViewModel? server)
    {
        if (server == null) return;

        // Must disconnect before editing
        if (server.Connection?.IsConnected == true)
        {
            System.Windows.MessageBox.Show(
                "Please disconnect from the server before editing.", 
                "Cannot Edit", 
                System.Windows.MessageBoxButton.OK, 
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var dialog = new EditServerDialog(server.Server)
        {
            Owner = App.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.HasChanges)
        {
            // Update the display
            server.OnPropertyChanged(nameof(server.DisplayName));
            
            // Save to config
            _configService.AddServer(server.Server);
            await SaveConfigurationAsync();
            
            StatusText = $"Server {server.Server.Name} updated";
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageInput)) return;

        var message = MessageInput;
        MessageInput = string.Empty;

        // Handle commands - some commands work without a connection
        if (message.StartsWith('/'))
        {
            var connection = SelectedChannel?.ServerViewModel.Connection;
            try
            {
                await HandleCommandAsync(message, connection);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling command: {Command}", message);
                StatusText = $"Command error: {ex.Message}";
            }
            return;
        }

        // For regular messages, we need a channel and connection
        if (SelectedChannel == null) return;

        var messageConnection = SelectedChannel.ServerViewModel.Connection;
        if (messageConnection == null || !messageConnection.IsConnected) return;

        // Determine the target - for private messages use the nickname, otherwise the channel name
        var target = SelectedChannel.IsPrivateMessage 
            ? SelectedChannel.PrivateMessageTarget 
            : SelectedChannel.Channel.Name;

        await messageConnection.SendMessageAsync(target, message);

        // Check if message was encrypted
        var wasEncrypted = _fishCrypt?.HasKey(messageConnection.Server.Id, target) ?? false;

        // Add our own message to the channel
        var ircMessage = new IrcMessage
        {
            Type = MessageType.Normal,
            Source = messageConnection.CurrentNickname,
            Target = target,
            Content = message,
            IsEncrypted = wasEncrypted
        };
        SelectedChannel.AddMessage(new MessageViewModel(ircMessage));

        // Log our own message
        var serverName = SelectedChannel.ServerViewModel.Server.Name;
        var logTarget = SelectedChannel.IsPrivateMessage 
            ? $"PM-{SelectedChannel.PrivateMessageTarget}" 
            : target;
        LoggingService.Instance.LogMessage(serverName, logTarget, ircMessage);
    }

    private async Task HandleCommandAsync(string input, IrcConnection? connection)
    {
        var parts = input[1..].Split(' ', 2);
        var command = parts[0].ToUpperInvariant();
        var args = parts.Length > 1 ? parts[1] : "";

        // Commands that don't require a connection
        switch (command)
        {
            case "SCRIPT":
                await HandleScriptCommandAsync(args, connection);
                return;
                
            case "SCRIPTS":
                ShowScriptManager();
                return;
                
            case "STATS":
                ShowChannelStats();
                return;
        }
        
        // Commands that require a connection
        if (connection == null || !connection.IsConnected)
        {
            StatusText = "Not connected to a server";
            return;
        }

        switch (command)
        {
            case "JOIN":
            case "J":
                if (!string.IsNullOrEmpty(args))
                {
                    var channelParts = args.Split(' ', 2);
                    await connection.JoinChannelAsync(channelParts[0], channelParts.Length > 1 ? channelParts[1] : null);
                }
                break;

            case "PART":
            case "LEAVE":
                var channelToPart = string.IsNullOrEmpty(args) ? SelectedChannel?.Channel.Name : args.Split(' ')[0];
                if (!string.IsNullOrEmpty(channelToPart))
                {
                    await connection.PartChannelAsync(channelToPart);
                }
                break;

            case "NICK":
                if (!string.IsNullOrEmpty(args))
                {
                    await connection.SetNicknameAsync(args.Split(' ')[0]);
                }
                break;

            case "MSG":
            case "PRIVMSG":
                var msgParts = args.Split(' ', 2);
                if (msgParts.Length >= 2)
                {
                    var targetNick = msgParts[0];
                    var msgContent = msgParts[1];
                    await connection.SendMessageAsync(targetNick, msgContent);
                    
                    // If target is a user (not a channel), open a DM window
                    if (!targetNick.StartsWith("#") && !targetNick.StartsWith("&"))
                    {
                        var server = Servers.FirstOrDefault(sv => sv.Server.Id == connection.Server.Id);
                        if (server != null)
                        {
                            var dmChannel = GetOrCreatePrivateMessageChannel(server, targetNick);
                            var wasEncrypted = _fishCrypt?.HasKey(connection.Server.Id, targetNick) ?? false;
                            var ircMessage = new IrcMessage
                            {
                                Type = MessageType.Normal,
                                Source = connection.CurrentNickname,
                                Target = targetNick,
                                Content = msgContent,
                                IsEncrypted = wasEncrypted
                            };
                            dmChannel.AddMessage(new MessageViewModel(ircMessage));
                            LoggingService.Instance.LogMessage(server.Server.Name, $"PM-{targetNick}", ircMessage);
                            SelectedChannel = dmChannel;
                        }
                    }
                }
                break;

            case "QUERY":
                // Open a private message window with a user
                if (!string.IsNullOrEmpty(args))
                {
                    var queryParts = args.Split(' ', 2);
                    var targetUser = queryParts[0];
                    var server = Servers.FirstOrDefault(sv => sv.Server.Id == connection.Server.Id);
                    if (server != null)
                    {
                        var dmChannel = GetOrCreatePrivateMessageChannel(server, targetUser);
                        SelectedChannel = dmChannel;
                        
                        // If there's a message, send it
                        if (queryParts.Length >= 2)
                        {
                            var queryMsg = queryParts[1];
                            await connection.SendMessageAsync(targetUser, queryMsg);
                            var wasEncrypted = _fishCrypt?.HasKey(connection.Server.Id, targetUser) ?? false;
                            var ircMessage = new IrcMessage
                            {
                                Type = MessageType.Normal,
                                Source = connection.CurrentNickname,
                                Target = targetUser,
                                Content = queryMsg,
                                IsEncrypted = wasEncrypted
                            };
                            dmChannel.AddMessage(new MessageViewModel(ircMessage));
                            LoggingService.Instance.LogMessage(server.Server.Name, $"PM-{targetUser}", ircMessage);
                        }
                    }
                }
                break;

            case "ME":
                if (!string.IsNullOrEmpty(args) && SelectedChannel != null)
                {
                    var meTarget = SelectedChannel.IsPrivateMessage 
                        ? SelectedChannel.PrivateMessageTarget 
                        : SelectedChannel.Channel.Name;
                    await connection.SendActionAsync(meTarget, args);
                    var actionMsg = IrcMessage.CreateAction(connection.CurrentNickname, args);
                    SelectedChannel.AddMessage(new MessageViewModel(actionMsg));
                    
                    // Log the action
                    var serverName = SelectedChannel.ServerViewModel.Server.Name;
                    var logTarget = SelectedChannel.IsPrivateMessage 
                        ? $"PM-{SelectedChannel.PrivateMessageTarget}" 
                        : meTarget;
                    LoggingService.Instance.LogMessage(serverName, logTarget, actionMsg);
                }
                break;

            case "CLOSE":
                // Close the current private message window
                if (SelectedChannel?.IsPrivateMessage == true)
                {
                    var server = SelectedChannel.ServerViewModel;
                    server.Channels.Remove(SelectedChannel);
                    SelectedChannel = server.Channels.FirstOrDefault();
                }
                break;

            case "TOPIC":
                if (SelectedChannel != null)
                {
                    await connection.SendRawAsync($"TOPIC {SelectedChannel.Channel.Name} :{args}");
                }
                break;

            case "QUIT":
                await connection.DisconnectAsync(string.IsNullOrEmpty(args) ? null : args);
                break;

            case "RAW":
                if (!string.IsNullOrEmpty(args))
                {
                    await connection.SendRawAsync(args);
                }
                break;

            case "LIST":
                ShowChannelList(connection);
                break;

            case "AWAY":
                if (string.IsNullOrEmpty(args))
                {
                    // Remove away status
                    await connection.SendRawAsync("AWAY");
                    StatusText = "You are no longer marked as away";
                }
                else
                {
                    // Set away message
                    await connection.SendRawAsync($"AWAY :{args}");
                    StatusText = $"You are now away: {args}";
                }
                break;
                
            case "RELOAD":
                if (!string.IsNullOrEmpty(args))
                {
                    // Reload a specific script
                    var result = await _scriptManager.ReloadScriptAsync(args.Trim());
                    if (result.Success)
                    {
                        StatusText = $"Script '{args.Trim()}' reloaded";
                    }
                    else
                    {
                        StatusText = $"Failed to reload script: {result.Error}";
                    }
                }
                else
                {
                    // Reload all scripts
                    await LoadScriptsAsync();
                    StatusText = $"Reloaded {_scriptManager.GetLoadedScripts().Count()} scripts";
                }
                break;
                
            case "SETKEY":
                HandleSetKeyCommand(connection, args);
                break;
                
            case "DELKEY":
                HandleDelKeyCommand(connection, args);
                break;
                
            case "KEYX":
            case "KEYEXCHANGE":
                await HandleKeyExchangeCommandAsync(connection, args);
                break;
                
            case "SHOWKEY":
                HandleShowKeyCommand(connection, args);
                break;

            default:
                // Send as raw command
                await connection.SendRawAsync($"{command} {args}".Trim());
                break;
        }
    }
    
    /// <summary>
    /// Handles /setkey [target] key - Sets a FiSH encryption key.
    /// </summary>
    private void HandleSetKeyCommand(IrcConnection connection, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string target;
        string key;

        if (parts.Length == 1)
        {
            // /setkey key - use current channel/PM target
            if (SelectedChannel == null)
            {
                StatusText = "Usage: /setkey [#channel|nick] <key>";
                return;
            }
            target = SelectedChannel.IsPrivateMessage 
                ? SelectedChannel.PrivateMessageTarget ?? "" 
                : SelectedChannel.Channel.Name;
            key = parts[0];
        }
        else if (parts.Length >= 2)
        {
            target = parts[0];
            key = parts[1];
        }
        else
        {
            StatusText = "Usage: /setkey [#channel|nick] <key>";
            return;
        }

        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(key))
        {
            StatusText = "Usage: /setkey [#channel|nick] <key>";
            return;
        }

        _fishCrypt.SetKey(connection.Server.Id, target, key);
        StatusText = $"🔐 FiSH key set for {target}";
        SelectedChannel?.AddSystemMessage($"🔐 FiSH encryption key set for {target}");
    }

    /// <summary>
    /// Handles /delkey [target] - Removes a FiSH encryption key.
    /// </summary>
    private void HandleDelKeyCommand(IrcConnection connection, string args)
    {
        string target;

        if (string.IsNullOrWhiteSpace(args))
        {
            // Use current channel/PM target
            if (SelectedChannel == null)
            {
                StatusText = "Usage: /delkey [#channel|nick]";
                return;
            }
            target = SelectedChannel.IsPrivateMessage 
                ? SelectedChannel.PrivateMessageTarget ?? "" 
                : SelectedChannel.Channel.Name;
        }
        else
        {
            target = args.Trim();
        }

        if (string.IsNullOrEmpty(target))
        {
            StatusText = "Usage: /delkey [#channel|nick]";
            return;
        }

        _fishCrypt.SetKey(connection.Server.Id, target, null);
        StatusText = $"🔓 FiSH key removed for {target}";
        SelectedChannel?.AddSystemMessage($"🔓 FiSH encryption key removed for {target}");
    }

    /// <summary>
    /// Handles /keyx [nick] - Initiates DH1080 key exchange.
    /// </summary>
    private async Task HandleKeyExchangeCommandAsync(IrcConnection connection, string args)
    {
        string targetNick;

        if (string.IsNullOrWhiteSpace(args))
        {
            // Use current PM target
            if (SelectedChannel?.IsPrivateMessage != true || string.IsNullOrEmpty(SelectedChannel.PrivateMessageTarget))
            {
                StatusText = "Usage: /keyx <nick>";
                return;
            }
            targetNick = SelectedChannel.PrivateMessageTarget;
        }
        else
        {
            targetNick = args.Split(' ')[0];
        }

        if (connection.Dh1080Manager == null)
        {
            StatusText = "FiSH encryption not available";
            return;
        }

        var initMessage = connection.Dh1080Manager.InitiateKeyExchange(connection.Server.Id, targetNick);
        await connection.SendNoticeAsync(targetNick, initMessage);
        
        StatusText = $"🔑 Key exchange initiated with {targetNick}";
        SelectedChannel?.AddSystemMessage($"🔑 Initiating DH1080 key exchange with {targetNick}...");
    }

    /// <summary>
    /// Handles /showkey [target] - Shows the current FiSH key.
    /// </summary>
    private void HandleShowKeyCommand(IrcConnection connection, string args)
    {
        string target;

        if (string.IsNullOrWhiteSpace(args))
        {
            // Use current channel/PM target
            if (SelectedChannel == null)
            {
                StatusText = "Usage: /showkey [#channel|nick]";
                return;
            }
            target = SelectedChannel.IsPrivateMessage 
                ? SelectedChannel.PrivateMessageTarget ?? "" 
                : SelectedChannel.Channel.Name;
        }
        else
        {
            target = args.Trim();
        }

        var key = _fishCrypt.GetKey(connection.Server.Id, target);
        if (key != null)
        {
            // Show first and last 4 chars with asterisks in between for security
            var maskedKey = key.Length > 8 
                ? $"{key[..4]}****{key[^4..]}" 
                : "****";
            SelectedChannel?.AddSystemMessage($"🔐 FiSH key for {target}: {maskedKey}");
        }
        else
        {
            SelectedChannel?.AddSystemMessage($"🔓 No FiSH key set for {target}");
        }
    }

    /// <summary>
    /// Sets a FiSH encryption key for a channel via context menu.
    /// </summary>
    [RelayCommand]
    private void SetFishKeyForChannel(ChannelViewModel? channel)
    {
        var targetChannel = channel ?? SelectedChannel;
        if (SelectedServer?.Connection == null || targetChannel == null) return;
        
        var target = targetChannel.IsPrivateMessage 
            ? targetChannel.PrivateMessageTarget ?? "" 
            : targetChannel.Channel.Name;
        
        if (string.IsNullOrEmpty(target)) return;
        
        var prompt = string.Format(Strings.FiSH_SetKeyPrompt, target);
        var dialog = new Views.InputDialog(Strings.FiSH_SetKeyTitle, prompt);
        dialog.Owner = App.Current.MainWindow;
        
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            _fishCrypt.SetKey(SelectedServer.Connection.Server.Id, target, dialog.InputText);
            StatusText = $"🔐 FiSH key set for {target}";
            targetChannel.AddSystemMessage($"🔐 FiSH encryption enabled for {target}");
        }
    }

    /// <summary>
    /// Removes the FiSH encryption key for a channel via context menu.
    /// </summary>
    [RelayCommand]
    private void RemoveFishKeyForChannel(ChannelViewModel? channel)
    {
        var targetChannel = channel ?? SelectedChannel;
        if (SelectedServer?.Connection == null || targetChannel == null) return;
        
        var target = targetChannel.IsPrivateMessage 
            ? targetChannel.PrivateMessageTarget ?? "" 
            : targetChannel.Channel.Name;
        
        if (string.IsNullOrEmpty(target)) return;
        
        _fishCrypt.SetKey(SelectedServer.Connection.Server.Id, target, null);
        StatusText = $"🔓 FiSH key removed for {target}";
        targetChannel.AddSystemMessage($"🔓 FiSH encryption key removed for {target}");
    }

    /// <summary>
    /// Shows the FiSH encryption key for a channel via context menu.
    /// </summary>
    [RelayCommand]
    private void ShowFishKeyForChannel(ChannelViewModel? channel)
    {
        var targetChannel = channel ?? SelectedChannel;
        if (SelectedServer?.Connection == null || targetChannel == null) return;
        
        var target = targetChannel.IsPrivateMessage 
            ? targetChannel.PrivateMessageTarget ?? "" 
            : targetChannel.Channel.Name;
        
        if (string.IsNullOrEmpty(target)) return;
        
        var key = _fishCrypt.GetKey(SelectedServer.Connection.Server.Id, target);
        if (key != null)
        {
            var maskedKey = key.Length > 8 
                ? $"{key[..4]}****{key[^4..]}" 
                : "****";
            targetChannel.AddSystemMessage($"🔐 FiSH key for {target}: {maskedKey}");
        }
        else
        {
            targetChannel.AddSystemMessage($"🔓 No FiSH key set for {target}");
        }
    }

    /// <summary>
    /// Initiates DH1080 key exchange for a PM via context menu.
    /// </summary>
    [RelayCommand]
    private async Task InitiateFishKeyExchangeAsync(ChannelViewModel? channel)
    {
        var targetChannel = channel ?? SelectedChannel;
        if (SelectedServer?.Connection == null || targetChannel == null) return;
        
        if (!targetChannel.IsPrivateMessage || string.IsNullOrEmpty(targetChannel.PrivateMessageTarget))
        {
            StatusText = "Key exchange is only available for private messages";
            return;
        }
        
        var targetNick = targetChannel.PrivateMessageTarget;
        var connection = SelectedServer.Connection;
        
        if (connection.Dh1080Manager == null)
        {
            StatusText = "FiSH encryption not available";
            return;
        }
        
        var initMessage = connection.Dh1080Manager.InitiateKeyExchange(connection.Server.Id, targetNick);
        await connection.SendNoticeAsync(targetNick, initMessage);
        
        StatusText = $"🔑 Key exchange initiated with {targetNick}";
        targetChannel.AddSystemMessage($"🔑 Initiating DH1080 key exchange with {targetNick}...");
    }

    /// <summary>
    /// Sets a FiSH key for a specific user (from user list context menu).
    /// </summary>
    [RelayCommand]
    private void SetFishKeyForUser(IrcUser? user)
    {
        if (SelectedServer?.Connection == null || user == null) return;
        
        var prompt = string.Format(Strings.FiSH_SetKeyPrompt, user.Nickname);
        var dialog = new Views.InputDialog(Strings.FiSH_SetKeyTitle, prompt);
        dialog.Owner = App.Current.MainWindow;
        
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            _fishCrypt.SetKey(SelectedServer.Connection.Server.Id, user.Nickname, dialog.InputText);
            StatusText = $"🔐 FiSH key set for {user.Nickname}";
            SelectedChannel?.AddSystemMessage($"🔐 FiSH encryption enabled for {user.Nickname}");
        }
    }

    /// <summary>
    /// Initiates DH1080 key exchange with a specific user (from user list context menu).
    /// </summary>
    [RelayCommand]
    private async Task InitiateFishKeyExchangeForUserAsync(IrcUser? user)
    {
        if (SelectedServer?.Connection == null || user == null) return;
        
        var connection = SelectedServer.Connection;
        
        if (connection.Dh1080Manager == null)
        {
            StatusText = "FiSH encryption not available";
            return;
        }
        
        var initMessage = connection.Dh1080Manager.InitiateKeyExchange(connection.Server.Id, user.Nickname);
        await connection.SendNoticeAsync(user.Nickname, initMessage);
        
        StatusText = $"🔑 Key exchange initiated with {user.Nickname}";
        SelectedChannel?.AddSystemMessage($"🔑 Initiating DH1080 key exchange with {user.Nickname}...");
    }

    /// <summary>
    /// Opens the topic editor dialog for the current channel.
    /// </summary>
    [RelayCommand]
    private async Task EditTopicAsync()
    {
        if (SelectedServer?.Connection == null || SelectedChannel == null) return;
        if (!SelectedChannel.CanEditTopic) return;
        
        var channelName = SelectedChannel.Channel.Name;
        var currentTopic = SelectedChannel.Topic;
        var hasEncryptionKey = SelectedChannel.HasEncryptionKey;
        
        var dialog = new Views.TopicEditorDialog(currentTopic, hasEncryptionKey);
        dialog.Owner = App.Current.MainWindow;
        
        if (dialog.ShowDialog() == true)
        {
            var newTopic = dialog.TopicText;
            
            // Encrypt topic if requested
            if (dialog.EncryptTopic && hasEncryptionKey)
            {
                var encrypted = _fishCrypt.Encrypt(
                    SelectedServer.Connection.Server.Id, 
                    channelName, 
                    newTopic);
                    
                if (encrypted != null)
                {
                    newTopic = encrypted;
                }
            }
            
            await SelectedServer.Connection.SendRawAsync($"TOPIC {channelName} :{newTopic}");
        }
    }
    
    /// <summary>
    /// Handles the /script command with subcommands.
    /// </summary>
    private async Task HandleScriptCommandAsync(string args, IrcConnection? connection)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subCommand = parts.Length > 0 ? parts[0].ToUpper() : string.Empty;
        var scriptArg = parts.Length > 1 ? parts[1] : string.Empty;
        
        switch (subCommand)
        {
            case "LIST":
                var scripts = _scriptManager.GetLoadedScripts().ToList();
                if (SelectedChannel != null)
                {
                    SelectedChannel.AddSystemMessage($"Loaded scripts ({scripts.Count}):");
                    foreach (var script in scripts)
                    {
                        SelectedChannel.AddSystemMessage($"  • {script.Name} ({Path.GetExtension(script.FilePath)})");
                    }
                }
                break;
                
            case "ENABLE":
            case "LOAD":
                if (string.IsNullOrEmpty(scriptArg))
                {
                    StatusText = "Usage: /script enable <script_name>";
                    return;
                }
                var loadPath = FindScriptPath(scriptArg);
                if (loadPath != null)
                {
                    var loadResult = await _scriptManager.LoadScriptAsync(loadPath);
                    StatusText = loadResult.Success 
                        ? $"Enabled: {scriptArg}" 
                        : $"Failed to enable: {loadResult.Error}";
                }
                else
                {
                    StatusText = $"Script not found: {scriptArg}";
                }
                break;
                
            case "DISABLE":
            case "UNLOAD":
                if (string.IsNullOrEmpty(scriptArg))
                {
                    StatusText = "Usage: /script disable <script_name>";
                    return;
                }
                if (_scriptManager.UnloadScript(scriptArg))
                {
                    StatusText = $"Disabled: {scriptArg}";
                }
                else
                {
                    StatusText = $"Script not loaded: {scriptArg}";
                }
                break;
                
            case "RELOAD":
                if (string.IsNullOrEmpty(scriptArg))
                {
                    await LoadScriptsAsync();
                    StatusText = $"Reloaded {_scriptManager.GetLoadedScripts().Count()} scripts";
                }
                else
                {
                    var reloadResult = await _scriptManager.ReloadScriptAsync(scriptArg);
                    StatusText = reloadResult.Success 
                        ? $"Reloaded: {scriptArg}" 
                        : $"Failed to reload: {reloadResult.Error}";
                }
                break;
                
            case "NEW":
                ShowScriptManager();
                break;
                
            case "CONSOLE":
                ShowScriptConsole();
                break;
                
            case "":
            default:
                ShowScriptManager();
                break;
        }
    }
    
    /// <summary>
    /// Finds a script file path by name.
    /// </summary>
    private string? FindScriptPath(string scriptName)
    {
        var scriptsDir = _scriptManager.Context.ScriptsDirectory;
        if (!Directory.Exists(scriptsDir)) return null;
        
        // Try exact match first
        var extensions = new[] { ".lua", ".triggers.json", ".cs" };
        foreach (var ext in extensions)
        {
            var path = Path.Combine(scriptsDir, scriptName + ext);
            if (File.Exists(path)) return path;
            
            path = Path.Combine(scriptsDir, scriptName);
            if (File.Exists(path)) return path;
        }
        
        // Search recursively
        foreach (var file in Directory.GetFiles(scriptsDir, $"*{scriptName}*", SearchOption.AllDirectories))
        {
            return file;
        }
        
        return null;
    }

    [RelayCommand]
    private void SelectChannel(ChannelViewModel? channel)
    {
        if (channel == null) return;

        SelectedChannel = channel;
        SelectedServer = channel.ServerViewModel;
        channel.UnreadCount = 0;
        channel.HasMention = false;
    }

    private void ShowChannelList(IrcConnection connection)
    {
        var listWindow = new ChannelListWindow(connection);
        listWindow.Owner = App.Current.MainWindow;
        if (listWindow.ShowDialog() == true && !string.IsNullOrEmpty(listWindow.SelectedChannelName))
        {
            _ = connection.JoinChannelAsync(listWindow.SelectedChannelName);
        }
    }
    
    /// <summary>
    /// Shows the script manager window.
    /// </summary>
    [RelayCommand]
    private void ShowScriptManager()
    {
        try
        {
            if (_scriptManagerWindow == null || !_scriptManagerWindow.IsLoaded)
            {
                _scriptManagerWindow = new ScriptManagerWindow(_scriptManager);
                if (App.Current.MainWindow != null && App.Current.MainWindow != _scriptManagerWindow)
                {
                    _scriptManagerWindow.Owner = App.Current.MainWindow;
                }
                _scriptManagerWindow.Closed += (s, e) => _scriptManagerWindow = null;
                _scriptManagerWindow.Show();
            }
            else
            {
                _scriptManagerWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show script manager");
            StatusText = $"Error opening Script Manager: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Shows the script console window.
    /// </summary>
    [RelayCommand]
    private void ShowScriptConsole()
    {
        if (_scriptConsoleWindow == null || !_scriptConsoleWindow.IsLoaded)
        {
            _scriptConsoleWindow = new ScriptConsoleWindow(_scriptManager);
            _scriptConsoleWindow.Owner = App.Current.MainWindow;
            _scriptConsoleWindow.Closed += (s, e) => _scriptConsoleWindow = null;
            _scriptConsoleWindow.Show();
        }
        else
        {
            _scriptConsoleWindow.Activate();
        }
    }
    
    private void ShowChannelStats()
    {
        if (SelectedChannel == null || SelectedChannel.IsServerConsole)
        {
            StatusText = "Select a channel to view stats";
            return;
        }
        
        var stats = SelectedChannel.GetStats();
        var statsWindow = new ChannelStatsWindow(stats);
        statsWindow.Owner = App.Current.MainWindow;
        statsWindow.ShowDialog();
    }

    [RelayCommand]
    private async Task JoinChannelAsync(string? channelName)
    {
        if (string.IsNullOrEmpty(channelName) || SelectedServer?.Connection == null) return;

        await SelectedServer.Connection.JoinChannelAsync(channelName);
    }

    [RelayCommand]
    private async Task PartChannelAsync(ChannelViewModel? channel)
    {
        if (channel == null) return;

        // For private message channels, just close the window
        if (channel.IsPrivateMessage)
        {
            channel.ServerViewModel.Channels.Remove(channel);
            if (SelectedChannel == channel)
            {
                SelectedChannel = channel.ServerViewModel.Channels.FirstOrDefault();
            }
            return;
        }

        var connection = channel.ServerViewModel.Connection;
        if (connection != null)
        {
            await connection.PartChannelAsync(channel.Channel.Name);
        }

        channel.ServerViewModel.Channels.Remove(channel);
        if (SelectedChannel == channel)
        {
            SelectedChannel = channel.ServerViewModel.Channels.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void StartPrivateMessage(UserViewModel? user)
    {
        if (user == null || SelectedServer == null) return;

        var dmChannel = GetOrCreatePrivateMessageChannel(SelectedServer, user.User.Nickname);
        SelectedChannel = dmChannel;
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
        {
            SearchQuery = string.Empty;
            SearchResultCount = 0;
            SearchResults.Clear();
        }
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchVisible = false;
        SearchQuery = string.Empty;
        SearchResultCount = 0;
        SearchResults.Clear();
        
        // Clear search highlights in messages
        if (SelectedChannel != null)
        {
            foreach (var msg in SelectedChannel.Messages)
            {
                msg.IsSearchMatch = false;
            }
        }
    }
    
    [RelayCommand]
    private void JumpToLatest()
    {
        // This command is handled in the view (MainWindow.xaml.cs)
        // via the JumpToLatestButton_Click handler
    }

    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SelectedChannel == null || SelectedServer == null)
            return;

        SearchResults.Clear();

        // Search in current channel's log files
        var results = LoggingService.Instance.Search(
            SelectedServer.Server.Name,
            SelectedChannel.Channel.Name,
            SearchQuery);

        foreach (var (date, line) in results)
        {
            SearchResults.Add(line);
        }

        SearchResultCount = SearchResults.Count;

        // Also highlight matching messages in current view
        foreach (var msg in SelectedChannel.Messages)
        {
            msg.IsSearchMatch = msg.Content?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    [RelayCommand]
    private void Whois(UserViewModel? user)
    {
        if (user == null || SelectedServer?.Connection == null) return;

        var userHost = !string.IsNullOrEmpty(user.User.Username) && !string.IsNullOrEmpty(user.User.Hostname)
            ? $"{user.User.Username}@{user.User.Hostname}"
            : null;
            
        var profileWindow = new UserProfileWindow(SelectedServer.Connection, user.User.Nickname, userHost);
        profileWindow.Owner = App.Current.MainWindow;
        
        if (profileWindow.ShowDialog() == true && profileWindow.StartPrivateMessage)
        {
            // Start private message with this user
            StartPrivateMessageCommand.Execute(user);
        }
    }

    [RelayCommand]
    private void IgnoreUser(UserViewModel? user)
    {
        if (user == null) return;

        var nickname = user.User.Nickname;
        if (!_configService.Configuration.Settings.IgnoredUsers.Contains(nickname, StringComparer.OrdinalIgnoreCase))
        {
            _configService.Configuration.Settings.IgnoredUsers.Add(nickname);
            _ = _configService.SaveAsync();
            StatusText = $"Now ignoring {nickname}";
        }
    }

    [RelayCommand]
    private void UnignoreUser(UserViewModel? user)
    {
        if (user == null) return;

        var nickname = user.User.Nickname;
        var existing = _configService.Configuration.Settings.IgnoredUsers
            .FirstOrDefault(n => n.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        
        if (existing != null)
        {
            _configService.Configuration.Settings.IgnoredUsers.Remove(existing);
            _ = _configService.SaveAsync();
            StatusText = $"No longer ignoring {nickname}";
        }
    }

    private bool IsUserIgnored(string nickname)
    {
        return _configService.Configuration.Settings.IgnoredUsers
            .Contains(nickname, StringComparer.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ShowRawIrcLog()
    {
        if (_rawIrcLogWindow == null || !_rawIrcLogWindow.IsLoaded)
        {
            _rawIrcLogWindow = new RawIrcLogWindow();
            _rawIrcLogWindow.Closed += (s, e) => _rawIrcLogWindow = null;
            _rawIrcLogWindow.Show();
        }
        else
        {
            _rawIrcLogWindow.Activate();
        }
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsWindow = new SettingsWindow(_configService);
        settingsWindow.Owner = App.Current.MainWindow;
        if (settingsWindow.ShowDialog() == true)
        {
            // Apply updated settings to all connections
            var settings = _configService.Configuration.Settings;
            foreach (var server in Servers)
            {
                if (server.Connection != null)
                {
                    server.Connection.HighlightWords = settings.HighlightWords;
                }
            }
            
            // Update notification service
            NotificationService.Instance.EnableSoundNotifications = settings.EnableSoundNotifications;
            NotificationService.Instance.EnableToastNotifications = settings.EnableFlashNotifications;
            NotificationService.Instance.OnlyWhenMinimized = settings.OnlyNotifyWhenInactive;
            
            StatusText = "Settings saved";
        }
    }

    /// <summary>
    /// Toggles the user list visibility.
    /// </summary>
    [RelayCommand]
    private void ToggleUserList()
    {
        IsUserListVisible = !IsUserListVisible;
    }

    /// <summary>
    /// Selects a server and displays its channels in the sidebar.
    /// </summary>
    [RelayCommand]
    private void SelectServerInRail(ServerViewModel? server)
    {
        if (server == null) return;
        SelectedServer = server;
        
        // If the server has channels, select the first one (usually server console)
        if (server.Channels.Count > 0 && SelectedChannel?.ServerViewModel != server)
        {
            SelectedChannel = server.Channels[0];
        }
    }
    
    /// <summary>
    /// Adds a nickname to the notify list for the current server.
    /// </summary>
    /// <param name="nickname">The nickname to add.</param>
    public void AddToNotifyList(string nickname)
    {
        if (SelectedServer == null || SelectedChannel == null) return;
        
        var serverName = SelectedServer.Server.Name;
        var added = NotifyListService.Instance.AddToNotifyList(serverName, nickname);
        
        if (added)
        {
            SelectedChannel.AddSystemMessage($"Added {nickname} to notify list");
        }
        else
        {
            SelectedChannel.AddSystemMessage($"{nickname} is already on notify list");
        }
    }
    
    /// <summary>
    /// Removes a nickname from the notify list for the current server.
    /// </summary>
    /// <param name="nickname">The nickname to remove.</param>
    public void RemoveFromNotifyList(string nickname)
    {
        if (SelectedServer == null || SelectedChannel == null) return;
        
        var serverName = SelectedServer.Server.Name;
        var removed = NotifyListService.Instance.RemoveFromNotifyList(serverName, nickname);
        
        if (removed)
        {
            SelectedChannel.AddSystemMessage($"Removed {nickname} from notify list");
        }
        else
        {
            SelectedChannel.AddSystemMessage($"{nickname} is not on notify list");
        }
    }
    
    /// <summary>
    /// Sorts channels with favorites first, then by name.
    /// Server console is always first.
    /// </summary>
    /// <param name="server">The server whose channels to sort.</param>
    public void SortChannels(ServerViewModel server)
    {
        if (server?.Channels == null || server.Channels.Count <= 1) return;
        
        // Get sorted list
        var sorted = server.Channels
            .OrderByDescending(c => c.IsServerConsole) // Server console first
            .ThenByDescending(c => c.IsFavorite)        // Then favorites
            .ThenBy(c => c.DisplayName)                  // Then alphabetically
            .ToList();
        
        // Update collection in place
        for (int i = 0; i < sorted.Count; i++)
        {
            var currentIndex = server.Channels.IndexOf(sorted[i]);
            if (currentIndex != i)
            {
                server.Channels.Move(currentIndex, i);
            }
        }
    }
}
