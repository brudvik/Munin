using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Advanced tests for IrcMessageParser - numeric replies, complex messages, and edge cases.
/// </summary>
public class IrcParserAdvancedTests
{
    private readonly IrcMessageParser _parser = new();

    #region Numeric Replies (001-999)

    [Fact]
    public void Parse_NumericReply001_Welcome()
    {
        var message = ":irc.server.net 001 nick :Welcome to the IRC Network nick!user@host";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("001");
        parsed.Prefix.Should().Be("irc.server.net");
        parsed.Parameters.Should().HaveCount(2);
        parsed.Parameters[0].Should().Be("nick");
        parsed.Trailing.Should().Contain("Welcome");
    }

    [Fact]
    public void Parse_NumericReply353_NamesReply()
    {
        var message = ":server 353 mynick = #channel :@op +voice regular";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("353");
        parsed.Parameters.Should().HaveCount(4);
        parsed.Parameters[0].Should().Be("mynick");
        parsed.Parameters[1].Should().Be("="); // Channel type
        parsed.Parameters[2].Should().Be("#channel");
        parsed.Trailing.Should().Be("@op +voice regular");
    }

    [Fact]
    public void Parse_NumericReply366_EndOfNames()
    {
        var message = ":server 366 mynick #channel :End of /NAMES list.";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("366");
        parsed.Parameters[0].Should().Be("mynick");
        parsed.Parameters[1].Should().Be("#channel");
        parsed.Trailing.Should().Contain("End of /NAMES");
    }

    [Fact]
    public void Parse_NumericReply311_WhoisUser()
    {
        var message = ":server 311 mynick targetnick username hostname * :Real Name";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("311");
        parsed.Parameters.Should().HaveCount(6);
        parsed.Parameters[1].Should().Be("targetnick");
        parsed.Parameters[2].Should().Be("username");
        parsed.Parameters[3].Should().Be("hostname");
        parsed.Trailing.Should().Be("Real Name");
    }

    [Fact]
    public void Parse_NumericReply324_ChannelModeIs()
    {
        var message = ":server 324 mynick #channel +nt";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("324");
        parsed.Parameters[1].Should().Be("#channel");
        parsed.Parameters[2].Should().Be("+nt");
    }

    [Fact]
    public void Parse_NumericReply332_TopicIs()
    {
        var message = ":server 332 mynick #channel :This is the channel topic";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("332");
        parsed.Parameters[1].Should().Be("#channel");
        parsed.Trailing.Should().Be("This is the channel topic");
    }

    [Fact]
    public void Parse_NumericReply333_TopicSetBy()
    {
        var message = ":server 333 mynick #channel setter!user@host 1609459200";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("333");
        parsed.Parameters[1].Should().Be("#channel");
        parsed.Parameters[2].Should().Be("setter!user@host");
        parsed.Parameters[3].Should().Be("1609459200"); // Unix timestamp
    }

    [Fact]
    public void Parse_NumericReply352_WhoReply()
    {
        var message = ":server 352 mynick #channel username host server nick H :0 Real Name";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("352");
        parsed.Parameters[1].Should().Be("#channel");
        parsed.Parameters[2].Should().Be("username");
        parsed.Parameters[3].Should().Be("host");
        parsed.Parameters[5].Should().Be("nick");
        parsed.Parameters[6].Should().Be("H"); // Here, not away
        parsed.Trailing.Should().Contain("Real Name");
    }

    [Fact]
    public void Parse_NumericReply433_NicknameInUse()
    {
        var message = ":server 433 * desirednick :Nickname is already in use.";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("433");
        parsed.Parameters[0].Should().Be("*"); // No nick yet
        parsed.Parameters[1].Should().Be("desirednick");
        parsed.Trailing.Should().Contain("already in use");
    }

    [Fact]
    public void Parse_NumericReply005_ISupport()
    {
        var message = ":server 005 nick NETWORK=Libera CHANTYPES=# PREFIX=(ov)@+ :are supported";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("005");
        parsed.Parameters[1].Should().Be("NETWORK=Libera");
        parsed.Parameters[2].Should().Be("CHANTYPES=#");
        parsed.Parameters[3].Should().Be("PREFIX=(ov)@+");
        parsed.Trailing.Should().Be("are supported");
    }

    #endregion

    #region Complex Mode Changes

    [Fact]
    public void Parse_ComplexModeWithMultipleParameters()
    {
        var message = ":op!user@host MODE #channel +ov-b user1 user2 ban*!*@*";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("MODE");
        parsed.Parameters[0].Should().Be("#channel");
        parsed.Parameters[1].Should().Be("+ov-b");
        parsed.Parameters[2].Should().Be("user1");
        parsed.Parameters[3].Should().Be("user2");
        parsed.Parameters[4].Should().Be("ban*!*@*");
    }

    [Fact]
    public void Parse_UserModeChange()
    {
        var message = ":server MODE nick :+i";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("MODE");
        parsed.Parameters[0].Should().Be("nick");
        parsed.Trailing.Should().Be("+i");
    }

