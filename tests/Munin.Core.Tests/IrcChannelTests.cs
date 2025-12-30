using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for IrcChannel model.
/// Verifies channel state management, topic handling, and mode tracking.
/// </summary>
public class IrcChannelTests
{
    [Fact]
    public void Constructor_InitializesEmptyState()
    {
        var channel = new IrcChannel();
        
        channel.Name.Should().BeEmpty();
        channel.Topic.Should().BeNull();
        channel.TopicSetBy.Should().BeNull();
        channel.TopicSetAt.Should().BeNull();
        channel.Users.Should().BeEmpty();
        channel.Messages.Should().BeEmpty();
        channel.IsJoined.Should().BeFalse();
        channel.Key.Should().BeNull();
        channel.UnreadCount.Should().Be(0);
        channel.HasMention.Should().BeFalse();
        channel.Modes.Should().NotBeNull();
    }

    [Fact]
    public void Name_CanBeSetAndRetrieved()
    {
        var channel = new IrcChannel { Name = "#general" };
        
        channel.Name.Should().Be("#general");
    }

    [Fact]
    public void Topic_CanBeSetAndCleared()
    {
        var channel = new IrcChannel { Topic = "Welcome to the channel!" };
        
        channel.Topic.Should().Be("Welcome to the channel!");
        
        channel.Topic = null;
        channel.Topic.Should().BeNull();
    }

    [Fact]
    public void TopicMetadata_CanBeSet()
    {
        var channel = new IrcChannel
        {
            Topic = "Test topic",
            TopicSetBy = "Alice!alice@example.com",
            TopicSetAt = new DateTime(2025, 12, 30, 10, 30, 0, DateTimeKind.Utc)
        };
        
        channel.TopicSetBy.Should().Be("Alice!alice@example.com");
        channel.TopicSetAt.Should().Be(new DateTime(2025, 12, 30, 10, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Users_CanAddAndRemove()
    {
        var channel = new IrcChannel();
        var user1 = new IrcUser { Nickname = "Alice" };
        var user2 = new IrcUser { Nickname = "Bob" };
        
        channel.Users.Add(user1);
        channel.Users.Add(user2);
        
        channel.Users.Should().HaveCount(2);
        channel.Users.Should().Contain(user1);
        channel.Users.Should().Contain(user2);
        
        channel.Users.Remove(user1);
        channel.Users.Should().ContainSingle();
        channel.Users.Should().Contain(user2);
    }

    [Fact]
    public void Messages_CanAccumulateHistory()
    {
        var channel = new IrcChannel();
        var msg1 = new IrcMessage
        {
            Type = MessageType.Normal,
            Source = "Alice",
            Content = "Hello!"
        };
        var msg2 = new IrcMessage
        {
            Type = MessageType.Normal,
            Source = "Bob",
            Content = "Hi Alice!"
        };
        
        channel.Messages.Add(msg1);
        channel.Messages.Add(msg2);
        
        channel.Messages.Should().HaveCount(2);
        channel.Messages[0].Should().Be(msg1);
        channel.Messages[1].Should().Be(msg2);
    }

    [Fact]
    public void IsJoined_TracksJoinState()
    {
        var channel = new IrcChannel { Name = "#test" };
        
        channel.IsJoined.Should().BeFalse();
        
        channel.IsJoined = true;
        channel.IsJoined.Should().BeTrue();
    }

    [Fact]
    public void Key_CanBeSetForKeyProtectedChannel()
    {
        var channel = new IrcChannel { Key = "secret123" };
        
        channel.Key.Should().Be("secret123");
    }

    [Fact]
    public void UnreadCount_CanBeIncremented()
    {
        var channel = new IrcChannel();
        
        channel.UnreadCount = 0;
        channel.UnreadCount++;
        channel.UnreadCount++;
        
        channel.UnreadCount.Should().Be(2);
    }

    [Fact]
    public void HasMention_TracksHighlightState()
    {
        var channel = new IrcChannel();
        
        channel.HasMention.Should().BeFalse();
        
        channel.HasMention = true;
        channel.HasMention.Should().BeTrue();
    }

    [Fact]
    public void Modes_InitializesWithDefaults()
    {
        var channel = new IrcChannel();
        
        channel.Modes.InviteOnly.Should().BeFalse();
        channel.Modes.Moderated.Should().BeFalse();
        channel.Modes.NoExternalMessages.Should().BeFalse();
        channel.Modes.Private.Should().BeFalse();
        channel.Modes.Secret.Should().BeFalse();
        channel.Modes.TopicProtected.Should().BeFalse();
        channel.Modes.UserLimit.Should().BeNull();
        channel.Modes.Key.Should().BeNull();
    }
}

/// <summary>
/// Tests for ChannelModes model.
/// Verifies channel mode flags and parameters.
/// </summary>
public class ChannelModesTests
{
    [Fact]
    public void InviteOnly_CanBeSetAndUnset()
    {
        var modes = new ChannelModes { InviteOnly = true };
        
        modes.InviteOnly.Should().BeTrue();
        
        modes.InviteOnly = false;
        modes.InviteOnly.Should().BeFalse();
    }

    [Fact]
    public void Moderated_CanBeSetAndUnset()
    {
        var modes = new ChannelModes { Moderated = true };
        
        modes.Moderated.Should().BeTrue();
    }

    [Fact]
    public void NoExternalMessages_CanBeSetAndUnset()
    {
        var modes = new ChannelModes { NoExternalMessages = true };
        
        modes.NoExternalMessages.Should().BeTrue();
    }

    [Fact]
    public void Private_CanBeSetAndUnset()
    {
        var modes = new ChannelModes { Private = true };
        
        modes.Private.Should().BeTrue();
    }

    [Fact]
    public void Secret_CanBeSetAndUnset()
    {
        var modes = new ChannelModes { Secret = true };
        
        modes.Secret.Should().BeTrue();
    }

    [Fact]
    public void TopicProtected_CanBeSetAndUnset()
    {
        var modes = new ChannelModes { TopicProtected = true };
        
        modes.TopicProtected.Should().BeTrue();
    }

    [Fact]
    public void UserLimit_CanBeSetAndCleared()
    {
        var modes = new ChannelModes { UserLimit = 50 };
        
        modes.UserLimit.Should().Be(50);
        
        modes.UserLimit = null;
        modes.UserLimit.Should().BeNull();
    }

    [Fact]
    public void Key_CanBeSetAndCleared()
    {
        var modes = new ChannelModes { Key = "secret" };
        
        modes.Key.Should().Be("secret");
        
        modes.Key = null;
        modes.Key.Should().BeNull();
    }

    [Fact]
    public void AllModes_CanBeSetSimultaneously()
    {
        var modes = new ChannelModes
        {
            InviteOnly = true,
            Moderated = true,
            NoExternalMessages = true,
            Private = true,
            Secret = true,
            TopicProtected = true,
            UserLimit = 100,
            Key = "password123"
        };
        
        modes.InviteOnly.Should().BeTrue();
        modes.Moderated.Should().BeTrue();
        modes.NoExternalMessages.Should().BeTrue();
        modes.Private.Should().BeTrue();
        modes.Secret.Should().BeTrue();
        modes.TopicProtected.Should().BeTrue();
        modes.UserLimit.Should().Be(100);
        modes.Key.Should().Be("password123");
    }

    [Fact]
    public void PrivateAndSecret_CanBothBeSet()
    {
        // Some networks allow both +p and +s simultaneously
        var modes = new ChannelModes
        {
            Private = true,
            Secret = true
        };
        
        modes.Private.Should().BeTrue();
        modes.Secret.Should().BeTrue();
    }
}
