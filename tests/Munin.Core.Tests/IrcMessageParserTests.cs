using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for IrcMessageParser - IRC protocol message parsing.
/// </summary>
public class IrcMessageParserTests
{
    private readonly IrcMessageParser _parser = new();

    [Fact]
    public void Parse_SimpleCommand_ParsesCorrectly()
    {
        var result = _parser.Parse("PING");
        
        result.Command.Should().Be("PING");
        result.Parameters.Should().BeEmpty();
        result.Trailing.Should().BeNull();
    }

    [Fact]
    public void Parse_CommandWithOneParameter_ParsesCorrectly()
    {
        var result = _parser.Parse("NICK TestNick");
        
        result.Command.Should().Be("NICK");
        result.Parameters.Should().ContainSingle().Which.Should().Be("TestNick");
    }

    [Fact]
    public void Parse_CommandWithMultipleParameters_ParsesCorrectly()
    {
        var result = _parser.Parse("JOIN #channel key");
        
        result.Command.Should().Be("JOIN");
        result.Parameters.Should().HaveCount(2);
        result.Parameters[0].Should().Be("#channel");
        result.Parameters[1].Should().Be("key");
    }

    [Fact]
    public void Parse_CommandWithTrailing_ParsesCorrectly()
    {
        var result = _parser.Parse("PRIVMSG #channel :Hello World");
        
        result.Command.Should().Be("PRIVMSG");
        result.Parameters.Should().HaveCount(2);
        result.Parameters[0].Should().Be("#channel");
        result.Trailing.Should().Be("Hello World");
        result.Parameters[1].Should().Be("Hello World");
    }

    [Fact]
    public void Parse_CommandWithPrefix_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!user@host PRIVMSG #channel :message");
        
