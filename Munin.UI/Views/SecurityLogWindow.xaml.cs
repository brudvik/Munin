using System.Windows;
using Munin.Core.Services;

namespace Munin.UI.Views;

/// <summary>
/// Window for displaying security audit events.
/// Shows unlock attempts, auto-lock events, and other security-related activities.
/// </summary>
public partial class SecurityLogWindow : Window
{
    private readonly SecurityAuditService? _auditService;
    
    /// <summary>
    /// Initializes a new instance of the SecurityLogWindow.
    /// </summary>
    /// <param name="storage">The secure storage service.</param>
    public SecurityLogWindow(SecureStorageService? storage)
    {
        InitializeComponent();
        
        if (storage != null)
        {
            _auditService = new SecurityAuditService(storage);
        }
        
        LoadEvents();
    }
    
    /// <summary>
    /// Loads security events into the data grid.
    /// </summary>
    private void LoadEvents()
    {
        if (_auditService == null)
        {
            EventsDataGrid.ItemsSource = new List<SecurityEventViewModel>();
            return;
        }
        
        var events = _auditService.GetRecentEvents(100);
        var viewModels = events.Select(e => new SecurityEventViewModel(e)).ToList();
        EventsDataGrid.ItemsSource = viewModels;
    }
    
    /// <summary>
    /// Handles the refresh button click.
    /// </summary>
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadEvents();
    }
    
    /// <summary>
    /// Handles the close button click.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// View model for displaying security events in the data grid.
/// </summary>
public class SecurityEventViewModel
{
    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// Icon indicating success or failure.
    /// </summary>
    public string StatusIcon { get; }
    
    /// <summary>
    /// Description of the event.
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// Name of the machine where the event occurred.
    /// </summary>
    public string MachineName { get; }
    
    /// <summary>
    /// Name of the Windows user.
    /// </summary>
    public string UserName { get; }
    
    /// <summary>
    /// Creates a view model from a security event.
    /// </summary>
    public SecurityEventViewModel(SecurityEvent evt)
    {
        // Convert from UTC to local time
        Timestamp = evt.Timestamp.ToLocalTime();
        StatusIcon = evt.Success ? "✓" : "✗";
        Description = evt.Description;
        MachineName = evt.MachineName;
        UserName = evt.UserName;
    }
}
