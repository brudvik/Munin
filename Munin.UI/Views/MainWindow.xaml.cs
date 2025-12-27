using Munin.Core.Services;
using Munin.UI.Resources;
using Munin.UI.Services;
using Munin.UI.ViewModels;
using Serilog;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Munin.UI.Views;

/// <summary>
/// The main application window for the Munin.
/// Provides the primary user interface for chat, server management, and navigation.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger _logger;
    private bool _autoScrollEnabled = true;
    private ScrollViewer? _messagesScrollViewer;
    private INotifyCollectionChanged? _currentCollection;
    private int _unreadWhileScrolledUp;
    
    public MainWindow()
    {
        InitializeComponent();
        _logger = SerilogConfig.ForContext<MainWindow>();
        
        // Update maximize icon when window state changes
        StateChanged += MainWindow_StateChanged;
    }
    
    /// <summary>
    /// Scrolls the messages list to the bottom.
    /// </summary>
    private void ScrollToBottom()
    {
        // Force layout update first
        MessagesListBox.UpdateLayout();
        
        // Try to get ScrollViewer if not cached
        if (_messagesScrollViewer == null)
        {
            _messagesScrollViewer = FindVisualChild<ScrollViewer>(MessagesListBox);
        }
        
        if (_messagesScrollViewer != null)
        {
            _messagesScrollViewer.ScrollToEnd();
        }
        else if (MessagesListBox.Items.Count > 0)
        {
            MessagesListBox.ScrollIntoView(MessagesListBox.Items[^1]);
        }
    }

    #region Window Chrome Event Handlers
    
    /// <summary>
    /// Handles title bar mouse down for window dragging.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle maximize
            ToggleMaximize();
        }
        else
        {
            // Start drag
            if (WindowState == WindowState.Maximized)
            {
                // Restore before dragging when maximized
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                Left = point.X - Width / 2;
                Top = point.Y - 20;
            }
            DragMove();
        }
    }
    
    /// <summary>
    /// Handles title bar mouse up event.
    /// </summary>
    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Released - nothing special needed
    }
    
    /// <summary>
    /// Handles title bar mouse move for potential snap functionality.
    /// </summary>
    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        // Future: Could add window snap preview
    }
    
    /// <summary>
    /// Minimizes the window.
    /// </summary>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    
    /// <summary>
    /// Toggles between maximized and normal window state.
    /// </summary>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }
    
    /// <summary>
    /// Closes the window.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    
    /// <summary>
    /// Toggles the window between maximized and normal state.
    /// </summary>
    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }
    
    /// <summary>
    /// Updates the maximize button icon based on window state.
    /// </summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeIcon != null)
        {
            // Change icon to restore icon when maximized
            MaximizeIcon.Data = WindowState == WindowState.Maximized
                ? System.Windows.Media.Geometry.Parse("M0,3 H7 V10 H0 Z M3,0 H10 V7 H7 M3,3 V0")  // Restore icon
                : System.Windows.Media.Geometry.Parse("M0,0 H10 V10 H0 Z");  // Maximize icon
        }
    }
    
    #endregion

    /// <summary>
    /// Handles the Loaded event for the messages list box.
    /// Sets up automatic scrolling to the bottom when new messages arrive,
    /// with smart pause when user scrolls up manually.
    /// </summary>
    /// <param name="sender">The event source (ListBox).</param>
    /// <param name="e">The routed event arguments.</param>
    private void MessagesListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            // Find the ScrollViewer inside the ListBox
            _messagesScrollViewer = FindVisualChild<ScrollViewer>(listBox);
            
            if (_messagesScrollViewer != null)
            {
                _messagesScrollViewer.ScrollChanged -= MessagesScrollViewer_ScrollChanged;
                _messagesScrollViewer.ScrollChanged += MessagesScrollViewer_ScrollChanged;
            }
            
            // Unsubscribe from previous collection if exists
            if (_currentCollection != null)
            {
                _currentCollection.CollectionChanged -= OnMessagesCollectionChanged;
            }
            
            // Auto-scroll to bottom when new messages arrive
            if (listBox.ItemsSource is INotifyCollectionChanged collection)
            {
                _currentCollection = collection;
                collection.CollectionChanged += OnMessagesCollectionChanged;
            }
        }
    }
    
    /// <summary>
    /// Handles when the ItemsSource binding is updated (channel changed).
    /// </summary>
    private void MessagesListBox_TargetUpdated(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        try
        {
            if (e.Property == ItemsControl.ItemsSourceProperty && sender is ListBox listBox)
            {
                // Re-find ScrollViewer
                _messagesScrollViewer = FindVisualChild<ScrollViewer>(listBox);
                
                // Unsubscribe from previous collection
                if (_currentCollection != null)
                {
                    _currentCollection.CollectionChanged -= OnMessagesCollectionChanged;
                }
                
                // Subscribe to new collection
                if (listBox.ItemsSource is INotifyCollectionChanged collection)
                {
                    _currentCollection = collection;
                    collection.CollectionChanged += OnMessagesCollectionChanged;
                }
                
                // Reset auto-scroll and scroll to bottom after a delay
                _autoScrollEnabled = true;
                _unreadWhileScrolledUp = 0;
                
                // Wait for items to render, then scroll
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    try
                    {
                        ScrollToBottom();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in ScrollToBottom timer");
                    }
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in MessagesListBox_TargetUpdated");
        }
    }
    
    /// <summary>
    /// Handles scroll changes to detect manual scrolling.
    /// </summary>
    private void MessagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        
        // Check if we're at the bottom (with some tolerance for rounding)
        var isAtBottom = scrollViewer.ScrollableHeight <= 0 || 
                         scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 10;
        
        // Only update auto-scroll state on user-initiated scrolls (ExtentHeightChange == 0)
        // But always update the button visibility based on current position
        if (e.ExtentHeightChange == 0)
        {
            _autoScrollEnabled = isAtBottom;
            
            // Reset unread count when scrolling to bottom
            if (isAtBottom)
            {
                _unreadWhileScrolledUp = 0;
            }
        }
        
        // Always update button visibility based on current scroll position
        UpdateJumpToLatestVisibility(!isAtBottom);
    }
    
    /// <summary>
    /// Handles new messages being added to the collection.
    /// </summary>
    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Handle both Add (new messages) and Reset (channel switch with history)
        if (_autoScrollEnabled && MessagesListBox.Items.Count > 0)
        {
            // Scroll to bottom - use ScrollToEnd for reliable scrolling
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_messagesScrollViewer != null)
                    {
                        _messagesScrollViewer.ScrollToEnd();
                    }
                    else if (MessagesListBox.Items.Count > 0)
                    {
                        // Fallback if ScrollViewer not found
                        MessagesListBox.ScrollIntoView(MessagesListBox.Items[MessagesListBox.Items.Count - 1]);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error scrolling to bottom in collection changed");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Track unread messages while scrolled up (only for new messages, not history)
            _unreadWhileScrolledUp += e.NewItems?.Count ?? 0;
            UpdateJumpToLatestVisibility();
        }
    }
    
    /// <summary>
    /// Updates visibility of the Jump to Latest button and shows unread count.
    /// </summary>
    /// <param name="showButton">Whether to show the button (optional, uses _autoScrollEnabled if not specified).</param>
    private void UpdateJumpToLatestVisibility(bool? showButton = null)
    {
        if (JumpToLatestButton != null)
        {
            var shouldShow = showButton ?? !_autoScrollEnabled;
            JumpToLatestButton.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            
            // Update the button content to show unread count if any
            if (shouldShow && _unreadWhileScrolledUp > 0)
            {
                var panel = JumpToLatestButton.Content as StackPanel;
                if (panel?.Children.Count > 1 && panel.Children[1] is TextBlock textBlock)
                {
                    // Use localization service to get the base text
                    var baseText = LocalizationService.Instance.GetString("MainWindow_JumpToLatest");
                    textBlock.Text = $"{baseText} ({_unreadWhileScrolledUp})";
                }
            }
            else if (shouldShow)
            {
                // Reset to base text when no unread count
                var panel = JumpToLatestButton.Content as StackPanel;
                if (panel?.Children.Count > 1 && panel.Children[1] is TextBlock textBlock)
                {
                    textBlock.Text = LocalizationService.Instance.GetString("MainWindow_JumpToLatest");
                }
            }
        }
    }
    
    /// <summary>
    /// Jumps to the latest message and re-enables auto-scroll.
    /// </summary>
    private void JumpToLatestButton_Click(object sender, RoutedEventArgs e)
    {
        _autoScrollEnabled = true;
        _unreadWhileScrolledUp = 0;
        UpdateJumpToLatestVisibility();
        
        // Use ScrollToEnd for reliable scrolling to bottom
        if (_messagesScrollViewer != null)
        {
            _messagesScrollViewer.ScrollToEnd();
        }
        else if (MessagesListBox.Items.Count > 0)
        {
            MessagesListBox.ScrollIntoView(MessagesListBox.Items[^1]);
        }
    }
    
    /// <summary>
    /// Finds a child element of the specified type in the visual tree.
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
                
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }
    
    /// <summary>
    /// Handles mouse click events on link previews.
    /// Opens the URL in the default system browser.
    /// </summary>
    /// <param name="sender">The clicked element.</param>
    /// <param name="e">The mouse button event arguments.</param>
    private void LinkPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is LinkPreview preview)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = preview.Url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to open URL: {Url}", preview.Url);
            }
        }
    }
    
    #region Context Menu Handlers
    
    /// <summary>
    /// Copies the message content to clipboard.
    /// </summary>
    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromContextMenu(sender) is { } msg)
        {
            try
            {
                Clipboard.SetText(msg.Content);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to copy message to clipboard");
            }
        }
    }
    
    /// <summary>
    /// Copies the nickname to clipboard.
    /// </summary>
    private void CopyNickname_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromContextMenu(sender) is { Source: not null } msg)
        {
            try
            {
                Clipboard.SetText(msg.Source);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to copy nickname to clipboard");
            }
        }
    }
    
    /// <summary>
    /// Sends a /whois command for the message sender.
    /// </summary>
    private void WhoisUser_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromContextMenu(sender) is { Source: not null } msg && DataContext is MainViewModel vm)
        {
            vm.MessageInput = $"/whois {msg.Source}";
            vm.SendMessageCommand.Execute(null);
        }
    }
    
    /// <summary>
    /// Opens a private message window with the user.
    /// </summary>
    private void OpenQuery_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromContextMenu(sender) is { Source: not null } msg && DataContext is MainViewModel vm)
        {
            vm.MessageInput = $"/query {msg.Source}";
            vm.SendMessageCommand.Execute(null);
        }
    }
    
    /// <summary>
    /// Ignores the message sender.
    /// </summary>
    private void IgnoreUser_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromContextMenu(sender) is { Source: not null } msg && DataContext is MainViewModel vm)
        {
            vm.MessageInput = $"/ignore {msg.Source}";
            vm.SendMessageCommand.Execute(null);
        }
    }
    
    /// <summary>
    /// Quotes the message and sets up a reply.
    /// </summary>
    private void QuoteReply_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromContextMenu(sender) is { } msg && DataContext is MainViewModel vm)
        {
            // Format: <nick> message (first 50 chars if long)
            var quotedContent = msg.Content.Length > 50 
                ? msg.Content[..50] + "..." 
                : msg.Content;
            var quote = $"<{msg.Source}> {quotedContent}";
            
            // Set the input with reply prefix
            vm.MessageInput = $"{msg.Source}: ";
            MessageInputTextBox.Focus();
            MessageInputTextBox.CaretIndex = vm.MessageInput.Length;
            
            // Show the quoted message in a tooltip-style way by inserting in chat
            // Actually, just prepare the reply - the user sees the quote context
        }
    }
    
    /// <summary>
    /// Adds the user to the notify list.
    /// </summary>
    private void AddToNotify_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromContextMenu(sender) is { Source: not null } msg && DataContext is MainViewModel vm)
        {
            vm.AddToNotifyList(msg.Source);
        }
    }
    
    /// <summary>
    /// Gets the MessageViewModel from a context menu item.
    /// </summary>
    private static MessageViewModel? GetMessageFromContextMenu(object sender)
    {
        if (sender is MenuItem menuItem && 
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement element)
        {
            return element.DataContext as MessageViewModel;
        }
        return null;
    }
    
    #endregion
    
    #region Message List Keyboard and Mouse Handlers
    
    /// <summary>
    /// Handles keyboard navigation in the messages list.
    /// </summary>
    private void MessagesListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_messagesScrollViewer == null) return;
        
        switch (e.Key)
        {
            case Key.PageUp:
                _messagesScrollViewer.PageUp();
                e.Handled = true;
                break;
            case Key.PageDown:
                _messagesScrollViewer.PageDown();
                e.Handled = true;
                break;
            case Key.Home when Keyboard.Modifiers == ModifierKeys.Control:
                _messagesScrollViewer.ScrollToTop();
                e.Handled = true;
                break;
            case Key.End when Keyboard.Modifiers == ModifierKeys.Control:
                JumpToLatestButton_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
    
    /// <summary>
    /// Handles double-click on message items to copy the message content.
    /// </summary>
    private void MessageItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is MessageViewModel msg)
        {
            try
            {
                Clipboard.SetText(msg.Content);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to copy message on double-click");
            }
        }
    }
    
    #endregion
    
    #region Emoji Picker
    
    /// <summary>
    /// Opens the emoji picker popup.
    /// </summary>
    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
    }
    
    /// <summary>
    /// Inserts the selected text emoji into the message input.
    /// </summary>
    private void InsertEmoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string emoji)
        {
            // Get current cursor position and insert emoji
            var caretIndex = MessageInputTextBox.CaretIndex;
            var currentText = MessageInputTextBox.Text ?? "";
            
            MessageInputTextBox.Text = currentText.Insert(caretIndex, emoji + " ");
            MessageInputTextBox.CaretIndex = caretIndex + emoji.Length + 1;
            MessageInputTextBox.Focus();
            
            EmojiPopup.IsOpen = false;
        }
    }
    
    #endregion
    
    #region Message Formatting
    
    // IRC formatting control characters
    private const char CtrlBold = '\x02';
    private const char CtrlItalic = '\x1D';
    private const char CtrlUnderline = '\x1F';
    
    /// <summary>
    /// Wraps the selected text with bold formatting, or inserts bold toggle.
    /// </summary>
    private void FormatBold_Click(object sender, RoutedEventArgs e)
    {
        InsertFormatting(CtrlBold);
    }
    
    /// <summary>
    /// Wraps the selected text with italic formatting, or inserts italic toggle.
    /// </summary>
    private void FormatItalic_Click(object sender, RoutedEventArgs e)
    {
        InsertFormatting(CtrlItalic);
    }
    
    /// <summary>
    /// Wraps the selected text with underline formatting, or inserts underline toggle.
    /// </summary>
    private void FormatUnderline_Click(object sender, RoutedEventArgs e)
    {
        InsertFormatting(CtrlUnderline);
    }
    
    /// <summary>
    /// Inserts formatting around selected text or at cursor position.
    /// </summary>
    /// <param name="formatChar">The IRC format control character.</param>
    private void InsertFormatting(char formatChar)
    {
        var selectedText = MessageInputTextBox.SelectedText;
        var selectionStart = MessageInputTextBox.SelectionStart;
        var selectionLength = MessageInputTextBox.SelectionLength;
        var currentText = MessageInputTextBox.Text ?? "";
        
        if (selectionLength > 0)
        {
            // Wrap selected text with format characters
            var formattedText = $"{formatChar}{selectedText}{formatChar}";
            MessageInputTextBox.Text = currentText.Remove(selectionStart, selectionLength)
                                                  .Insert(selectionStart, formattedText);
            MessageInputTextBox.CaretIndex = selectionStart + formattedText.Length;
        }
        else
        {
            // Insert toggle character at cursor
            MessageInputTextBox.Text = currentText.Insert(selectionStart, formatChar.ToString());
            MessageInputTextBox.CaretIndex = selectionStart + 1;
        }
        
        MessageInputTextBox.Focus();
    }
    
    #endregion
    
    #region Away Status
    
    /// <summary>
    /// Toggles away status with a popup for entering away message.
    /// </summary>
    private void AwayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedServer == null) return;
        
        var server = vm.SelectedServer;
        
        if (server.IsAway)
        {
            // Return from away
            vm.MessageInput = "/back";
            vm.SendMessageCommand.Execute(null);
        }
        else
        {
            // Show input dialog for away message
            var defaultMessage = Strings.ResourceManager.GetString("MainWindow_DefaultAwayMessage") ?? "I'm currently away";
            var setAwayTitle = Strings.ResourceManager.GetString("MainWindow_SetAway") ?? "Set Away";
            var awayMessagePrompt = Strings.ResourceManager.GetString("MainWindow_AwayMessage") ?? "Away message:";
            var dialog = new InputDialog(
                setAwayTitle,
                awayMessagePrompt,
                defaultMessage)
            {
                Owner = this
            };
            
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                vm.MessageInput = $"/away {dialog.InputText}";
                vm.SendMessageCommand.Execute(null);
            }
        }
    }
    
    #endregion
    
    #region Favorites
    
    /// <summary>
    /// Toggles the favorite status of a channel.
    /// </summary>
    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && 
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement element &&
            element.DataContext is ChannelViewModel channel)
        {
            channel.IsFavorite = !channel.IsFavorite;
            
            // Re-sort the channels to put favorites at top
            if (DataContext is MainViewModel vm && vm.SelectedServer != null)
            {
                vm.SortChannels(vm.SelectedServer);
            }
        }
    }
    
    #endregion
}
