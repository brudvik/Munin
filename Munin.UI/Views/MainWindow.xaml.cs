using Munin.Core.Services;
using Munin.UI.Services;
using Serilog;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
    }

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
}
