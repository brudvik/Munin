using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for AutoPerformService - automatic command execution on connect/join
/// </summary>
public class AutoPerformServiceTests
{
    private readonly AutoPerformService _service;

    public AutoPerformServiceTests()
    {
        _service = new AutoPerformService();
    }

    [Fact]
    public void GlobalCommands_InitiallyEmpty()
    {
        // Assert
        _service.GlobalCommands.Should().BeEmpty();
    }

    [Fact]
    public void AddGlobalCommand_AddsCommand()
    {
        // Act
        _service.AddGlobalCommand("MODE $me +x");

        // Assert
        _service.GlobalCommands.Should().ContainSingle()
            .Which.Should().Be("MODE $me +x");
    }

    [Fact]
    public void AddGlobalCommand_DuplicateCommand_OnlyAddedOnce()
    {
        // Act
        _service.AddGlobalCommand("JOIN #lobby");
        _service.AddGlobalCommand("JOIN #lobby");

        // Assert
        _service.GlobalCommands.Should().ContainSingle();
    }

    [Fact]
    public void RemoveGlobalCommand_RemovesCommand()
    {
        // Arrange
        _service.AddGlobalCommand("PRIVMSG NickServ :IDENTIFY password");

        // Act
        var removed = _service.RemoveGlobalCommand("PRIVMSG NickServ :IDENTIFY password");

        // Assert
        removed.Should().BeTrue();
        _service.GlobalCommands.Should().BeEmpty();
    }

