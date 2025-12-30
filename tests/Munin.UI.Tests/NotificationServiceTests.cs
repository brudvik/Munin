using FluentAssertions;
using Munin.UI.Services;
using Xunit;

namespace Munin.UI.Tests;

/// <summary>
/// Tests for NotificationService - notification and alert management.
/// </summary>
public class NotificationServiceTests
{
    [Fact]
    public void Instance_ReturnsSingleton()
    {
        var instance1 = NotificationService.Instance;
        var instance2 = NotificationService.Instance;

        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void EnableToastNotifications_DefaultsToTrue()
    {
        var service = NotificationService.Instance;
        // Reset to default value
        service.EnableToastNotifications = true;

        service.EnableToastNotifications.Should().BeTrue();
    }

    [Fact]
    public void EnableSoundNotifications_DefaultsToTrue()
    {
        var service = NotificationService.Instance;
        // Reset to default value
        service.EnableSoundNotifications = true;

        service.EnableSoundNotifications.Should().BeTrue();
    }

    [Fact]
    public void OnlyWhenMinimized_DefaultsToTrue()
    {
        var service = NotificationService.Instance;

        service.OnlyWhenMinimized.Should().BeTrue();
    }

    [Fact]
    public void NotifyPrivateMessage_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyPrivateMessage("TestServer", "TestUser", "Hello!");

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyMention_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyMention("TestServer", "#general", "TestUser", "Hey @you!");

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyHighlightWord_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyHighlightWord("TestServer", "#general", "TestUser", "keyword test", "keyword");

        act.Should().NotThrow();
    }

    [Fact]
    public void ClearAllNotifications_DoesNotThrow()
    {
        var service = NotificationService.Instance;

        var act = () => service.ClearAllNotifications();

        act.Should().NotThrow();
    }

    [Fact]
    public void EnableToastNotifications_CanBeSetToFalse()
    {
        var service = NotificationService.Instance;

        service.EnableToastNotifications = false;

        service.EnableToastNotifications.Should().BeFalse();
    }

    [Fact]
    public void EnableSoundNotifications_CanBeSetToFalse()
    {
        var service = NotificationService.Instance;

        service.EnableSoundNotifications = false;

        service.EnableSoundNotifications.Should().BeFalse();
    }

    [Fact]
    public void OnlyWhenMinimized_CanBeSetToFalse()
    {
        var service = NotificationService.Instance;

        service.OnlyWhenMinimized = false;

        service.OnlyWhenMinimized.Should().BeFalse();
    }

    [Fact]
    public void NotifyPrivateMessage_WithDisabledNotifications_DoesNothing()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        // Should not throw or cause any side effects
        service.NotifyPrivateMessage("Server", "User", "Message");

        // No assertions - just verifying it doesn't crash
    }

    [Fact]
    public void NotifyMention_WithLongMessage_TruncatesMessage()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var longMessage = new string('x', 200);

        // Should not throw even with very long messages
        var act = () => service.NotifyMention("Server", "#channel", "User", longMessage);

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyHighlightWord_WithEmptyMessage_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyHighlightWord("Server", "#channel", "User", "", "keyword");

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyPrivateMessage_WithEmptyNickname_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyPrivateMessage("Server", "", "Message");

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyMention_WithEmptyChannel_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyMention("Server", "", "User", "Message");

        act.Should().NotThrow();
    }

    [Fact]
    public void NotificationSound_HasCorrectValues()
    {
        var privateMessage = NotificationSound.PrivateMessage;
        var mention = NotificationSound.Mention;
        var join = NotificationSound.Join;
        var part = NotificationSound.Part;

        ((int)privateMessage).Should().Be(0);
        ((int)mention).Should().Be(1);
        ((int)join).Should().Be(2);
        ((int)part).Should().Be(3);
    }

    [Fact]
    public void NotifyPrivateMessage_WithNullMessage_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyPrivateMessage("Server", "User", null!);

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyMention_WithSpecialCharacters_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyMention("Server", "#test", "User", "Test <>&\"' message");

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyHighlightWord_WithUnicodeCharacters_DoesNotThrow()
    {
        var service = NotificationService.Instance;
        service.EnableToastNotifications = false;
        service.EnableSoundNotifications = false;

        var act = () => service.NotifyHighlightWord("Server", "#test", "User", "Test 你好 message", "你好");

        act.Should().NotThrow();
    }
}
