using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for ServerGroup - server organization and grouping functionality
/// </summary>
public class ServerGroupTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        // Act
        var group = new ServerGroup();

        // Assert
        group.Id.Should().NotBeNullOrEmpty();
        group.Name.Should().BeEmpty();
        group.SortOrder.Should().Be(0);
        group.IsCollapsed.Should().BeFalse();
        group.Icon.Should().BeNull();
    }

    [Fact]
    public void Id_IsUniqueForEachInstance()
    {
        // Act
        var group1 = new ServerGroup();
        var group2 = new ServerGroup();

        // Assert
        group1.Id.Should().NotBe(group2.Id);
    }

    [Fact]
    public void Name_CanBeSet()
    {
        // Arrange
        var group = new ServerGroup();

        // Act
        group.Name = "Work Servers";

        // Assert
        group.Name.Should().Be("Work Servers");
    }

    [Fact]
    public void SortOrder_CanBeSet()
    {
        // Arrange
        var group = new ServerGroup();

        // Act
        group.SortOrder = 5;

        // Assert
        group.SortOrder.Should().Be(5);
    }

    [Fact]
    public void SortOrder_CanBeNegative()
    {
        // Arrange
        var group = new ServerGroup();

        // Act
        group.SortOrder = -1;

        // Assert
        group.SortOrder.Should().Be(-1);
    }

    [Fact]
    public void IsCollapsed_DefaultsFalse()
    {
        // Act
        var group = new ServerGroup();

        // Assert
        group.IsCollapsed.Should().BeFalse();
    }

    [Fact]
    public void IsCollapsed_CanBeSetTrue()
    {
        // Arrange
        var group = new ServerGroup();

        // Act
        group.IsCollapsed = true;

        // Assert
        group.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void IsCollapsed_CanBeToggled()
    {
        // Arrange
        var group = new ServerGroup();

        // Act & Assert
        group.IsCollapsed = true;
        group.IsCollapsed.Should().BeTrue();

        group.IsCollapsed = false;
        group.IsCollapsed.Should().BeFalse();
    }

    [Fact]
    public void Icon_CanBeSet()
    {
        // Arrange
        var group = new ServerGroup();

        // Act
        group.Icon = "🖥️";

        // Assert
        group.Icon.Should().Be("🖥️");
    }

    [Fact]
    public void Icon_CanBeCleared()
    {
        // Arrange
        var group = new ServerGroup { Icon = "🖥️" };

        // Act
        group.Icon = null;

        // Assert
        group.Icon.Should().BeNull();
    }

    [Fact]
    public void CompleteGroup_AllPropertiesSet()
    {
        // Arrange & Act
        var group = new ServerGroup
        {
            Name = "Personal Networks",
            SortOrder = 1,
            IsCollapsed = false,
            Icon = "🏠"
        };

        // Assert
        group.Name.Should().Be("Personal Networks");
        group.SortOrder.Should().Be(1);
        group.IsCollapsed.Should().BeFalse();
        group.Icon.Should().Be("🏠");
        group.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MultipleGroups_HaveUniqueIds()
    {
        // Act
        var groups = Enumerable.Range(0, 10)
            .Select(_ => new ServerGroup())
            .ToList();

        // Assert
        var ids = groups.Select(g => g.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SortOrder_CanOrderGroups()
    {
        // Arrange
        var groups = new List<ServerGroup>
        {
            new ServerGroup { Name = "Third", SortOrder = 3 },
            new ServerGroup { Name = "First", SortOrder = 1 },
            new ServerGroup { Name = "Second", SortOrder = 2 }
        };

        // Act
        var sorted = groups.OrderBy(g => g.SortOrder).ToList();

        // Assert
        sorted[0].Name.Should().Be("First");
        sorted[1].Name.Should().Be("Second");
        sorted[2].Name.Should().Be("Third");
    }

    [Fact]
    public void Group_CanHaveEmojiIcon()
    {
        // Arrange
        var group = new ServerGroup();

        // Act
        group.Icon = "⚙️";

        // Assert
        group.Icon.Should().Be("⚙️");
    }

    [Fact]
    public void Group_CanHaveTextIcon()
    {
        // Arrange
        var group = new ServerGroup();

        // Act
        group.Icon = "WRK";

        // Assert
        group.Icon.Should().Be("WRK");
    }

    [Fact]
    public void WorkScenario_OrganizeServers()
    {
        // Arrange & Act
        var workGroup = new ServerGroup
        {
            Name = "Work",
            SortOrder = 1,
            IsCollapsed = false,
            Icon = "💼"
        };

        var personalGroup = new ServerGroup
        {
            Name = "Personal",
            SortOrder = 2,
            IsCollapsed = false,
            Icon = "🏠"
        };

        var communityGroup = new ServerGroup
        {
            Name = "Communities",
            SortOrder = 3,
            IsCollapsed = true,
            Icon = "👥"
        };

        // Assert
        workGroup.Name.Should().Be("Work");
        workGroup.SortOrder.Should().Be(1);
        
        personalGroup.Name.Should().Be("Personal");
        personalGroup.SortOrder.Should().Be(2);
        
        communityGroup.Name.Should().Be("Communities");
        communityGroup.SortOrder.Should().Be(3);
        communityGroup.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void EmptyGroup_ValidState()
    {
        // Act
        var group = new ServerGroup
        {
            Name = ""
        };

        // Assert
        group.Name.Should().BeEmpty();
        group.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Group_LongName()
    {
        // Arrange
        var longName = "Super Long Group Name With Many Words And Characters";

        // Act
        var group = new ServerGroup
        {
            Name = longName
        };

        // Assert
        group.Name.Should().Be(longName);
        group.Name.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void Group_NegativeSortOrder_ForPinning()
    {
        // Arrange - Use negative sort order to pin to top
        var pinnedGroup = new ServerGroup
        {
            Name = "Favorites",
            SortOrder = -100,
            Icon = "⭐"
        };

        var regularGroup = new ServerGroup
        {
            Name = "Others",
            SortOrder = 0
        };

        // Act
        var groups = new List<ServerGroup> { regularGroup, pinnedGroup }
            .OrderBy(g => g.SortOrder)
            .ToList();

        // Assert
        groups[0].Name.Should().Be("Favorites");
        groups[1].Name.Should().Be("Others");
    }
}
