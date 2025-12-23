using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Munin.Core.Models;
using Munin.Core.Services;
using Munin.UI.Services;
using Munin.UI.Views;
using Serilog;
using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private ObservableCollection<ServerViewModel> _servers = new();

    [ObservableProperty]
    private ServerViewModel? _selectedServer;

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

    private readonly HashSet<string> _loadedHistoryChannels = new();
    private RawIrcLogWindow? _rawIrcLogWindow;

    partial void OnSelectedChannelChanged(ChannelViewModel? value)
    {
        _logger.Information("OnSelectedChannelChanged: value={Name}", value?.Channel?.Name ?? "null");
        if (value != null && SelectedServer != null)
        {
            // Clear unread when selecting a channel
            value.UnreadCount = 0;
            value.HasMention = false;
            
            _logger.Information("OnSelectedChannelChanged: About to load channel history");
            // Load history if not already loaded
            LoadChannelHistory(value);
            _logger.Information("OnSelectedChannelChanged: Channel history loaded");
        }
    }

    private void LoadChannelHistory(ChannelViewModel channel)
    {
        _logger.Information("LoadChannelHistory: Starting for {Name}", channel.Channel?.Name ?? "null");
        if (SelectedServer == null) return;
        
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
        
        _logger.Information("MainViewModel initialized");
        
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
    }

    private async Task LoadConfigurationAsync()
    {
        _logger.Information("LoadConfigurationAsync: Starting");
        await _configService.LoadAsync();
        _logger.Information("LoadConfigurationAsync: Configuration loaded");
        
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
                    server.IsConnected = true;
                    StatusText = $"Connected to {e.Server.Name}";
                    
                    // Update the current nickname for message coloring
                    MessageViewModel.CurrentNickname = server.Connection?.CurrentNickname;
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
                    server.IsConnected = false;
                    StatusText = $"Disconnected from {e.Server.Name}";
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
                        if (SelectedChannel == null || SelectedChannel.IsServerConsole)
                        {
                            SelectedChannel = channelVm;
                        }
                        
                        // Save channel to auto-join list
                        _configService.AddChannelToServer(e.Server.Id, e.Channel.Name);
                        _ = SaveConfigurationAsync();
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
                    channel.Messages.Add(new MessageViewModel(e.Message));
                    
                    // Track message for statistics
                    channel.TrackMessage(e.Message.Source);
                    
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
                    channel.Messages.Add(new MessageViewModel(joinMsg));
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
                    channel.Messages.Add(new MessageViewModel(partMsg));
                }
            });
        };

        _clientManager.UserQuit += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                // Add quit message to all channels where this user was present
                foreach (var server in Servers.Where(sv => sv.Server.Id == e.Server.Id))
                {
                    foreach (var channel in server.Channels.Where(c => !c.IsServerConsole && !c.IsPrivateMessage))
                    {
                        // Check if user was in this channel (they should still be in the list before refresh)
                        if (channel.Users.Any(u => u.User.Nickname.Equals(e.User.Nickname, StringComparison.OrdinalIgnoreCase)))
                        {
                            var quitMsg = IrcMessage.CreateQuit(e.User.Nickname);
                            channel.Messages.Add(new MessageViewModel(quitMsg));
                        }
                        channel.RefreshUsers();
                    }
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
                        channel.Messages.Add(new MessageViewModel(nickMsg));
                        channel.RefreshUsers();
                    }
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
                    console.Messages.Add(new MessageViewModel(IrcMessage.CreateError(e.Message)));
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
                        console.Messages.Add(new MessageViewModel(message));
                        
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
                    dmChannel.Messages.Add(new MessageViewModel(e.Message));
                    
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
                }
            });
        };

        _clientManager.RawMessage += (s, e) =>
        {
            _rawIrcLogWindow?.AddEntry(e.IsOutgoing, e.Message);
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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to connect to {ServerName}", server.Server.Name);
            StatusText = $"Failed to connect: {ex.Message}";
        }
        finally
        {
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
        if (string.IsNullOrWhiteSpace(MessageInput) || SelectedChannel == null) return;

        var connection = SelectedChannel.ServerViewModel.Connection;
        if (connection == null || !connection.IsConnected) return;

        var message = MessageInput;
        MessageInput = string.Empty;

        // Handle commands
        if (message.StartsWith('/'))
        {
            await HandleCommandAsync(message, connection);
            return;
        }

        // Determine the target - for private messages use the nickname, otherwise the channel name
        var target = SelectedChannel.IsPrivateMessage 
            ? SelectedChannel.PrivateMessageTarget 
            : SelectedChannel.Channel.Name;

        await connection.SendMessageAsync(target, message);

        // Add our own message to the channel
        var ircMessage = new IrcMessage
        {
            Type = MessageType.Normal,
            Source = connection.CurrentNickname,
            Target = target,
            Content = message
        };
        SelectedChannel.Messages.Add(new MessageViewModel(ircMessage));
    }

    private async Task HandleCommandAsync(string input, IrcConnection connection)
    {
        var parts = input[1..].Split(' ', 2);
        var command = parts[0].ToUpperInvariant();
        var args = parts.Length > 1 ? parts[1] : "";

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
                            var ircMessage = new IrcMessage
                            {
                                Type = MessageType.Normal,
                                Source = connection.CurrentNickname,
                                Target = targetNick,
                                Content = msgContent
                            };
                            dmChannel.Messages.Add(new MessageViewModel(ircMessage));
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
                            var ircMessage = new IrcMessage
                            {
                                Type = MessageType.Normal,
                                Source = connection.CurrentNickname,
                                Target = targetUser,
                                Content = queryMsg
                            };
                            dmChannel.Messages.Add(new MessageViewModel(ircMessage));
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
                    SelectedChannel.Messages.Add(new MessageViewModel(actionMsg));
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
                
            case "STATS":
                ShowChannelStats();
                break;

            default:
                // Send as raw command
                await connection.SendRawAsync($"{command} {args}".Trim());
                break;
        }
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
}
