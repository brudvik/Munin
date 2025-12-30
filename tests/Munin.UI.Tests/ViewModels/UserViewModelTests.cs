using FluentAssertions;
using Munin.Core.Models;
using Munin.UI.ViewModels;
using Xunit;

namespace Munin.UI.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="UserViewModel"/>.
/// </summary>
/// <remarks>
/// Covers:
/// - Constructor initialization
/// - DisplayName with prefixes based on UserMode
/// - Mode detection and coloring
/// - Away status
/// - Group classification for user list
/// </remarks>
public class UserViewModelTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser" };
        
        // Act
        var viewModel = new UserViewModel(user);
        
        // Assert
        viewModel.User.Should().Be(user);
        viewModel.Nickname.Should().Be("TestUser");
    }
    
    [Fact]
    public void DisplayName_WithNoPrefix_ReturnsNickname()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Normal };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.DisplayName.Should().Be("TestUser");
    }
    
    [Fact]
    public void DisplayName_WithOpPrefix_ReturnsNicknameWithAt()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Operator };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.DisplayName.Should().Be("@TestUser");
    }
    
    [Fact]
    public void DisplayName_WithVoicePrefix_ReturnsNicknameWithPlus()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Voice };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.DisplayName.Should().Be("+TestUser");
    }
    
    [Fact]
    public void Mode_ReturnsUserMode()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Operator };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.Mode.Should().Be(UserMode.Operator);
    }
    
    [Fact]
    public void IsAway_WhenUserAway_ReturnsTrue()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", IsAway = true };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.IsAway.Should().BeTrue();
    }
    
    [Fact]
    public void GroupName_WhenOwner_ReturnsOwners()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Owner };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.GroupName.Should().Be("Owners");
    }
    
    [Fact]
    public void GroupName_WhenOperator_ReturnsOperators()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Operator };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.GroupName.Should().Be("Operators");
    }
    
    [Fact]
    public void GroupName_WhenVoiced_ReturnsVoiced()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Voice };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.GroupName.Should().Be("Voiced");
    }
    
    [Fact]
    public void GroupName_WhenRegularUser_ReturnsUsers()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Normal };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.GroupName.Should().Be("Users");
    }
    
    [Fact]
    public void Initials_ReturnsTwoCharacters()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser" };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.Initials.Should().Be("TE");
    }
    
    [Fact]
    public void Initials_WithShortNickname_ReturnsSingleCharacter()
    {
        // Arrange
        var user = new IrcUser { Nickname = "X" };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.Initials.Should().Be("X");
    }
    
    [Fact]
    public void StatusTooltip_WhenAway_ReturnsAwayMessage()
    {
        // Arrange
        var user = new IrcUser 
        { 
            Nickname = "TestUser",
            IsAway = true,
            AwayMessage = "Gone for lunch"
        };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.StatusTooltip.Should().Be("Gone for lunch");
    }
    
    [Fact]
    public void StatusTooltip_WhenOnline_ReturnsOnline()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", IsAway = false };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.StatusTooltip.Should().Be("Online");
    }
    
    [Fact]
    public void AwayOpacity_WhenAway_ReturnsDimmed()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", IsAway = true };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.AwayOpacity.Should().Be(0.6);
    }
    
    [Fact]
    public void AwayOpacity_WhenOnline_ReturnsFull()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", IsAway = false };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.AwayOpacity.Should().Be(1.0);
    }
    
    [Fact]
    public void SortOrder_WhenOperator_ReturnsCorrectOrder()
    {
        // Arrange
        var user = new IrcUser { Nickname = "TestUser", Mode = UserMode.Operator };
        var viewModel = new UserViewModel(user);
        
        // Act & Assert
        viewModel.SortOrder.Should().Be(2); // Operator = 2
    }
}