        result.Prefix.Should().Be("nick!user@host");
        result.Nick.Should().Be("nick");
        result.User.Should().Be("user");
        result.Host.Should().Be("host");
        result.Command.Should().Be("PRIVMSG");
    }

    [Fact]
    public void Parse_NumericReply_ParsesCorrectly()
    {
        var result = _parser.Parse(":server 001 mynick :Welcome to the Internet Relay Network");
        
        result.Command.Should().Be("001");
        result.Parameters.Should().Contain("mynick");
        result.Trailing.Should().Be("Welcome to the Internet Relay Network");
    }

    [Fact]
    public void Parse_ServerPrefix_ParsesCorrectly()
    {
        var result = _parser.Parse(":irc.example.com NOTICE * :*** Looking up your hostname");
        
        result.Prefix.Should().Be("irc.example.com");
        result.Nick.Should().Be("irc.example.com");
        result.Command.Should().Be("NOTICE");
    }

    [Fact]
    public void Parse_WithIRCv3Tags_ParsesCorrectly()
    {
        var result = _parser.Parse("@time=2021-01-01T12:00:00.000Z :nick!user@host PRIVMSG #channel :message");
        
        result.Tags.Should().ContainKey("time");
        result.Tags["time"].Should().Be("2021-01-01T12:00:00.000Z");
        result.Nick.Should().Be("nick");
        result.Command.Should().Be("PRIVMSG");
    }

    [Fact]
    public void Parse_WithMultipleTags_ParsesCorrectly()
    {
        var result = _parser.Parse("@tag1=value1;tag2=value2 :nick!user@host PRIVMSG #channel :message");
        
        result.Tags.Should().HaveCount(2);
        result.Tags["tag1"].Should().Be("value1");
        result.Tags["tag2"].Should().Be("value2");
    }

    [Fact]
    public void Parse_TagWithoutValue_ParsesCorrectly()
    {
        var result = _parser.Parse("@flag :nick!user@host PRIVMSG #channel :message");
        
        result.Tags.Should().ContainKey("flag");
        result.Tags["flag"].Should().Be("");
    }

    [Fact]
    public void Parse_EscapedTagValues_UnescapesCorrectly()
    {
        var result = _parser.Parse(@"@msg=Hello\sWorld\:test\\escaped :nick!user@host PRIVMSG #channel :message");
        
        result.Tags["msg"].Should().Be("Hello World;test\\escaped");
    }

    [Fact]
    public void Parse_PrefixWithoutUser_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick@host PRIVMSG #channel :message");
        
        result.Nick.Should().Be("nick");
        result.User.Should().BeNull();
        result.Host.Should().Be("host");
    }

    [Fact]
    public void Parse_PrefixWithoutHost_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!user PRIVMSG #channel :message");
        
        result.Nick.Should().Be("nick");
        result.User.Should().Be("user");
        result.Host.Should().BeNull();
    }

    [Fact]
    public void Parse_PrefixWithOnlyNick_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick PRIVMSG #channel :message");
        
        result.Nick.Should().Be("nick");
        result.User.Should().BeNull();
        result.Host.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyTrailing_ParsesCorrectly()
    {
        var result = _parser.Parse("PRIVMSG #channel :");
        
        result.Command.Should().Be("PRIVMSG");
        result.Trailing.Should().Be("");
    }

    [Fact]
    public void Parse_TrailingWithColons_ParsesCorrectly()
    {
        var result = _parser.Parse("PRIVMSG #channel :Message with : colons : inside");
        
        result.Trailing.Should().Be("Message with : colons : inside");
    }

    [Fact]
    public void Parse_ModeCommand_ParsesCorrectly()
    {
        var result = _parser.Parse("MODE #channel +o nick");
        
        result.Command.Should().Be("MODE");
        result.Parameters.Should().HaveCount(3);
        result.Parameters[0].Should().Be("#channel");
        result.Parameters[1].Should().Be("+o");
        result.Parameters[2].Should().Be("nick");
    }

    [Fact]
    public void Parse_KickCommand_ParsesCorrectly()
    {
        var result = _parser.Parse("KICK #channel baduser :Reason for kick");
        
        result.Command.Should().Be("KICK");
        result.Parameters[0].Should().Be("#channel");
        result.Parameters[1].Should().Be("baduser");
        result.Trailing.Should().Be("Reason for kick");
    }

    [Fact]
    public void Parse_InvalidMessage_StillParsesCommand()
    {
        var result = _parser.Parse("This is not a valid IRC message!");
        
        // Parser may extract "THIS" as command even from invalid message
        result.Command.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_EmptyString_ReturnsUnknownCommand()
    {
        var result = _parser.Parse("");
        
        result.Command.Should().Be("UNKNOWN");
    }

    [Fact]
    public void Parse_LowercaseCommand_ConvertsToUppercase()
    {
        var result = _parser.Parse("privmsg #channel :test");
        
        result.Command.Should().Be("PRIVMSG");
    }

    [Fact]
    public void Parse_MixedCaseCommand_ConvertsToUppercase()
    {
        var result = _parser.Parse("PrIvMsG #channel :test");
        
        result.Command.Should().Be("PRIVMSG");
    }

    [Fact]
    public void Parse_RealWorldPing_ParsesCorrectly()
    {
        var result = _parser.Parse("PING :irc.example.com");
        
        result.Command.Should().Be("PING");
        result.Trailing.Should().Be("irc.example.com");
    }

    [Fact]
    public void Parse_RealWorldWelcome_ParsesCorrectly()
    {
        var result = _parser.Parse(":irc.example.com 001 mynick :Welcome to the Example IRC Network mynick!user@host");
        
        result.Command.Should().Be("001");
        result.Prefix.Should().Be("irc.example.com");
        result.Parameters[0].Should().Be("mynick");
        result.Trailing.Should().StartWith("Welcome to");
    }

    [Fact]
    public void Parse_RealWorldJoin_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!~user@host.example.com JOIN #channel");
        
        result.Command.Should().Be("JOIN");
        result.Nick.Should().Be("nick");
        result.User.Should().Be("~user");
        result.Host.Should().Be("host.example.com");
        result.Parameters[0].Should().Be("#channel");
    }

    [Fact]
    public void Parse_RealWorldQuit_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!user@host QUIT :Quit: Leaving");
        
        result.Command.Should().Be("QUIT");
        result.Trailing.Should().Be("Quit: Leaving");
    }

    [Fact]
    public void Parse_CapLs_ParsesCorrectly()
    {
        var result = _parser.Parse(":irc.example.com CAP * LS :multi-prefix sasl");
        
        result.Command.Should().Be("CAP");
        result.Parameters[0].Should().Be("*");
        result.Parameters[1].Should().Be("LS");
        result.Trailing.Should().Be("multi-prefix sasl");
    }

    [Fact]
    public void IsCTCP_WithCTCPMessage_ReturnsTrue()
    {
        IrcMessageParser.IsCTCP("\x01VERSION\x01").Should().BeTrue();
    }

    [Fact]
    public void IsCTCP_WithNormalMessage_ReturnsFalse()
    {
        IrcMessageParser.IsCTCP("Normal message").Should().BeFalse();
    }

    [Fact]
    public void IsCTCP_WithPartialCTCP_ReturnsFalse()
    {
        IrcMessageParser.IsCTCP("\x01VERSION").Should().BeFalse();
        IrcMessageParser.IsCTCP("VERSION\x01").Should().BeFalse();
    }

    [Fact]
    public void ParseCTCP_SimpleCommand_ParsesCorrectly()
    {
        var (command, parameter) = IrcMessageParser.ParseCTCP("\x01VERSION\x01");
        
        command.Should().Be("VERSION");
        parameter.Should().BeNull();
    }

    [Fact]
    public void ParseCTCP_CommandWithParameter_ParsesCorrectly()
    {
        var (command, parameter) = IrcMessageParser.ParseCTCP("\x01PING 1234567890\x01");
        
        command.Should().Be("PING");
        parameter.Should().Be("1234567890");
    }

    [Fact]
    public void ParseCTCP_Action_ParsesCorrectly()
    {
        var message = "\u0001ACTION does something\u0001";
        var (command, parameter) = IrcMessageParser.ParseCTCP(message);
        
        command.Should().Be("ACTION");
        parameter.Should().Be("does something");
    }

    [Fact]
    public void Parse_CTCPInPrivmsg_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!user@host PRIVMSG #channel :\x01VERSION\x01");
        
        result.Command.Should().Be("PRIVMSG");
        result.Trailing.Should().Be("\x01VERSION\x01");
        IrcMessageParser.IsCTCP(result.Trailing!).Should().BeTrue();
    }

    [Fact]
    public void Parse_WhoisReply_ParsesCorrectly()
    {
        var result = _parser.Parse(":irc.example.com 311 mynick othernick ~user host.example.com * :Real Name");
        
        result.Command.Should().Be("311");
        result.Parameters.Should().HaveCount(6);
        result.Parameters[0].Should().Be("mynick");
        result.Parameters[1].Should().Be("othernick");
        result.Parameters[2].Should().Be("~user");
        result.Parameters[3].Should().Be("host.example.com");
        result.Parameters[4].Should().Be("*");
        result.Trailing.Should().Be("Real Name");
    }

    [Fact]
    public void Parse_TopicReply_ParsesCorrectly()
    {
        var result = _parser.Parse(":irc.example.com 332 mynick #channel :This is the channel topic");
        
        result.Command.Should().Be("332");
        result.Trailing.Should().Be("This is the channel topic");
    }

    [Fact]
    public void Parse_NamesReply_ParsesCorrectly()
    {
        var result = _parser.Parse(":irc.example.com 353 mynick = #channel :@op +voice regular");
        
        result.Command.Should().Be("353");
        result.Parameters[0].Should().Be("mynick");
        result.Parameters[1].Should().Be("=");
        result.Parameters[2].Should().Be("#channel");
        result.Trailing.Should().Be("@op +voice regular");
    }

    [Fact]
    public void Parse_MessageWithUnicodeCharacters_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!user@host PRIVMSG #channel :Hello 世界 🌍");
        
        result.Trailing.Should().Be("Hello 世界 🌍");
    }

    [Fact]
    public void Parse_InviteMessage_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!user@host INVITE othernick :#channel");
        
        result.Command.Should().Be("INVITE");
        result.Parameters[0].Should().Be("othernick");
        result.Trailing.Should().Be("#channel");
    }

    [Fact]
    public void Parse_PartMessage_ParsesCorrectly()
    {
        var result = _parser.Parse(":nick!user@host PART #channel :Leaving the channel");
        
        result.Command.Should().Be("PART");
        result.Parameters[0].Should().Be("#channel");
        result.Trailing.Should().Be("Leaving the channel");
    }

    [Fact]
    public void Parse_AwayReply_ParsesCorrectly()
    {
        var result = _parser.Parse(":irc.example.com 301 mynick othernick :I am away");
        
        result.Command.Should().Be("301");
        result.Trailing.Should().Be("I am away");
    }

    [Fact]
    public void Parse_ErrorMessage_ParsesCorrectly()
    {
        var result = _parser.Parse("ERROR :Closing Link: host (Connection timeout)");
        
        result.Command.Should().Be("ERROR");
        result.Trailing.Should().Be("Closing Link: host (Connection timeout)");
    }

    [Fact]
    public void Parse_BatchStartTag_ParsesCorrectly()
    {
        var result = _parser.Parse("@batch=abc123 :nick!user@host PRIVMSG #channel :message");
        
        result.Tags["batch"].Should().Be("abc123");
    }

    [Fact]
    public void ParseCTCP_LowercaseCommand_ConvertsToUppercase()
    {
        var (command, parameter) = IrcMessageParser.ParseCTCP("\x01version\x01");
        
        command.Should().Be("VERSION");
    }

    [Fact]
    public void Parse_StoresRawMessage()
    {
        var rawMessage = ":nick!user@host PRIVMSG #channel :test";
        var result = _parser.Parse(rawMessage);
        
        result.RawMessage.Should().Be(rawMessage);
    }

    [Fact]
    public void Parse_WhitespaceAtEnd_HandlesCorrectly()
    {
        var result = _parser.Parse("PING :server   ");
        
        result.Command.Should().Be("PING");
        result.Trailing.Should().Be("server   ");
    }
}
