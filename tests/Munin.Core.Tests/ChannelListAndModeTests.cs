using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for ChannelListEntry and ChannelModeState - LIST command data and mode management
/// </summary>
public class ChannelListAndModeTests
{
    #region ChannelListEntry Tests

    [Fact]
    public void ChannelListEntry_Constructor_InitializesWithDefaults()
    {
        // Act
        var entry = new ChannelListEntry();

        // Assert
        entry.Name.Should().BeEmpty();
        entry.UserCount.Should().Be(0);
        entry.Topic.Should().BeEmpty();
    }

    [Fact]
    public void ChannelListEntry_CanSetProperties()
    {
        // Arrange
        var entry = new ChannelListEntry();

        // Act
        entry.Name = "#linux";
        entry.UserCount = 523;
        entry.Topic = "Linux support and discussion";

        // Assert
        entry.Name.Should().Be("#linux");
        entry.UserCount.Should().Be(523);
        entry.Topic.Should().Be("Linux support and discussion");
    }

    [Fact]
    public void ChannelListEntry_EmptyTopic()
    {
        // Arrange & Act
        var entry = new ChannelListEntry
        {
            Name = "#general",
            UserCount = 10,
            Topic = ""
        };

        // Assert
        entry.Topic.Should().BeEmpty();
    }

    [Fact]
    public void ChannelListEntry_LargeTopic()
    {
        // Arrange
        var longTopic = new string('A', 500);
        
        // Act
        var entry = new ChannelListEntry
        {
            Name = "#topic-test",
            UserCount = 5,
            Topic = longTopic
        };

        // Assert
        entry.Topic.Should().HaveLength(500);
    }

    [Fact]
    public void ChannelListEntry_ZeroUsers()
    {
        // Act
        var entry = new ChannelListEntry
        {
            Name = "#empty",
            UserCount = 0
        };

        // Assert
        entry.UserCount.Should().Be(0);
    }

    #endregion

    #region ChannelModeState Tests

    [Fact]
    public void ChannelModeState_Constructor_InitializesWithDefaults()
    {
        // Act
        var state = new ChannelModeState();

        // Assert
        state.Channel.Should().BeEmpty();
        state.SimpleModes.Should().BeEmpty();
        state.ParameterModes.Should().BeEmpty();
        state.IsModerated.Should().BeFalse();
        state.IsSecret.Should().BeFalse();
        state.IsPrivate.Should().BeFalse();
        state.IsInviteOnly.Should().BeFalse();
        state.TopicProtected.Should().BeFalse();
        state.NoExternalMessages.Should().BeFalse();
        state.Limit.Should().BeNull();
        state.Key.Should().BeNull();
    }

