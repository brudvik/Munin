using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Munin.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Munin.UI.ViewModels;

/// <summary>
/// ViewModel for managing remote Munin agents.
/// </summary>
public partial class AgentManagerViewModel : ObservableObject
{
    private readonly ConfigurationService _configService;
    private readonly EncryptionService _encryptionService;

    [ObservableProperty]
    private ObservableCollection<AgentConnection> _agents = new();

    [ObservableProperty]
    private AgentConnection? _selectedAgent;

    [ObservableProperty]
    private string _statusText = "Not connected to any agents";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private ObservableCollection<AgentChannelInfo> _selectedAgentChannels = new();

    [ObservableProperty]
    private ObservableCollection<AgentLogEntry> _selectedAgentLogs = new();

    public AgentManagerViewModel(ConfigurationService configService, EncryptionService encryptionService)
    {
        _configService = configService;
        _encryptionService = encryptionService;
        LoadSavedAgents();
    }

    private void LoadSavedAgents()
    {
        // Load saved agent configurations from settings
        foreach (var saved in _configService.Configuration.SavedAgents)
        {
            Agents.Add(new AgentConnection
            {
                Name = saved.Name,
                Host = saved.Host,
                Port = saved.Port,
                UseTls = saved.UseTls,
                AuthToken = saved.AuthToken ?? ""
            });
        }
    }

    [RelayCommand]
    private async Task AddAgentAsync()
    {
        var dialog = new Views.AddAgentDialog
        {
            Owner = App.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var result = dialog.Result;
            var agent = new AgentConnection
            {
                Name = result.Name,
                Host = result.Host,
                Port = result.Port,
                UseTls = result.UseTls,
                AuthToken = result.AuthToken
            };

            Agents.Add(agent);

            // Save to configuration if requested
            if (result.SaveCredentials)
            {
                _configService.SaveAgent(agent);
            }

            StatusText = $"Added agent: {agent.Name}";

            // Optionally auto-connect
            await ConnectAgentAsync(agent);
        }
    }

    [RelayCommand]
    private void EditAgent(AgentConnection agent)
    {
        // Store original values for config update
        var originalHost = agent.Host;
        var originalPort = agent.Port;

        var dialog = new Views.AddAgentDialog(
            agent.Name,
            agent.Host,
            agent.Port,
            agent.UseTls,
            agent.AuthToken)
        {
            Owner = App.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var result = dialog.Result;

            // Disconnect if connected (settings changed)
            if (agent.IsConnected)
            {
                agent.CancellationTokenSource?.Cancel();
                agent.Client?.Close();
                agent.IsConnected = false;
            }

            // Update agent properties
            agent.Name = result.Name;
            agent.Host = result.Host;
            agent.Port = result.Port;
            agent.UseTls = result.UseTls;
            agent.AuthToken = result.AuthToken;

            // Update in configuration
            if (result.SaveCredentials)
            {
                // Remove old entry if host/port changed
                if (originalHost != result.Host || originalPort != result.Port)
                {
                    _configService.RemoveAgent(originalHost, originalPort);
                }
                _configService.SaveAgent(agent);
            }

            StatusText = $"Updated agent: {agent.Name}";
        }
    }

