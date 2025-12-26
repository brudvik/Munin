using Munin.Core.Events;
using Munin.Core.Models;
using Munin.Core.Services;
using Munin.UI.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Munin.UI.Views;

/// <summary>
/// Window that displays detailed profile information for an IRC user.
/// Shows WHOIS data including nickname, host, real name, channels, and status.
/// </summary>
public partial class UserProfileWindow : Window
{
    private readonly IrcConnection _connection;
    private readonly string _nickname;
    private WhoisInfo? _whoisInfo;
    
    /// <summary>
    /// Gets the nickname of the user being viewed.
    /// </summary>
    public string? SelectedNickname => _nickname;
    
    /// <summary>
    /// Gets a value indicating whether a private message should be started with the user.
    /// </summary>
    public bool StartPrivateMessage { get; private set; }

    public UserProfileWindow(IrcConnection connection, string nickname, string? userHost = null)
    {
        InitializeComponent();
        
        _connection = connection;
        _nickname = nickname;
        
        // Set initial values
        NicknameText.Text = nickname;
        UserHostText.Text = userHost ?? "";
        
        // Set avatar
        var avatarUrl = GravatarService.GetGravatarUrl(nickname, userHost ?? $"{nickname}@unknown");
        try
        {
            AvatarBrush.ImageSource = new BitmapImage(new Uri(avatarUrl));
        }
        catch { }
        
        // Subscribe to WHOIS response
        _connection.WhoisReceived += OnWhoisReceived;
        
        // Show loading
        LoadingText.Visibility = Visibility.Visible;
        ButtonsPanel.Visibility = Visibility.Collapsed;
        
        // Request WHOIS
        _ = _connection.SendRawAsync($"WHOIS {nickname}");
    }

    /// <summary>
    /// Handles the WHOIS response from the server and updates the UI with user information.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The WHOIS event arguments containing user information.</param>
    private void OnWhoisReceived(object? sender, IrcWhoisEventArgs e)
    {
        if (!e.Info.Nickname.Equals(_nickname, StringComparison.OrdinalIgnoreCase))
            return;
            
        _whoisInfo = e.Info;
        
        Dispatcher.Invoke(() =>
        {
            // Hide loading
            LoadingText.Visibility = Visibility.Collapsed;
            ButtonsPanel.Visibility = Visibility.Visible;
            
            // Update UI
            UserHostText.Text = e.Info.UserHost;
            RealNameText.Text = e.Info.RealName ?? "-";
            ServerText.Text = e.Info.Server ?? "-";
            AccountText.Text = e.Info.Account ?? "(not identified)";
            IdleText.Text = e.Info.FormattedIdleTime;
            SignonText.Text = e.Info.SignonTime?.ToString("g") ?? "-";
            
            // Away status
            if (e.Info.IsAway)
            {
                AwayLabel.Visibility = Visibility.Visible;
                AwayText.Visibility = Visibility.Visible;
                AwayText.Text = e.Info.AwayMessage ?? "Away";
                StatusBadge.Text = "🔸 Away";
                StatusBadge.Foreground = System.Windows.Media.Brushes.Orange;
            }
            else
            {
                StatusBadge.Text = "🟢 Online";
                StatusBadge.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            
            // Operator status
            if (e.Info.IsOperator)
            {
                StatusBadge.Text = "⭐ IRC Operator";
                StatusBadge.Foreground = System.Windows.Media.Brushes.Gold;
            }
            
            // Secure connection
            if (e.Info.IsSecure)
            {
                StatusBadge.Text += " 🔒";
            }
            
            // Channels
            ChannelsText.Text = e.Info.Channels.Count > 0 
                ? string.Join(", ", e.Info.Channels) 
                : "No shared channels";
        });
    }

    private void SendMessage_Click(object sender, RoutedEventArgs e)
    {
        StartPrivateMessage = true;
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Handles mouse drag on the custom title bar to move the window.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            return; // No maximize for dialogs
        
        DragMove();
    }

    /// <summary>
    /// Handles the close button click in the custom title bar.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _connection.WhoisReceived -= OnWhoisReceived;
        base.OnClosed(e);
    }
}
