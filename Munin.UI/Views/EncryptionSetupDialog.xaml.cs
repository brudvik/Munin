using System.Windows;
using System.Windows.Input;
using Munin.UI.Resources;

namespace Munin.UI.Views;

/// <summary>
/// Dialog for setting up encryption for the first time.
/// Allows users to create a master password or skip encryption.
/// </summary>
public partial class EncryptionSetupDialog : Window
{
    /// <summary>
    /// Gets the password if encryption was enabled.
    /// </summary>
    public string? Password { get; private set; }
    
    /// <summary>
    /// Gets whether the user chose to enable encryption.
    /// </summary>
    public bool EnableEncryption { get; private set; }

    public EncryptionSetupDialog()
    {
        InitializeComponent();
        PasswordBox.Focus();
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
    /// Handles the close button click in the custom title bar.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate password
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ShowError(Strings.EncryptionSetup_PasswordEmpty);
            return;
        }
        
        if (PasswordBox.Password.Length < 8)
        {
            ShowError(Strings.EncryptionSetup_PasswordTooShort);
            return;
        }
        
        if (PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ShowError(Strings.EncryptionSetup_PasswordMismatch);
            return;
        }
        
        // Check password strength
        if (!IsPasswordStrong(PasswordBox.Password))
        {
            ShowError(Strings.EncryptionSetup_PasswordWeak);
            return;
        }
        
        Password = PasswordBox.Password;
        EnableEncryption = true;
        DialogResult = true;
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            Strings.EncryptionSetup_SkipConfirmTitle + "\n\n" + Strings.EncryptionSetup_SkipConfirmMessage,
            Strings.Confirm,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            EnableEncryption = false;
            DialogResult = true;
            Close();
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static bool IsPasswordStrong(string password)
    {
        // Require at least one of: uppercase, digit, or special character
        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));
        
        // Must have lowercase and at least one other type
        int complexity = (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
        
        return hasLower && complexity >= 1;
    }
}