    [Fact]
    public void Parse_ChannelModeWithKey()
    {
        var message = ":op!user@host MODE #channel +k secretkey";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("MODE");
        parsed.Parameters[1].Should().Be("+k");
        parsed.Parameters[2].Should().Be("secretkey");
    }

    [Fact]
    public void Parse_ChannelModeWithLimit()
    {
        var message = ":op!user@host MODE #channel +l 100";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("MODE");
        parsed.Parameters[1].Should().Be("+l");
        parsed.Parameters[2].Should().Be("100");
    }

    #endregion

    #region Extended Join (IRCv3)

    [Fact]
    public void Parse_ExtendedJoinWithAccountName()
    {
        var message = ":nick!user@host JOIN #channel accountname :Real Name";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("JOIN");
        parsed.Parameters[0].Should().Be("#channel");
        parsed.Parameters[1].Should().Be("accountname");
        parsed.Trailing.Should().Be("Real Name");
    }

    [Fact]
    public void Parse_ExtendedJoinWithoutAccount()
    {
        var message = ":nick!user@host JOIN #channel * :Real Name";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("JOIN");
        parsed.Parameters[1].Should().Be("*"); // Not authenticated
        parsed.Trailing.Should().Be("Real Name");
    }

    #endregion

    #region SASL Authentication

    [Fact]
    public void Parse_SaslAuthenticate()
    {
        var message = "AUTHENTICATE PLAIN";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("AUTHENTICATE");
        parsed.Parameters[0].Should().Be("PLAIN");
    }

    [Fact]
    public void Parse_SaslAuthenticateChunked()
    {
        var message = "AUTHENTICATE +";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("AUTHENTICATE");
        parsed.Parameters[0].Should().Be("+");
    }

    [Fact]
    public void Parse_SaslSuccess900()
    {
        var message = ":server 900 nick nick!user@host accountname :You are now logged in as accountname";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("900");
        parsed.Parameters[1].Should().Be("nick!user@host");
        parsed.Parameters[2].Should().Be("accountname");
        parsed.Trailing.Should().Contain("logged in");
    }

    [Fact]
    public void Parse_SaslFailed904()
    {
        var message = ":server 904 nick :SASL authentication failed";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("904");
        parsed.Trailing.Should().Contain("failed");
    }

    #endregion

    #region CAP Negotiation

    [Fact]
    public void Parse_CapLs()
    {
        var message = ":server CAP * LS :multi-prefix sasl account-notify";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("CAP");
        parsed.Parameters[0].Should().Be("*");
        parsed.Parameters[1].Should().Be("LS");
        parsed.Trailing.Should().Contain("multi-prefix");
    }

    [Fact]
    public void Parse_CapAck()
    {
        var message = ":server CAP nick ACK :multi-prefix sasl";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("CAP");
        parsed.Parameters[1].Should().Be("ACK");
        parsed.Trailing.Should().Contain("multi-prefix sasl");
    }

    [Fact]
    public void Parse_CapNak()
    {
        var message = ":server CAP nick NAK :unsupported-capability";

        var parsed = _parser.Parse(message);

        parsed.Command.Should().Be("CAP");
        parsed.Parameters[1].Should().Be("NAK");
    }

    #endregion

    #region IRCv3 Tags

    [Fact]
    public void Parse_MessageWithTimeTag()
    {
        var message = "@time=2021-01-01T12:00:00.000Z :nick!user@host PRIVMSG #channel :Hello";

        var parsed = _parser.Parse(message);

        parsed.Tags.Should().ContainKey("time");
        parsed.Tags["time"].Should().Be("2021-01-01T12:00:00.000Z");
    }

    [Fact]
    public void Parse_MessageWithMsgIdTag()
    {
        var message = "@msgid=abc123 :nick!user@host PRIVMSG #channel :Hello";

        var parsed = _parser.Parse(message);

        parsed.Tags.Should().ContainKey("msgid");
        parsed.Tags["msgid"].Should().Be("abc123");
    }

    [Fact]
    public void Parse_MessageWithMultipleTags()
    {
        var message = "@time=2021-01-01T00:00:00.000Z;msgid=abc123;account=mynick :nick!user@host PRIVMSG #channel :Hi";

        var parsed = _parser.Parse(message);

        parsed.Tags.Should().HaveCount(3);
        parsed.Tags["time"].Should().NotBeEmpty();
        parsed.Tags["msgid"].Should().Be("abc123");
        parsed.Tags["account"].Should().Be("mynick");
    }

    [Fact]
    public void Parse_TagWithEscapedSemicolon()
    {
        var message = @"@label=test\:value :server PRIVMSG #channel :Message";

        var parsed = _parser.Parse(message);

        parsed.Tags["label"].Should().Be("test;value");
    }

    [Fact]
    public void Parse_TagWithEscapedSpace()
    {
        var message = @"@label=test\svalue :server PRIVMSG #channel :Message";

        var parsed = _parser.Parse(message);

        parsed.Tags["label"].Should().Be("test value");
    }

