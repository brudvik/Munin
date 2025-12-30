using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for IrcUser model.
/// Verifies user identity, modes, and privilege tracking.
/// </summary>
public class IrcUserTests
{
    [Fact]
    public void Constructor_InitializesDefaults()
    {
        var user = new IrcUser();
        
        user.Nickname.Should().BeEmpty();
        user.Username.Should().BeNull();
        user.Hostname.Should().BeNull();
        user.RealName.Should().BeNull();
        user.Account.Should().BeNull();
        user.Mode.Should().Be(UserMode.Normal);
        user.IsAway.Should().BeFalse();
        user.AwayMessage.Should().BeNull();
    }

    [Fact]
    public void Nickname_CanBeSetAndRetrieved()
    {
        var user = new IrcUser { Nickname = "Alice" };
        
        user.Nickname.Should().Be("Alice");
    }

    [Fact]
    public void Identity_CanBeFullySet()
    {
        var user = new IrcUser
        {
            Nickname = "Alice",
            Username = "alice",
            Hostname = "example.com",
            RealName = "Alice Smith"
        };
        
        user.Nickname.Should().Be("Alice");
        user.Username.Should().Be("alice");
        user.Hostname.Should().Be("example.com");
        user.RealName.Should().Be("Alice Smith");
    }

    [Fact]
    public void Account_CanBeSet()
    {
        var user = new IrcUser { Account = "AliceAccount" };
        
        user.Account.Should().Be("AliceAccount");
        user.IsLoggedIn.Should().BeTrue();
    }

    [Fact]
    public void IsLoggedIn_ReturnsFalseWhenAccountIsNull()
    {
        var user = new IrcUser { Account = null };
        
        user.IsLoggedIn.Should().BeFalse();
    }

    [Fact]
    public void IsLoggedIn_ReturnsFalseWhenAccountIsEmpty()
    {
        var user = new IrcUser { Account = "" };
        
        user.IsLoggedIn.Should().BeFalse();
    }

    [Fact]
    public void IsLoggedIn_ReturnsFalseWhenAccountIsAsterisk()
    {
        // "*" is used by IRC to indicate "not logged in"
        var user = new IrcUser { Account = "*" };
        
        user.IsLoggedIn.Should().BeFalse();
    }

    [Fact]
    public void Mode_CanBeSetToVoice()
    {
        var user = new IrcUser { Mode = UserMode.Voice };
        
        user.Mode.Should().Be(UserMode.Voice);
    }

    [Fact]
    public void Mode_CanBeSetToOperator()
    {
        var user = new IrcUser { Mode = UserMode.Operator };
        
        user.Mode.Should().Be(UserMode.Operator);
    }

    [Fact]
    public void Prefix_ReturnsCorrectSymbolForNormal()
    {
        var user = new IrcUser { Mode = UserMode.Normal };
        
        user.Prefix.Should().Be("");
    }

    [Fact]
    public void Prefix_ReturnsCorrectSymbolForVoice()
    {
        var user = new IrcUser { Mode = UserMode.Voice };
        
        user.Prefix.Should().Be("+");
    }

    [Fact]
    public void Prefix_ReturnsCorrectSymbolForHalfOp()
    {
        var user = new IrcUser { Mode = UserMode.HalfOperator };
        
        user.Prefix.Should().Be("%");
    }

    [Fact]
    public void Prefix_ReturnsCorrectSymbolForOperator()
    {
        var user = new IrcUser { Mode = UserMode.Operator };
        
        user.Prefix.Should().Be("@");
    }

    [Fact]
    public void Prefix_ReturnsCorrectSymbolForAdmin()
    {
        var user = new IrcUser { Mode = UserMode.Admin };
        
        user.Prefix.Should().Be("&");
    }

    [Fact]
    public void Prefix_ReturnsCorrectSymbolForOwner()
    {
        var user = new IrcUser { Mode = UserMode.Owner };
        
        user.Prefix.Should().Be("~");
    }

    [Fact]
    public void DisplayName_CombinesPrefixAndNickname()
    {
        var user = new IrcUser
        {
            Nickname = "Alice",
            Mode = UserMode.Operator
        };
        
        user.DisplayName.Should().Be("@Alice");
    }

    [Fact]
    public void DisplayName_NoPrefix_ReturnsOnlyNickname()
    {
        var user = new IrcUser
        {
            Nickname = "Bob",
            Mode = UserMode.Normal
        };
        
        user.DisplayName.Should().Be("Bob");
    }

    [Fact]
    public void SortOrder_Owner_HasLowestValue()
    {
        var user = new IrcUser { Mode = UserMode.Owner };
        
        user.SortOrder.Should().Be(0);
    }

