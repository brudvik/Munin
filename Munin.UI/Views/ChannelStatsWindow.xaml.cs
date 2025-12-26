using Munin.UI.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Window that displays statistics for an IRC channel.
/// Shows message counts, top chatters, and activity metrics.
/// </summary>
public partial class ChannelStatsWindow : Window
{
    public ChannelStatsWindow(ChannelStats stats)
    {
        InitializeComponent();
        DataContext = stats;
        
        // Populate top chatters with rank
        var rankedChatters = stats.TopChatters
            .Select((c, i) => new { Rank = i + 1, c.Nickname, c.Count })
            .ToList();
        TopChattersItems.ItemsSource = rankedChatters;
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
    /// Handles the close button click.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