    [Fact]
    public void ChannelModeState_ApplyMode_SimpleMode_Add()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 'm');
        state.ApplyMode(true, 't');
        state.ApplyMode(true, 'n');

        // Assert
        state.SimpleModes.Should().Contain('m');
        state.SimpleModes.Should().Contain('t');
        state.SimpleModes.Should().Contain('n');
        state.IsModerated.Should().BeTrue();
        state.TopicProtected.Should().BeTrue();
        state.NoExternalMessages.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_ApplyMode_SimpleMode_Remove()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };
        state.ApplyMode(true, 'm');
        state.ApplyMode(true, 't');

        // Act
        state.ApplyMode(false, 'm');

        // Assert
        state.SimpleModes.Should().NotContain('m');
        state.SimpleModes.Should().Contain('t');
        state.IsModerated.Should().BeFalse();
        state.TopicProtected.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_ApplyMode_ParameterMode_Limit()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 'l', "50");

        // Assert
        state.ParameterModes.Should().ContainKey('l');
        state.ParameterModes['l'].Should().Be("50");
        state.Limit.Should().Be(50);
    }

    [Fact]
    public void ChannelModeState_ApplyMode_ParameterMode_Key()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 'k', "secret123");

        // Assert
        state.ParameterModes.Should().ContainKey('k');
        state.ParameterModes['k'].Should().Be("secret123");
        state.Key.Should().Be("secret123");
    }

    [Fact]
    public void ChannelModeState_ApplyMode_RemoveParameterMode()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };
        state.ApplyMode(true, 'l', "50");
        state.ApplyMode(true, 'k', "secret");

        // Act
        state.ApplyMode(false, 'l');

        // Assert
        state.ParameterModes.Should().NotContainKey('l');
        state.ParameterModes.Should().ContainKey('k');
        state.Limit.Should().BeNull();
        state.Key.Should().Be("secret");
    }

    [Fact]
    public void ChannelModeState_Limit_SetProperty()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.Limit = 100;

        // Assert
        state.Limit.Should().Be(100);
        state.ParameterModes.Should().ContainKey('l');
        state.ParameterModes['l'].Should().Be("100");
    }

    [Fact]
    public void ChannelModeState_Limit_SetNull_Removes()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };
        state.Limit = 50;

        // Act
        state.Limit = null;

        // Assert
        state.Limit.Should().BeNull();
        state.ParameterModes.Should().NotContainKey('l');
    }

    [Fact]
    public void ChannelModeState_Key_SetProperty()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.Key = "mypassword";

        // Assert
        state.Key.Should().Be("mypassword");
        state.ParameterModes.Should().ContainKey('k');
        state.ParameterModes['k'].Should().Be("mypassword");
    }

    [Fact]
    public void ChannelModeState_Key_SetNull_Removes()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };
        state.Key = "secret";

        // Act
        state.Key = null;

        // Assert
        state.Key.Should().BeNull();
        state.ParameterModes.Should().NotContainKey('k');
    }

    [Fact]
    public void ChannelModeState_IsModerated()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 'm');

        // Assert
        state.IsModerated.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_IsSecret()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 's');

        // Assert
        state.IsSecret.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_IsPrivate()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 'p');

        // Assert
        state.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_IsInviteOnly()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 'i');

        // Assert
        state.IsInviteOnly.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_TopicProtected()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 't');

        // Assert
        state.TopicProtected.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_NoExternalMessages()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        state.ApplyMode(true, 'n');

        // Assert
        state.NoExternalMessages.Should().BeTrue();
    }

    [Fact]
    public void ChannelModeState_GetModeString_SimpleModes()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };
        state.ApplyMode(true, 'n');
        state.ApplyMode(true, 't');
        state.ApplyMode(true, 's');

        // Act
        var modeString = state.GetModeString();

        // Assert
        modeString.Should().Be("+nst");
    }

    [Fact]
    public void ChannelModeState_GetModeString_WithParameters()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };
        state.ApplyMode(true, 'n');
        state.ApplyMode(true, 't');
        state.ApplyMode(true, 'l', "50");
        state.ApplyMode(true, 'k', "secret");

        // Act
        var modeString = state.GetModeString();

        // Assert
        modeString.Should().StartWith("+");
        modeString.Should().Contain("n");
        modeString.Should().Contain("t");
        modeString.Should().Contain("l");
        modeString.Should().Contain("k");
        modeString.Should().Contain("50");
        modeString.Should().Contain("secret");
    }

    [Fact]
    public void ChannelModeState_GetModeString_Empty()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };

        // Act
        var modeString = state.GetModeString();

        // Assert
        modeString.Should().BeEmpty();
    }

    [Fact]
    public void ChannelModeState_Clear_RemovesAllModes()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#test" };
        state.ApplyMode(true, 'n');
        state.ApplyMode(true, 't');
        state.ApplyMode(true, 'l', "50");
        state.ApplyMode(true, 'k', "secret");

        // Act
        state.Clear();

        // Assert
        state.SimpleModes.Should().BeEmpty();
        state.ParameterModes.Should().BeEmpty();
        state.Limit.Should().BeNull();
        state.Key.Should().BeNull();
        state.IsModerated.Should().BeFalse();
    }

    [Fact]
    public void ChannelModeState_ComplexScenario()
    {
        // Arrange
        var state = new ChannelModeState { Channel = "#private-ops" };

        // Act - Simulate complex mode changes
        state.ApplyMode(true, 'n'); // No external messages
        state.ApplyMode(true, 't'); // Topic protected
        state.ApplyMode(true, 's'); // Secret
        state.ApplyMode(true, 'i'); // Invite only
        state.ApplyMode(true, 'l', "100"); // Limit 100 users
        state.ApplyMode(true, 'k', "pass123"); // Key

        // Assert
        state.NoExternalMessages.Should().BeTrue();
        state.TopicProtected.Should().BeTrue();
        state.IsSecret.Should().BeTrue();
        state.IsInviteOnly.Should().BeTrue();
        state.Limit.Should().Be(100);
        state.Key.Should().Be("pass123");
        
        var modeString = state.GetModeString();
        modeString.Should().Contain("+");
        modeString.Should().Contain("n");
        modeString.Should().Contain("t");
        modeString.Should().Contain("s");
        modeString.Should().Contain("i");
    }

    #endregion
}