    [Fact]
    public void SortOrder_Admin_HasSecondLowestValue()
    {
        var user = new IrcUser { Mode = UserMode.Admin };
        
        user.SortOrder.Should().Be(1);
    }

    [Fact]
    public void SortOrder_Operator_HasThirdLowestValue()
    {
        var user = new IrcUser { Mode = UserMode.Operator };
        
        user.SortOrder.Should().Be(2);
    }

    [Fact]
    public void SortOrder_HalfOperator_HasFourthLowestValue()
    {
        var user = new IrcUser { Mode = UserMode.HalfOperator };
        
        user.SortOrder.Should().Be(3);
    }

    [Fact]
    public void SortOrder_Voice_HasFifthLowestValue()
    {
        var user = new IrcUser { Mode = UserMode.Voice };
        
        user.SortOrder.Should().Be(4);
    }

    [Fact]
    public void SortOrder_Normal_HasHighestValue()
    {
        var user = new IrcUser { Mode = UserMode.Normal };
        
        user.SortOrder.Should().Be(5);
    }

    [Fact]
    public void SortOrder_CanSortUserList()
    {
        var users = new List<IrcUser>
        {
            new() { Nickname = "Alice", Mode = UserMode.Normal },
            new() { Nickname = "Bob", Mode = UserMode.Operator },
            new() { Nickname = "Charlie", Mode = UserMode.Voice },
            new() { Nickname = "Dave", Mode = UserMode.Owner }
        };
        
        var sorted = users.OrderBy(u => u.SortOrder).ThenBy(u => u.Nickname).ToList();
        
        sorted[0].Nickname.Should().Be("Dave");   // Owner
        sorted[1].Nickname.Should().Be("Bob");    // Operator
        sorted[2].Nickname.Should().Be("Charlie"); // Voice
        sorted[3].Nickname.Should().Be("Alice");  // Normal
    }

    [Fact]
    public void IsAway_CanBeSetAndUnset()
    {
        var user = new IrcUser();
        
        user.IsAway.Should().BeFalse();
        
        user.IsAway = true;
        user.IsAway.Should().BeTrue();
        
        user.IsAway = false;
        user.IsAway.Should().BeFalse();
    }

    [Fact]
    public void AwayMessage_CanBeSetAndCleared()
    {
        var user = new IrcUser { AwayMessage = "Gone for lunch" };
        
        user.AwayMessage.Should().Be("Gone for lunch");
        
        user.AwayMessage = null;
        user.AwayMessage.Should().BeNull();
    }

    [Fact]
    public void AllProperties_CanBeSetSimultaneously()
    {
        var user = new IrcUser
        {
            Nickname = "Alice",
            Username = "alice",
            Hostname = "example.com",
            RealName = "Alice Smith",
            Account = "AliceAccount",
            Mode = UserMode.Operator,
            IsAway = true,
            AwayMessage = "AFK"
        };
        
        user.Nickname.Should().Be("Alice");
        user.Username.Should().Be("alice");
        user.Hostname.Should().Be("example.com");
        user.RealName.Should().Be("Alice Smith");
        user.Account.Should().Be("AliceAccount");
        user.Mode.Should().Be(UserMode.Operator);
        user.IsAway.Should().BeTrue();
        user.AwayMessage.Should().Be("AFK");
        user.IsLoggedIn.Should().BeTrue();
        user.DisplayName.Should().Be("@Alice");
    }
}

/// <summary>
/// Tests for UserMode enum.
/// Verifies correct privilege ordering.
/// </summary>
public class UserModeTests
{
    [Fact]
    public void UserMode_EnumValues_InAscendingPrivilege()
    {
        ((int)UserMode.Normal).Should().BeLessThan((int)UserMode.Voice);
        ((int)UserMode.Voice).Should().BeLessThan((int)UserMode.HalfOperator);
        ((int)UserMode.HalfOperator).Should().BeLessThan((int)UserMode.Operator);
        ((int)UserMode.Operator).Should().BeLessThan((int)UserMode.Admin);
        ((int)UserMode.Admin).Should().BeLessThan((int)UserMode.Owner);
    }

    [Fact]
    public void UserMode_AllValuesAreDefined()
    {
        Enum.IsDefined(typeof(UserMode), UserMode.Normal).Should().BeTrue();
        Enum.IsDefined(typeof(UserMode), UserMode.Voice).Should().BeTrue();
        Enum.IsDefined(typeof(UserMode), UserMode.HalfOperator).Should().BeTrue();
        Enum.IsDefined(typeof(UserMode), UserMode.Operator).Should().BeTrue();
        Enum.IsDefined(typeof(UserMode), UserMode.Admin).Should().BeTrue();
        Enum.IsDefined(typeof(UserMode), UserMode.Owner).Should().BeTrue();
    }
}
