using Munin.Core.Services;
using Munin.UI.Services;
using Serilog;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Munin.UI.Views;

/// <summary>
/// The main application window for the Munin.
/// Provides the primary user interface for chat, server management, and navigation.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger _logger;
    
    public MainWindow()
    {
        InitializeComponent();
        _logger = SerilogConfig.ForContext<MainWindow>();
        
        // Update maximize icon when window state changes
        StateChanged += MainWindow_StateChanged;
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
    /// Sets up automatic scrolling to the bottom when new messages arrive.
    /// </summary>
    /// <param name="sender">The event source (ListBox).</param>
    /// <param name="e">The routed event arguments.</param>
    private void MessagesListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            // Auto-scroll to bottom when new messages arrive
            if (listBox.ItemsSource is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += (s, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Add && listBox.Items.Count > 0)
                    {
                        listBox.ScrollIntoView(listBox.Items[^1]);
                    }
                };
            }
        }
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
}
