using Munin.UI.ViewModels;
using System.Windows;

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
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