    [Fact]
    public void RemoveGlobalCommand_NonExistentCommand_ReturnsFalse()
    {
        // Act
        var removed = _service.RemoveGlobalCommand("NONEXISTENT");

        // Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public void AddServerCommand_AddsCommandForServer()
    {
        // Act
        _service.AddServerCommand("irc.libera.chat", "PRIVMSG NickServ :IDENTIFY mypass");

        // Assert
        _service.GetServerCommands("irc.libera.chat").Should().ContainSingle()
            .Which.Should().Be("PRIVMSG NickServ :IDENTIFY mypass");
    }

    [Fact]
    public void AddServerCommand_MultipleServers_IndependentCommands()
    {
        // Act
        _service.AddServerCommand("server1", "COMMAND1");
        _service.AddServerCommand("server2", "COMMAND2");

        // Assert
        _service.GetServerCommands("server1").Should().ContainSingle()
            .Which.Should().Be("COMMAND1");
        _service.GetServerCommands("server2").Should().ContainSingle()
            .Which.Should().Be("COMMAND2");
    }

    [Fact]
    public void AddServerCommand_DuplicateCommand_OnlyAddedOnce()
    {
        // Act
        _service.AddServerCommand("server1", "MODE $me +B");
        _service.AddServerCommand("server1", "MODE $me +B");

        // Assert
        _service.GetServerCommands("server1").Should().ContainSingle();
    }

    [Fact]
    public void RemoveServerCommand_RemovesCommand()
    {
        // Arrange
        _service.AddServerCommand("server1", "WHOIS $me");

        // Act
        var removed = _service.RemoveServerCommand("server1", "WHOIS $me");

        // Assert
        removed.Should().BeTrue();
        _service.GetServerCommands("server1").Should().BeEmpty();
    }

    [Fact]
    public void RemoveServerCommand_NonExistentServer_ReturnsFalse()
    {
        // Act
        var removed = _service.RemoveServerCommand("nonexistent", "COMMAND");

        // Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public void GetServerCommands_EmptyServer_ReturnsEmpty()
    {
        // Act
        var commands = _service.GetServerCommands("empty-server");

        // Assert
        commands.Should().BeEmpty();
    }

    [Fact]
    public void AddChannelCommand_AddsCommandForChannel()
    {
        // Act
        _service.AddChannelCommand("irc.example.com", "#channel", "MODE #channel +m");

        // Assert
        _service.GetChannelCommands("irc.example.com", "#channel").Should().ContainSingle()
            .Which.Should().Be("MODE #channel +m");
    }

    [Fact]
    public void AddChannelCommand_MultipleChannels_IndependentCommands()
    {
        // Act
        _service.AddChannelCommand("server1", "#chan1", "TOPIC #chan1 :Welcome");
        _service.AddChannelCommand("server1", "#chan2", "TOPIC #chan2 :Hello");

        // Assert
        _service.GetChannelCommands("server1", "#chan1").Should().ContainSingle()
            .Which.Should().Be("TOPIC #chan1 :Welcome");
        _service.GetChannelCommands("server1", "#chan2").Should().ContainSingle()
            .Which.Should().Be("TOPIC #chan2 :Hello");
    }

    [Fact]
    public void AddChannelCommand_DuplicateCommand_OnlyAddedOnce()
    {
        // Act
        _service.AddChannelCommand("server1", "#chan", "INVITE friend #chan");
        _service.AddChannelCommand("server1", "#chan", "INVITE friend #chan");

        // Assert
        _service.GetChannelCommands("server1", "#chan").Should().ContainSingle();
    }

    [Fact]
    public void RemoveChannelCommand_RemovesCommand()
    {
        // Arrange
        _service.AddChannelCommand("server1", "#chan", "MODE #chan +i");

        // Act
        var removed = _service.RemoveChannelCommand("server1", "#chan", "MODE #chan +i");

        // Assert
        removed.Should().BeTrue();
        _service.GetChannelCommands("server1", "#chan").Should().BeEmpty();
    }

    [Fact]
    public void RemoveChannelCommand_NonExistentChannel_ReturnsFalse()
    {
        // Act
        var removed = _service.RemoveChannelCommand("server1", "#nonexistent", "COMMAND");

        // Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public void GetChannelCommands_EmptyChannel_ReturnsEmpty()
    {
        // Act
        var commands = _service.GetChannelCommands("server1", "#empty");

        // Assert
        commands.Should().BeEmpty();
    }

    [Fact]
    public void GetConnectionCommands_ReturnsGlobalThenServer()
    {
        // Arrange
        _service.AddGlobalCommand("GLOBAL1");
        _service.AddGlobalCommand("GLOBAL2");
        _service.AddServerCommand("server1", "SERVER1");
        _service.AddServerCommand("server1", "SERVER2");

        // Act
        var commands = _service.GetConnectionCommands("server1").ToList();

        // Assert
        commands.Should().HaveCount(4);
        commands[0].Should().Be("GLOBAL1");
        commands[1].Should().Be("GLOBAL2");
        commands[2].Should().Be("SERVER1");
        commands[3].Should().Be("SERVER2");
    }

    [Fact]
    public void GetConnectionCommands_OnlyGlobal_ReturnsGlobal()
    {
        // Arrange
        _service.AddGlobalCommand("GLOBAL");

        // Act
        var commands = _service.GetConnectionCommands("any-server").ToList();

        // Assert
        commands.Should().ContainSingle().Which.Should().Be("GLOBAL");
    }

    [Fact]
    public void GetConnectionCommands_OnlyServer_ReturnsServer()
    {
        // Arrange
        _service.AddServerCommand("server1", "SERVER");

        // Act
        var commands = _service.GetConnectionCommands("server1").ToList();

        // Assert
        commands.Should().ContainSingle().Which.Should().Be("SERVER");
    }

    [Fact]
    public void GetJoinCommands_ReturnsChannelCommands()
    {
        // Arrange
        _service.AddChannelCommand("server1", "#chan", "CMD1");
        _service.AddChannelCommand("server1", "#chan", "CMD2");

        // Act
        var commands = _service.GetJoinCommands("server1", "#chan").ToList();

        // Assert
        commands.Should().HaveCount(2);
        commands.Should().Contain(new[] { "CMD1", "CMD2" });
    }

    [Fact]
    public void GetJoinCommands_NoCommands_ReturnsEmpty()
    {
        // Act
        var commands = _service.GetJoinCommands("server1", "#empty").ToList();

        // Assert
        commands.Should().BeEmpty();
    }

    [Fact]
    public void Clear_RemovesAllCommands()
    {
        // Arrange
        _service.AddGlobalCommand("GLOBAL");
        _service.AddServerCommand("server1", "SERVER");
        _service.AddChannelCommand("server1", "#chan", "CHANNEL");

        // Act
        _service.Clear();

        // Assert
        _service.GlobalCommands.Should().BeEmpty();
        _service.GetServerCommands("server1").Should().BeEmpty();
        _service.GetChannelCommands("server1", "#chan").Should().BeEmpty();
    }

    [Fact]
    public void Serialize_CreatesValidDictionary()
    {
        // Arrange
        _service.AddGlobalCommand("GLOBAL");
        _service.AddServerCommand("server1", "SERVER");
        _service.AddChannelCommand("server1", "#chan", "CHANNEL");

        // Act
        var data = _service.Serialize();

        // Assert
        data.Should().ContainKey("global");
        data.Should().ContainKey("servers");
        data.Should().ContainKey("channels");
    }

    [Fact]
    public void Deserialize_LoadsGlobalCommands()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            ["global"] = new List<object> { "CMD1", "CMD2" }
        };

        // Act
        _service.Deserialize(data);

        // Assert
        _service.GlobalCommands.Should().HaveCount(2);
        _service.GlobalCommands.Should().Contain(new[] { "CMD1", "CMD2" });
    }

    [Fact]
    public void Deserialize_LoadsServerCommands()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            ["servers"] = new Dictionary<string, object>
            {
                ["server1"] = new List<object> { "CMD1", "CMD2" }
            }
        };

