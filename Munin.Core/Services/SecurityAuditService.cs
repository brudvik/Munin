using System.Text.Json;
using Serilog;

namespace Munin.Core.Services;

/// <summary>
/// Service for logging security events such as unlock attempts.
/// Provides an audit trail for security-related activities.
/// </summary>
/// <remarks>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Logs unlock attempts with timestamp and success/failure</description></item>
///   <item><description>Stores machine name for multi-device scenarios</description></item>
///   <item><description>Encrypted storage when encryption is enabled</description></item>
///   <item><description>Configurable retention period</description></item>
/// </list>
/// </remarks>
public class SecurityAuditService
{
    private readonly ILogger _logger;
    private readonly SecureStorageService _storage;
    private readonly string _auditLogPath = "security_audit.json";
    private SecurityAuditLog _auditLog;
    
    /// <summary>
    /// Initializes a new instance of the SecurityAuditService.
    /// </summary>
    /// <param name="storage">The secure storage service for persisting audit logs.</param>
    public SecurityAuditService(SecureStorageService storage)
    {
        _logger = SerilogConfig.ForContext<SecurityAuditService>();
        _storage = storage;
        _auditLog = new SecurityAuditLog();
        
        LoadAuditLogAsync().GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Gets the list of security events.
    /// </summary>
    public IReadOnlyList<SecurityEvent> Events => _auditLog.Events.AsReadOnly();
    
    /// <summary>
    /// Logs an unlock attempt.
    /// </summary>
    /// <param name="success">Whether the unlock was successful.</param>
    /// <param name="failureReason">Reason for failure if unsuccessful.</param>
    public async Task LogUnlockAttemptAsync(bool success, string? failureReason = null)
    {
        var evt = new SecurityEvent
        {
            EventType = SecurityEventType.UnlockAttempt,
            Timestamp = DateTime.UtcNow,
            Success = success,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            Details = failureReason
        };
        
        _auditLog.Events.Add(evt);
        await SaveAuditLogAsync();
        
        if (success)
        {
            _logger.Information("Security audit: Successful unlock from {Machine}\\{User}", 
                Environment.MachineName, Environment.UserName);
        }
        else
        {
            _logger.Warning("Security audit: Failed unlock attempt from {Machine}\\{User}: {Reason}", 
                Environment.MachineName, Environment.UserName, failureReason ?? "Invalid password");
        }
    }
    
    /// <summary>
    /// Logs an auto-lock event.
    /// </summary>
    /// <param name="reason">Reason for the auto-lock (e.g., "Inactivity timeout").</param>
    public async Task LogAutoLockAsync(string reason)
    {
        var evt = new SecurityEvent
        {
            EventType = SecurityEventType.AutoLock,
            Timestamp = DateTime.UtcNow,
            Success = true,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            Details = reason
        };
        
        _auditLog.Events.Add(evt);
        await SaveAuditLogAsync();
        
        _logger.Information("Security audit: Auto-lock triggered - {Reason}", reason);
    }
    
    /// <summary>
    /// Logs a password change event.
    /// </summary>
    /// <param name="success">Whether the password change was successful.</param>
    public async Task LogPasswordChangeAsync(bool success)
    {
        var evt = new SecurityEvent
        {
            EventType = SecurityEventType.PasswordChange,
            Timestamp = DateTime.UtcNow,
            Success = success,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName
        };
        
        _auditLog.Events.Add(evt);
        await SaveAuditLogAsync();
        
        if (success)
        {
            _logger.Information("Security audit: Password changed successfully");
        }
        else
        {
            _logger.Warning("Security audit: Password change failed");
        }
    }
    
    /// <summary>
    /// Logs when encryption is enabled or disabled.
    /// </summary>
    /// <param name="enabled">True if encryption was enabled, false if disabled.</param>
    public async Task LogEncryptionStateChangeAsync(bool enabled)
    {
        var evt = new SecurityEvent
        {
            EventType = SecurityEventType.EncryptionStateChange,
            Timestamp = DateTime.UtcNow,
            Success = true,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            Details = enabled ? "Encryption enabled" : "Encryption disabled"
        };
        
        _auditLog.Events.Add(evt);
        await SaveAuditLogAsync();
        
        _logger.Information("Security audit: {State}", enabled ? "Encryption enabled" : "Encryption disabled");
    }
    
    /// <summary>
    /// Logs a data reset event.
    /// </summary>
    public async Task LogDataResetAsync()
    {
        var evt = new SecurityEvent
        {
            EventType = SecurityEventType.DataReset,
            Timestamp = DateTime.UtcNow,
            Success = true,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            Details = "All data reset by user"
        };
        
        _auditLog.Events.Add(evt);
        await SaveAuditLogAsync();
        
        _logger.Warning("Security audit: All data reset by user");
    }
    
    /// <summary>
    /// Gets recent unlock attempts for display.
    /// </summary>
    /// <param name="count">Number of recent events to return.</param>
    /// <returns>List of recent security events.</returns>
    public List<SecurityEvent> GetRecentEvents(int count = 50)
    {
        return _auditLog.Events
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }
    
    /// <summary>
    /// Gets failed unlock attempts within a time window.
    /// </summary>
    /// <param name="windowMinutes">Time window in minutes.</param>
    /// <returns>Number of failed attempts.</returns>
    public int GetRecentFailedAttempts(int windowMinutes = 60)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-windowMinutes);
        return _auditLog.Events
            .Where(e => e.EventType == SecurityEventType.UnlockAttempt && 
                        !e.Success && 
                        e.Timestamp > cutoff)
            .Count();
    }
    
    /// <summary>
    /// Clears old audit log entries beyond retention period.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain.</param>
    public async Task PruneOldEntriesAsync(int retentionDays = 365)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var originalCount = _auditLog.Events.Count;
        
        _auditLog.Events = _auditLog.Events
            .Where(e => e.Timestamp > cutoff)
            .ToList();
        
        if (_auditLog.Events.Count < originalCount)
        {
            await SaveAuditLogAsync();
            _logger.Information("Pruned {Count} old security audit entries", 
                originalCount - _auditLog.Events.Count);
        }
    }
    
    private async Task LoadAuditLogAsync()
    {
        try
        {
            if (_storage.FileExists(_auditLogPath))
            {
                var json = await _storage.ReadTextAsync(_auditLogPath);
                if (!string.IsNullOrEmpty(json))
                {
                    _auditLog = JsonSerializer.Deserialize<SecurityAuditLog>(json) ?? new SecurityAuditLog();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load security audit log");
            _auditLog = new SecurityAuditLog();
        }
    }
    
    private async Task SaveAuditLogAsync()
    {
        try
        {
            await _storage.WriteJsonAsync(_auditLogPath, _auditLog);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save security audit log");
        }
    }
}

/// <summary>
/// Container for security audit events.
/// </summary>
public class SecurityAuditLog
{
    /// <summary>
    /// List of security events.
    /// </summary>
    public List<SecurityEvent> Events { get; set; } = new();
}

/// <summary>
/// Represents a single security event.
/// </summary>
public class SecurityEvent
{
    /// <summary>
    /// Type of security event.
    /// </summary>
    public SecurityEventType EventType { get; set; }
    
    /// <summary>
    /// When the event occurred (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Whether the action was successful.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Name of the machine where the event occurred.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;
    
    /// <summary>
    /// Name of the Windows user.
    /// </summary>
    public string UserName { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional details about the event.
    /// </summary>
    public string? Details { get; set; }
    
    /// <summary>
    /// Gets a formatted description of the event.
    /// </summary>
    public string Description => EventType switch
    {
        SecurityEventType.UnlockAttempt => Success ? "Vellykket opplåsing" : $"Mislykket opplåsing: {Details ?? "Feil passord"}",
        SecurityEventType.AutoLock => $"Automatisk låsing: {Details}",
        SecurityEventType.PasswordChange => Success ? "Passord endret" : "Passordendring feilet",
        SecurityEventType.EncryptionStateChange => Details ?? "Krypteringsstatus endret",
        SecurityEventType.DataReset => "All data tilbakestilt",
        _ => Details ?? EventType.ToString()
    };
}

/// <summary>
/// Types of security events that can be logged.
/// </summary>
public enum SecurityEventType
{
    /// <summary>Attempt to unlock the application.</summary>
    UnlockAttempt,
    
    /// <summary>Automatic lock due to inactivity.</summary>
    AutoLock,
    
    /// <summary>Password was changed.</summary>
    PasswordChange,
    
    /// <summary>Encryption was enabled or disabled.</summary>
    EncryptionStateChange,
    
    /// <summary>All data was reset.</summary>
    DataReset
}
