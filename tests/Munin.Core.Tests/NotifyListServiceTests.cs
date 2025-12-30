using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for NotifyListService - friend list tracking, online/offline notifications
/// </summary>
public class NotifyListServiceTests
{
    private readonly NotifyListService _service;

    public NotifyListServiceTests()
    {
        _service = NotifyListService.Instance;
        // Clear state from previous tests
        _service.ClearServer("test-server");
        _service.ClearServer("irc.example.com");
        _service.ClearServer("another-server");
    }

    [Fact]
    public void Instance_ReturnsSingletonInstance()
    {
        // Act
        var instance1 = NotifyListService.Instance;
        var instance2 = NotifyListService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void AddToNotifyList_AddsNickname()
    {
        // Act
        var added = _service.AddToNotifyList("test-server", "Alice");

        // Assert
        added.Should().BeTrue();
        _service.IsOnNotifyList("test-server", "Alice").Should().BeTrue();
    }

    [Fact]
    public void AddToNotifyList_DuplicateNickname_ReturnsFalse()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Bob");

        // Act
        var added = _service.AddToNotifyList("test-server", "Bob");

        // Assert
        added.Should().BeFalse();
    }

    [Fact]
    public void AddToNotifyList_CaseInsensitive()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Charlie");

        // Act & Assert
        _service.IsOnNotifyList("test-server", "charlie").Should().BeTrue();
        _service.IsOnNotifyList("test-server", "CHARLIE").Should().BeTrue();
    }

    [Fact]
    public void RemoveFromNotifyList_RemovesNickname()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "David");

        // Act
        var removed = _service.RemoveFromNotifyList("test-server", "David");

        // Assert
        removed.Should().BeTrue();
        _service.IsOnNotifyList("test-server", "David").Should().BeFalse();
    }

    [Fact]
    public void RemoveFromNotifyList_NonExistentNickname_ReturnsFalse()
    {
        // Act
        var removed = _service.RemoveFromNotifyList("test-server", "NonExistent");

        // Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public void GetNotifyList_ReturnsAllNicknames()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Alice");
        _service.AddToNotifyList("test-server", "Bob");
        _service.AddToNotifyList("test-server", "Charlie");

        // Act
        var list = _service.GetNotifyList("test-server");

        // Assert
        list.Should().HaveCount(3);
        list.Should().Contain(new[] { "Alice", "Bob", "Charlie" });
    }

    [Fact]
    public void GetNotifyList_EmptyServer_ReturnsEmptySet()
    {
        // Act
        var list = _service.GetNotifyList("empty-server");

        // Assert
        list.Should().BeEmpty();
    }

    [Fact]
    public void UpdateOnlineStatus_UserComesOnline_RaisesEvent()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Alice");
        
        string? onlineServer = null;
        string? onlineNick = null;
        _service.UserOnline += (s, e) =>
        {
            onlineServer = e.ServerName;
            onlineNick = e.Nickname;
        };

        // Act
        _service.UpdateOnlineStatus("test-server", new[] { "Alice" });

        // Assert
        onlineServer.Should().Be("test-server");
        onlineNick.Should().Be("Alice");
    }

    [Fact]
    public void UpdateOnlineStatus_UserGoesOffline_RaisesEvent()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Bob");
        _service.UpdateOnlineStatus("test-server", new[] { "Bob" });
        
        string? offlineServer = null;
        string? offlineNick = null;
        _service.UserOffline += (s, e) =>
        {
            offlineServer = e.ServerName;
            offlineNick = e.Nickname;
        };

        // Act
        _service.UpdateOnlineStatus("test-server", Array.Empty<string>());

        // Assert
        offlineServer.Should().Be("test-server");
        offlineNick.Should().Be("Bob");
    }

    [Fact]
    public void UpdateOnlineStatus_UserNotOnNotifyList_NoEvent()
    {
        // Arrange
        var eventRaised = false;
        _service.UserOnline += (s, e) => eventRaised = true;

        // Act
        _service.UpdateOnlineStatus("test-server", new[] { "RandomUser" });

        // Assert
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void UpdateOnlineStatus_MultipleUsers_TracksCorrectly()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Alice");
        _service.AddToNotifyList("test-server", "Bob");
        _service.AddToNotifyList("test-server", "Charlie");

        var onlineUsers = new List<string>();
        _service.UserOnline += (s, e) => onlineUsers.Add(e.Nickname);

        // Act
        _service.UpdateOnlineStatus("test-server", new[] { "Alice", "Bob" });

        // Assert
        onlineUsers.Should().HaveCount(2);
        onlineUsers.Should().Contain(new[] { "Alice", "Bob" });
    }

    [Fact]
    public void HandleUserQuit_OnlineUser_RaisesOfflineEvent()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Charlie");
        _service.UpdateOnlineStatus("test-server", new[] { "Charlie" });

        string? offlineNick = null;
        _service.UserOffline += (s, e) => offlineNick = e.Nickname;

        // Act
        _service.HandleUserQuit("test-server", "Charlie");

        // Assert
        offlineNick.Should().Be("Charlie");
    }

    [Fact]
    public void HandleUserQuit_OfflineUser_NoEvent()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "David");
        
        var eventRaised = false;
        _service.UserOffline += (s, e) => eventRaised = true;

        // Act
        _service.HandleUserQuit("test-server", "David");

        // Assert
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void HandleUserJoin_NewUser_RaisesOnlineEvent()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Eve");

        string? onlineNick = null;
        _service.UserOnline += (s, e) => onlineNick = e.Nickname;

        // Act
        _service.HandleUserJoin("test-server", "Eve");

        // Assert
        onlineNick.Should().Be("Eve");
    }

    [Fact]
    public void HandleUserJoin_UserNotOnNotifyList_NoEvent()
    {
        // Arrange
        var eventRaised = false;
        _service.UserOnline += (s, e) => eventRaised = true;

        // Act
        _service.HandleUserJoin("test-server", "RandomUser");

        // Assert
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void HandleUserJoin_AlreadyOnline_NoEvent()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Frank");
        _service.HandleUserJoin("test-server", "Frank");

        var eventCount = 0;
        _service.UserOnline += (s, e) => eventCount++;

        // Act
        _service.HandleUserJoin("test-server", "Frank");

        // Assert
        eventCount.Should().Be(0);
    }

    [Fact]
    public void GetIsonCommand_ReturnsFormattedCommand()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Alice");
        _service.AddToNotifyList("test-server", "Bob");
        _service.AddToNotifyList("test-server", "Charlie");

        // Act
        var command = _service.GetIsonCommand("test-server");

        // Assert
        command.Should().StartWith("ISON ");
        command.Should().Contain("Alice");
        command.Should().Contain("Bob");
        command.Should().Contain("Charlie");
    }

    [Fact]
    public void GetIsonCommand_EmptyList_ReturnsNull()
    {
        // Act
        var command = _service.GetIsonCommand("empty-server");

        // Assert
        command.Should().BeNull();
    }

    [Fact]
    public void ClearServer_RemovesAllData()
    {
        // Arrange
        _service.AddToNotifyList("test-server", "Alice");
        _service.AddToNotifyList("test-server", "Bob");
        _service.UpdateOnlineStatus("test-server", new[] { "Alice" });

        // Act
        _service.ClearServer("test-server");

        // Assert
        _service.GetNotifyList("test-server").Should().BeEmpty();
        _service.GetIsonCommand("test-server").Should().BeNull();
    }

    [Fact]
    public void ExportNotifyLists_SerializesData()
    {
        // Arrange
        _service.AddToNotifyList("irc.example.com", "Alice");
        _service.AddToNotifyList("irc.example.com", "Bob");
        _service.AddToNotifyList("another-server", "Charlie");

        // Act
        var exported = _service.ExportNotifyLists();

        // Assert
        exported.Should().ContainKey("irc.example.com");
        exported["irc.example.com"].Should().Contain(new[] { "Alice", "Bob" });
        exported.Should().ContainKey("another-server");
        exported["another-server"].Should().Contain("Charlie");
    }

    [Fact]
    public void ImportNotifyLists_LoadsData()
    {
        // Arrange
        var data = new Dictionary<string, List<string>>
        {
            ["irc.example.com"] = new List<string> { "Alice", "Bob" },
            ["another-server"] = new List<string> { "Charlie" }
        };

        // Act
        _service.ImportNotifyLists(data);

        // Assert
        _service.IsOnNotifyList("irc.example.com", "Alice").Should().BeTrue();
        _service.IsOnNotifyList("irc.example.com", "Bob").Should().BeTrue();
        _service.IsOnNotifyList("another-server", "Charlie").Should().BeTrue();
    }

    [Fact]
    public void ImportNotifyLists_NullData_DoesNothing()
    {
        // Act
        _service.ImportNotifyLists(null);

        // Assert - No exception thrown
    }

    [Fact]
    public void MultipleServers_IndependentTracking()
    {
        // Arrange
        _service.AddToNotifyList("server1", "Alice");
        _service.AddToNotifyList("server2", "Alice");

        // Act
        _service.UpdateOnlineStatus("server1", new[] { "Alice" });

        // Assert
        _service.GetNotifyList("server1").Should().Contain("Alice");
        _service.GetNotifyList("server2").Should().Contain("Alice");
        // Both servers track the same nickname independently
    }
}
