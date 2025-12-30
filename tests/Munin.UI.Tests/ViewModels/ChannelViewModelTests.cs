using FluentAssertions;
using Munin.Core.Models;
using Munin.UI.ViewModels;
using Xunit;

namespace Munin.UI.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ChannelViewModel"/>.
/// </summary>
/// <remarks>
/// Covers:
/// - Constructor initialization
/// - Message collection management
/// - User collection management
/// - Unread tracking
/// - Server console vs. channel vs. private message
/// </remarks>
public class ChannelViewModelTests
{
    private ServerViewModel CreateTestServerViewModel()
    {
        var server = new IrcServer { Name = "TestServer" };
        return new ServerViewModel(server);
    }
    
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        
        // Act
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Assert
        viewModel.Channel.Should().Be(channel);
        viewModel.ServerViewModel.Should().Be(serverViewModel);
    }
    
    [Fact]
    public void HasNoMessages_WhenEmpty_ReturnsTrue()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act & Assert
        viewModel.HasNoMessages.Should().BeTrue();
    }
    
    [Fact]
    public void HasNoMessages_WhenHasMessages_ReturnsFalse()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        var message = new IrcMessage { Content = "Test" };
        
        // Act
        viewModel.Messages.Add(new MessageViewModel(message));
        
        // Assert
        viewModel.HasNoMessages.Should().BeFalse();
    }
    
    [Fact]
    public void IsServerConsole_CanBeSet()
    {
        // Arrange
        var channel = new IrcChannel { Name = "Server" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act
        viewModel.IsServerConsole = true;
        
        // Assert
        viewModel.IsServerConsole.Should().BeTrue();
    }
    
    [Fact]
    public void IsPrivateMessage_CanBeSet()
    {
        // Arrange
        var channel = new IrcChannel { Name = "OtherUser" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act
        viewModel.IsPrivateMessage = true;
        
        // Assert
        viewModel.IsPrivateMessage.Should().BeTrue();
    }
    
    [Fact]
    public void UnreadCount_CanBeIncremented()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act
        viewModel.UnreadCount = 5;
        
        // Assert
        viewModel.UnreadCount.Should().Be(5);
    }
    
    [Fact]
    public void HasMention_CanBeSet()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act
        viewModel.HasMention = true;
        
        // Assert
        viewModel.HasMention.Should().BeTrue();
    }
    
    [Fact]
    public void Topic_CanBeSet()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act
        viewModel.Topic = "Welcome to #test";
        
        // Assert
        viewModel.Topic.Should().Be("Welcome to #test");
    }
    
    [Fact]
    public void Users_DefaultsToEmptyCollection()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act & Assert
        viewModel.Users.Should().NotBeNull();
        viewModel.Users.Should().BeEmpty();
    }
    
    [Fact]
    public void Messages_DefaultsToEmptyCollection()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act & Assert
        viewModel.Messages.Should().NotBeNull();
        viewModel.Messages.Should().BeEmpty();
    }
    
    [Fact]
    public void SortOrder_CanBeSet()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act
        viewModel.SortOrder = 10;
        
        // Assert
        viewModel.SortOrder.Should().Be(10);
    }
    
    [Fact]
    public void PrivateMessageTarget_CanBeSet()
    {
        // Arrange
        var channel = new IrcChannel { Name = "OtherUser" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act
        viewModel.PrivateMessageTarget = "OtherUser";
        
        // Assert
        viewModel.PrivateMessageTarget.Should().Be("OtherUser");
    }
    
    [Fact]
    public void IsLoadingHistory_DefaultsToFalse()
    {
        // Arrange
        var channel = new IrcChannel { Name = "#test" };
        var serverViewModel = CreateTestServerViewModel();
        var viewModel = new ChannelViewModel(channel, serverViewModel);
        
        // Act & Assert
        viewModel.IsLoadingHistory.Should().BeFalse();
    }
}
