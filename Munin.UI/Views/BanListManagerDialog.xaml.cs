using Munin.Core.Models;
using Munin.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Display item for ban list entries with formatted display properties.
/// </summary>
public class BanListDisplayItem
{
    public ChannelListModeEntry Entry { get; set; } = null!;
    public string Mask => Entry.Mask;
    public string SetBy => Entry.SetBy ?? "-";
    public string SetAtDisplay => Entry.SetAt?.ToString("yyyy-MM-dd HH:mm") ?? "-";
}

/// <summary>
/// Dialog for managing channel ban lists, exceptions, and invite lists.
/// </summary>
public partial class BanListManagerDialog : Window
{
    private readonly IrcConnection _connection;
    private readonly string _channelName;
    
    private ObservableCollection<BanListDisplayItem> _bans = new();
    private ObservableCollection<BanListDisplayItem> _exceptions = new();
    private ObservableCollection<BanListDisplayItem> _invites = new();
    
    private char _currentMode = 'b';

    /// <summary>
    /// Creates a new ban list manager dialog.
    /// </summary>
    /// <param name="connection">The IRC connection.</param>
    /// <param name="channelName">The channel name.</param>
    public BanListManagerDialog(IrcConnection connection, string channelName)
    {
        InitializeComponent();
        
        _connection = connection;
        _channelName = channelName;
        
        ChannelNameText.Text = channelName;
        EntryListView.ItemsSource = _bans;
        
        // Subscribe to mode list events
        _connection.ChannelModeListReceived += OnModeListReceived;
        
        Loaded += OnLoaded;
        Closed += (s, e) => _connection.ChannelModeListReceived -= OnModeListReceived;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Request all lists
        await RequestListAsync('b');
        await RequestListAsync('e');
        await RequestListAsync('I');
        
        UpdateListView();
    }

    private async Task RequestListAsync(char mode)
    {
        LoadingText.Visibility = Visibility.Visible;
        await _connection.SendRawAsync($"MODE {_channelName} +{mode}");
        // Give server time to respond
        await Task.Delay(500);
        LoadingText.Visibility = Visibility.Collapsed;
    }

    private void OnModeListReceived(object? sender, Core.Events.IrcChannelModeListEventArgs e)
    {
        if (!e.Channel.Equals(_channelName, StringComparison.OrdinalIgnoreCase))
            return;

        App.Current.Dispatcher.Invoke(() =>
        {
            var item = new BanListDisplayItem { Entry = e.Entry };
            
            switch (e.Entry.Mode)
            {
                case 'b':
                    if (!_bans.Any(b => b.Mask == item.Mask))
                        _bans.Add(item);
                    break;
                case 'e':
                    if (!_exceptions.Any(b => b.Mask == item.Mask))
                        _exceptions.Add(item);
                    break;
                case 'I':
                    if (!_invites.Any(b => b.Mask == item.Mask))
                        _invites.Add(item);
                    break;
            }
            
            UpdateStatusText();
        });
    }

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (TabBans?.IsChecked == true)
        {
            _currentMode = 'b';
            EntryListView.ItemsSource = _bans;
            NewMaskInput.Text = "*!*@";
        }
        else if (TabExceptions?.IsChecked == true)
        {
            _currentMode = 'e';
            EntryListView.ItemsSource = _exceptions;
            NewMaskInput.Text = "*!*@";
        }
        else if (TabInvites?.IsChecked == true)
        {
            _currentMode = 'I';
            EntryListView.ItemsSource = _invites;
            NewMaskInput.Text = "";
        }
        
        UpdateListView();
    }

    private void UpdateListView()
    {
        var collection = _currentMode switch
        {
            'b' => _bans,
            'e' => _exceptions,
            'I' => _invites,
            _ => _bans
        };
        
        EmptyText.Visibility = collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        var count = _currentMode switch
        {
            'b' => _bans.Count,
            'e' => _exceptions.Count,
            'I' => _invites.Count,
            _ => 0
        };
        
        StatusText.Text = $"{count} entries";
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var mask = NewMaskInput.Text.Trim();
        if (string.IsNullOrEmpty(mask)) return;
        
        await _connection.SendRawAsync($"MODE {_channelName} +{_currentMode} {mask}");
        NewMaskInput.Text = _currentMode == 'I' ? "" : "*!*@";
        
        // Refresh the list
        await Task.Delay(300);
        await RequestListAsync(_currentMode);
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (EntryListView.SelectedItem is not BanListDisplayItem item) return;
        
        await _connection.SendRawAsync($"MODE {_channelName} -{_currentMode} {item.Mask}");
        
        // Remove from local list
        switch (_currentMode)
        {
            case 'b': _bans.Remove(item); break;
            case 'e': _exceptions.Remove(item); break;
            case 'I': _invites.Remove(item); break;
        }
        
        UpdateListView();
    }

    #region Window Chrome
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        DragMove();
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion
}
