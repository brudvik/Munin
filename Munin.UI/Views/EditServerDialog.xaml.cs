using System.Windows;
using System.Windows.Input;
using Munin.Core.Models;

namespace Munin.UI.Views;

/// <summary>
/// Dialog window for editing an existing IRC server configuration.
/// Allows modification of connection settings, credentials, and options.
/// </summary>
public partial class EditServerDialog : Window
{
    /// <summary>
    /// Gets the server configuration being edited.
    /// </summary>
    public IrcServer Server { get; private set; }
    
    /// <summary>
    /// Gets a value indicating whether changes were made to the server configuration.
    /// </summary>
    public bool HasChanges { get; private set; }

    public EditServerDialog(IrcServer server)
    {
        InitializeComponent();
        Server = server;
        LoadServerData();
    }

    /// <summary>
    /// Loads the server configuration data into the form fields.
    /// </summary>
    private void LoadServerData()
    {
        ServerNameTextBox.Text = Server.Name;
        HostnameTextBox.Text = Server.Hostname;
        PortTextBox.Text = Server.Port.ToString();
        UseSslCheckBox.IsChecked = Server.UseSsl;
        AcceptInvalidCertsCheckBox.IsChecked = Server.AcceptInvalidCertificates;
        PreferIPv6CheckBox.IsChecked = Server.PreferIPv6;
        NicknameTextBox.Text = Server.Nickname;
        UsernameTextBox.Text = Server.Username;
        RealNameTextBox.Text = Server.RealName;
        AutoConnectCheckBox.IsChecked = Server.AutoConnect;
        SaslUsernameTextBox.Text = Server.SaslUsername ?? "";
        
        // Note: Passwords are not loaded for security - user must re-enter if changing
    }

    /// <summary>
    /// Handles the Save button click. Validates input and updates the server configuration.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The routed event arguments.</param>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
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

        // Update server properties
        Server.Name = ServerNameTextBox.Text.Trim();
        Server.Hostname = HostnameTextBox.Text.Trim();
        Server.Port = port;
        Server.UseSsl = UseSslCheckBox.IsChecked == true;
        Server.AcceptInvalidCertificates = AcceptInvalidCertsCheckBox.IsChecked == true;
        Server.PreferIPv6 = PreferIPv6CheckBox.IsChecked == true;
        Server.Nickname = NicknameTextBox.Text.Trim();
        Server.Username = string.IsNullOrWhiteSpace(UsernameTextBox.Text) 
            ? NicknameTextBox.Text.Trim() 
            : UsernameTextBox.Text.Trim();
        Server.RealName = string.IsNullOrWhiteSpace(RealNameTextBox.Text) 
            ? NicknameTextBox.Text.Trim() 
            : RealNameTextBox.Text.Trim();
        Server.AutoConnect = AutoConnectCheckBox.IsChecked == true;

        // Only update passwords if user entered something
        if (!string.IsNullOrEmpty(PasswordBox.Password))
        {
            Server.Password = PasswordBox.Password;
        }
        if (!string.IsNullOrEmpty(NickServPasswordBox.Password))
        {
            Server.NickServPassword = NickServPasswordBox.Password;
        }
        
        // SASL credentials
        if (!string.IsNullOrWhiteSpace(SaslUsernameTextBox.Text))
        {
            Server.SaslUsername = SaslUsernameTextBox.Text.Trim();
        }
        if (!string.IsNullOrEmpty(SaslPasswordBox.Password))
        {
            Server.SaslPassword = SaslPasswordBox.Password;
        }

        HasChanges = true;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Handles the Cancel button click. Closes the dialog without saving changes.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The routed event arguments.</param>
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
