using CommunityToolkit.Mvvm.ComponentModel;
using Munin.Core.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Munin.UI.ViewModels;

/// <summary>
/// ViewModel for a server group/folder in the server rail.
/// </summary>
/// <remarks>
/// <para>Provides UI-bindable properties for:</para>
/// <list type="bullet">
///   <item><description>Group name and icon</description></item>
///   <item><description>Collapse/expand state</description></item>
///   <item><description>Contained servers</description></item>
/// </list>
/// </remarks>
public partial class ServerGroupViewModel : ObservableObject, IServerRailItem
{
    /// <summary>
    /// The underlying server group model.
    /// </summary>
    [ObservableProperty]
    private ServerGroup _group;

    /// <summary>
    /// Whether the group is currently collapsed.
    /// </summary>
    [ObservableProperty]
    private bool _isCollapsed;

    /// <summary>
    /// Whether this group is currently selected in the UI.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Servers contained in this group.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ServerViewModel> _servers = new();

    /// <summary>
    /// Creates a new ServerGroupViewModel.
    /// </summary>
    /// <param name="group">The underlying server group model.</param>
    public ServerGroupViewModel(ServerGroup group)
    {
        _group = group;
        _isCollapsed = group.IsCollapsed;
    }

    /// <summary>
    /// Display name for the UI.
    /// </summary>
    public string DisplayName => Group.Name;

    /// <summary>
    /// Gets the icon for the group (emoji or first character).
    /// </summary>
    public string Icon => Group.Icon ?? "📁";

    /// <summary>
    /// Gets whether this item is a group.
    /// </summary>
    public bool IsGroup => true;

    /// <summary>
    /// Gets the sort order for this item.
    /// </summary>
    public int SortOrder => Group.SortOrder;

    /// <summary>
    /// Gets whether any server in this group is connected.
    /// </summary>
    public bool HasConnectedServer => Servers.Any(s => s.IsConnected);

    /// <summary>
    /// Gets the total unread count across all servers in this group.
    /// </summary>
    public int TotalUnreadCount => Servers.Sum(s => s.Channels.Sum(c => c.UnreadCount));

    /// <summary>
    /// Gets the background brush for the group icon.
    /// </summary>
    public Brush InitialsBackground => new SolidColorBrush(Color.FromRgb(0x5B, 0x6E, 0xAE));

    /// <summary>
    /// Gets the initials/icon for the group.
    /// </summary>
    public string Initials => Icon;
}

/// <summary>
/// Interface for items that can appear in the server rail.
/// </summary>
public interface IServerRailItem
{
    /// <summary>Display name for the UI.</summary>
    string DisplayName { get; }

    /// <summary>Whether this item is a group.</summary>
    bool IsGroup { get; }

    /// <summary>Sort order for this item.</summary>
    int SortOrder { get; }

    /// <summary>Whether this item is selected.</summary>
    bool IsSelected { get; set; }

    /// <summary>Background brush for the icon.</summary>
    Brush InitialsBackground { get; }

    /// <summary>Initials or icon text.</summary>
    string Initials { get; }
}
