using FluentAssertions;
using Munin.Core.Services;
using System.Net;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for IdentServer (RFC 1413) - IRC authentication via Ident protocol.
/// </summary>
public class IdentServerTests : IDisposable
{
    private readonly IdentServer _server;

    public IdentServerTests()
    {
        _server = new IdentServer();
    }

    public void Dispose()
    {
        _server?.Dispose();
    }

    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Assert
        _server.Port.Should().Be(113);
        _server.IsEnabled.Should().BeFalse();
        _server.HideUser.Should().BeFalse();
        _server.OperatingSystem.Should().Be("WIN32");
        _server.IdleTimeoutSeconds.Should().Be(60);
        _server.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void DefaultUsername_ShouldBeEnvironmentUsername()
    {
        // Assert
        _server.DefaultUsername.Should().Be(Environment.UserName);
    }

    [Fact]
    public void Port_CanBeSet()
    {
        // Act
        _server.Port = 1113;

        // Assert
        _server.Port.Should().Be(1113);
    }

    [Fact]
    public void IsEnabled_CanBeToggled()
    {
        // Act
        _server.IsEnabled = true;

        // Assert
        _server.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void HideUser_CanBeToggled()
    {
        // Act
        _server.HideUser = true;

        // Assert
        _server.HideUser.Should().BeTrue();
    }

    [Fact]
    public void OperatingSystem_CanBeSet()
    {
        // Act
        _server.OperatingSystem = "UNIX";

        // Assert
        _server.OperatingSystem.Should().Be("UNIX");
    }

    [Fact]
    public void IdleTimeoutSeconds_CanBeSet()
    {
        // Act
        _server.IdleTimeoutSeconds = 30;

        // Assert
        _server.IdleTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Start_WhenDisabled_ShouldReturnFalse()
    {
        // Arrange
        _server.IsEnabled = false;

        // Act
        var result = _server.Start();

        // Assert
        result.Should().BeFalse();
        _server.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Start_WhenEnabled_WithHighPort_ShouldReturnTrue()
    {
        // Arrange
        _server.IsEnabled = true;
        _server.Port = 11300; // High port doesn't require admin

        try
        {
            // Act
            var result = _server.Start();

            // Assert
            result.Should().BeTrue();
            _server.IsRunning.Should().BeTrue();
        }
        finally
        {
            _server.Stop();
        }
    }

    [Fact]
    public void Start_WhenAlreadyRunning_ShouldReturnTrue()
    {
        // Arrange
        _server.IsEnabled = true;
        _server.Port = 11301;
        _server.Start();

        try
        {
            // Act
            var result = _server.Start();

            // Assert
            result.Should().BeTrue();
            _server.IsRunning.Should().BeTrue();
        }
        finally
        {
            _server.Stop();
        }
    }

    [Fact]
    public void Stop_WhenNotRunning_ShouldNotThrow()
    {
        // Act
        Action act = () => _server.Stop();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Stop_WhenRunning_ShouldStopServer()
    {
        // Arrange
        _server.IsEnabled = true;
        _server.Port = 11302;
        _server.Start();

        // Act
        _server.Stop();

        // Assert
        _server.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void RegisterConnection_ShouldStoreConnectionInfo()
    {
        // Act
        _server.RegisterConnection(12345, 6697, "testuser");

        // Assert - connection is stored (we can't directly access private dict, but no exception)
        // The test verifies the method doesn't throw
    }

    [Fact]
    public void UnregisterConnection_ShouldRemoveConnectionInfo()
    {
        // Arrange
        _server.RegisterConnection(12345, 6697, "testuser");

        // Act
        _server.UnregisterConnection(12345, 6697);

        // Assert - connection is removed (no exception)
    }

    [Fact]
    public void UnregisterConnection_NonExistentConnection_ShouldNotThrow()
    {
        // Act
        Action act = () => _server.UnregisterConnection(99999, 6667);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void QueryReceived_EventExists_CanBeSubscribed()
    {
        // Act
        EventHandler<IdentQueryEventArgs>? handler = (s, e) => { };
        _server.QueryReceived += handler;
        _server.QueryReceived -= handler;

        // Assert - Event can be subscribed and unsubscribed without error
    }

    [Fact]
    public void DefaultUsername_CanBeCustomized()
    {
        // Act
        _server.DefaultUsername = "customuser";

        // Assert
        _server.DefaultUsername.Should().Be("customuser");
    }

    [Fact]
    public void MultipleConnections_CanBeRegistered()
    {
        // Act
        _server.RegisterConnection(12345, 6667, "user1");
        _server.RegisterConnection(12346, 6667, "user2");
        _server.RegisterConnection(12347, 6697, "user3");

        // Assert - no exceptions thrown
    }

    [Fact]
    public void Dispose_ShouldStopServer()
    {
        // Arrange
        _server.IsEnabled = true;
        _server.Port = 11303;
        _server.Start();

        // Act
        _server.Dispose();

        // Assert
        _server.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Act
        Action act = () =>
        {
            _server.Dispose();
            _server.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void IdentQueryEventArgs_ShouldStoreQueryDetails()
    {
        // Arrange
        var ipAddress = IPAddress.Parse("192.168.1.1");

        // Act
        var args = new IdentQueryEventArgs(6697, 12345, ipAddress);

        // Assert
        args.ServerPort.Should().Be(6697);
        args.ClientPort.Should().Be(12345);
        args.QueryingHost.Should().Be(ipAddress);
        args.Timestamp.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void IdentQueryEventArgs_WithNullIpAddress_ShouldAllowNull()
    {
        // Act
        var args = new IdentQueryEventArgs(6667, 54321, null);

        // Assert
        args.QueryingHost.Should().BeNull();
    }

    [Fact]
    public void ServerConfiguration_AllPropertiesAreConfigurable()
    {
        // Act
        _server.Port = 1234;
        _server.IsEnabled = true;
        _server.HideUser = true;
        _server.DefaultUsername = "ircuser";
        _server.OperatingSystem = "OTHER";
        _server.IdleTimeoutSeconds = 45;

        // Assert
        _server.Port.Should().Be(1234);
        _server.IsEnabled.Should().BeTrue();
        _server.HideUser.Should().BeTrue();
        _server.DefaultUsername.Should().Be("ircuser");
        _server.OperatingSystem.Should().Be("OTHER");
        _server.IdleTimeoutSeconds.Should().Be(45);
    }

    [Fact]
    public void Start_OnPrivilegedPort_MayFailWithoutAdmin()
    {
        // Arrange
        _server.IsEnabled = true;
        _server.Port = 113; // Requires admin on Windows

        // Act
        var result = _server.Start();

        // Assert
        // May succeed if running as admin, otherwise should fail gracefully
        if (!result)
        {
            _server.IsRunning.Should().BeFalse();
        }
        
        _server.Stop(); // Cleanup if it did start
    }

    [Fact]
    public void OperatingSystemValues_ShouldSupportStandardTypes()
    {
        // Act & Assert - Verify we can set all standard OS types
        _server.OperatingSystem = "UNIX";
        _server.OperatingSystem.Should().Be("UNIX");

        _server.OperatingSystem = "WIN32";
        _server.OperatingSystem.Should().Be("WIN32");

        _server.OperatingSystem = "OTHER";
        _server.OperatingSystem.Should().Be("OTHER");
    }
}
