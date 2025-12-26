using Munin.Core.Scripting.Triggers;
using Munin.UI.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Dialog for creating and editing triggers visually without coding.
/// </summary>
public partial class TriggerBuilderDialog : Window
{
    /// <summary>
    /// Gets the created or edited trigger definition.
    /// </summary>
    public TriggerDefinition CreatedTrigger { get; private set; } = new();

    private readonly TriggerDefinition? _editingTrigger;

    /// <summary>
    /// Creates a new trigger builder dialog.
    /// </summary>
    public TriggerBuilderDialog()
    {
        InitializeComponent();
        EventTypeCombo.SelectedIndex = 0;
        ActionTypeCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Creates a trigger builder dialog for editing an existing trigger.
    /// </summary>
    /// <param name="trigger">The trigger to edit.</param>
    public TriggerBuilderDialog(TriggerDefinition trigger)
    {
        InitializeComponent();
        _editingTrigger = trigger;
        
        CreateButton.Content = Strings.TriggerBuilder_Save;
        
        // Populate fields from existing trigger
        SelectComboByTag(EventTypeCombo, trigger.On);
        MatchPatternBox.Text = trigger.Match ?? string.Empty;
        SelectComboByTag(MatchTypeCombo, trigger.MatchType ?? "contains");
        ChannelFilterBox.Text = trigger.Channel ?? string.Empty;
        SelectComboByTag(ActionTypeCombo, trigger.Action ?? "reply");
        MessageTextBox.Text = trigger.Message ?? string.Empty;
        TargetChannelBox.Text = trigger.Channel ?? string.Empty;
        CommandBox.Text = trigger.Command ?? string.Empty;
        SelectComboByTag(SoundCombo, trigger.Sound ?? "default");
    }

    private void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void EventTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Show/hide match pattern based on event type
        if (EventTypeCombo.SelectedItem is ComboBoxItem item)
        {
            var eventType = item.Tag?.ToString();
            var showMatch = eventType == "message" || eventType == "topic" || eventType == "ctcp";
            
            if (MatchPatternBox != null)
            {
                MatchPatternBox.IsEnabled = showMatch;
                MatchTypeCombo.IsEnabled = showMatch;
            }
        }
    }

    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionTypeCombo.SelectedItem is ComboBoxItem item && MessagePanel != null)
        {
            var action = item.Tag?.ToString();
            
            // Show/hide relevant panels
            MessagePanel.Visibility = action is "reply" or "send" or "msg" or "notify" or "log" 
                ? Visibility.Visible 
                : Visibility.Collapsed;
            
            TargetChannelPanel.Visibility = action is "send" or "msg"
                ? Visibility.Visible 
                : Visibility.Collapsed;
            
            CommandPanel.Visibility = action == "command" 
                ? Visibility.Visible 
                : Visibility.Collapsed;
            
            SoundPanel.Visibility = action == "sound" 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate
        if (EventTypeCombo.SelectedItem is not ComboBoxItem eventItem)
        {
            MessageBox.Show("Please select an event type.", Title, MessageBoxButton.OK);
            return;
        }

        if (ActionTypeCombo.SelectedItem is not ComboBoxItem actionItem)
        {
            MessageBox.Show("Please select an action.", Title, MessageBoxButton.OK);
            return;
        }

        var action = actionItem.Tag?.ToString() ?? "reply";
        
        // Validate action-specific fields
        if (action is "reply" or "send" or "msg" or "notify" or "log" && 
            string.IsNullOrWhiteSpace(MessageTextBox.Text))
        {
            MessageBox.Show("Please enter a message.", Title, MessageBoxButton.OK);
            return;
        }

        if (action == "command" && string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            MessageBox.Show("Please enter a command.", Title, MessageBoxButton.OK);
            return;
        }

        // Build trigger
        CreatedTrigger = new TriggerDefinition
        {
            On = eventItem.Tag?.ToString() ?? "message",
            Match = string.IsNullOrWhiteSpace(MatchPatternBox.Text) ? null : MatchPatternBox.Text,
            MatchType = (MatchTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            Channel = string.IsNullOrWhiteSpace(ChannelFilterBox.Text) ? null : ChannelFilterBox.Text,
            Action = action,
            Message = action is "reply" or "send" or "msg" or "notify" or "log" 
                ? MessageTextBox.Text 
                : null,
            Command = action == "command" ? CommandBox.Text : null,
            Sound = action == "sound" 
                ? (SoundCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() 
                : null
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Handles title bar mouse down for window dragging.
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
        DialogResult = false;
        Close();
    }
}
