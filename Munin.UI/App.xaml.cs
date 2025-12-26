using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Munin.Core.Services;
using Munin.UI.Resources;
using Munin.UI.Views;
using Serilog;

namespace Munin.UI;

/// <summary>
/// The main application class for the Munin.
/// Handles application lifecycle events, logging initialization, and encryption.
/// </summary>
public partial class App : Application
{
    private SecureStorageService? _storage;
    private SecurityAuditService? _securityAudit;
    private DispatcherTimer? _idleTimer;
    private DateTime _lastActivityTime;
    private bool _isLocked = false;
    
    // P/Invoke for idle time detection
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
    
    /// <summary>
    /// Gets the shared secure storage service.
    /// </summary>
    public SecureStorageService? Storage => _storage;
    
    /// <summary>
    /// Gets the security audit service.
    /// </summary>
    public SecurityAuditService? SecurityAudit => _securityAudit;
    
    /// <summary>
    /// Event raised when the application is locked due to inactivity.
    /// </summary>
    public event EventHandler? ApplicationLocked;
    
    /// <summary>
    /// Gets whether the application is currently locked.
    /// </summary>
    public bool IsLocked => _isLocked;
    
    /// <summary>
    /// Called when the application starts. Initializes Serilog logging and handles encryption.
    /// </summary>
    /// <param name="e">The startup event arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        // Set up global exception handlers
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        
        try
        {
            // Call base first to initialize WPF properly
            base.OnStartup(e);
            
            // Prevent app from shutting down when dialogs close
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            // Initialize Serilog for the entire application
            SerilogConfig.Initialize();
            
            // Initialize localization (uses system language by default)
            _ = Services.LocalizationService.Instance;
            
            Log.Information("Munin starting up (Portable mode: {IsPortable}, Language: {Language})", 
                PortableMode.IsPortable, Services.LocalizationService.Instance.CurrentLanguageCode);
            
            // Initialize secure storage using portable-aware path
            _storage = new SecureStorageService(PortableMode.BasePath);
            
            // Handle encryption
            if (!HandleEncryption())
            {
                // User cancelled or reset - exit application
                Shutdown();
                return;
            }
            
            // Initialize security audit service (if not already done in HandleEncryption)
            _securityAudit ??= new SecurityAuditService(_storage);
            
            // Initialize logging service with storage (run on thread pool to avoid UI deadlock)
            Task.Run(() => LoggingService.InitializeAsync(_storage)).GetAwaiter().GetResult();
            
            // Start idle timer for auto-lock
            StartIdleTimer();
            
            // Create and show main window explicitly
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            
            // Now set shutdown mode to close when main window closes
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error: {ex.Message}\n\n{ex.StackTrace}", Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
    
    /// <summary>
    /// Starts the idle detection timer for auto-lock functionality.
    /// </summary>
    private void StartIdleTimer()
    {
        _lastActivityTime = DateTime.Now;
        _idleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30) // Check every 30 seconds
        };
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();
    }
    
    /// <summary>
    /// Checks for user inactivity and locks the application if needed.
    /// </summary>
    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (_isLocked || _storage == null || !_storage.IsEncryptionEnabled)
            return;
        
        // Get settings - we need ConfigurationService
        // For now, we check if auto-lock is enabled via a simple approach
        var idleMinutes = GetIdleTimeMinutes();
        var autoLockMinutes = GetAutoLockMinutes();
        
        if (autoLockMinutes > 0 && idleMinutes >= autoLockMinutes)
        {
            LockApplication("Inaktivitet i " + autoLockMinutes + " minutter");
        }
    }
    
    /// <summary>
    /// Gets the system idle time in minutes.
    /// </summary>
    private double GetIdleTimeMinutes()
    {
        var lastInput = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lastInput))
        {
            var idleTime = Environment.TickCount - lastInput.dwTime;
            return idleTime / 60000.0; // Convert ms to minutes
        }
        return 0;
    }
    
    /// <summary>
    /// Gets the auto-lock timeout in minutes from settings.
    /// Returns 0 if auto-lock is disabled.
    /// </summary>
    private int GetAutoLockMinutes()
    {
        // This will be updated when MainWindow sets it
        return _autoLockMinutes;
    }
    
    private int _autoLockMinutes = 0;
    
    /// <summary>
    /// Configures auto-lock settings.
    /// </summary>
    /// <param name="enabled">Whether auto-lock is enabled.</param>
    /// <param name="minutes">Minutes of inactivity before lock.</param>
    public void ConfigureAutoLock(bool enabled, int minutes)
    {
        _autoLockMinutes = enabled ? minutes : 0;
        Log.Information("Auto-lock configured: enabled={Enabled}, minutes={Minutes}", enabled, minutes);
    }
    
    /// <summary>
    /// Locks the application, requiring password to unlock.
    /// </summary>
    /// <param name="reason">Reason for the lock.</param>
    public async void LockApplication(string reason)
    {
        if (_isLocked || _storage == null || !_storage.IsEncryptionEnabled)
            return;
        
        _isLocked = true;
        _storage.Lock();
        
        // Log the auto-lock event
        if (_securityAudit != null)
        {
            await _securityAudit.LogAutoLockAsync(reason);
        }
        
        Log.Information("Application locked: {Reason}", reason);
        
        // Raise event so MainWindow can show lock dialog
        ApplicationLocked?.Invoke(this, EventArgs.Empty);
        
        // Show unlock dialog
        ShowUnlockDialog();
    }
    
    /// <summary>
    /// Shows the unlock dialog after auto-lock.
    /// </summary>
    private void ShowUnlockDialog()
    {
        if (_storage == null) return;
        
        var unlockDialog = new UnlockDialog
        {
            ValidatePassword = password => 
            {
                var success = _storage.Unlock(password);
                _ = _securityAudit?.LogUnlockAttemptAsync(success, success ? null : "Feil passord");
                return success;
            }
        };
        
        var result = unlockDialog.ShowDialog();
        
        if (result == true && !string.IsNullOrEmpty(unlockDialog.Password))
        {
            _isLocked = false;
            Log.Information("Application unlocked");
        }
        else if (unlockDialog.ResetRequested)
        {
            // Reset requested - shutdown and restart
            _storage.ResetAll();
            _ = _securityAudit?.LogDataResetAsync();
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            Shutdown();
        }
        else
        {
            // User closed dialog without unlocking - shutdown
            Shutdown();
        }
    }
    
    /// <summary>
    /// Handles encryption setup and unlock flow.
    /// </summary>
    /// <returns>True if the application should continue, false if it should exit.</returns>
    private bool HandleEncryption()
    {
        Log.Information("HandleEncryption: Starting");
        
        if (_storage == null) 
        {
            Log.Warning("HandleEncryption: Storage is null");
            return false;
        }
        
        Log.Information("HandleEncryption: IsEncryptionEnabled = {Enabled}", _storage.IsEncryptionEnabled);
        
        // Check if this is first run (no config exists)
        bool isFirstRun = !_storage.FileExists("config.json") && !_storage.IsEncryptionEnabled;
        Log.Information("HandleEncryption: isFirstRun = {FirstRun}", isFirstRun);
        
        if (isFirstRun)
        {
            // Show encryption setup dialog
            Log.Information("HandleEncryption: Showing encryption setup dialog");
            var setupDialog = new EncryptionSetupDialog();
            var result = setupDialog.ShowDialog();
            
            if (result != true)
            {
                return false; // User closed dialog
            }
            
            if (setupDialog.EnableEncryption && !string.IsNullOrEmpty(setupDialog.Password))
            {
                // Enable encryption - run on thread pool to avoid UI deadlock
                var success = Task.Run(() => _storage.EnableEncryptionAsync(setupDialog.Password)).GetAwaiter().GetResult();
                
                if (!success)
                {
                    MessageBox.Show(Strings.EncryptionSetup_Failed, Strings.Error, 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                Log.Information("Encryption enabled by user");
            }
            else
            {
                Log.Information("User chose to skip encryption");
            }
            
            return true;
        }
        
        // If encryption is enabled, show unlock dialog
        if (_storage.IsEncryptionEnabled)
        {
            Log.Information("HandleEncryption: Encryption is enabled, showing unlock dialog");
            
            try
            {
                Log.Information("HandleEncryption: Creating unlock dialog");
                var unlockDialog = new UnlockDialog();
                Log.Information("HandleEncryption: Unlock dialog created");
                
                unlockDialog.ValidatePassword = password =>
                {
                    var success = _storage.Unlock(password);
                    // Log to Serilog only at this point (audit service requires unlocked storage)
                    if (success)
                        Log.Information("Unlock attempt successful");
                    else
                        Log.Warning("Unlock attempt failed: Feil passord");
                    return success;
                };
                
                Log.Information("HandleEncryption: About to call ShowDialog on unlock dialog");
                var result = unlockDialog.ShowDialog();
                Log.Information("HandleEncryption: ShowDialog returned {Result}", result);
                
                if (result != true)
                {
                    Log.Information("HandleEncryption: User cancelled unlock dialog");
                    return false; // User closed dialog
                }
                
                if (unlockDialog.ResetRequested)
                {
                    // Reset all data (can't log to audit service since storage is locked)
                    _storage.ResetAll();
                    Log.Warning("User reset all data");
                    
                    // Restart with fresh setup
                    return HandleEncryption();
                }
                
                if (string.IsNullOrEmpty(unlockDialog.Password))
                {
                    return false;
                }
                
                // Now storage is unlocked, create audit service and log the successful unlock
                _securityAudit = new SecurityAuditService(_storage);
                _ = _securityAudit.LogUnlockAttemptAsync(true, null);
                
                Log.Information("Storage unlocked successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HandleEncryption: Exception during unlock dialog");
                MessageBox.Show($"{Strings.Error}: {ex.Message}\n\n{ex.StackTrace}", Strings.Error, 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// Called when the application exits. Flushes and closes the Serilog logger.
    /// </summary>
    /// <param name="e">The exit event arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Munin shutting down");
        
        // Stop idle timer
        _idleTimer?.Stop();
        _idleTimer = null;
        
        // Dispose logging service FIRST (while storage is still unlocked, to flush buffers)
        LoggingService.Instance.Dispose();
        
        // Lock storage and wipe encryption keys from memory
        if (_storage != null)
        {
            _storage.Lock();
            
            // Wipe encryption service memory
            _storage.EncryptionService.WipeMemory();
        }
        
        // Force garbage collection to clear any sensitive data
        WipeMemoryOnExit();
        
        // Ensure all logs are flushed before exit
        SerilogConfig.CloseAndFlush();
        
        base.OnExit(e);
    }
    
    /// <summary>
    /// Attempts to clear sensitive data from memory on exit.
    /// </summary>
    private void WipeMemoryOnExit()
    {
        try
        {
            // Force a garbage collection to clean up any unreferenced sensitive data
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            Log.Debug("Memory wipe completed on exit");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to complete memory wipe on exit");
        }
    }
    
    /// <summary>
    /// Handles unhandled exceptions on the UI thread.
    /// </summary>
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI exception");
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}\n\nCheck logs for details.", 
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
    
    /// <summary>
    /// Handles unhandled exceptions from non-UI threads.
    /// </summary>
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unhandled domain exception (IsTerminating: {IsTerminating})", e.IsTerminating);
        }
    }
    
    /// <summary>
    /// Handles unobserved task exceptions.
    /// </summary>
    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
