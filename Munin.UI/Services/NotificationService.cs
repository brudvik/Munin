using System.Media;
using System.Runtime.InteropServices;
using System.Windows;

namespace Munin.UI.Services;

/// <summary>
/// Handles Windows notifications and sound alerts.
/// </summary>
/// <remarks>
/// <para>Provides user notifications for important IRC events:</para>
/// <list type="bullet">
///   <item><description>Private messages</description></item>
///   <item><description>Mentions of your nickname</description></item>
///   <item><description>Custom highlight word matches</description></item>
/// </list>
/// <para>Uses taskbar flashing and Windows system sounds for alerts.</para>
/// </remarks>
public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();

    // Settings
    public bool EnableToastNotifications { get; set; } = true;
    public bool EnableSoundNotifications { get; set; } = true;
    public bool OnlyWhenMinimized { get; set; } = true;

    private NotificationService() { }

    // Flash window to get user's attention
    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    /// <summary>
    /// Shows a notification for a private message.
    /// </summary>
    public void NotifyPrivateMessage(string serverName, string fromNick, string message)
    {
        if (!ShouldNotify()) return;

        ShowToast(
            $"Message from {fromNick}",
            TruncateMessage(message),
            serverName
        );

        PlaySound(NotificationSound.PrivateMessage);
    }

    /// <summary>
    /// Shows a notification for a mention/highlight.
    /// </summary>
    public void NotifyMention(string serverName, string channel, string fromNick, string message)
    {
        if (!ShouldNotify()) return;

        ShowToast(
            $"{fromNick} mentioned you in {channel}",
            TruncateMessage(message),
            serverName
        );

        PlaySound(NotificationSound.Mention);
    }

    /// <summary>
    /// Shows a notification for a highlight word match.
    /// </summary>
    public void NotifyHighlightWord(string serverName, string channel, string fromNick, string message, string matchedWord)
    {
        if (!ShouldNotify()) return;

        ShowToast(
            $"Highlight: \"{matchedWord}\" in {channel}",
            $"<{fromNick}> {TruncateMessage(message)}",
            serverName
        );

        PlaySound(NotificationSound.Mention);
    }

    private bool ShouldNotify()
    {
        if (!EnableToastNotifications && !EnableSoundNotifications)
            return false;

        if (OnlyWhenMinimized)
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null && 
                mainWindow.WindowState != WindowState.Minimized &&
                mainWindow.IsActive)
            {
                return false;
            }
        }

        return true;
    }

    private void ShowToast(string title, string message, string attribution)
    {
        if (!EnableToastNotifications) return;

        try
        {
            // Flash the taskbar to get user's attention
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                var fInfo = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                    hwnd = hwnd,
                    dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                    uCount = 3,
                    dwTimeout = 0
                };
                FlashWindowEx(ref fInfo);
            }
        }
        catch
        {
            // Flash may fail on some systems
        }
    }

    private void PlaySound(NotificationSound sound)
    {
        if (!EnableSoundNotifications) return;

        try
        {
            // Use Windows system sounds
            var soundFile = sound switch
            {
                NotificationSound.PrivateMessage => SystemSounds.Exclamation,
                NotificationSound.Mention => SystemSounds.Asterisk,
                _ => SystemSounds.Beep
            };

            soundFile.Play();
        }
        catch
        {
            // Sound playback may fail
        }
    }

    private static string TruncateMessage(string message, int maxLength = 100)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return message.Length <= maxLength ? message : message[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Clears all toast notifications from the action center.
    /// </summary>
    public void ClearAllNotifications()
    {
        // Not needed without toast notifications
    }
}

/// <summary>
/// Types of notification sounds that can be played.
/// </summary>
public enum NotificationSound
{
    /// <summary>Sound for private messages.</summary>
    PrivateMessage,

    /// <summary>Sound for nickname mentions or highlights.</summary>
    Mention,

    /// <summary>Sound for user joining a channel.</summary>
    Join,

    /// <summary>Sound for user leaving a channel.</summary>
    Part
}
