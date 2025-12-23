using System.Windows;
using IrcClient.Core.Models;

namespace IrcClient.UI.Views;

/// <summary>
/// Dialog window for adding a new IRC server configuration.
/// Collects server connection details, authentication credentials, and auto-join channels.
/// </summary>
public partial class AddServerDialog : Window
{
    /// <summary>
    /// Gets the configured server if the dialog was confirmed, or null if cancelled.
    /// </summary>
    public IrcServer? Server { get; private set; }

    public AddServerDialog()
    {
        InitializeComponent();
        
        // Generate random nickname
        NicknameTextBox.Text = $"User{Random.Shared.Next(1000, 9999)}";
    }

    /// <summary>
    /// Handles the Add button click. Validates input and creates the server configuration.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The routed event arguments.</param>
    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerNameTextBox.Text) ||
            string.IsNullOrWhiteSpace(HostnameTextBox.Text) ||
            string.IsNullOrWhiteSpace(NicknameTextBox.Text))
        {
            MessageBox.Show("Please fill in all required fields.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PortTextBox.Text, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Please enter a valid port number (1-65535).", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Server = new IrcServer
        {
            Name = ServerNameTextBox.Text.Trim(),
            Hostname = HostnameTextBox.Text.Trim(),
            Port = port,
            UseSsl = UseSslCheckBox.IsChecked == true,
            AcceptInvalidCertificates = AcceptInvalidCertsCheckBox.IsChecked == true,
            Nickname = NicknameTextBox.Text.Trim(),
            Username = string.IsNullOrWhiteSpace(UsernameTextBox.Text) 
                ? NicknameTextBox.Text.Trim() 
                : UsernameTextBox.Text.Trim(),
            RealName = string.IsNullOrWhiteSpace(RealNameTextBox.Text) 
                ? NicknameTextBox.Text.Trim() 
                : RealNameTextBox.Text.Trim(),
            Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password,
            SaslUsername = string.IsNullOrWhiteSpace(SaslUsernameTextBox.Text) ? null : SaslUsernameTextBox.Text.Trim(),
            SaslPassword = string.IsNullOrEmpty(SaslPasswordBox.Password) ? null : SaslPasswordBox.Password,
            AutoJoinChannels = AutoJoinTextBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c.StartsWith('#') || c.StartsWith('&'))
                .ToList(),
            AutoConnect = AutoConnectCheckBox.IsChecked == true
        };

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Handles the Cancel button click. Closes the dialog without saving.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The routed event arguments.</param>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
