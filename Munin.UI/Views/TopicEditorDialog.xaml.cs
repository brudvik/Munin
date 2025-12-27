using System.Windows;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Dialog for editing a channel topic with optional FiSH encryption.
/// </summary>
public partial class TopicEditorDialog : Window
{
    /// <summary>
    /// The topic text entered by the user.
    /// </summary>
    public string TopicText => TopicTextBox.Text;
    
    /// <summary>
    /// Whether the user wants to encrypt the topic.
    /// </summary>
    public bool EncryptTopic => EncryptCheckBox.IsChecked == true;
    
    /// <summary>
    /// Creates a new topic editor dialog.
    /// </summary>
    /// <param name="currentTopic">The current topic to pre-fill.</param>
    /// <param name="hasEncryptionKey">Whether the channel has an encryption key set.</param>
    public TopicEditorDialog(string? currentTopic, bool hasEncryptionKey)
    {
        InitializeComponent();
        
        TopicTextBox.Text = currentTopic ?? string.Empty;
        TopicTextBox.SelectAll();
        TopicTextBox.Focus();
        
        // Show encryption option only if channel has encryption key
        if (hasEncryptionKey)
        {
            EncryptionPanel.Visibility = Visibility.Visible;
        }
    }
    
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    private void SetTopicButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
