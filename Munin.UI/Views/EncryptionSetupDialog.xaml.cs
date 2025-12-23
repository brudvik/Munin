using System.Windows;

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

    private void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate password
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ShowError("Passord kan ikke være tomt.");
            return;
        }
        
        if (PasswordBox.Password.Length < 8)
        {
            ShowError("Passord må være minst 8 tegn.");
            return;
        }
        
        if (PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ShowError("Passordene samsvarer ikke.");
            return;
        }
        
        // Check password strength
        if (!IsPasswordStrong(PasswordBox.Password))
        {
            ShowError("Passordet er for svakt. Bruk store/små bokstaver, tall eller spesialtegn.");
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
            "Er du sikker på at du vil fortsette uten kryptering?\n\n" +
            "Dine serverpassord og chat-logger vil lagres ukryptert på disk.",
            "Bekreft",
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
