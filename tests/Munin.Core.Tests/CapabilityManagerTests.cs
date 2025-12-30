using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for IRCv3 Capability Manager.
/// Verifies capability negotiation, parsing, and state management.
/// </summary>
public class CapabilityManagerTests
{
    [Fact]
    public void Constructor_InitializesEmptyState()
    {
        var manager = new CapabilityManager();
        
        manager.AvailableCapabilities.Should().BeEmpty();
        manager.EnabledCapabilities.Should().BeEmpty();
        manager.SaslMethods.Should().BeEmpty();
        manager.IsNegotiating.Should().BeFalse();
        manager.IsComplete.Should().BeFalse();
        manager.IsMultiline.Should().BeFalse();
    }

    [Fact]
    public void ParseAvailableCapabilities_SingleLine_ParsesCorrectly()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("multi-prefix sasl server-time", isMultiline: false);
        
        manager.AvailableCapabilities.Should().HaveCount(3);
        manager.AvailableCapabilities.Should().ContainKey("multi-prefix");
        manager.AvailableCapabilities.Should().ContainKey("sasl");
        manager.AvailableCapabilities.Should().ContainKey("server-time");
        manager.IsMultiline.Should().BeFalse();
    }

    [Fact]
    public void ParseAvailableCapabilities_Multiline_SetsMultilineFlag()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("multi-prefix sasl", isMultiline: true);
        
        manager.IsMultiline.Should().BeTrue();
        manager.AvailableCapabilities.Should().HaveCount(2);
    }

    [Fact]
    public void ParseAvailableCapabilities_WithValues_ParsesCorrectly()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("sasl=PLAIN,SCRAM-SHA-256 draft/chathistory=100", isMultiline: false);
        
        manager.AvailableCapabilities.Should().HaveCount(2);
        manager.AvailableCapabilities["sasl"].Should().Be("PLAIN,SCRAM-SHA-256");
        manager.AvailableCapabilities["draft/chathistory"].Should().Be("100");
    }

    [Fact]
    public void ParseAvailableCapabilities_SaslWithMethods_ParsesSaslMethods()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("sasl=PLAIN,SCRAM-SHA-256,EXTERNAL", isMultiline: false);
        
        manager.SaslMethods.Should().HaveCount(3);
        manager.SaslMethods.Should().Contain("PLAIN");
        manager.SaslMethods.Should().Contain("SCRAM-SHA-256");
        manager.SaslMethods.Should().Contain("EXTERNAL");
    }

    [Fact]
    public void ParseAvailableCapabilities_SaslWithoutValue_NoSaslMethods()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("sasl multi-prefix", isMultiline: false);
        
        manager.AvailableCapabilities.Should().ContainKey("sasl");
        manager.AvailableCapabilities["sasl"].Should().BeNull();
        manager.SaslMethods.Should().BeEmpty();
    }

    [Fact]
    public void ParseAvailableCapabilities_EmptyString_DoesNothing()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("", isMultiline: false);
        
        manager.AvailableCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void ParseAvailableCapabilities_MultipleCallsMultiline_AccumulatesCapabilities()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("multi-prefix sasl", isMultiline: true);
        manager.ParseAvailableCapabilities("server-time batch", isMultiline: false);
        
        manager.AvailableCapabilities.Should().HaveCount(4);
        manager.IsMultiline.Should().BeFalse();
    }

    [Fact]
    public void ParseAvailableCapabilities_CaseInsensitive_TreatsCapsSameRegardlessOfCase()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("SASL Multi-Prefix SERVER-TIME", isMultiline: false);
        
        manager.AvailableCapabilities.Should().ContainKey("sasl");
        manager.AvailableCapabilities.Should().ContainKey("multi-prefix");
        manager.AvailableCapabilities.Should().ContainKey("server-time");
    }

    [Fact]
    public void GetCapabilitiesToRequest_ReturnsOnlyWantedAndAvailable()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("multi-prefix sasl server-time unknown-cap", isMultiline: false);
        
        var toRequest = manager.GetCapabilitiesToRequest().ToList();
        
        toRequest.Should().Contain("multi-prefix");
        toRequest.Should().Contain("sasl");
        toRequest.Should().Contain("server-time");
        toRequest.Should().NotContain("unknown-cap");
    }

    [Fact]
    public void GetCapabilitiesToRequest_ReturnsEmptyIfNoneAvailable()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("unknown-cap1 unknown-cap2", isMultiline: false);
        
        var toRequest = manager.GetCapabilitiesToRequest().ToList();
        
        toRequest.Should().BeEmpty();
    }

    [Fact]
    public void ProcessAck_AddsCapabilitiesToEnabled()
    {
        var manager = new CapabilityManager();
        
        manager.ProcessAck("multi-prefix sasl server-time");
        
        manager.EnabledCapabilities.Should().HaveCount(3);
        manager.EnabledCapabilities.Should().Contain("multi-prefix");
        manager.EnabledCapabilities.Should().Contain("sasl");
        manager.EnabledCapabilities.Should().Contain("server-time");
    }

    [Fact]
    public void ProcessAck_WithModifier_RemovesCapability()
    {
        var manager = new CapabilityManager();
        manager.EnabledCapabilities.Add("multi-prefix");
        manager.EnabledCapabilities.Add("sasl");
        
        manager.ProcessAck("-multi-prefix sasl");
        
        manager.EnabledCapabilities.Should().NotContain("multi-prefix");
        manager.EnabledCapabilities.Should().Contain("sasl");
    }

    [Fact]
    public void ProcessAck_WithStickyModifier_AddsCapability()
    {
        var manager = new CapabilityManager();
        
        manager.ProcessAck("~sasl");
        
        manager.EnabledCapabilities.Should().Contain("sasl");
    }

    [Fact]
    public void ProcessAck_EmptyString_DoesNothing()
    {
        var manager = new CapabilityManager();
        
        manager.ProcessAck("");
        
        manager.EnabledCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void ProcessNak_DoesNotAddCapabilities()
    {
        var manager = new CapabilityManager();
        
        manager.ProcessNak("multi-prefix sasl");
        
        manager.EnabledCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void ProcessDel_RemovesFromAvailableAndEnabled()
    {
        var manager = new CapabilityManager();
        manager.AvailableCapabilities["multi-prefix"] = null;
        manager.AvailableCapabilities["sasl"] = "PLAIN";
        manager.EnabledCapabilities.Add("multi-prefix");
        manager.EnabledCapabilities.Add("sasl");
        
        manager.ProcessDel("multi-prefix sasl");
        
        manager.AvailableCapabilities.Should().BeEmpty();
        manager.EnabledCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void ProcessDel_NonExistentCapability_NoError()
    {
        var manager = new CapabilityManager();
        
        var act = () => manager.ProcessDel("unknown-cap");
        
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessNew_AddsToAvailableCapabilities()
    {
        var manager = new CapabilityManager();
        
        manager.ProcessNew("new-cap=value another-cap");
        
        manager.AvailableCapabilities.Should().HaveCount(2);
        manager.AvailableCapabilities["new-cap"].Should().Be("value");
        manager.AvailableCapabilities.Should().ContainKey("another-cap");
    }

    [Fact]
    public void HasCapability_ReturnsTrueIfEnabled()
    {
        var manager = new CapabilityManager();
        manager.EnabledCapabilities.Add("sasl");
        
        manager.HasCapability("sasl").Should().BeTrue();
        manager.HasCapability("SASL").Should().BeTrue(); // Case insensitive
    }

    [Fact]
    public void HasCapability_ReturnsFalseIfNotEnabled()
    {
        var manager = new CapabilityManager();
        
        manager.HasCapability("sasl").Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var manager = new CapabilityManager();
        manager.AvailableCapabilities["sasl"] = "PLAIN";
        manager.EnabledCapabilities.Add("sasl");
        manager.SaslMethods.Add("PLAIN");
        manager.IsNegotiating = true;
        manager.IsComplete = true;
        manager.IsMultiline = true;
        
        manager.Reset();
        
        manager.AvailableCapabilities.Should().BeEmpty();
        manager.EnabledCapabilities.Should().BeEmpty();
        manager.SaslMethods.Should().BeEmpty();
        manager.IsNegotiating.Should().BeFalse();
        manager.IsComplete.Should().BeFalse();
        manager.IsMultiline.Should().BeFalse();
    }

    [Fact]
    public void WantedCapabilities_ContainsCoreCapabilities()
    {
        CapabilityManager.WantedCapabilities.Should().Contain("multi-prefix");
        CapabilityManager.WantedCapabilities.Should().Contain("sasl");
        CapabilityManager.WantedCapabilities.Should().Contain("server-time");
        CapabilityManager.WantedCapabilities.Should().Contain("batch");
    }

    [Fact]
    public void ParseAvailableCapabilities_ComplexRealWorldExample_ParsesCorrectly()
    {
        var manager = new CapabilityManager();
        
        // Simulate Libera.Chat CAP LS response
        var capLine = "account-notify away-notify batch cap-notify chghost extended-join " +
                      "invite-notify message-tags multi-prefix sasl=PLAIN,ECDSA-NIST256P-CHALLENGE,EXTERNAL,SCRAM-SHA-256 " +
                      "server-time userhost-in-names";
        
        manager.ParseAvailableCapabilities(capLine, isMultiline: false);
        
        manager.AvailableCapabilities.Should().HaveCount(12);
        manager.SaslMethods.Should().HaveCount(4);
        manager.SaslMethods.Should().Contain("SCRAM-SHA-256");
    }

    [Fact]
    public void ProcessAck_MultipleCapabilitiesWithModifiers_HandlesCorrectly()
    {
        var manager = new CapabilityManager();
        manager.EnabledCapabilities.Add("old-cap");
        
        manager.ProcessAck("-old-cap new-cap1 new-cap2 ~sticky-cap");
        
        manager.EnabledCapabilities.Should().NotContain("old-cap");
        manager.EnabledCapabilities.Should().Contain("new-cap1");
        manager.EnabledCapabilities.Should().Contain("new-cap2");
        manager.EnabledCapabilities.Should().Contain("sticky-cap");
    }

    [Fact]
    public void GetCapabilitiesToRequest_OrderedAlphabetically()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("server-time batch multi-prefix sasl", isMultiline: false);
        
        var toRequest = manager.GetCapabilitiesToRequest().ToList();
        
        toRequest.Should().BeInAscendingOrder();
    }

    [Fact]
    public void ParseAvailableCapabilities_UpdatesSaslMethodsOnSubsequentCalls()
    {
        var manager = new CapabilityManager();
        
        manager.ParseAvailableCapabilities("sasl=PLAIN", isMultiline: false);
        manager.SaslMethods.Should().ContainSingle().Which.Should().Be("PLAIN");
        
        // Server updates SASL methods
        manager.ParseAvailableCapabilities("sasl=PLAIN,SCRAM-SHA-256", isMultiline: false);
        
        manager.SaslMethods.Should().HaveCount(2);
        manager.SaslMethods.Should().Contain("SCRAM-SHA-256");
    }
}
