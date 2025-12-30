using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for PrivacyService.
/// Verifies filename anonymization, path sanitization, and mapping management.
/// </summary>
public class PrivacyServiceTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        var service = new PrivacyService();
        
        service.IsEnabled.Should().BeTrue();
        service.ObfuscateDates.Should().BeFalse();
        service.IsMappingLoaded.Should().BeFalse();
    }

    [Fact]
    public void AnonymizeServerName_WhenDisabled_ReturnsSanitizedName()
    {
        var service = new PrivacyService { IsEnabled = false };
        
        var result = service.AnonymizeServerName("libera.chat");
        
        result.Should().Be("libera.chat");
    }

    [Fact]
    public void AnonymizeServerName_WhenEnabled_ReturnsHash()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result = service.AnonymizeServerName("libera.chat");
        
        result.Should().StartWith("srv_");
        result.Should().HaveLength(10); // "srv_" + 6 hex chars
    }

    [Fact]
    public void AnonymizeServerName_SameInput_ReturnsSameHash()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result1 = service.AnonymizeServerName("libera.chat");
        var result2 = service.AnonymizeServerName("libera.chat");
        
        result1.Should().Be(result2);
    }

    [Fact]
    public void AnonymizeServerName_DifferentInput_ReturnsDifferentHash()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result1 = service.AnonymizeServerName("libera.chat");
        var result2 = service.AnonymizeServerName("oftc.net");
        
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void AnonymizeServerName_CaseInsensitive_ReturnsSameHash()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result1 = service.AnonymizeServerName("Libera.Chat");
        var result2 = service.AnonymizeServerName("libera.chat");
        
        result1.Should().Be(result2);
    }

    [Fact]
    public void AnonymizeServerName_EmptyString_ReturnsEmpty()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result = service.AnonymizeServerName("");
        
        result.Should().BeEmpty();
    }

    [Fact]
    public void AnonymizeChannelName_WhenEnabled_ReturnsHash()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result = service.AnonymizeChannelName("#norway");
        
        result.Should().StartWith("ch_");
        result.Should().HaveLength(9); // "ch_" + 6 hex chars
    }

    [Fact]
    public void AnonymizeChannelName_SameInput_ReturnsSameHash()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result1 = service.AnonymizeChannelName("#norway");
        var result2 = service.AnonymizeChannelName("#norway");
        
        result1.Should().Be(result2);
    }

    [Fact]
    public void AnonymizeChannelName_WhenDisabled_ReturnsSanitizedName()
    {
        var service = new PrivacyService { IsEnabled = false };
        
        var result = service.AnonymizeChannelName("#norway");
        
        result.Should().Be("#norway");
    }

    [Fact]
    public void AnonymizeChannelName_WithInvalidChars_SanitizesWhenDisabled()
    {
        var service = new PrivacyService { IsEnabled = false };
        
        // Use characters that are invalid on all platforms
        var channelWithInvalidChars = "#test" + new string(Path.GetInvalidFileNameChars().Take(3).ToArray());
        var result = service.AnonymizeChannelName(channelWithInvalidChars);
        
        // Verify invalid chars are replaced with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars.Take(3))
        {
            result.Should().NotContain(invalidChar.ToString());
        }
        result.Should().Contain("_"); // Should have underscores as replacements
    }

    [Fact]
    public void GetAnonymizedLogPath_WithoutDateObfuscation_UsesStandardDate()
    {
        var service = new PrivacyService 
        { 
            IsEnabled = true,
            ObfuscateDates = false 
        };
        var date = new DateTime(2025, 12, 30);
        
        var result = service.GetAnonymizedLogPath("libera.chat", "#norway", date);
        
        result.Should().StartWith("logs/srv_");
        result.Should().Contain("_2025-12-30.log");
    }

    [Fact]
    public void GetAnonymizedLogPath_WithDateObfuscation_UsesHashedDate()
    {
        var service = new PrivacyService 
        { 
            IsEnabled = true,
            ObfuscateDates = true 
        };
        var date = new DateTime(2025, 12, 30);
        
        var result = service.GetAnonymizedLogPath("libera.chat", "#norway", date);
        
        result.Should().StartWith("logs/srv_");
        result.Should().NotContain("2025-12-30");
    }

    [Fact]
    public void GetAnonymizedLogPath_WhenDisabled_UsesRealNames()
    {
        var service = new PrivacyService { IsEnabled = false };
        var date = new DateTime(2025, 12, 30);
        
        var result = service.GetAnonymizedLogPath("libera.chat", "#norway", date);
        
        result.Should().Contain("libera.chat");
        result.Should().Contain("#norway");
        result.Should().Contain("2025-12-30");
    }

    [Fact]
    public void LookupRealName_ExistingHash_ReturnsOriginalName()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var hash = service.AnonymizeServerName("libera.chat");
        var result = service.LookupRealName(hash);
        
        result.Should().Be("libera.chat");
    }

    [Fact]
    public void LookupRealName_NonExistentHash_ReturnsHash()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result = service.LookupRealName("srv_999999");
        
        result.Should().Be("srv_999999");
    }

    [Fact]
    public void GetServerMappings_ReturnsOnlyServerHashes()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        service.AnonymizeServerName("libera.chat");
        service.AnonymizeServerName("oftc.net");
        service.AnonymizeChannelName("#norway");
        
        var mappings = service.GetServerMappings();
        
        mappings.Should().HaveCount(2);
        mappings.Values.Should().Contain("libera.chat");
        mappings.Values.Should().Contain("oftc.net");
        mappings.Values.Should().NotContain("#norway");
    }

    [Fact]
    public void GetChannelMappings_ReturnsOnlyChannelHashes()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        service.AnonymizeServerName("libera.chat");
        service.AnonymizeChannelName("#norway");
        service.AnonymizeChannelName("#test");
        
        var mappings = service.GetChannelMappings();
        
        mappings.Should().HaveCount(2);
        mappings.Values.Should().Contain("#norway");
        mappings.Values.Should().Contain("#test");
        mappings.Values.Should().NotContain("libera.chat");
    }

    [Fact]
    public void AnonymizeServerName_MultipleServers_TracksAll()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var hash1 = service.AnonymizeServerName("libera.chat");
        var hash2 = service.AnonymizeServerName("oftc.net");
        var hash3 = service.AnonymizeServerName("undernet.org");
        
        var mappings = service.GetServerMappings();
        
        mappings.Should().HaveCount(3);
        mappings[hash1].Should().Be("libera.chat");
        mappings[hash2].Should().Be("oftc.net");
        mappings[hash3].Should().Be("undernet.org");
    }

    [Fact]
    public void AnonymizeChannelName_MultipleChannels_TracksAll()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var hash1 = service.AnonymizeChannelName("#norway");
        var hash2 = service.AnonymizeChannelName("#test");
        var hash3 = service.AnonymizeChannelName("#dev");
        
        var mappings = service.GetChannelMappings();
        
        mappings.Should().HaveCount(3);
        mappings[hash1].Should().Be("#norway");
        mappings[hash2].Should().Be("#test");
        mappings[hash3].Should().Be("#dev");
    }

    [Fact]
    public void GetAnonymizedLogPath_ConsistentFormat()
    {
        var service = new PrivacyService { IsEnabled = true };
        var date = new DateTime(2025, 12, 30);
        
        var result = service.GetAnonymizedLogPath("libera.chat", "#norway", date);
        
        result.Should().MatchRegex(@"^logs/srv_[a-f0-9]{6}/ch_[a-f0-9]{6}_\d{4}-\d{2}-\d{2}\.log$");
    }

    [Fact]
    public void AnonymizeServerName_SpecialCharacters_HandledCorrectly()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result = service.AnonymizeServerName("irc.example.com:6697");
        
        result.Should().StartWith("srv_");
        result.Should().MatchRegex(@"^srv_[a-f0-9]{6}$");
    }

    [Fact]
    public async Task InitializeAsync_WithoutStorage_DoesNotThrow()
    {
        var service = new PrivacyService();
        
        var act = async () => await service.InitializeAsync();
        
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_WithLockedStorage_LoadsMappingSuccessfully()
    {
        var storage = new SecureStorageService(Path.GetTempPath());
        var service = new PrivacyService(storage);
        
        await service.InitializeAsync();
        
        service.IsMappingLoaded.Should().BeTrue();
    }

    [Fact]
    public void ObfuscateDates_CanBeToggled()
    {
        var service = new PrivacyService();
        
        service.ObfuscateDates.Should().BeFalse();
        
        service.ObfuscateDates = true;
        service.ObfuscateDates.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_CanBeToggled()
    {
        var service = new PrivacyService();
        
        service.IsEnabled.Should().BeTrue();
        
        service.IsEnabled = false;
        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AnonymizeServerName_NullOrEmptyString_ReturnsEmpty()
    {
        var service = new PrivacyService { IsEnabled = true };
        
        var result1 = service.AnonymizeServerName("");
        var result2 = service.AnonymizeServerName(string.Empty);
        
        result1.Should().BeEmpty();
        result2.Should().BeEmpty();
    }

    [Fact]
    public void GetAnonymizedLogPath_DifferentDates_DifferentPaths()
    {
        var service = new PrivacyService { IsEnabled = true };
        var date1 = new DateTime(2025, 12, 30);
        var date2 = new DateTime(2025, 12, 31);
        
        var result1 = service.GetAnonymizedLogPath("libera.chat", "#norway", date1);
        var result2 = service.GetAnonymizedLogPath("libera.chat", "#norway", date2);
        
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void GetAnonymizedLogPath_ObfuscatedDates_SameWeek_MightBeSame()
    {
        var service = new PrivacyService 
        { 
            IsEnabled = true,
            ObfuscateDates = true 
        };
        // Same week (both Monday to Sunday of same week)
        var date1 = new DateTime(2025, 12, 29); // Monday
        var date2 = new DateTime(2025, 12, 30); // Tuesday
        
        var result1 = service.GetAnonymizedLogPath("libera.chat", "#norway", date1);
        var result2 = service.GetAnonymizedLogPath("libera.chat", "#norway", date2);
        
        // Should have same date hash since they're in the same week
        var dateHash1 = result1.Split('_').Last().Replace(".log", "");
        var dateHash2 = result2.Split('_').Last().Replace(".log", "");
        
        dateHash1.Should().Be(dateHash2);
    }
}
