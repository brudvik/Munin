using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using IrcClient.Core.Events;
using IrcClient.Core.Models;
using IrcClient.Core.Services;

namespace IrcClient.UI.Views;

/// <summary>
/// Window that displays the list of available channels on an IRC server.
/// Allows searching, filtering, and joining channels.
/// </summary>
public partial class ChannelListWindow : Window
{
    private readonly IrcConnection _connection;
    private readonly ObservableCollection<ChannelListEntry> _allChannels = new();
    private readonly ObservableCollection<ChannelListEntry> _filteredChannels = new();

    /// <summary>
    /// Gets the name of the selected channel if the user chose to join one.
    /// </summary>
    public string? SelectedChannelName { get; private set; }

    public ChannelListWindow(IrcConnection connection)
    {
        InitializeComponent();
        _connection = connection;
        ChannelListView.ItemsSource = _filteredChannels;

        _connection.ChannelListReceived += OnChannelReceived;
        _connection.ChannelListComplete += OnListComplete;

        RequestChannelList();
    }

    /// <summary>
    /// Requests the channel list from the IRC server by sending the LIST command.
    /// </summary>
    private void RequestChannelList()
    {
        _allChannels.Clear();
        _filteredChannels.Clear();
        StatusText.Text = "Loading channels...";
        CountText.Text = "0 channels";
        
        // Send LIST command
        _ = _connection.SendRawAsync("LIST");
    }

    /// <summary>
    /// Handles receiving individual channel entries from the server.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The channel list event arguments containing the entry.</param>
    private void OnChannelReceived(object? sender, IrcChannelListEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _allChannels.Add(e.Entry);
            
            // Apply filter if needed
            if (MatchesFilter(e.Entry))
            {
                _filteredChannels.Add(e.Entry);
            }
            
            CountText.Text = $"{_filteredChannels.Count} of {_allChannels.Count} channels";
        });
    }

    /// <summary>
    /// Handles the completion of the channel list from the server.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnListComplete(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"Found {_allChannels.Count} channels";
        });
    }

    /// <summary>
    /// Determines if a channel entry matches the current search filter.
    /// </summary>
    /// <param name="entry">The channel entry to check.</param>
    /// <returns>True if the entry matches the filter or no filter is set.</returns>
    private bool MatchesFilter(ChannelListEntry entry)
    {
        var filter = SearchTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(filter))
            return true;

        return entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               entry.Topic.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies the current search filter to the channel list.
    /// </summary>
    private void ApplyFilter()
    {
        _filteredChannels.Clear();
        foreach (var channel in _allChannels)
        {
            if (MatchesFilter(channel))
            {
                _filteredChannels.Add(channel);
            }
        }
        CountText.Text = $"{_filteredChannels.Count} of {_allChannels.Count} channels";
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RequestChannelList();
    }

    private void JoinButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelListView.SelectedItem is ChannelListEntry entry)
        {
            SelectedChannelName = entry.Name;
            DialogResult = true;
            Close();
        }
    }

    private void ChannelListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChannelListView.SelectedItem is ChannelListEntry entry)
        {
            SelectedChannelName = entry.Name;
            DialogResult = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _connection.ChannelListReceived -= OnChannelReceived;
        _connection.ChannelListComplete -= OnListComplete;
        base.OnClosed(e);
    }
}
