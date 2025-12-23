using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace IrcClient.UI.Views;

/// <summary>
/// Window that displays raw IRC protocol messages for debugging.
/// Shows incoming and outgoing messages with timestamps and direction indicators.
/// </summary>
public partial class RawIrcLogWindow : Window
{
    /// <summary>
    /// Gets the collection of raw IRC log entries.
    /// </summary>
    public ObservableCollection<RawIrcEntry> LogEntries { get; } = new();
    
    private const int MaxEntries = 5000;

    public RawIrcLogWindow()
    {
        InitializeComponent();
        LogListBox.ItemsSource = LogEntries;

        LogEntries.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && AutoScrollCheckBox.IsChecked == true)
            {
                if (LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            }
            CountText.Text = $"{LogEntries.Count} messages";
        };
    }

    /// <summary>
    /// Adds a new entry to the log window.
    /// </summary>
    /// <param name="isOutgoing">True if the message was sent, false if received.</param>
    /// <param name="message">The raw IRC message text.</param>
    public void AddEntry(bool isOutgoing, string message)
    {
        Dispatcher.Invoke(() =>
        {
            // Trim old entries if needed
            while (LogEntries.Count >= MaxEntries)
            {
                LogEntries.RemoveAt(0);
            }

            LogEntries.Add(new RawIrcEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                IsOutgoing = isOutgoing,
                Direction = isOutgoing ? "→" : "←",
                Message = message.TrimEnd('\r', '\n')
            });
        });
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        LogEntries.Clear();
    }
}

/// <summary>
/// Represents a single entry in the raw IRC log.
/// </summary>
public class RawIrcEntry
{
    /// <summary>
    /// Gets or sets the formatted timestamp of the message.
    /// </summary>
    public string Timestamp { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets a value indicating whether the message was outgoing.
    /// </summary>
    public bool IsOutgoing { get; set; }
    
    /// <summary>
    /// Gets or sets the direction indicator (arrow).
    /// </summary>
    public string Direction { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the raw message text.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
