using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for WhoInfo - WHO reply parsing and user information tracking
/// </summary>
public class WhoInfoTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        // Act
        var whoInfo = new WhoInfo();

        // Assert
        whoInfo.Channel.Should().BeEmpty();
        whoInfo.Username.Should().BeEmpty();
        whoInfo.Hostname.Should().BeEmpty();
        whoInfo.Server.Should().BeEmpty();
        whoInfo.Nickname.Should().BeEmpty();
        whoInfo.Flags.Should().BeEmpty();
        whoInfo.HopCount.Should().Be(0);
        whoInfo.RealName.Should().BeEmpty();
        whoInfo.Account.Should().BeNull();
        whoInfo.IsAway.Should().BeFalse();
        whoInfo.IsOper.Should().BeFalse();
        whoInfo.IsChannelOp.Should().BeFalse();
        whoInfo.HasVoice.Should().BeFalse();
    }

    [Fact]
    public void BasicInfo_CanBeSet()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Channel = "#linux";
        whoInfo.Username = "alice";
        whoInfo.Hostname = "user.example.com";
        whoInfo.Server = "irc.libera.chat";
        whoInfo.Nickname = "Alice";
        whoInfo.RealName = "Alice Smith";
        whoInfo.HopCount = 2;

        // Assert
        whoInfo.Channel.Should().Be("#linux");
        whoInfo.Username.Should().Be("alice");
        whoInfo.Hostname.Should().Be("user.example.com");
        whoInfo.Server.Should().Be("irc.libera.chat");
        whoInfo.Nickname.Should().Be("Alice");
        whoInfo.RealName.Should().Be("Alice Smith");
        whoInfo.HopCount.Should().Be(2);
    }

    [Fact]
    public void Flags_Here_NotAway()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "H";

        // Assert
        whoInfo.Flags.Should().Be("H");
        whoInfo.IsAway.Should().BeFalse();
    }

    [Fact]
    public void Flags_Gone_IsAway()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "G";

        // Assert
        whoInfo.Flags.Should().Be("G");
        whoInfo.IsAway.Should().BeTrue();
    }

    [Fact]
    public void Flags_Oper_IsOper()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "H*";

        // Assert
        whoInfo.Flags.Should().Be("H*");
        whoInfo.IsOper.Should().BeTrue();
        whoInfo.IsAway.Should().BeFalse();
    }

    [Fact]
    public void Flags_ChannelOp_IsChannelOp()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "H@";

        // Assert
        whoInfo.Flags.Should().Be("H@");
        whoInfo.IsChannelOp.Should().BeTrue();
        whoInfo.IsAway.Should().BeFalse();
    }

    [Fact]
    public void Flags_Voice_HasVoice()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "H+";

        // Assert
        whoInfo.Flags.Should().Be("H+");
        whoInfo.HasVoice.Should().BeTrue();
        whoInfo.IsAway.Should().BeFalse();
    }

    [Fact]
    public void Flags_Combined_AllFlags()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "H*@";

        // Assert
        whoInfo.IsAway.Should().BeFalse();
        whoInfo.IsOper.Should().BeTrue();
        whoInfo.IsChannelOp.Should().BeTrue();
    }

    [Fact]
    public void Flags_AwayAndOp()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "G@";

        // Assert
        whoInfo.IsAway.Should().BeTrue();
        whoInfo.IsChannelOp.Should().BeTrue();
    }

    [Fact]
    public void Flags_AwayAndVoice()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "G+";

        // Assert
        whoInfo.IsAway.Should().BeTrue();
        whoInfo.HasVoice.Should().BeTrue();
    }

    [Fact]
    public void Account_CanBeSet()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Account = "alice_account";

        // Assert
        whoInfo.Account.Should().Be("alice_account");
    }

    [Fact]
    public void Account_NullByDefault()
    {
        // Arrange & Act
        var whoInfo = new WhoInfo();

        // Assert
        whoInfo.Account.Should().BeNull();
    }

    [Fact]
    public void Channel_Wildcard_NoSpecificChannel()
    {
        // Arrange & Act
        var whoInfo = new WhoInfo
        {
            Channel = "*",
            Nickname = "Bob",
            Username = "bob",
            Hostname = "host.example.com"
        };

        // Assert
        whoInfo.Channel.Should().Be("*");
    }

    [Fact]
    public void HopCount_Zero()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.HopCount = 0;

        // Assert
        whoInfo.HopCount.Should().Be(0);
    }

    [Fact]
    public void HopCount_MultipleHops()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.HopCount = 5;

        // Assert
        whoInfo.HopCount.Should().Be(5);
    }

    [Fact]
    public void CompleteWhoInfo_ChannelUser()
    {
        // Arrange & Act
        var whoInfo = new WhoInfo
        {
            Channel = "#programming",
            Username = "developer",
            Hostname = "dev.example.com",
            Server = "irc.libera.chat",
            Nickname = "DevUser",
            Flags = "H@",
            HopCount = 1,
            RealName = "Dev User",
            Account = "devaccount"
        };

        // Assert
        whoInfo.Channel.Should().Be("#programming");
        whoInfo.Username.Should().Be("developer");
        whoInfo.Hostname.Should().Be("dev.example.com");
        whoInfo.Server.Should().Be("irc.libera.chat");
        whoInfo.Nickname.Should().Be("DevUser");
        whoInfo.Flags.Should().Be("H@");
        whoInfo.HopCount.Should().Be(1);
        whoInfo.RealName.Should().Be("Dev User");
        whoInfo.Account.Should().Be("devaccount");
        whoInfo.IsAway.Should().BeFalse();
        whoInfo.IsChannelOp.Should().BeTrue();
        whoInfo.HasVoice.Should().BeFalse();
        whoInfo.IsOper.Should().BeFalse();
    }

    [Fact]
    public void CompleteWhoInfo_OperatorAway()
    {
        // Arrange & Act
        var whoInfo = new WhoInfo
        {
            Channel = "#opers",
            Username = "oper",
            Hostname = "staff.example.com",
            Server = "irc.example.com",
            Nickname = "NetworkOper",
            Flags = "G*@",
            HopCount = 0,
            RealName = "Network Operator"
        };

        // Assert
        whoInfo.IsAway.Should().BeTrue();
        whoInfo.IsOper.Should().BeTrue();
        whoInfo.IsChannelOp.Should().BeTrue();
    }

    [Fact]
    public void Flags_EmptyString_NoFlags()
    {
        // Arrange
        var whoInfo = new WhoInfo();

        // Act
        whoInfo.Flags = "";

        // Assert
        whoInfo.IsAway.Should().BeFalse();
        whoInfo.IsOper.Should().BeFalse();
        whoInfo.IsChannelOp.Should().BeFalse();
        whoInfo.HasVoice.Should().BeFalse();
    }

    [Fact]
    public void RealName_WithSpaces()
    {
        // Arrange & Act
        var whoInfo = new WhoInfo
        {
            RealName = "John Q. Public"
        };

        // Assert
        whoInfo.RealName.Should().Be("John Q. Public");
    }

    [Fact]
    public void WhoInfo_MultipleChannelOps()
    {
        // Arrange - Simulate WHO response with multiple operators
        var ops = new List<WhoInfo>
        {
            new WhoInfo { Nickname = "Op1", Flags = "H@", Channel = "#channel" },
            new WhoInfo { Nickname = "Op2", Flags = "H@", Channel = "#channel" },
            new WhoInfo { Nickname = "User1", Flags = "H", Channel = "#channel" }
        };

        // Assert
        ops.Count(w => w.IsChannelOp).Should().Be(2);
        ops.Count(w => !w.IsChannelOp).Should().Be(1);
    }
}
