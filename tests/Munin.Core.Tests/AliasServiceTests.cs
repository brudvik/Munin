using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for AliasService - command alias expansion and management.
/// </summary>
public class AliasServiceTests
{
    [Fact]
    public void Constructor_ShouldLoadDefaultAliases()
    {
        // Act
        var service = new AliasService();

        // Assert
        var aliases = service.GetAliases();
        aliases.Should().NotBeEmpty();
        aliases.Should().ContainKey("ns");
        aliases.Should().ContainKey("j");
        aliases.Should().ContainKey("kb");
    }

    [Fact]
    public void DefaultAliases_ShouldContainCommonAliases()
    {
        // Assert
        AliasService.DefaultAliases.Should().ContainKey("ns");
        AliasService.DefaultAliases.Should().ContainKey("cs");
        AliasService.DefaultAliases.Should().ContainKey("j");
        AliasService.DefaultAliases.Should().ContainKey("p");
        AliasService.DefaultAliases.Should().ContainKey("q");
        AliasService.DefaultAliases.Should().ContainKey("k");
        AliasService.DefaultAliases.Should().ContainKey("kb");
        AliasService.DefaultAliases.Should().ContainKey("slap");
    }

    [Fact]
    public void SetAlias_ShouldAddNewAlias()
    {
        // Arrange
        var service = new AliasService();

        // Act
        service.SetAlias("test", "/msg test $1");

        // Assert
        var aliases = service.GetAliases();
        aliases.Should().ContainKey("test");
        aliases["test"].Expansion.Should().Be("/msg test $1");
    }

    [Fact]
    public void SetAlias_ShouldUpdateExistingAlias()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/msg test $1");

        // Act
        service.SetAlias("test", "/msg updated $1");