    [RelayCommand]
    private async Task ConnectAgentAsync(AgentConnection agent)
    {
        if (agent.IsConnected)
        {
            await DisconnectAgentAsync(agent);
            return;
        }

        try
        {
            IsConnecting = true;
            StatusText = $"Connecting to {agent.Name}...";

            var client = new TcpClient();
            await client.ConnectAsync(agent.Host, agent.Port);

            // TODO: Implement TLS and authentication
            agent.Client = client;
            agent.IsConnected = true;
            agent.ConnectedAt = DateTime.UtcNow;

            StatusText = $"Connected to {agent.Name}";

            // Start message loop
            _ = MessageLoopAsync(agent);

            // Request initial status
            await SendCommandAsync(agent, "status");
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to connect: {ex.Message}";
            agent.IsConnected = false;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAgentAsync(AgentConnection agent)
    {
        try
        {
            agent.CancellationTokenSource?.Cancel();
            agent.Client?.Close();
            agent.IsConnected = false;
            StatusText = $"Disconnected from {agent.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error disconnecting: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveAgent(AgentConnection agent)
    {
        // Show confirmation dialog
        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to remove the agent '{agent.Name}'?\n\nThis will also remove it from saved configuration.",
            "Remove Agent",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        // Disconnect if connected
        agent.CancellationTokenSource?.Cancel();
        agent.Client?.Close();
        
        // Remove from list
        Agents.Remove(agent);

        // Remove from saved configuration
        _configService.RemoveAgent(agent.Host, agent.Port);

        StatusText = $"Removed agent: {agent.Name}";
    }

    private async Task SendCommandAsync(AgentConnection agent, string command)
    {
        if (!agent.IsConnected || agent.Client == null)
            return;

        try
        {
            var stream = agent.Client.GetStream();
            var message = JsonSerializer.Serialize(new { command });
            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            await stream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            StatusText = $"Error sending command: {ex.Message}";
        }
    }

    private async Task MessageLoopAsync(AgentConnection agent)
    {
        agent.CancellationTokenSource = new CancellationTokenSource();
        var ct = agent.CancellationTokenSource.Token;

        try
        {
            var reader = new StreamReader(agent.Client!.GetStream(), Encoding.UTF8);

            while (!ct.IsCancellationRequested && agent.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line))
                {
                    agent.IsConnected = false;
                    break;
                }

                ProcessAgentMessage(agent, line);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            StatusText = $"Connection error: {ex.Message}";
        }
        finally
        {
            agent.IsConnected = false;
        }
    }

    private void ProcessAgentMessage(AgentConnection agent, string message)
    {
        try
        {
            var json = JsonDocument.Parse(message);
            var type = json.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "status":
                    ProcessStatusResponse(agent, json.RootElement);
                    break;
                case "channels":
                    ProcessChannelsResponse(agent, json.RootElement);
                    break;
                case "log":
                    ProcessLogEntry(agent, json.RootElement);
                    break;
                case "irc_message":
                    ProcessIrcMessage(agent, json.RootElement);
                    break;
            }
        }
        catch
        {
            // Ignore parse errors
        }
    }

    private void ProcessStatusResponse(AgentConnection agent, JsonElement data)
    {
        if (data.TryGetProperty("servers", out var servers))
        {
            agent.ServerCount = servers.GetArrayLength();
        }
        if (data.TryGetProperty("uptime", out var uptime))
        {
            agent.Uptime = uptime.GetString() ?? "";
        }
    }

    private void ProcessChannelsResponse(AgentConnection agent, JsonElement data)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (SelectedAgent == agent)
            {
                SelectedAgentChannels.Clear();
                
                if (data.TryGetProperty("channels", out var channels))
                {
                    foreach (var channel in channels.EnumerateArray())
                    {
                        SelectedAgentChannels.Add(new AgentChannelInfo
                        {
                            Name = channel.GetProperty("name").GetString() ?? "",
                            UserCount = channel.GetProperty("userCount").GetInt32(),
                            Topic = channel.TryGetProperty("topic", out var topic) ? topic.GetString() ?? "" : ""
                        });
                    }
                }
            }
        });
    }

    private void ProcessLogEntry(AgentConnection agent, JsonElement data)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (SelectedAgent == agent)
            {
                SelectedAgentLogs.Insert(0, new AgentLogEntry
                {
                    Timestamp = DateTime.Parse(data.GetProperty("timestamp").GetString() ?? DateTime.UtcNow.ToString()),
                    Level = data.GetProperty("level").GetString() ?? "Info",
                    Message = data.GetProperty("message").GetString() ?? ""
                });

                // Keep only last 500 log entries
                while (SelectedAgentLogs.Count > 500)
                {
                    SelectedAgentLogs.RemoveAt(SelectedAgentLogs.Count - 1);
                }
            }
        });
    }

    private void ProcessIrcMessage(AgentConnection agent, JsonElement data)
    {
        // Handle IRC messages for live view
    }

    partial void OnSelectedAgentChanged(AgentConnection? value)
    {
        SelectedAgentChannels.Clear();
        SelectedAgentLogs.Clear();

        if (value?.IsConnected == true)
        {
            _ = SendCommandAsync(value, "channels");
        }
    }
}

/// <summary>
/// Represents a connection to a remote agent.
/// </summary>
public partial class AgentConnection : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _host = "";

    [ObservableProperty]
    private int _port = 5550;

    [ObservableProperty]
    private bool _useTls = true;

    [ObservableProperty]
    private string _authToken = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private DateTime _connectedAt;

    [ObservableProperty]
    private int _serverCount;

    [ObservableProperty]
    private string _uptime = "";

    public TcpClient? Client { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}

/// <summary>
/// Channel info from an agent.
/// </summary>
public class AgentChannelInfo
{
    public string Name { get; set; } = "";
    public int UserCount { get; set; }
    public string Topic { get; set; } = "";
}

/// <summary>
/// Log entry from an agent.
/// </summary>
public class AgentLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}
