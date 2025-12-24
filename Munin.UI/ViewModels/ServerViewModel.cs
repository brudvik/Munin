using CommunityToolkit.Mvvm.ComponentModel;
using Munin.Core.Models;
using Munin.Core.Services;
using System.Collections.ObjectModel;

namespace Munin.UI.ViewModels;

/// <summary>
/// ViewModel for an IRC server connection.
/// </summary>
/// <remarks>
/// <para>Provides UI-bindable properties for:</para>
/// <list type="bullet">
///   <item><description>Connection state and status icons</description></item>
///   <item><description>Channels list</description></item>
///   <item><description>Server latency/ping</description></item>
/// </list>
/// </remarks>
public partial class ServerViewModel : ObservableObject
{
    /// <summary>
    /// The underlying IRC server model.
    /// </summary>
    [ObservableProperty]
    private IrcServer _server;

    /// <summary>
    /// Whether the server is currently connected.
    /// </summary>
    [ObservableProperty]
    private bool _isConnected;
    
    /// <summary>
    /// Whether the server is currently attempting to connect.
    /// </summary>
    [ObservableProperty]
    private bool _isConnecting;

    /// <summary>
    /// Whether the server node is expanded in the tree view.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Collection of joined channels.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChannelViewModel> _channels = new();
    
    /// <summary>
    /// Current latency to the server in milliseconds.
    /// </summary>
    [ObservableProperty]
    private int _latencyMs;

    /// <summary>
    /// The active connection to this server.
    /// </summary>
    public IrcConnection? Connection { get; set; }

    /// <summary>
    /// Display name for the UI.
    /// </summary>
    public string DisplayName => Server.Name;
    
    /// <summary>
    /// Status icon indicating connection state.
    /// </summary>
    public string StatusIcon => IsConnecting ? "🔄" : (IsConnected ? "🟢" : "⚫");
    
    /// <summary>
    /// Formatted latency string for display.
    /// </summary>
    public string LatencyDisplay => LatencyMs > 0 ? $"{LatencyMs}ms" : "—";

    public ServerViewModel(IrcServer server)
    {
        _server = server;
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusIcon));
        if (!value)
        {
            LatencyMs = 0;
        }
    }
    
    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusIcon));
    }
    
    partial void OnLatencyMsChanged(int value)
    {
        OnPropertyChanged(nameof(LatencyDisplay));
    }

    partial void OnServerChanged(IrcServer value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    public new void OnPropertyChanged(string propertyName)
    {
        base.OnPropertyChanged(propertyName);
    }
}