    [Fact]
    public void Parse_TagWithEscapedBackslash()
    {
        var message = @"@label=test\\value :server PRIVMSG #channel :Message";

        var parsed = _parser.Parse(message);

        parsed.Tags["label"].Should().Be(@"test\value");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_EmptyTrailing()
    {
        var message = ":nick!user@host PRIVMSG #channel :";

        var parsed = _parser.Parse(message);

        parsed.Trailing.Should().BeEmpty();
        parsed.Parameters.Should().Contain("");
    }

    [Fact]
    public void Parse_NoTrailing()
    {
        var message = ":server 324 nick #channel +nt";

        var parsed = _parser.Parse(message);

        parsed.Trailing.Should().BeNull();
    }

    [Fact]
    public void Parse_NoPrefix()
    {
        var message = "PING :server.hostname";

        var parsed = _parser.Parse(message);

        parsed.Prefix.Should().BeNull();
        parsed.Nick.Should().BeNull();
    }

    [Fact]
    public void Parse_NoParameters()
    {
        var message = ":server 001";

        var parsed = _parser.Parse(message);

        parsed.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ColonInMiddleParameter()
    {
        var message = ":server 005 nick NETWORK=Libera.Chat :are supported";

        var parsed = _parser.Parse(message);

        // Colon in NETWORK value should be preserved
        parsed.Parameters[1].Should().Contain("Libera.Chat");
    }

    [Fact]
    public void Parse_MultipleSpacesBetweenParameters_AreCollapsed()
    {
        var message = ":server 324 nick #channel +nt";

        var parsed = _parser.Parse(message);

        parsed.Parameters.Should().Contain("#channel");
        parsed.Parameters.Should().Contain("+nt");
    }

    [Fact]
    public void Parse_UnicodeInMessage()
    {
        var message = ":nick!user@host PRIVMSG #channel :Hello 世界 🌍";

        var parsed = _parser.Parse(message);

        parsed.Trailing.Should().Contain("世界");
        parsed.Trailing.Should().Contain("🌍");
    }

    [Fact]
    public void Parse_VeryLongMessage()
    {
        var longText = new string('x', 1000);
        var message = $":nick!user@host PRIVMSG #channel :{longText}";

        var parsed = _parser.Parse(message);

        parsed.Trailing.Should().HaveLength(1000);
    }

    [Fact]
    public void Parse_MalformedPrefix_OnlyNick()
    {
        var message = ":nick PRIVMSG #channel :Hello";

        var parsed = _parser.Parse(message);

        parsed.Nick.Should().Be("nick");
        parsed.User.Should().BeNull();
        parsed.Host.Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedPrefix_NoHost()
    {
        var message = ":nick!user PRIVMSG #channel :Hello";

        var parsed = _parser.Parse(message);

        parsed.Nick.Should().Be("nick");
        parsed.User.Should().Be("user");
        parsed.Host.Should().BeNull();
    }

    [Fact]
    public void Parse_ServerPrefix()
    {
        var message = ":irc.server.net 001 nick :Welcome";

        var parsed = _parser.Parse(message);

        parsed.Prefix.Should().Be("irc.server.net");
        parsed.Nick.Should().Be("irc.server.net"); // Server name parsed as nick
    }

    #endregion

    #region CTCP Messages

    [Fact]
    public void IsCTCP_RecognizesCTCPMessage()
    {
        var message = "\u0001VERSION\u0001";

        IrcMessageParser.IsCTCP(message).Should().BeTrue();
    }

    [Fact]
    public void IsCTCP_RejectsNormalMessage()
    {
        var message = "Hello world";

        IrcMessageParser.IsCTCP(message).Should().BeFalse();
    }

    [Fact]
    public void ParseCTCP_ParsesVersionRequest()
    {
        var message = "\u0001VERSION\u0001";

        var (command, parameter) = IrcMessageParser.ParseCTCP(message);

        command.Should().Be("VERSION");
        parameter.Should().BeNull();
    }

    [Fact]
    public void ParseCTCP_ParsesPingWithParameter()
    {
        var message = "\u0001PING 1234567890\u0001";

        var (command, parameter) = IrcMessageParser.ParseCTCP(message);

        command.Should().Be("PING");
        parameter.Should().Be("1234567890");
    }

    [Fact]
    public void ParseCTCP_ParsesAction()
    {
        var message = "\u0001ACTION does something\u0001";

        var (command, parameter) = IrcMessageParser.ParseCTCP(message);

        command.Should().Be("ACTION");
        parameter.Should().Be("does something");
    }

    [Fact]
    public void ParseCTCP_HandlesMultipleSpaces()
    {
        var message = "\u0001COMMAND   multiple   spaces\u0001";

        var (command, parameter) = IrcMessageParser.ParseCTCP(message);

        command.Should().Be("COMMAND");
        parameter.Should().Be("  multiple   spaces"); // First space is delimiter, rest preserved
    }

    #endregion
}
