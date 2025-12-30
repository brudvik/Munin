using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for ChannelListModeEntry - ban/exception/invite list entries.
/// </summary>
public class ChannelListModeEntryTests
{
    [Fact]
    public void Constructor_ShouldAllowCreation()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry();

        // Assert
        entry.Should().NotBeNull();
        entry.Mask.Should().BeEmpty();
    }

    [Fact]
    public void BanEntry_ShouldStoreBanMask()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@*.example.com"
        };

        // Assert
        entry.Mode.Should().Be('b');
        entry.Mask.Should().Be("*!*@*.example.com");
    }

    [Fact]
    public void ExceptionEntry_ShouldStoreExceptionMask()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'e',
            Mask = "*!*@trusted.host.com"
        };

        // Assert
        entry.Mode.Should().Be('e');
        entry.Mask.Should().Be("*!*@trusted.host.com");
    }

    [Fact]
    public void InviteEntry_ShouldStoreInviteMask()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'I',
            Mask = "*!*@vip.host.com"
        };

        // Assert
        entry.Mode.Should().Be('I');
        entry.Mask.Should().Be("*!*@vip.host.com");
    }

    [Fact]
    public void SetBy_ShouldStoreWhoSetTheEntry()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@spam.com",
            SetBy = "operator"
        };

        // Assert
        entry.SetBy.Should().Be("operator");
    }

    [Fact]
    public void SetBy_CanBeNull()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@test.com"
        };

        // Assert
        entry.SetBy.Should().BeNull();
    }

    [Fact]
    public void SetAt_ShouldStoreTimestamp()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@spam.com",
            SetAt = timestamp
        };

        // Assert
        entry.SetAt.Should().Be(timestamp);
    }

    [Fact]
    public void SetAt_CanBeNull()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@test.com"
        };

        // Assert
        entry.SetAt.Should().BeNull();
    }

    [Fact]
    public void CompleteEntry_AllFieldsPopulated()
    {
        // Arrange
        var timestamp = new DateTime(2025, 12, 30, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@abuser.example.com",
            SetBy = "admin",
            SetAt = timestamp
        };

        // Assert
        entry.Mode.Should().Be('b');
        entry.Mask.Should().Be("*!*@abuser.example.com");
        entry.SetBy.Should().Be("admin");
        entry.SetAt.Should().Be(timestamp);
    }

    [Fact]
    public void Mask_CanBeSimpleNick()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "BadNick!*@*"
        };

        // Assert
        entry.Mask.Should().Be("BadNick!*@*");
    }

    [Fact]
    public void Mask_CanBeHostOnly()
    {
        // Arrange & Act
        var entry = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@spam.server.net"
        };

        // Assert
        entry.Mask.Should().Be("*!*@spam.server.net");
    }

    [Fact]
    public void ChannelListModeType_BanEnum()
    {
        // Arrange & Act
        var type = ChannelListModeType.Ban;

        // Assert
        type.Should().Be(ChannelListModeType.Ban);
        ((int)type).Should().Be(0);
    }

    [Fact]
    public void ChannelListModeType_ExceptionEnum()
    {
        // Arrange & Act
        var type = ChannelListModeType.Exception;

        // Assert
        type.Should().Be(ChannelListModeType.Exception);
        ((int)type).Should().Be(1);
    }

    [Fact]
    public void ChannelListModeType_InviteEnum()
    {
        // Arrange & Act
        var type = ChannelListModeType.Invite;

        // Assert
        type.Should().Be(ChannelListModeType.Invite);
        ((int)type).Should().Be(2);
    }

    [Fact]
    public void MultipleEntries_CanExistIndependently()
    {
        // Arrange
        var ban = new ChannelListModeEntry
        {
            Mode = 'b',
            Mask = "*!*@banned.com",
            SetBy = "op1"
        };

        var exception = new ChannelListModeEntry
        {
            Mode = 'e',
            Mask = "*!*@trusted.com",
            SetBy = "op2"
        };

        // Act
        var entries = new List<ChannelListModeEntry> { ban, exception };

        // Assert
        entries.Should().HaveCount(2);
        entries[0].Mode.Should().Be('b');
        entries[1].Mode.Should().Be('e');
    }
}
