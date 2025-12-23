using CommunityToolkit.Mvvm.ComponentModel;
using Munin.Core.Models;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace Munin.UI.ViewModels;

/// <summary>
/// ViewModel for an IRC channel, private message, or server console.
/// </summary>
/// <remarks>
/// <para>Provides UI-bindable properties for:</para>
/// <list type="bullet">
///   <item><description>Message history</description></item>
///   <item><description>User list with prefixes</description></item>
///   <item><description>Unread message counts and mention tracking</description></item>
///   <item><description>Channel statistics</description></item>
/// </list>
/// </remarks>
public partial class ChannelViewModel : ObservableObject
{
    /// <summary>
    /// The underlying IRC channel model.
    /// </summary>
    [ObservableProperty]
    private IrcChannel _channel;

    /// <summary>
    /// The parent server ViewModel.
    /// </summary>
    [ObservableProperty]
    private ServerViewModel _serverViewModel;

    /// <summary>
    /// Collection of messages for display.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MessageViewModel> _messages = new();

    /// <summary>
    /// Collection of users in the channel.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<UserViewModel> _users = new();

    /// <summary>
    /// Number of unread messages.
    /// </summary>
    [ObservableProperty]
    private int _unreadCount;

    /// <summary>
    /// Whether there are unread messages that mention the user.
    /// </summary>
    [ObservableProperty]
    private bool _hasMention;

    /// <summary>
    /// Whether this is the server console (not a real channel).
    /// </summary>
    [ObservableProperty]
    private bool _isServerConsole;

    /// <summary>
    /// Whether this is a private message window.
    /// </summary>
    [ObservableProperty]
    private bool _isPrivateMessage;

    /// <summary>
    /// For private messages, this holds the other user's nickname.
    /// </summary>
    [ObservableProperty]
    private string _privateMessageTarget = string.Empty;
    
    /// <summary>
    /// Whether this channel is currently selected/active.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
    
    /// <summary>
    /// Message count per user (nickname -> count).
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _userMessageCounts = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Total message count in this channel.
    /// </summary>
    [ObservableProperty]
    private int _totalMessageCount;
    
    /// <summary>
    /// Channel creation time for stats.
    /// </summary>
    public DateTime JoinedAt { get; } = DateTime.Now;

    public string DisplayName => IsPrivateMessage ? PrivateMessageTarget : Channel.Name;
    
    public string DisplayNameWithBadge => UnreadCount > 0 
        ? $"{DisplayName} ({UnreadCount})" 
        : DisplayName;

    public string ChannelIcon => IsServerConsole ? "📡" : IsPrivateMessage ? "💬" : "#";

    public ChannelViewModel(IrcChannel channel, ServerViewModel serverViewModel)
    {
        _channel = channel;
        _serverViewModel = serverViewModel;
        RefreshUsers();
    }

    public void RefreshUsers()
    {
        Users.Clear();
        var sortedUsers = Channel.Users
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Nickname, StringComparer.OrdinalIgnoreCase);

        foreach (var user in sortedUsers)
        {
            Users.Add(new UserViewModel(user));
        }
    }
    
    /// <summary>
    /// Adds a system message to the channel (e.g., reconnection notices).
    /// </summary>
    public void AddSystemMessage(string text)
    {
        var message = IrcMessage.CreateSystem(text);
        Messages.Add(new MessageViewModel(message));
    }
    
    /// <summary>
    /// Tracks a message from a user for statistics.
    /// </summary>
    public void TrackMessage(string? nickname)
    {
        TotalMessageCount++;
        
        if (!string.IsNullOrEmpty(nickname))
        {
            _userMessageCounts.AddOrUpdate(nickname, 1, (_, count) => count + 1);
        }
    }
    
    /// <summary>
    /// Gets message count for a specific user.
    /// </summary>
    public int GetUserMessageCount(string nickname)
    {
        return _userMessageCounts.TryGetValue(nickname, out var count) ? count : 0;
    }
    
    /// <summary>
    /// Gets top N chatters in the channel.
    /// </summary>
    public IEnumerable<(string Nickname, int Count)> GetTopChatters(int count = 10)
    {
        return _userMessageCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(count)
            .Select(kvp => (kvp.Key, kvp.Value));
    }
    
    /// <summary>
    /// Gets channel statistics summary.
    /// </summary>
    public ChannelStats GetStats()
    {
        var duration = DateTime.Now - JoinedAt;
        return new ChannelStats
        {
            ChannelName = DisplayName,
            TotalMessages = TotalMessageCount,
            UniqueUsers = _userMessageCounts.Count,
            MessagesPerMinute = duration.TotalMinutes > 0 ? TotalMessageCount / duration.TotalMinutes : 0,
            JoinedAt = JoinedAt,
            Duration = duration,
            TopChatters = GetTopChatters(5).ToList()
        };
    }

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayNameWithBadge));
    }
}
