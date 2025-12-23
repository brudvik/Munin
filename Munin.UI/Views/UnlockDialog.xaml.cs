using System.Windows;
using System.Windows.Input;
using Munin.UI.Resources;

namespace Munin.UI.Views;

/// <summary>
/// Dialog for unlocking encrypted storage with the master password.
/// Provides brute-force protection with attempt limiting.
/// </summary>
public partial class UnlockDialog : Window
{
    private int _attemptCount;
    private const int MaxAttempts = 10;
    private DateTime? _lockoutUntil;
    
    /// <summary>
    /// Gets the password if unlock was successful.
    /// </summary>
    public string? Password { get; private set; }
    
    /// <summary>
    /// Gets whether the user chose to reset all data.
    /// </summary>
    public bool ResetRequested { get; private set; }

    /// <summary>
    /// Event handler for validating the password.
    /// </summary>
    public Func<string, bool>? ValidatePassword { get; set; }

    public UnlockDialog()
    {
        InitializeComponent();
        PasswordBox.Focus();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UnlockButton_Click(sender, e);
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        // Check lockout
        if (_lockoutUntil.HasValue && DateTime.Now < _lockoutUntil.Value)
        {
            var remaining = (_lockoutUntil.Value - DateTime.Now).TotalSeconds;
            ShowError(string.Format(Strings.Unlock_TooManyAttempts, remaining.ToString("F0")));
            return;
        }
        
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ShowError(Strings.Unlock_EnterPassword);
            return;
        }
        
        // Validate password
        bool isValid = ValidatePassword?.Invoke(PasswordBox.Password) ?? true;
        
        if (isValid)
        {
            Password = PasswordBox.Password;
            DialogResult = true;
            Close();
        }
        else
        {
            _attemptCount++;
            
            if (_attemptCount >= MaxAttempts)
            {
                // Lock out for increasing time
                var lockoutSeconds = Math.Min(300, 30 * (_attemptCount - MaxAttempts + 1));
                _lockoutUntil = DateTime.Now.AddSeconds(lockoutSeconds);
                ShowError(string.Format(Strings.Unlock_TooManyAttempts, lockoutSeconds.ToString()));
            }
            else
            {
                ShowError(Strings.Unlock_WrongPassword);
                AttemptsText.Text = string.Format(Strings.Unlock_AttemptsCount, _attemptCount, MaxAttempts);
                AttemptsText.Visibility = Visibility.Visible;
            }
            
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private void ResetLink_Click(object sender, MouseButtonEventArgs e)
    {
        var result = MessageBox.Show(
            Strings.Unlock_ResetWarning,
            Strings.Unlock_ResetConfirmTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            // Double confirmation
            var confirm = MessageBox.Show(
                Strings.Unlock_ResetFinalConfirm,
                Strings.Unlock_ResetConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);
            
            if (confirm == MessageBoxResult.Yes)
            {
                ResetRequested = true;
                DialogResult = true;
                Close();
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
