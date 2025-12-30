using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for WhoisInfo - WHOIS data parsing, formatting, user information tracking
/// </summary>
public class WhoisInfoTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        // Act
        var whois = new WhoisInfo();

        // Assert
        whois.Nickname.Should().BeEmpty();
        whois.Username.Should().BeNull();
        whois.Hostname.Should().BeNull();
        whois.RealName.Should().BeNull();
        whois.Server.Should().BeNull();
        whois.ServerInfo.Should().BeNull();
        whois.Channels.Should().BeEmpty();
        whois.IsAway.Should().BeFalse();
        whois.AwayMessage.Should().BeNull();
        whois.IsOperator.Should().BeFalse();
        whois.IdleTime.Should().BeNull();
        whois.IdleSeconds.Should().Be(0);
        whois.SignonTime.Should().BeNull();
        whois.Account.Should().BeNull();
        whois.IsSecure.Should().BeFalse();
    }

    [Fact]
    public void BasicInfo_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        whois.Nickname = "TestUser";
        whois.Username = "testuser";
        whois.Hostname = "example.com";
        whois.RealName = "Test User";

        // Assert
        whois.Nickname.Should().Be("TestUser");
        whois.Username.Should().Be("testuser");
        whois.Hostname.Should().Be("example.com");
        whois.RealName.Should().Be("Test User");
    }

    [Fact]
    public void ServerInfo_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        whois.Server = "irc.libera.chat";
        whois.ServerInfo = "Libera Chat Server";

        // Assert
        whois.Server.Should().Be("irc.libera.chat");
        whois.ServerInfo.Should().Be("Libera Chat Server");
    }

    [Fact]
    public void Channels_CanBeAdded()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        whois.Channels.Add("@#ops");
        whois.Channels.Add("+#voice");
        whois.Channels.Add("#general");

        // Assert
        whois.Channels.Should().HaveCount(3);
        whois.Channels.Should().Contain(new[] { "@#ops", "+#voice", "#general" });
    }

    [Fact]
    public void AwayStatus_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        whois.IsAway = true;
        whois.AwayMessage = "Gone for lunch";

        // Assert
        whois.IsAway.Should().BeTrue();
        whois.AwayMessage.Should().Be("Gone for lunch");
    }

    [Fact]
    public void OperatorStatus_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        whois.IsOperator = true;

        // Assert
        whois.IsOperator.Should().BeTrue();
    }

    [Fact]
    public void IdleTime_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();
        var idleTime = DateTime.Now.AddMinutes(-30);

        // Act
        whois.IdleTime = idleTime;
        whois.IdleSeconds = 1800; // 30 minutes

        // Assert
        whois.IdleTime.Should().Be(idleTime);
        whois.IdleSeconds.Should().Be(1800);
    }

    [Fact]
    public void SignonTime_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();
        var signonTime = DateTime.Now.AddHours(-2);

        // Act
        whois.SignonTime = signonTime;

        // Assert
        whois.SignonTime.Should().Be(signonTime);
    }

    [Fact]
    public void Account_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        whois.Account = "accountname";

        // Assert
        whois.Account.Should().Be("accountname");
    }

    [Fact]
    public void SecureConnection_CanBeSet()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        whois.IsSecure = true;

        // Assert
        whois.IsSecure.Should().BeTrue();
    }

    [Fact]
    public void FormattedIdleTime_ActiveNow_WhenZeroSeconds()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 0 };

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Active now");
    }

    [Fact]
    public void FormattedIdleTime_ShowsSeconds_WhenLessThan1Minute()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 45 };

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Idle 45s");
    }

    [Fact]
    public void FormattedIdleTime_ShowsMinutes_WhenLessThan1Hour()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 300 }; // 5 minutes

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Idle 5m");
    }

    [Fact]
    public void FormattedIdleTime_ShowsHoursMinutes_WhenLessThan1Day()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 3900 }; // 1 hour 5 minutes

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Idle 1h 5m");
    }

    [Fact]
    public void FormattedIdleTime_ShowsDaysHours_When1DayOrMore()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 90000 }; // 1 day 1 hour

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Idle 1d 1h");
    }

    [Fact]
    public void UserHost_CombinesUsernameAndHostname()
    {
        // Arrange
        var whois = new WhoisInfo
        {
            Username = "testuser",
            Hostname = "example.com"
        };

        // Act
        var userHost = whois.UserHost;

        // Assert
        userHost.Should().Be("testuser@example.com");
    }

    [Fact]
    public void UserHost_HandlesNullValues()
    {
        // Arrange
        var whois = new WhoisInfo
        {
            Username = null,
            Hostname = null
        };

        // Act
        var userHost = whois.UserHost;

        // Assert
        userHost.Should().Be("@");
    }

    [Fact]
    public void ChannelsString_JoinsWithComma()
    {
        // Arrange
        var whois = new WhoisInfo();
        whois.Channels.Add("@#ops");
        whois.Channels.Add("#general");
        whois.Channels.Add("+#voice");

        // Act
        var channelsString = whois.ChannelsString;

        // Assert
        channelsString.Should().Be("@#ops, #general, +#voice");
    }

    [Fact]
    public void ChannelsString_EmptyWhenNoChannels()
    {
        // Arrange
        var whois = new WhoisInfo();

        // Act
        var channelsString = whois.ChannelsString;

        // Assert
        channelsString.Should().BeEmpty();
    }

    [Fact]
    public void CompleteWhoisInfo_AllFieldsPopulated()
    {
        // Arrange & Act
        var whois = new WhoisInfo
        {
            Nickname = "Alice",
            Username = "alice",
            Hostname = "user.example.com",
            RealName = "Alice Smith",
            Server = "irc.example.com",
            ServerInfo = "Example IRC Server",
            IsAway = true,
            AwayMessage = "Be back soon",
            IsOperator = true,
            IdleSeconds = 300,
            Account = "alice_account",
            IsSecure = true
        };
        whois.Channels.Add("@#admins");
        whois.Channels.Add("#chat");

        // Assert
        whois.Nickname.Should().Be("Alice");
        whois.Username.Should().Be("alice");
        whois.Hostname.Should().Be("user.example.com");
        whois.RealName.Should().Be("Alice Smith");
        whois.Server.Should().Be("irc.example.com");
        whois.ServerInfo.Should().Be("Example IRC Server");
        whois.IsAway.Should().BeTrue();
        whois.AwayMessage.Should().Be("Be back soon");
        whois.IsOperator.Should().BeTrue();
        whois.IdleSeconds.Should().Be(300);
        whois.Account.Should().Be("alice_account");
        whois.IsSecure.Should().BeTrue();
        whois.Channels.Should().HaveCount(2);
        whois.UserHost.Should().Be("alice@user.example.com");
        whois.ChannelsString.Should().Be("@#admins, #chat");
        whois.FormattedIdleTime.Should().Be("Idle 5m");
    }

    [Fact]
    public void FormattedIdleTime_ExactlyOneHour()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 3600 };

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Idle 1h 0m");
    }

    [Fact]
    public void FormattedIdleTime_ExactlyOneDay()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 86400 }; // 24 hours

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Idle 1d 0h");
    }

    [Fact]
    public void FormattedIdleTime_MultipleDays()
    {
        // Arrange
        var whois = new WhoisInfo { IdleSeconds = 259200 }; // 3 days

        // Act
        var formatted = whois.FormattedIdleTime;

        // Assert
        formatted.Should().Be("Idle 3d 0h");
    }
}
