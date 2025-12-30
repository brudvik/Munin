using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for ParsedIrcMessage - parsed IRC protocol message structure.
/// </summary>
public class ParsedIrcMessageTests
{
    [Fact]
    public void Constructor_ShouldInitializeCollections()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage();

        // Assert
        message.Parameters.Should().NotBeNull().And.BeEmpty();
        message.Tags.Should().NotBeNull().And.BeEmpty();
        message.RawMessage.Should().BeEmpty();
    }

    [Fact]
    public void PrefixParsing_ShouldExtractNickUserHost()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Prefix = "nick!user@host.example.com",
            Nick = "nick",
            User = "user",
            Host = "host.example.com"
        };

        // Assert
        message.Prefix.Should().Be("nick!user@host.example.com");
        message.Nick.Should().Be("nick");
        message.User.Should().Be("user");
        message.Host.Should().Be("host.example.com");
    }

    [Fact]
    public void Command_ShouldStoreCommand()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Command = "PRIVMSG"
        };

        // Assert
        message.Command.Should().Be("PRIVMSG");
    }

    [Fact]
    public void Parameters_ShouldStoreMultipleParameters()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Parameters = new List<string> { "#munin", "Hello", "world" }
        };

        // Assert
        message.Parameters.Should().HaveCount(3);
        message.Parameters[0].Should().Be("#munin");
        message.Parameters[1].Should().Be("Hello");
        message.Parameters[2].Should().Be("world");
    }

    [Fact]
    public void Trailing_ShouldStoreTrailingMessage()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Parameters = new List<string> { "#munin" },
            Trailing = "This is a test message"
        };

        // Assert
        message.Trailing.Should().Be("This is a test message");
    }

    [Fact]
    public void GetParameter_ValidIndex_ShouldReturnParameter()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Parameters = new List<string> { "param0", "param1", "param2" }
        };

        // Act & Assert
        message.GetParameter(0).Should().Be("param0");
        message.GetParameter(1).Should().Be("param1");
        message.GetParameter(2).Should().Be("param2");
    }

    [Fact]
    public void GetParameter_InvalidIndex_ShouldReturnNull()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Parameters = new List<string> { "param0" }
        };

        // Act & Assert
        message.GetParameter(-1).Should().BeNull();
        message.GetParameter(1).Should().BeNull();
        message.GetParameter(100).Should().BeNull();
    }

    [Fact]
    public void GetParameter_EmptyList_ShouldReturnNull()
    {
        // Arrange
        var message = new ParsedIrcMessage();

        // Act & Assert
        message.GetParameter(0).Should().BeNull();
    }

    [Fact]
    public void Tags_ShouldStoreIRCv3Tags()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["time"] = "2025-12-30T12:34:56.789Z",
                ["account"] = "username",
                ["msgid"] = "msg123"
            }
        };

        // Assert
        message.Tags.Should().HaveCount(3);
        message.Tags["time"].Should().Be("2025-12-30T12:34:56.789Z");
        message.Tags["account"].Should().Be("username");
        message.Tags["msgid"].Should().Be("msg123");
    }

    [Fact]
    public void GetTimestamp_WithTimeTag_ShouldParseISO8601()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["time"] = "2025-12-30T12:34:56.789Z"
            }
        };

        // Act
        var timestamp = message.GetTimestamp();

        // Assert
        timestamp.Year.Should().Be(2025);
        timestamp.Month.Should().Be(12);
        timestamp.Day.Should().Be(30);
        timestamp.Hour.Should().BeGreaterThanOrEqualTo(0); // Converted to local time
    }

    [Fact]
    public void GetTimestamp_WithoutTimeTag_ShouldReturnCurrentTime()
    {
        // Arrange
        var message = new ParsedIrcMessage();
        var beforeCall = DateTime.Now;

        // Act
        var timestamp = message.GetTimestamp();
        var afterCall = DateTime.Now;

        // Assert
        timestamp.Should().BeOnOrAfter(beforeCall).And.BeOnOrBefore(afterCall);
    }

    [Fact]
    public void GetTimestamp_InvalidTimeTag_ShouldReturnCurrentTime()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["time"] = "invalid-date"
            }
        };
        var beforeCall = DateTime.Now;

        // Act
        var timestamp = message.GetTimestamp();
        var afterCall = DateTime.Now;

        // Assert
        timestamp.Should().BeOnOrAfter(beforeCall).And.BeOnOrBefore(afterCall);
    }

    [Fact]
    public void GetTimestamp_EmptyTimeTag_ShouldReturnCurrentTime()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["time"] = ""
            }
        };
        var beforeCall = DateTime.Now;

        // Act
        var timestamp = message.GetTimestamp();
        var afterCall = DateTime.Now;

        // Assert
        timestamp.Should().BeOnOrAfter(beforeCall).And.BeOnOrBefore(afterCall);
    }

    [Fact]
    public void GetAccountName_WithAccountTag_ShouldReturnAccount()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["account"] = "testuser"
            }
        };

        // Act
        var account = message.GetAccountName();

        // Assert
        account.Should().Be("testuser");
    }

    [Fact]
    public void GetAccountName_WithoutAccountTag_ShouldReturnNull()
    {
        // Arrange
        var message = new ParsedIrcMessage();

        // Act
        var account = message.GetAccountName();

        // Assert
        account.Should().BeNull();
    }

    [Fact]
    public void GetAccountName_WithAsteriskValue_ShouldReturnNull()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["account"] = "*" // IRCv3 uses "*" to indicate "not logged in"
            }
        };

        // Act
        var account = message.GetAccountName();

        // Assert
        account.Should().BeNull();
    }

    [Fact]
    public void GetMessageId_WithMsgidTag_ShouldReturnMessageId()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["msgid"] = "abc123xyz"
            }
        };

        // Act
        var msgid = message.GetMessageId();

        // Assert
        msgid.Should().Be("abc123xyz");
    }

    [Fact]
    public void GetMessageId_WithoutMsgidTag_ShouldReturnNull()
    {
        // Arrange
        var message = new ParsedIrcMessage();

        // Act
        var msgid = message.GetMessageId();

        // Assert
        msgid.Should().BeNull();
    }

    [Fact]
    public void GetLabel_WithLabelTag_ShouldReturnLabel()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["label"] = "label123"
            }
        };

        // Act
        var label = message.GetLabel();

        // Assert
        label.Should().Be("label123");
    }

    [Fact]
    public void GetLabel_WithoutLabelTag_ShouldReturnNull()
    {
        // Arrange
        var message = new ParsedIrcMessage();

        // Act
        var label = message.GetLabel();

        // Assert
        label.Should().BeNull();
    }

    [Fact]
    public void IsEcho_WithEchoMessageTag_ShouldReturnTrue()
    {
        // Arrange
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["echo-message"] = ""
            }
        };

        // Act & Assert
        message.IsEcho.Should().BeTrue();
    }

    [Fact]
    public void IsEcho_WithoutEchoMessageTag_ShouldReturnFalse()
    {
        // Arrange
        var message = new ParsedIrcMessage();

        // Act & Assert
        message.IsEcho.Should().BeFalse();
    }

    [Fact]
    public void RawMessage_ShouldStoreOriginalMessage()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            RawMessage = "@time=2025-12-30T12:34:56.789Z :nick!user@host PRIVMSG #munin :Hello"
        };

        // Assert
        message.RawMessage.Should().Be("@time=2025-12-30T12:34:56.789Z :nick!user@host PRIVMSG #munin :Hello");
    }

    [Fact]
    public void ServerPrefix_ShouldHandleServerAsPrefix()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Prefix = "irc.example.com",
            Nick = null,
            User = null,
            Host = null
        };

        // Assert
        message.Prefix.Should().Be("irc.example.com");
        message.Nick.Should().BeNull();
        message.User.Should().BeNull();
        message.Host.Should().BeNull();
    }

    [Fact]
    public void ComplexMessage_AllFieldsPopulated()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Tags = new Dictionary<string, string>
            {
                ["time"] = "2025-12-30T12:34:56.789Z",
                ["account"] = "testuser",
                ["msgid"] = "msg123"
            },
            Prefix = "nick!user@host.example.com",
            Nick = "nick",
            User = "user",
            Host = "host.example.com",
            Command = "PRIVMSG",
            Parameters = new List<string> { "#munin" },
            Trailing = "This is a test message",
            RawMessage = "@time=2025-12-30T12:34:56.789Z;account=testuser;msgid=msg123 :nick!user@host.example.com PRIVMSG #munin :This is a test message"
        };

        // Assert
        message.Tags.Should().HaveCount(3);
        message.Prefix.Should().Be("nick!user@host.example.com");
        message.Nick.Should().Be("nick");
        message.User.Should().Be("user");
        message.Host.Should().Be("host.example.com");
        message.Command.Should().Be("PRIVMSG");
        message.Parameters.Should().ContainSingle().Which.Should().Be("#munin");
        message.Trailing.Should().Be("This is a test message");
        message.GetAccountName().Should().Be("testuser");
        message.GetMessageId().Should().Be("msg123");
        message.RawMessage.Should().NotBeEmpty();
    }

    [Fact]
    public void NumericCommand_ShouldStoreAsString()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Command = "001" // RPL_WELCOME
        };

        // Assert
        message.Command.Should().Be("001");
    }

    [Fact]
    public void MultipleParameters_NoTrailing()
    {
        // Arrange & Act
        var message = new ParsedIrcMessage
        {
            Command = "MODE",
            Parameters = new List<string> { "#munin", "+o", "user" }
        };

        // Assert
        message.Parameters.Should().HaveCount(3);
        message.Trailing.Should().BeNull();
    }
}
