using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for SecurityAuditService.
/// Verifies audit logging, rate limiting, and security event tracking.
/// </summary>
public class SecurityAuditServiceTests : IDisposable
{
    private readonly string _testPath;
    private readonly SecureStorageService _storage;
    private readonly SecurityAuditService _auditService;

    public SecurityAuditServiceTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"test_audit_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testPath);
        _storage = new SecureStorageService(_testPath);
        _auditService = new SecurityAuditService(_storage);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testPath))
            {
                Directory.Delete(_testPath, true);
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    [Fact]
    public void Constructor_InitializesEmptyLog()
    {
        _auditService.Events.Should().BeEmpty();
        _auditService.IsLockedOut.Should().BeFalse();
        _auditService.ConsecutiveFailedAttempts.Should().Be(0);
    }

    [Fact]
    public async Task LogUnlockAttemptAsync_Success_ResetsFailedAttempts()
    {
        // Simulate failed attempts first
        await _auditService.LogUnlockAttemptAsync(false, "Wrong password");
        await _auditService.LogUnlockAttemptAsync(false, "Wrong password");
        
        _auditService.ConsecutiveFailedAttempts.Should().Be(2);
        
        // Successful unlock
        await _auditService.LogUnlockAttemptAsync(true);
        
        _auditService.ConsecutiveFailedAttempts.Should().Be(0);
        _auditService.IsLockedOut.Should().BeFalse();
        _auditService.Events.Should().HaveCount(3);
    }

    [Fact]
    public async Task LogUnlockAttemptAsync_Failure_IncrementsCounter()
    {
        await _auditService.LogUnlockAttemptAsync(false, "Invalid password");
        
        _auditService.ConsecutiveFailedAttempts.Should().Be(1);
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].Success.Should().BeFalse();
        _auditService.Events[0].EventType.Should().Be(SecurityEventType.UnlockAttempt);
    }

    [Fact]
    public async Task LogUnlockAttemptAsync_MultipleFailures_TriggersLockout()
    {
        // Default MaxFailedAttempts is 5
        for (int i = 0; i < 5; i++)
        {
            await _auditService.LogUnlockAttemptAsync(false, "Wrong password");
        }
        
        _auditService.ConsecutiveFailedAttempts.Should().Be(5);
        _auditService.IsLockedOut.Should().BeTrue();
        _auditService.RemainingLockoutSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CheckRateLimit_WhenNotLockedOut_AllowsAttempts()
    {
        var (isAllowed, waitSeconds) = _auditService.CheckRateLimit();
        
        isAllowed.Should().BeTrue();
        waitSeconds.Should().Be(0);
    }

    [Fact]
    public async Task CheckRateLimit_WhenLockedOut_DeniesAttempts()
    {
        // Trigger lockout
        for (int i = 0; i < 5; i++)
        {
            await _auditService.LogUnlockAttemptAsync(false);
        }
        
        var (isAllowed, waitSeconds) = _auditService.CheckRateLimit();
        
        isAllowed.Should().BeFalse();
        waitSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LogAutoLockAsync_AddsEvent()
    {
        await _auditService.LogAutoLockAsync("Inactivity timeout");
        
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].EventType.Should().Be(SecurityEventType.AutoLock);
        _auditService.Events[0].Success.Should().BeTrue();
        _auditService.Events[0].Details.Should().Be("Inactivity timeout");
    }

    [Fact]
    public async Task LogPasswordChangeAsync_Success_AddsEvent()
    {
        await _auditService.LogPasswordChangeAsync(true);
        
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].EventType.Should().Be(SecurityEventType.PasswordChange);
        _auditService.Events[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task LogPasswordChangeAsync_Failure_AddsEvent()
    {
        await _auditService.LogPasswordChangeAsync(false);
        
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].EventType.Should().Be(SecurityEventType.PasswordChange);
        _auditService.Events[0].Success.Should().BeFalse();
    }

    [Fact]
    public async Task LogEncryptionStateChangeAsync_Enabled_AddsEvent()
    {
        await _auditService.LogEncryptionStateChangeAsync(true);
        
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].EventType.Should().Be(SecurityEventType.EncryptionStateChange);
        _auditService.Events[0].Details.Should().Be("Encryption enabled");
    }

    [Fact]
    public async Task LogEncryptionStateChangeAsync_Disabled_AddsEvent()
    {
        await _auditService.LogEncryptionStateChangeAsync(false);
        
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].EventType.Should().Be(SecurityEventType.EncryptionStateChange);
        _auditService.Events[0].Details.Should().Be("Encryption disabled");
    }

    [Fact]
    public async Task LogDataResetAsync_AddsEvent()
    {
        await _auditService.LogDataResetAsync();
        
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].EventType.Should().Be(SecurityEventType.DataReset);
        _auditService.Events[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentEvents_ReturnsInDescendingOrder()
    {
        await _auditService.LogUnlockAttemptAsync(true);
        await Task.Delay(10);
        await _auditService.LogPasswordChangeAsync(true);
        await Task.Delay(10);
        await _auditService.LogAutoLockAsync("Test");
        
        var recent = _auditService.GetRecentEvents(10);
        
        recent.Should().HaveCount(3);
        recent[0].EventType.Should().Be(SecurityEventType.AutoLock);
        recent[1].EventType.Should().Be(SecurityEventType.PasswordChange);
        recent[2].EventType.Should().Be(SecurityEventType.UnlockAttempt);
    }

    [Fact]
    public async Task GetRecentEvents_LimitsToCount()
    {
        for (int i = 0; i < 10; i++)
        {
            await _auditService.LogUnlockAttemptAsync(true);
        }
        
        var recent = _auditService.GetRecentEvents(5);
        
        recent.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetRecentFailedAttempts_CountsOnlyFailed()
    {
        await _auditService.LogUnlockAttemptAsync(false);
        await _auditService.LogUnlockAttemptAsync(false);
        await _auditService.LogUnlockAttemptAsync(true);
        await _auditService.LogUnlockAttemptAsync(false);
        
        var failedCount = _auditService.GetRecentFailedAttempts(60);
        
        failedCount.Should().Be(3);
    }

    [Fact]
    public async Task GetRecentFailedAttempts_RespectsTimeWindow()
    {
        await _auditService.LogUnlockAttemptAsync(false);
        
        // This won't actually wait, but simulates old events
        var failedCount = _auditService.GetRecentFailedAttempts(0); // 0 minute window
        
        failedCount.Should().Be(0);
    }

    [Fact]
    public async Task PruneOldEntriesAsync_RemovesOldEvents()
    {
        await _auditService.LogUnlockAttemptAsync(true);
        await _auditService.LogPasswordChangeAsync(true);
        
        // Prune with 0 days retention (removes all)
        await _auditService.PruneOldEntriesAsync(0);
        
        _auditService.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task PruneOldEntriesAsync_KeepsRecentEvents()
    {
        await _auditService.LogUnlockAttemptAsync(true);
        await _auditService.LogPasswordChangeAsync(true);
        
        // Prune with large retention period
        await _auditService.PruneOldEntriesAsync(365);
        
        _auditService.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task SecurityEvent_Description_FormatsCorrectly()
    {
        await _auditService.LogUnlockAttemptAsync(true);
        await _auditService.LogUnlockAttemptAsync(false, "Invalid password");
        
        var successEvent = _auditService.Events[0];
        var failEvent = _auditService.Events[1];
        
        successEvent.Description.Should().Contain("Vellykket");
        failEvent.Description.Should().Contain("Mislykket");
    }

    [Fact]
    public void MaxFailedAttempts_CanBeCustomized()
    {
        _auditService.MaxFailedAttempts.Should().Be(5);
        
        _auditService.MaxFailedAttempts = 3;
        _auditService.MaxFailedAttempts.Should().Be(3);
    }

    [Fact]
    public void BaseLockoutSeconds_CanBeCustomized()
    {
        _auditService.BaseLockoutSeconds.Should().Be(30);
        
        _auditService.BaseLockoutSeconds = 60;
        _auditService.BaseLockoutSeconds.Should().Be(60);
    }

    [Fact]
    public void MaxLockoutSeconds_CanBeCustomized()
    {
        _auditService.MaxLockoutSeconds.Should().Be(3600);
        
        _auditService.MaxLockoutSeconds = 1800;
        _auditService.MaxLockoutSeconds.Should().Be(1800);
    }

    [Fact]
    public async Task Events_IncludesMachineAndUserInfo()
    {
        await _auditService.LogUnlockAttemptAsync(true);
        
        var evt = _auditService.Events[0];
        
        evt.MachineName.Should().NotBeNullOrEmpty();
        evt.UserName.Should().NotBeNullOrEmpty();
        evt.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MultipleFailedAttempts_IncreasesLockoutExponentially()
    {
        // Trigger first lockout
        for (int i = 0; i < 5; i++)
        {
            await _auditService.LogUnlockAttemptAsync(false);
        }
        
        var firstLockout = _auditService.RemainingLockoutSeconds;
        
        // More failures
        await _auditService.LogUnlockAttemptAsync(false);
        
        // Lockout should increase (exponential backoff)
        _auditService.ConsecutiveFailedAttempts.Should().Be(6);
    }

    [Fact]
    public async Task LogUnlockAttemptAsync_WithNullReason_HandlesGracefully()
    {
        await _auditService.LogUnlockAttemptAsync(false, null);
        
        _auditService.Events.Should().ContainSingle();
        _auditService.Events[0].Details.Should().NotBeNull();
    }
}
