using System.Windows;

namespace Munin.UI.Views;

/// <summary>
/// Dialog for changing the master password.
/// </summary>
public partial class ChangePasswordDialog : Window
{
    /// <summary>
    /// Gets the current password.
    /// </summary>
    public string? CurrentPassword { get; private set; }
    
    /// <summary>
    /// Gets the new password.
    /// </summary>
    public string? NewPassword { get; private set; }

    public ChangePasswordDialog()
    {
        InitializeComponent();
        CurrentPasswordBox.Focus();
    }

    private void ChangeButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate
        if (string.IsNullOrEmpty(CurrentPasswordBox.Password))
        {
            ShowError("Skriv inn nåværende passord.");
            return;
        }
        
        if (string.IsNullOrEmpty(NewPasswordBox.Password))
        {
            ShowError("Skriv inn nytt passord.");
            return;
        }
        
        if (NewPasswordBox.Password.Length < 8)
        {
            ShowError("Nytt passord må være minst 8 tegn.");
            return;
        }
        
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ShowError("Passordene samsvarer ikke.");
            return;
        }
        
        if (NewPasswordBox.Password == CurrentPasswordBox.Password)
        {
            ShowError("Nytt passord må være forskjellig fra nåværende.");
            return;
        }
        
        CurrentPassword = CurrentPasswordBox.Password;
        NewPassword = NewPasswordBox.Password;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