        // Assert
        var aliases = service.GetAliases();
        aliases["test"].Expansion.Should().Be("/msg updated $1");
    }

    [Fact]
    public void SetAlias_ShouldStripLeadingSlash()
    {
        // Arrange
        var service = new AliasService();

        // Act
        service.SetAlias("/test", "/msg test $1");

        // Assert
        var aliases = service.GetAliases();
        aliases.Should().ContainKey("test");
    }

    [Fact]
    public void RemoveAlias_ExistingAlias_ShouldReturnTrue()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/msg test $1");

        // Act
        var result = service.RemoveAlias("test");

        // Assert
        result.Should().BeTrue();
        service.GetAliases().Should().NotContainKey("test");
    }

    [Fact]
    public void RemoveAlias_NonExistentAlias_ShouldReturnFalse()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result = service.RemoveAlias("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void RemoveAlias_ShouldStripLeadingSlash()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/msg test $1");

        // Act
        var result = service.RemoveAlias("/test");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ExpandAlias_SimpleAlias_ShouldExpand()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result = service.ExpandAlias("/j #test");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle()
            .Which.Should().Be("/join #test");
    }

    [Fact]
    public void ExpandAlias_WithArguments_ShouldSubstitute()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/msg $1 hello $2");

        // Act
        var result = service.ExpandAlias("/test john world");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle()
            .Which.Should().Be("/msg john hello world");
    }

    [Fact]
    public void ExpandAlias_WithRangeArgument_ShouldSubstituteAll()
    {
        // Arrange
        var service = new AliasService();

        // Act - ns alias uses $1-
        var result = service.ExpandAlias("/ns identify mypassword");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle()
            .Which.Should().Be("/msg NickServ identify mypassword");
    }

    [Fact]
    public void ExpandAlias_WithChannelVariable_ShouldSubstitute()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/topic $channel $1-");

        // Act
        var result = service.ExpandAlias("/test New topic", "#munin", "user");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle()
            .Which.Should().Be("/topic #munin New topic");
    }

    [Fact]
    public void ExpandAlias_WithNickVariable_ShouldSubstitute()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/msg $1 Hello from $me");

        // Act
        var result = service.ExpandAlias("/test john", "#test", "alice");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle()
            .Which.Should().Be("/msg john Hello from alice");
    }

    [Fact]
    public void ExpandAlias_MultipleCommands_ShouldSplitBySemicolon()
    {
        // Arrange
        var service = new AliasService();

        // Act - kb alias has two commands
        var result = service.ExpandAlias("/kb baduser Spam", "#test", "op");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].Should().Be("/mode #test +b baduser");
        result[1].Should().Be("/kick baduser Spam");
    }

    [Fact]
    public void ExpandAlias_NoMatch_ShouldReturnNull()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result = service.ExpandAlias("/nonexistent test");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExpandAlias_NotACommand_ShouldReturnNull()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result = service.ExpandAlias("regular text");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExpandAlias_EmptyInput_ShouldReturnNull()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result = service.ExpandAlias("/");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExpandAlias_CaseInsensitive_ShouldMatch()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result1 = service.ExpandAlias("/NS identify pass");
        var result2 = service.ExpandAlias("/ns identify pass");
        var result3 = service.ExpandAlias("/Ns identify pass");

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result3.Should().NotBeNull();
        result1![0].Should().Be(result2![0]);
        result2[0].Should().Be(result3![0]);
    }

    [Fact]
    public void IsBuiltInCommand_WithBuiltIn_ShouldReturnTrue()
    {
        // Assert
        AliasService.IsBuiltInCommand("join").Should().BeTrue();
        AliasService.IsBuiltInCommand("/join").Should().BeTrue();
        AliasService.IsBuiltInCommand("msg").Should().BeTrue();
        AliasService.IsBuiltInCommand("quit").Should().BeTrue();
    }

    [Fact]
    public void IsBuiltInCommand_WithAlias_ShouldReturnFalse()
    {
        // Assert
        AliasService.IsBuiltInCommand("ns").Should().BeFalse();
        AliasService.IsBuiltInCommand("j").Should().BeFalse();
        AliasService.IsBuiltInCommand("kb").Should().BeFalse();
    }

    [Fact]
    public void IsBuiltInCommand_CaseInsensitive_ShouldMatch()
    {
        // Assert
        AliasService.IsBuiltInCommand("JOIN").Should().BeTrue();
        AliasService.IsBuiltInCommand("Join").Should().BeTrue();
        AliasService.IsBuiltInCommand("JoIn").Should().BeTrue();
    }

    [Fact]
    public void AliasDefinition_IsBuiltIn_ShouldDetectDefaultAliases()
    {
        // Arrange
        var service = new AliasService();
        var aliases = service.GetAliases();

        // Assert
        aliases["ns"].IsBuiltIn.Should().BeTrue();
        aliases["j"].IsBuiltIn.Should().BeTrue();
        aliases["slap"].IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void AliasDefinition_IsBuiltIn_CustomAlias_ShouldBeFalse()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("custom", "/msg test $1");
        var aliases = service.GetAliases();

        // Assert
        aliases["custom"].IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void ExpandAlias_MissingArguments_ShouldCleanUpUnreplaced()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/msg $1 hello $2");

        // Act - Only provide one argument
        var result = service.ExpandAlias("/test john");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle();
        result![0].Should().Be("/msg john hello");
        result[0].Should().NotContain("$2");
    }

    [Fact]
    public void ExpandAlias_MissingChannelVariable_ShouldRemoveIt()
    {
        // Arrange
        var service = new AliasService();
        service.SetAlias("test", "/topic $channel Test");

        // Act - No channel provided
        var result = service.ExpandAlias("/test", null, "user");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle()
            .Which.Should().Be("/topic Test");
    }

    [Fact]
    public void ExpandAlias_ComplexKickBanAlias_ShouldExpandCorrectly()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result = service.ExpandAlias("/kb spammer Advertising bots", "#munin", "op");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].Should().Be("/mode #munin +b spammer");
        result[1].Should().Be("/kick spammer Advertising bots");
    }

    [Fact]
    public void ExpandAlias_SlapAlias_ShouldExpandWithAction()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var result = service.ExpandAlias("/slap john");

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainSingle()
            .Which.Should().Be("/me slaps john around a bit with a large trout");
    }

    [Fact]
    public void GetAliases_ShouldReturnReadOnlyDictionary()
    {
        // Arrange
        var service = new AliasService();

        // Act
        var aliases = service.GetAliases();

        // Assert
        aliases.Should().BeAssignableTo<IReadOnlyDictionary<string, AliasDefinition>>();
    }
}
