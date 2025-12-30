using FluentAssertions;
using Munin.Core.Models;
using Munin.UI.ViewModels;
using Xunit;

namespace Munin.UI.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ServerViewModel"/>.
/// </summary>
/// <remarks>
/// Covers:
/// - Constructor initialization
/// - Connection state tracking
/// - Status icons and display
/// - Latency formatting
/// - Away status
/// </remarks>
public class ServerViewModelTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange
        var server = new IrcServer
        {
            Name = "TestServer",
            Hostname = "irc.example.com",
            Port = 6667
        };
        
        // Act
        var viewModel = new ServerViewModel(server);
        
        // Assert
        viewModel.Server.Should().Be(server);
        viewModel.DisplayName.Should().Be("TestServer");
    }
    
    [Fact]
    public void DisplayName_ReturnsServerName()
    {
        // Arrange
        var server = new IrcServer { Name = "FreeNode" };
        var viewModel = new ServerViewModel(server);
        
        // Act & Assert
        viewModel.DisplayName.Should().Be("FreeNode");
    }
    
    [Fact]
    public void SortOrder_ReturnsServerSortOrder()
    {
        // Arrange
        var server = new IrcServer { Name = "Test", SortOrder = 5 };
        var viewModel = new ServerViewModel(server);
        
        // Act & Assert
        viewModel.SortOrder.Should().Be(5);
    }
    
    [Fact]
    public void StatusIcon_WhenDisconnected_ReturnsBlackCircle()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server)
        {
            IsConnected = false,
            IsConnecting = false
        };
        
        // Act & Assert
        viewModel.StatusIcon.Should().Be("⚫");
    }
    
    [Fact]
    public void StatusIcon_WhenConnecting_ReturnsSpinner()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server)
        {
            IsConnecting = true,
            IsConnected = false
        };
        
        // Act & Assert
        viewModel.StatusIcon.Should().Be("🔄");
    }
    
    [Fact]
    public void StatusIcon_WhenConnected_ReturnsGreenCircle()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server)
        {
            IsConnected = true,
            IsConnecting = false
        };
        
        // Act & Assert
        viewModel.StatusIcon.Should().Be("🟢");
    }
    
    [Fact]
    public void LatencyDisplay_WhenZero_ReturnsDash()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server)
        {
            LatencyMs = 0
        };
        
        // Act & Assert
        viewModel.LatencyDisplay.Should().Be("—");
    }
    
    [Fact]
    public void LatencyDisplay_WhenNonZero_ReturnsFormattedMs()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server)
        {
            LatencyMs = 42
        };
        
        // Act & Assert
        viewModel.LatencyDisplay.Should().Be("42ms");
    }
    
    [Fact]
    public void IsSelected_CanBeSet()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server);
        
        // Act
        viewModel.IsSelected = true;
        
        // Assert
        viewModel.IsSelected.Should().BeTrue();
    }
    
    [Fact]
    public void IsExpanded_DefaultsToTrue()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server);
        
        // Act & Assert
        viewModel.IsExpanded.Should().BeTrue();
    }
    
    [Fact]
    public void IsExpanded_CanBeToggled()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server);
        
        // Act
        viewModel.IsExpanded = false;
        
        // Assert
        viewModel.IsExpanded.Should().BeFalse();
    }
    
    [Fact]
    public void AwayMessage_CanBeSet()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server);
        
        // Act
        viewModel.AwayMessage = "Gone for lunch";
        
        // Assert
        viewModel.AwayMessage.Should().Be("Gone for lunch");
    }
    
    [Fact]
    public void IsAway_CanBeSet()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server);
        
        // Act
        viewModel.IsAway = true;
        
        // Assert
        viewModel.IsAway.Should().BeTrue();
    }
    
    [Fact]
    public void Initials_ReturnsFirstTwoCharacters()
    {
        // Arrange
        var server = new IrcServer { Name = "FreeNode" };
        var viewModel = new ServerViewModel(server);
        
        // Act & Assert
        viewModel.Initials.Should().Be("FR");
    }
    
    [Fact]
    public void Initials_WithShortName_ReturnsUppercase()
    {
        // Arrange
        var server = new IrcServer { Name = "X" };
        var viewModel = new ServerViewModel(server);
        
        // Act & Assert
        viewModel.Initials.Should().Be("X");
    }
    
    [Fact]
    public void IsGroup_ReturnsFalse()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server);
        
        // Act & Assert
        viewModel.IsGroup.Should().BeFalse();
    }
    
    [Fact]
    public void Channels_DefaultsToEmptyCollection()
    {
        // Arrange
        var server = new IrcServer { Name = "Test" };
        var viewModel = new ServerViewModel(server);
        
        // Act & Assert
        viewModel.Channels.Should().NotBeNull();
        viewModel.Channels.Should().BeEmpty();
    }
}