        // Act
        _service.Deserialize(data);

        // Assert
        _service.GetServerCommands("server1").Should().HaveCount(2);
        _service.GetServerCommands("server1").Should().Contain(new[] { "CMD1", "CMD2" });
    }

    [Fact]
    public void Deserialize_LoadsChannelCommands()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            ["channels"] = new Dictionary<string, object>
            {
                ["server1"] = new Dictionary<string, object>
                {
                    ["#chan"] = new List<object> { "CMD1", "CMD2" }
                }
            }
        };

        // Act
        _service.Deserialize(data);

        // Assert
        _service.GetChannelCommands("server1", "#chan").Should().HaveCount(2);
        _service.GetChannelCommands("server1", "#chan").Should().Contain(new[] { "CMD1", "CMD2" });
    }

    [Fact]
    public void Deserialize_NullData_DoesNothing()
    {
        // Act
        _service.Deserialize(null);

        // Assert - No exception thrown
        _service.GlobalCommands.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_PartialData_LoadsAvailableData()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            ["global"] = new List<object> { "GLOBAL" }
        };

        // Act
        _service.Deserialize(data);

        // Assert
        _service.GlobalCommands.Should().ContainSingle();
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip()
    {
        // Arrange
        _service.AddGlobalCommand("GLOBAL");
        _service.AddServerCommand("server1", "SERVER");
        _service.AddChannelCommand("server1", "#chan", "CHANNEL");

        // Act - Serialize then convert to object types to simulate JSON round-trip
        var serialized = _service.Serialize();
        
        // Simulate JSON serialization/deserialization type conversions
        var jsonSimulated = new Dictionary<string, object>
        {
            ["global"] = ((List<string>)serialized["global"]).Cast<object>().ToList(),
            ["servers"] = ((Dictionary<string, List<string>>)serialized["servers"])
                .ToDictionary(k => k.Key, v => (object)v.Value.Cast<object>().ToList()),
            ["channels"] = ((Dictionary<string, Dictionary<string, List<string>>>)serialized["channels"])
                .ToDictionary(k => k.Key, v => (object)v.Value.ToDictionary(
                    k2 => k2.Key, v2 => (object)v2.Value.Cast<object>().ToList()))
        };
        
        var newService = new AutoPerformService();
        newService.Deserialize(jsonSimulated);

        // Assert
        newService.GlobalCommands.Should().Equal(_service.GlobalCommands);
        newService.GetServerCommands("server1").Should().Equal(_service.GetServerCommands("server1"));
        newService.GetChannelCommands("server1", "#chan").Should().Equal(_service.GetChannelCommands("server1", "#chan"));
    }

    [Fact]
    public void ServerCommands_CaseInsensitiveServerNames()
    {
        // Arrange
        _service.AddServerCommand("IRC.Example.Com", "CMD1");

        // Act
        var commands = _service.GetServerCommands("irc.example.com");

        // Assert
        commands.Should().ContainSingle();
    }

    [Fact]
    public void ChannelCommands_CaseInsensitiveChannelNames()
    {
        // Arrange
        _service.AddChannelCommand("server1", "#Channel", "CMD1");

        // Act
        var commands = _service.GetChannelCommands("server1", "#channel");

        // Assert
        commands.Should().ContainSingle();
    }
}
