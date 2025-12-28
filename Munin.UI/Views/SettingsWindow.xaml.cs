using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Munin.Core.Services;

namespace Munin.UI.Views;

/// <summary>
/// Window for configuring application settings.
/// Allows users to customize notifications, highlight words, ignored users, and encryption.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ConfigurationService _configService;

    public SettingsWindow(ConfigurationService configService)
    {
        InitializeComponent();
        _configService = configService;
        LoadSettings();
        LoadEncryptionStatus();
        LoadPortableModeStatus();
    }

    /// <summary>
    /// Loads the current settings from the configuration service into the form fields.
    /// </summary>
    private void LoadSettings()
    {
        var settings = _configService.Configuration.Settings;
        
        EnableSoundCheckBox.IsChecked = settings.EnableSoundNotifications;
        EnableFlashCheckBox.IsChecked = settings.EnableFlashNotifications;
        OnlyWhenInactiveCheckBox.IsChecked = settings.OnlyNotifyWhenInactive;
        
        HighlightWordsTextBox.Text = string.Join(Environment.NewLine, settings.HighlightWords);
        IgnoredUsersTextBox.Text = string.Join(Environment.NewLine, settings.IgnoredUsers);
        
        // Security settings
        AutoLockEnabledCheckBox.IsChecked = settings.AutoLockEnabled;
        AutoLockMinutesTextBox.Text = settings.AutoLockMinutes.ToString();
        AutoLockMinutesTextBox.IsEnabled = settings.AutoLockEnabled;
        
        AutoDeleteLogsEnabledCheckBox.IsChecked = settings.AutoDeleteLogsEnabled;
        AutoDeleteLogsDaysTextBox.Text = settings.AutoDeleteLogsDays.ToString();
        AutoDeleteLogsDaysTextBox.IsEnabled = settings.AutoDeleteLogsEnabled;
        
        SecureDeleteEnabledCheckBox.IsChecked = settings.SecureDeleteEnabled;
        SecureDeleteEnabledCheckBox.IsEnabled = settings.AutoDeleteLogsEnabled;
        
        // History settings
        HistoryLinesToLoadTextBox.Text = settings.HistoryLinesToLoad.ToString();
        
        // Ident server settings
        IdentServerEnabledCheckBox.IsChecked = settings.IdentServerEnabled;
        IdentServerPortTextBox.Text = settings.IdentServerPort.ToString();
        IdentUsernameTextBox.Text = settings.IdentUsername;
        IdentHideUserCheckBox.IsChecked = settings.IdentHideUser;
        
        // Set operating system combo box
        switch (settings.IdentOperatingSystem.ToUpperInvariant())
        {
            case "UNIX": IdentOperatingSystemComboBox.SelectedIndex = 0; break;
            case "WIN32": IdentOperatingSystemComboBox.SelectedIndex = 1; break;
            case "OTHER": IdentOperatingSystemComboBox.SelectedIndex = 2; break;
            default: IdentOperatingSystemComboBox.SelectedIndex = 1; break;
        }
        
        // TLS security settings
        switch (settings.MinimumTlsVersion?.ToUpperInvariant())
        {
            case "TLS13": MinimumTlsVersionComboBox.SelectedIndex = 0; break;
            case "TLS12": MinimumTlsVersionComboBox.SelectedIndex = 1; break;
            case "NONE": MinimumTlsVersionComboBox.SelectedIndex = 2; break;
            default: MinimumTlsVersionComboBox.SelectedIndex = 1; break;
        }
        EnableCertificateRevocationCheckBox.IsChecked = settings.EnableCertificateRevocationCheck;
    }
    
    /// <summary>
    /// Loads and displays the encryption status.
    /// </summary>
    private void LoadEncryptionStatus()
    {
        if (_configService.IsEncryptionEnabled)
        {
            EncryptionStatusText.Text = "Aktivert 🔒";
            EncryptionStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
            ChangePasswordButton.Visibility = Visibility.Visible;
            DisableEncryptionButton.Visibility = Visibility.Visible;
            EnableEncryptionPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            EncryptionStatusText.Text = "Deaktivert 🔓";
            EncryptionStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            ChangePasswordButton.Visibility = Visibility.Collapsed;
            DisableEncryptionButton.Visibility = Visibility.Collapsed;
            EnableEncryptionPanel.Visibility = Visibility.Visible;
        }
        
        // Load security audit info
        LoadSecurityAuditInfo();
    }
    
    /// <summary>
    /// Loads security audit information.
    /// </summary>
    private void LoadSecurityAuditInfo()
    {
        try
        {
            if (Application.Current is App app && app.SecurityAudit != null)
            {
                var failedAttempts = app.SecurityAudit.GetRecentFailedAttempts(24 * 60); // Last 24 hours
                if (failedAttempts > 0)
                {
                    RecentUnlockAttemptsText.Text = $"⚠️ {failedAttempts} mislykkede opplåsingsforsøk siste 24 timer";
                    RecentUnlockAttemptsText.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else
                {
                    RecentUnlockAttemptsText.Text = "✓ Ingen mislykkede opplåsingsforsøk siste 24 timer";
                    RecentUnlockAttemptsText.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
            }
        }
        catch
        {
            RecentUnlockAttemptsText.Text = "";
        }
    }
    
    /// <summary>
    /// Handles the Change Password button click.
    /// </summary>
    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordDialog();
        if (dialog.ShowDialog() == true)
        {
            var success = await _configService.Storage.ChangePasswordAsync(
                dialog.CurrentPassword!, 
                dialog.NewPassword!);
            
            if (success)
            {
                MessageBox.Show("Passordet ble endret.", "Suksess", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Kunne ikke endre passord. Sjekk at nåværende passord er korrekt.", 
                    "Feil", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    /// <summary>
    /// Handles the Disable Encryption button click.
    /// </summary>
    private async void DisableEncryptionButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Er du sikker på at du vil deaktivere kryptering?\n\n" +
            "Alle data vil bli lagret ukryptert på disk.",
            "Bekreft",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result != MessageBoxResult.Yes)
            return;
        
        // Ask for current password
        var unlockDialog = new UnlockDialog
        {
            ValidatePassword = password => _configService.Storage.Unlock(password)
        };
        
        if (unlockDialog.ShowDialog() != true || string.IsNullOrEmpty(unlockDialog.Password))
            return;
        
        var success = await _configService.Storage.DisableEncryptionAsync(unlockDialog.Password);
        
        if (success)
        {
            MessageBox.Show("Kryptering ble deaktivert.", "Suksess", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            LoadEncryptionStatus();
        }
        else
        {
            MessageBox.Show("Kunne ikke deaktivere kryptering.", "Feil", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    /// <summary>
    /// Handles the Enable Encryption button click.
    /// Encrypts all existing data with a new master password.
    /// </summary>
    private async void EnableEncryptionButton_Click(object sender, RoutedEventArgs e)
    {
        // Show encryption setup dialog
        var setupDialog = new EncryptionSetupDialog();
        if (setupDialog.ShowDialog() != true || !setupDialog.EnableEncryption || string.IsNullOrEmpty(setupDialog.Password))
        {
            return;
        }
        
        // Confirm action
        var result = MessageBox.Show(
            "Er du sikker på at du vil aktivere kryptering?\n\n" +
            "• All eksisterende data vil bli kryptert\n" +
            "• Du MÅ huske master-passordet\n" +
            "• Uten passordet kan data IKKE gjenopprettes",
            "Bekreft aktivering av kryptering",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result != MessageBoxResult.Yes)
            return;
        
        EnableEncryptionButton.IsEnabled = false;
        EnableEncryptionButton.Content = "Krypterer...";
        
        try
        {
            // Enable encryption - this will encrypt all existing files
            var success = await _configService.Storage.EnableEncryptionAsync(setupDialog.Password);
            
            if (success)
            {
                // Re-save configuration to encrypt it
                await _configService.SaveAsync();
                
                // Log the event
                if (Application.Current is App app && app.SecurityAudit != null)
                {
                    await app.SecurityAudit.LogEncryptionStateChangeAsync(true);
                }
                
                MessageBox.Show(
                    "Kryptering er nå aktivert!\n\n" +
                    "All data er kryptert med ditt master-passord.\n" +
                    "Du må oppgi passordet ved oppstart.", 
                    "Suksess", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                LoadEncryptionStatus();
            }
            else
            {
                MessageBox.Show("Kunne ikke aktivere kryptering.", "Feil", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Feil ved aktivering av kryptering:\n{ex.Message}", "Feil", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EnableEncryptionButton.IsEnabled = true;
            EnableEncryptionButton.Content = "Aktiver kryptering";
        }
    }

    /// <summary>
    /// Handles the Save button click. Saves the settings and closes the dialog.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The routed event arguments.</param>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = _configService.Configuration.Settings;
        
        settings.EnableSoundNotifications = EnableSoundCheckBox.IsChecked == true;
        settings.EnableFlashNotifications = EnableFlashCheckBox.IsChecked == true;
        settings.OnlyNotifyWhenInactive = OnlyWhenInactiveCheckBox.IsChecked == true;
        
        settings.HighlightWords = HighlightWordsTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        
        settings.IgnoredUsers = IgnoredUsersTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        
        // Security settings
        settings.AutoLockEnabled = AutoLockEnabledCheckBox.IsChecked == true;
        if (int.TryParse(AutoLockMinutesTextBox.Text, out var minutes) && minutes >= 1 && minutes <= 60)
        {
            settings.AutoLockMinutes = minutes;
        }
        
        settings.AutoDeleteLogsEnabled = AutoDeleteLogsEnabledCheckBox.IsChecked == true;
        if (int.TryParse(AutoDeleteLogsDaysTextBox.Text, out var days) && days >= 7 && days <= 365)
        {
            settings.AutoDeleteLogsDays = days;
        }
        
        settings.SecureDeleteEnabled = SecureDeleteEnabledCheckBox.IsChecked == true;
        
        // History settings
        if (int.TryParse(HistoryLinesToLoadTextBox.Text, out var historyLines) && historyLines >= 10 && historyLines <= 1000)
        {
            settings.HistoryLinesToLoad = historyLines;
        }
        
        // Ident server settings
        settings.IdentServerEnabled = IdentServerEnabledCheckBox.IsChecked == true;
        if (int.TryParse(IdentServerPortTextBox.Text, out var identPort) && identPort >= 1 && identPort <= 65535)
        {
            settings.IdentServerPort = identPort;
        }
        settings.IdentUsername = IdentUsernameTextBox.Text.Trim();
        settings.IdentHideUser = IdentHideUserCheckBox.IsChecked == true;
        settings.IdentOperatingSystem = IdentOperatingSystemComboBox.SelectedIndex switch
        {
            0 => "UNIX",
            1 => "WIN32",
            2 => "OTHER",
            _ => "WIN32"
        };
        
        // TLS security settings
        settings.MinimumTlsVersion = MinimumTlsVersionComboBox.SelectedIndex switch
        {
            0 => "Tls13",
            1 => "Tls12",
            2 => "None",
            _ => "Tls12"
        };
        settings.EnableCertificateRevocationCheck = EnableCertificateRevocationCheckBox.IsChecked == true;
        
        // Update auto-lock configuration
        if (Application.Current is App app)
        {
            app.ConfigureAutoLock(settings.AutoLockEnabled, settings.AutoLockMinutes);
        }
        
        _ = _configService.SaveAsync();
        
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
    
    /// <summary>
    /// Handles the auto-lock checkbox state change.
    /// </summary>
    private void AutoLockEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        AutoLockMinutesTextBox.IsEnabled = AutoLockEnabledCheckBox.IsChecked == true;
    }
    
    /// <summary>
    /// Handles the auto-delete logs checkbox state change.
    /// </summary>
    private void AutoDeleteLogsEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var isEnabled = AutoDeleteLogsEnabledCheckBox.IsChecked == true;
        AutoDeleteLogsDaysTextBox.IsEnabled = isEnabled;
        SecureDeleteEnabledCheckBox.IsEnabled = isEnabled;
        DeleteOldLogsButton.IsEnabled = true; // Always allow manual deletion
    }
    
    /// <summary>
    /// Handles the delete old logs button click.
    /// </summary>
    private async void DeleteOldLogsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AutoDeleteLogsDaysTextBox.Text, out var days) || days < 7)
        {
            MessageBox.Show("Ugyldig antall dager. Minst 7 dager kreves.", "Feil", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        var result = MessageBox.Show(
            $"Er du sikker på at du vil slette logger eldre enn {days} dager?\n\n" +
            (SecureDeleteEnabledCheckBox.IsChecked == true ? "Sikker sletting er aktivert." : "Normal sletting."),
            "Bekreft sletting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        
        if (result != MessageBoxResult.Yes)
            return;
        
        DeleteOldLogsButton.IsEnabled = false;
        DeleteOldLogsButton.Content = "Sletter...";
        
        try
        {
            var deletedCount = await LoggingService.Instance.DeleteOldLogsAsync(
                days, 
                SecureDeleteEnabledCheckBox.IsChecked == true);
            
            MessageBox.Show($"Slettet {deletedCount} loggfiler.", "Fullført", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Feil ved sletting: {ex.Message}", "Feil", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DeleteOldLogsButton.IsEnabled = true;
            DeleteOldLogsButton.Content = "Slett gamle logger nå...";
        }
    }
    
    /// <summary>
    /// Handles the view security log button click.
    /// </summary>
    private void ViewSecurityLogButton_Click(object sender, RoutedEventArgs e)
    {
        var securityLogWindow = new SecurityLogWindow(_configService.Storage);
        securityLogWindow.Owner = this;
        securityLogWindow.ShowDialog();
    }
    
    /// <summary>
    /// Loads and displays the portable mode status.
    /// </summary>
    private void LoadPortableModeStatus()
    {
        if (PortableMode.IsPortable)
        {
            PortableModeStatusText.Text = "Portabel 📁";
            PortableModeStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
            TogglePortableModeButton.Content = "Deaktiver portabel modus";
        }
        else
        {
            PortableModeStatusText.Text = "Normal 🏠";
            PortableModeStatusText.Foreground = System.Windows.Media.Brushes.DeepSkyBlue;
            TogglePortableModeButton.Content = "Aktiver portabel modus";
        }
        
        DataLocationText.Text = $"Data lagres i: {PortableMode.BasePath}";
    }
    
    /// <summary>
    /// Handles the toggle portable mode button click.
    /// </summary>
    private void TogglePortableModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (PortableMode.IsPortable)
        {
            // Disable portable mode
            var result = MessageBox.Show(
                "Vil du deaktivere portabel modus?\n\n" +
                "Data vil heretter lagres i:\n" +
                $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\IrcClient\n\n" +
                "Eksisterende data i portabel mappe vil IKKE flyttes automatisk.",
                "Deaktiver portabel modus",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                if (PortableMode.DisablePortableMode())
                {
                    MessageBox.Show(
                        "Portabel modus er deaktivert.\n\n" +
                        "Start programmet på nytt for at endringene skal tre i kraft.",
                        "Suksess",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    LoadPortableModeStatus();
                }
                else
                {
                    MessageBox.Show("Kunne ikke deaktivere portabel modus.", "Feil",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else
        {
            // Enable portable mode
            var portablePath = System.IO.Path.Combine(PortableMode.ExeDirectory, "data");
            
            var result = MessageBox.Show(
                "Vil du aktivere portabel modus?\n\n" +
                "Data vil heretter lagres i:\n" +
                $"{portablePath}\n\n" +
                "• Perfekt for USB-minnepinne\n" +
                "• Eksisterende data vil IKKE flyttes automatisk\n" +
                "• Du kan kopiere data manuelt hvis ønskelig",
                "Aktiver portabel modus",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                if (PortableMode.EnablePortableMode())
                {
                    MessageBox.Show(
                        "Portabel modus er aktivert!\n\n" +
                        $"En fil 'portable.txt' er opprettet i:\n{PortableMode.ExeDirectory}\n\n" +
                        "Start programmet på nytt for at endringene skal tre i kraft.",
                        "Suksess",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    LoadPortableModeStatus();
                }
                else
                {
                    MessageBox.Show(
                        "Kunne ikke aktivere portabel modus.\n\n" +
                        "Sjekk at programmet har skriverettigheter til egen mappe.",
                        "Feil",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
    
    /// <summary>
    /// Handles the open data folder button click.
    /// </summary>
    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PortableMode.BasePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kunne ikke åpne mappen:\n{ex.Message}", "Feil",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
