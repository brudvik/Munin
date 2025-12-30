using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for ISupport (IRC 005 ISUPPORT) server capability parsing.
/// </summary>
public class ISupportTests
{
    [Fact]
    public void DefaultValues_ShouldBeSet()
    {
        // Arrange & Act
        var isupport = new ISupport();

        // Assert
        isupport.ChanTypes.Should().Be("#&");
        isupport.PrefixChars.Should().Be("@+");
        isupport.PrefixModes.Should().Be("ov");
        isupport.CaseMapping.Should().Be("rfc1459");
        isupport.NickLen.Should().Be(9);
        isupport.ChannelLen.Should().Be(50);
        isupport.TopicLen.Should().Be(307);
        isupport.KickLen.Should().Be(255);
        isupport.AwayLen.Should().Be(200);
        isupport.Modes.Should().Be(3);
        isupport.MaxChannels.Should().Be(20);
    }

    [Fact]
    public void ParseTokens_Network_ShouldSetNetworkName()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "NETWORK=Libera.Chat" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.Network.Should().Be("Libera.Chat");
        isupport.RawTokens.Should().ContainKey("NETWORK").WhoseValue.Should().Be("Libera.Chat");
    }

    [Fact]
    public void ParseTokens_ChanTypes_ShouldSetChannelTypes()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "CHANTYPES=#" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.ChanTypes.Should().Be("#");
    }

    [Fact]
    public void ParseTokens_Prefix_ShouldParseModesAndSymbols()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "PREFIX=(qaohv)~&@%+" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.PrefixModes.Should().Be("qaohv");
        isupport.PrefixChars.Should().Be("~&@%+");
        isupport.Prefix.Should().HaveCount(5);
        isupport.Prefix['q'].Should().Be('~');
        isupport.Prefix['a'].Should().Be('&');
        isupport.Prefix['o'].Should().Be('@');
        isupport.Prefix['h'].Should().Be('%');
        isupport.Prefix['v'].Should().Be('+');
    }

    [Fact]
    public void ParseTokens_Prefix_InvalidFormat_ShouldNotCrash()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "PREFIX=invalid", "PREFIX=(ov", "PREFIX=@+" };

        // Act
        Action act = () => isupport.ParseTokens(tokens);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ParseTokens_ChanModes_ShouldParseModeCategories()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "CHANMODES=beI,k,l,imnpst" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.ChanModes.Should().HaveCount(4);
        isupport.ChanModes['A'].Should().BeEquivalentTo(new[] { 'b', 'e', 'I' });
        isupport.ChanModes['B'].Should().BeEquivalentTo(new[] { 'k' });
        isupport.ChanModes['C'].Should().BeEquivalentTo(new[] { 'l' });
        isupport.ChanModes['D'].Should().BeEquivalentTo(new[] { 'i', 'm', 'n', 'p', 's', 't' });
    }

    [Fact]
    public void ParseTokens_CaseMapping_ShouldSetMappingType()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "CASEMAPPING=strict-rfc1459" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.CaseMapping.Should().Be("strict-rfc1459");
    }

    [Fact]
    public void ParseTokens_Limits_ShouldParseVariousLengthLimits()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[]
        {
            "NICKLEN=30",
            "CHANNELLEN=50",
            "TOPICLEN=390",
            "KICKLEN=255",
            "AWAYLEN=200",
            "MODES=4"
        };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.NickLen.Should().Be(30);
        isupport.ChannelLen.Should().Be(50);
        isupport.TopicLen.Should().Be(390);
        isupport.KickLen.Should().Be(255);
        isupport.AwayLen.Should().Be(200);
        isupport.Modes.Should().Be(4);
    }

    [Fact]
    public void ParseTokens_MaxChannels_ShouldSetLimit()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "MAXCHANNELS=100" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.MaxChannels.Should().Be(100);
    }

    [Fact]
    public void ParseTokens_ChanLimit_ShouldParseLimitsByType()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "CHANLIMIT=#:25,&:10" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.ChanLimit.Should().HaveCount(2);
        isupport.ChanLimit["#"].Should().Be(25);
        isupport.ChanLimit["&"].Should().Be(10);
        isupport.MaxChannels.Should().Be(25); // Updated to highest limit
    }

    [Fact]
    public void ParseTokens_TargMax_ShouldParseTargetLimits()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "TARGMAX=PRIVMSG:4,NOTICE:4,JOIN:,KICK:1" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.TargMax.Should().HaveCount(4);
        isupport.TargMax["PRIVMSG"].Should().Be(4);
        isupport.TargMax["NOTICE"].Should().Be(4);
        isupport.TargMax["JOIN"].Should().Be(int.MaxValue); // Empty = no limit
        isupport.TargMax["KICK"].Should().Be(1);
    }

    [Fact]
    public void ParseTokens_MaxList_ShouldParseListModeLimits()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "MAXLIST=beI:100" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.MaxList.Should().HaveCount(3);
        isupport.MaxList['b'].Should().Be(100);
        isupport.MaxList['e'].Should().Be(100);
        isupport.MaxList['I'].Should().Be(100);
    }

    [Fact]
    public void ParseTokens_Excepts_ShouldEnableExceptsSupport()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "EXCEPTS=e" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.SupportsExcepts.Should().BeTrue();
        isupport.ExceptsMode.Should().Be('e');
    }

    [Fact]
    public void ParseTokens_Excepts_NoValue_ShouldUseDefaultMode()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "EXCEPTS" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.SupportsExcepts.Should().BeTrue();
        isupport.ExceptsMode.Should().Be('e'); // Default
    }

    [Fact]
    public void ParseTokens_Invex_ShouldEnableInviteExceptionSupport()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "INVEX=I" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.SupportsInvex.Should().BeTrue();
        isupport.InvexMode.Should().Be('I');
    }

    [Fact]
    public void ParseTokens_StatusMsg_ShouldSetPrefixes()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "STATUSMSG=@+" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.StatusMsg.Should().Be("@+");
    }

    [Fact]
    public void ParseTokens_Whox_ShouldEnableWhoxSupport()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "WHOX" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.SupportsWhox.Should().BeTrue();
    }

    [Fact]
    public void ParseTokens_Monitor_ShouldSetMonitorLimit()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "MONITOR=100" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.MonitorLimit.Should().Be(100);
    }

    [Fact]
    public void ParseTokens_UnknownToken_ShouldStoreInRawTokens()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "CUSTOMFEATURE=value", "ANOTHERFLAG" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.RawTokens.Should().ContainKey("CUSTOMFEATURE").WhoseValue.Should().Be("value");
        isupport.RawTokens.Should().ContainKey("ANOTHERFLAG").WhoseValue.Should().BeNull();
    }

    [Fact]
    public void ParseTokens_NegatedToken_ShouldRemoveFromRawTokens()
    {
        // Arrange
        var isupport = new ISupport();
        var tokens = new[] { "FEATURE=value", "-FEATURE" };

        // Act
        isupport.ParseTokens(tokens);

        // Assert
        isupport.RawTokens.Should().NotContainKey("FEATURE");
    }

    [Fact]
    public void IsChannelPrefix_ShouldReturnTrueForValidPrefix()
    {
        // Arrange
        var isupport = new ISupport();

        // Act & Assert
        isupport.IsChannelPrefix('#').Should().BeTrue();
        isupport.IsChannelPrefix('&').Should().BeTrue();
        isupport.IsChannelPrefix('@').Should().BeFalse();
        isupport.IsChannelPrefix('a').Should().BeFalse();
    }

    [Fact]
    public void IsChannel_ShouldValidateChannelName()
    {
        // Arrange
        var isupport = new ISupport();

        // Act & Assert
        isupport.IsChannel("#munin").Should().BeTrue();
        isupport.IsChannel("&local").Should().BeTrue();
        isupport.IsChannel("nick").Should().BeFalse();
        isupport.IsChannel("").Should().BeFalse();
    }

    [Fact]
    public void GetModeForPrefix_ShouldReturnCorrectMode()
    {
        // Arrange
        var isupport = new ISupport();
        isupport.ParseTokens(new[] { "PREFIX=(qaohv)~&@%+" });

        // Act & Assert
        isupport.GetModeForPrefix('~').Should().Be('q');
        isupport.GetModeForPrefix('@').Should().Be('o');
        isupport.GetModeForPrefix('+').Should().Be('v');
        isupport.GetModeForPrefix('!').Should().BeNull();
    }

    [Fact]
    public void GetPrefixForMode_ShouldReturnCorrectPrefix()
    {
        // Arrange
        var isupport = new ISupport();
        isupport.ParseTokens(new[] { "PREFIX=(qaohv)~&@%+" });

        // Act & Assert
        isupport.GetPrefixForMode('q').Should().Be('~');
        isupport.GetPrefixForMode('o').Should().Be('@');
        isupport.GetPrefixForMode('v').Should().Be('+');
        isupport.GetPrefixForMode('x').Should().BeNull();
    }

    [Fact]
    public void GetPrefixOrder_ShouldReturnCorrectOrder()
    {
        // Arrange
        var isupport = new ISupport();
        isupport.ParseTokens(new[] { "PREFIX=(qaohv)~&@%+" });

        // Act & Assert
        isupport.GetPrefixOrder('~').Should().Be(0); // Highest
        isupport.GetPrefixOrder('@').Should().Be(2);
        isupport.GetPrefixOrder('+').Should().Be(4); // Lowest
        isupport.GetPrefixOrder('!').Should().Be(int.MaxValue); // Not found
    }

    [Fact]
    public void NormalizeCase_Rfc1459_ShouldConvertBrackets()
    {
        // Arrange
        var isupport = new ISupport { CaseMapping = "rfc1459" };

        // Act
        var result = isupport.NormalizeCase("TEST[\\]^");

        // Assert
        result.Should().Be("test{|}~");
    }

    [Fact]
    public void NormalizeCase_StrictRfc1459_ShouldNotConvertCaret()
    {
        // Arrange
        var isupport = new ISupport { CaseMapping = "strict-rfc1459" };

        // Act
        var result = isupport.NormalizeCase("TEST[\\]^");

        // Assert
        result.Should().Be("test{|}^"); // ^ not converted
    }

    [Fact]
    public void NormalizeCase_Ascii_ShouldOnlyConvertAsciiLetters()
    {
        // Arrange
        var isupport = new ISupport { CaseMapping = "ascii" };

        // Act
        var result = isupport.NormalizeCase("TEST[\\]^");

        // Assert
        result.Should().Be("test[\\]^"); // No bracket conversion
    }

    [Fact]
    public void Compare_ShouldUseServerCaseMapping()
    {
        // Arrange
        var isupport = new ISupport { CaseMapping = "rfc1459" };

        // Act & Assert
        isupport.Compare("test[", "test{").Should().Be(0); // Equal with rfc1459
        isupport.Compare("abc", "xyz").Should().BeLessThan(0);
        isupport.Compare("xyz", "abc").Should().BeGreaterThan(0);
    }

    [Fact]
    public void Compare_NullHandling()
    {
        // Arrange
        var isupport = new ISupport();

        // Act & Assert
        isupport.Compare(null, null).Should().Be(0);
        isupport.Compare(null, "test").Should().Be(-1);
        isupport.Compare("test", null).Should().Be(1);
    }

    [Fact]
    public void Equals_ShouldUseServerCaseMapping()
    {
        // Arrange
        var isupport = new ISupport { CaseMapping = "rfc1459" };

        // Act & Assert
        isupport.Equals("test[", "test{").Should().BeTrue();
        isupport.Equals("TEST[", "test{").Should().BeTrue(); // Case insensitive
        isupport.Equals("abc", "xyz").Should().BeFalse();
    }

    [Fact]
    public void Equals_NullHandling()
    {
        // Arrange
        var isupport = new ISupport();

        // Act & Assert
        isupport.Equals(null, null).Should().BeTrue();
        isupport.Equals(null, "test").Should().BeFalse();
        isupport.Equals("test", null).Should().BeFalse();
    }

    [Fact]
    public void ParseTokens_MultipleCallsAccumulate()
    {
        // Arrange
        var isupport = new ISupport();

        // Act
        isupport.ParseTokens(new[] { "NETWORK=TestNet" });
        isupport.ParseTokens(new[] { "NICKLEN=20" });

        // Assert
        isupport.Network.Should().Be("TestNet");
        isupport.NickLen.Should().Be(20);
    }

    [Fact]
    public void ParseTokens_LaterValueOverridesPrevious()
    {
        // Arrange
        var isupport = new ISupport();

        // Act
        isupport.ParseTokens(new[] { "NETWORK=FirstNet" });
        isupport.ParseTokens(new[] { "NETWORK=SecondNet" });

        // Assert
        isupport.Network.Should().Be("SecondNet");
    }
}
