using FluentAssertions;
using Munin.Core.Models;
using Munin.UI.ViewModels;
using Xunit;

namespace Munin.UI.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MessageViewModel"/>.
/// </summary>
/// <remarks>
/// Covers:
/// - Constructor initialization
/// - Timestamp formatting
/// - Message type detection
/// - Own message detection
/// - Formatted message text
/// </remarks>
public class MessageViewModelTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange
        var message = new IrcMessage
        {
            Source = "TestUser",
            Content = "Hello, world!",
            Type = MessageType.Normal
        };
        
        // Act
        var viewModel = new MessageViewModel(message);
        
        // Assert
        viewModel.Message.Should().Be(message);
        viewModel.Source.Should().Be("TestUser");
        viewModel.Content.Should().Be("Hello, world!");
    }
    
    [Fact]
    public void Timestamp_ReturnsFormattedTime()
    {
        // Arrange
        var message = new IrcMessage
        {
            Timestamp = new DateTime(2024, 1, 15, 14, 30, 45),
            Content = "Test"
        };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.Timestamp.Should().Be("[14:30:45]");
    }
    
    [Fact]
    public void IsHighlight_WhenMessageIsHighlight_ReturnsTrue()
    {
        // Arrange
        var message = new IrcMessage
        {
            Content = "Test",
            IsHighlight = true
        };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.IsHighlight.Should().BeTrue();
    }
    
    [Fact]
    public void IsEncrypted_WhenMessageIsEncrypted_ReturnsTrue()
    {
        // Arrange
        var message = new IrcMessage
        {
            Content = "Test",
            IsEncrypted = true
        };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.IsEncrypted.Should().BeTrue();
    }
    
    [Fact]
    public void FormattedMessage_ForNormalMessage_ReturnsContent()
    {
        // Arrange
        var message = new IrcMessage
        {
            Type = MessageType.Normal,
            Content = "Hello"
        };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.FormattedMessage.Should().Be("Hello");
    }
    
    [Fact]
    public void FormattedMessage_ForActionMessage_ReturnsFormattedAction()
    {
        // Arrange
        var message = new IrcMessage
        {
            Type = MessageType.Action,
            Source = "TestUser",
            Content = "waves"
        };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.FormattedMessage.Should().Be("* TestUser waves");
    }
    
    [Fact]
    public void FormattedMessage_ForNotice_ReturnsFormattedNotice()
    {
        // Arrange
        var message = new IrcMessage
        {
            Type = MessageType.Notice,
            Source = "NickServ",
            Content = "You are now identified"
        };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.FormattedMessage.Should().Be("-NickServ- You are now identified");
    }
    
    [Fact]
    public void FormattedMessage_ForSystemMessage_ReturnsContent()
    {
        // Arrange
        var message = new IrcMessage
        {
            Type = MessageType.System,
            Content = "*** Connected to server"
        };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.FormattedMessage.Should().Be("*** Connected to server");
    }
    
    [Fact]
    public void ShowNickname_ForNormalMessage_ReturnsTrue()
    {
        // Arrange
        var message = new IrcMessage { Type = MessageType.Normal };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.ShowNickname.Should().BeTrue();
    }
    
    [Fact]
    public void ShowNickname_ForSystemMessage_ReturnsFalse()
    {
        // Arrange
        var message = new IrcMessage { Type = MessageType.System };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.ShowNickname.Should().BeFalse();
    }
    
    [Fact]
    public void IsOwnMessage_WhenSourceMatchesCurrentNickname_ReturnsTrue()
    {
        // Arrange
        MessageViewModel.CurrentNickname = "MyNick";
        var message = new IrcMessage { Source = "MyNick" };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.IsOwnMessage.Should().BeTrue();
    }
    
    [Fact]
    public void IsOwnMessage_WhenSourceDifferent_ReturnsFalse()
    {
        // Arrange
        MessageViewModel.CurrentNickname = "MyNick";
        var message = new IrcMessage { Source = "OtherUser" };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.IsOwnMessage.Should().BeFalse();
    }
    
    [Fact]
    public void PaddedNickname_PadsToFixedWidth()
    {
        // Arrange
        var message = new IrcMessage { Source = "Bob" };
        var viewModel = new MessageViewModel(message);
        
        // Act
        var padded = viewModel.PaddedNickname;
        
        // Assert
        padded.Length.Should().Be(MessageViewModel.MaxNicknameLength);
        padded.TrimStart().Should().Be("Bob");
    }
    
    [Fact]
    public void Type_ReturnsMessageType()
    {
        // Arrange
        var message = new IrcMessage { Type = MessageType.Join };
        var viewModel = new MessageViewModel(message);
        
        // Act & Assert
        viewModel.Type.Should().Be(MessageType.Join);
    }
}
