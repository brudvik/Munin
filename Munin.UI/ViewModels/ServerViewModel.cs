using CommunityToolkit.Mvvm.ComponentModel;
using Munin.Core.Models;
using Munin.Core.Services;
using System.Collections.ObjectModel;
using System.Windows.Media;

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
    /// Whether this server is currently selected in the UI.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

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
    /// Gets the first two characters of the server name as initials for the rail icon.
    /// </summary>
    public string Initials => Server.Name.Length >= 2 
        ? Server.Name[..2].ToUpperInvariant() 
        : Server.Name.ToUpperInvariant();
    
    /// <summary>
    /// Gets a consistent background color based on server name for the rail icon.
    /// </summary>
    public Brush InitialsBackground
    {
        get
        {
            var hash = Server.Name.GetHashCode();
            var hue = Math.Abs(hash) % 360;
            return new SolidColorBrush(HslToRgb(hue, 0.5, 0.4));
        }
    }
    
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
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(InitialsBackground));
    }

    public new void OnPropertyChanged(string propertyName)
    {
        base.OnPropertyChanged(propertyName);
    }
    
    /// <summary>
    /// Converts HSL color values to RGB Color.
    /// </summary>
    private static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;

        if (Math.Abs(s) < 0.001)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
            g = HueToRgb(p, q, h / 360.0);
            b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);
        }

        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
