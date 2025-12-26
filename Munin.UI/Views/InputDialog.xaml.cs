using System.Windows;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Simple input dialog for getting text input from the user.
/// </summary>
public partial class InputDialog : Window
{
    /// <summary>
    /// Gets the text entered by the user.
    /// </summary>
    public string InputText => InputTextBox.Text;

    /// <summary>
    /// Creates a new input dialog.
    /// </summary>
    /// <param name="title">The window title.</param>
    /// <param name="prompt">The prompt text to display.</param>
    /// <param name="defaultValue">The default value in the text box.</param>
    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        
        Title = title;
        TitleText.Text = title;
        PromptText.Text = prompt;
        InputTextBox.Text = defaultValue;
        
        Loaded += (s, e) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };
    }

    #region Window Chrome
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return; // No maximize for dialogs
        DragMove();
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    #endregion

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            System.Windows.MessageBox.Show("Please enter a value.", Title, MessageBoxButton.OK);
            return;
        }
        
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
