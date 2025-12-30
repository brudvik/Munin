using FluentAssertions;
using Munin.Core.Models;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for ConfigurationService - user settings and configuration management.
/// </summary>
public class ConfigurationServiceTests : IDisposable
{
    private readonly string _testDir;

    public ConfigurationServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"munin_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_WithNoConfigFile_CreatesDefaultConfiguration()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        await service.LoadAsync();

        service.Configuration.Should().NotBeNull();
        service.Configuration.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_CreatesConfigFile()
    {
        var service = new ConfigurationService(_testDir);
        // When encryption is not enabled, storage is automatically unlocked

        await service.SaveAsync();

        // File is saved (unencrypted when no encryption is enabled)
        service.Storage.FileExists("config.json").Should().BeTrue();
    }

    [Fact]
    public async Task AddServer_AddsServerToConfiguration()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer
        {
            Id = "test-server",
            Name = "Test Server",
            Hostname = "irc.example.com",
            Port = 6697,
            UseSsl = true,
            Nickname = "TestNick",
            Username = "testuser",
            RealName = "Test User"
        };

        service.AddServer(server);

        service.Configuration.Servers.Should().HaveCount(1);
        service.Configuration.Servers[0].Id.Should().Be("test-server");
        service.Configuration.Servers[0].Name.Should().Be("Test Server");
    }

    [Fact]
    public async Task AddServer_UpdatesExistingServer()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer
        {
            Id = "test-server",
            Name = "Original Name",
            Hostname = "irc.example.com",
            Port = 6667
        };

        service.AddServer(server);

        // Update the server
        server.Name = "Updated Name";
        server.Port = 6697;
        service.AddServer(server);

        service.Configuration.Servers.Should().HaveCount(1);
        service.Configuration.Servers[0].Name.Should().Be("Updated Name");
        service.Configuration.Servers[0].Port.Should().Be(6697);
    }

    [Fact]
    public async Task RemoveServer_RemovesServerFromConfiguration()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer { Id = "test-server", Name = "Test" };
        service.AddServer(server);

        service.RemoveServer("test-server");

        service.Configuration.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task AddChannelToServer_AddsChannelToAutoJoinList()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer { Id = "test-server", Name = "Test" };
        service.AddServer(server);

        service.AddChannelToServer("test-server", "#general");

        var savedServer = service.Configuration.Servers[0];
        savedServer.AutoJoinChannels.Should().Contain("#general");
    }

    [Fact]
    public async Task AddChannelToServer_DoesNotAddDuplicates()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer { Id = "test-server", Name = "Test" };
        service.AddServer(server);

        service.AddChannelToServer("test-server", "#general");
        service.AddChannelToServer("test-server", "#general");
        service.AddChannelToServer("test-server", "#GENERAL"); // Case insensitive

        var savedServer = service.Configuration.Servers[0];
        savedServer.AutoJoinChannels.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveChannelFromServer_RemovesChannelFromAutoJoinList()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer
        {
            Id = "test-server",
            Name = "Test",
            AutoJoinChannels = new List<string> { "#general", "#random" }
        };
        service.AddServer(server);

        service.RemoveChannelFromServer("test-server", "#general");

        var savedServer = service.Configuration.Servers[0];
        savedServer.AutoJoinChannels.Should().NotContain("#general");
        savedServer.AutoJoinChannels.Should().Contain("#random");
    }

    [Fact]
    public async Task GetServerById_ReturnsCorrectServer()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer
        {
            Id = "test-server",
            Name = "Test Server",
            Hostname = "irc.example.com"
        };
        service.AddServer(server);

        var retrieved = service.GetServerById("test-server");

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test Server");
        retrieved.Hostname.Should().Be("irc.example.com");
    }

    [Fact]
    public async Task GetServerById_ReturnsNullForNonExistentServer()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var retrieved = service.GetServerById("nonexistent");

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetAllServers_ReturnsAllServers()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        service.AddServer(new IrcServer { Id = "server1", Name = "Server 1" });
        service.AddServer(new IrcServer { Id = "server2", Name = "Server 2" });
        service.AddServer(new IrcServer { Id = "server3", Name = "Server 3" });

        var servers = service.GetAllServers();

        servers.Should().HaveCount(3);
        servers.Select(s => s.Id).Should().Contain(new[] { "server1", "server2", "server3" });
    }

    [Fact]
    public async Task SaveAndLoad_PersistsConfiguration()
    {
        var server = new IrcServer
        {
            Id = "test-server",
            Name = "Test Server",
            Hostname = "irc.example.com",
            Port = 6697,
            UseSsl = true,
            Nickname = "TestNick",
            AutoJoinChannels = new List<string> { "#general", "#random" }
        };

        // Save
        var saveService = new ConfigurationService(_testDir);
        saveService.Storage.Unlock("test123");
        saveService.AddServer(server);
        await saveService.SaveAsync();

        // Load
        var loadService = new ConfigurationService(_testDir);
        loadService.Storage.Unlock("test123");
        await loadService.LoadAsync();

        var loaded = loadService.GetServerById("test-server");
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Test Server");
        loaded.Port.Should().Be(6697);
        loaded.UseSsl.Should().BeTrue();
        loaded.AutoJoinChannels.Should().Contain("#general");
        loaded.AutoJoinChannels.Should().Contain("#random");
    }

    [Fact]
    public async Task LoadAsync_ThrowsIfStorageLockedWithEncryption()
    {
        var service = new ConfigurationService(_testDir);
        // Enable encryption first
        await service.Storage.EnableEncryptionAsync("test123");
        service.Storage.Lock();
        // Now storage is locked

        var act = async () => await service.LoadAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*locked*");
    }

    [Fact]
    public async Task SaveAsync_SkipsIfStorageLockedWithEncryption()
    {
        var service = new ConfigurationService(_testDir);
        // Enable encryption and lock it
        await service.Storage.EnableEncryptionAsync("test123");
        service.Storage.Lock();

        // Should not throw, but should skip saving
        await service.SaveAsync();

        // File should not be created when locked
        service.Storage.FileExists("config.json").Should().BeFalse();
    }

    [Fact]
    public async Task AddServerGroup_AddsGroupToConfiguration()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var group = new ServerGroup
        {
            Id = "work-servers",
            Name = "Work Servers",
            SortOrder = 1,
            IsCollapsed = true
        };

        service.AddServerGroup(group);

        var groups = service.GetServerGroups();
        groups.Should().HaveCount(1);
        groups[0].Name.Should().Be("Work Servers");
    }

    [Fact]
    public async Task RemoveServerGroup_RemovesGroupAndClearsServerAssignments()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var group = new ServerGroup { Id = "work-servers", Name = "Work" };
        service.AddServerGroup(group);

        var server = new IrcServer
        {
            Id = "server1",
            Name = "Server 1",
            Group = "work-servers"
        };
        service.AddServer(server);

        service.RemoveServerGroup("work-servers", moveServersToUngrouped: true);

        service.GetServerGroups().Should().BeEmpty();
        service.GetServerById("server1")!.Group.Should().BeNull();
    }

    [Fact]
    public async Task UpdateServerChannels_UpdatesAutoJoinList()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        var server = new IrcServer
        {
            Id = "test-server",
            Name = "Test",
            AutoJoinChannels = new List<string> { "#old" }
        };
        service.AddServer(server);

        service.UpdateServerChannels("test-server", new List<string> { "#new1", "#new2" });

        var updated = service.GetServerById("test-server");
        updated!.AutoJoinChannels.Should().NotContain("#old");
        updated.AutoJoinChannels.Should().Contain("#new1");
        updated.AutoJoinChannels.Should().Contain("#new2");
    }

    [Fact]
    public async Task IsEncryptionEnabled_ReflectsStorageState()
    {
        var service = new ConfigurationService(_testDir);

        service.IsEncryptionEnabled.Should().BeFalse();

        await service.Storage.EnableEncryptionAsync("test123");

        service.IsEncryptionEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsUnlocked_ReflectsStorageState()
    {
        var service = new ConfigurationService(_testDir);
        // When encryption is not enabled, storage is always unlocked
        service.IsUnlocked.Should().BeTrue();

        // Enable encryption and lock it
        await service.Storage.EnableEncryptionAsync("test123");
        service.Storage.Lock();

        service.IsUnlocked.Should().BeFalse();

        service.Storage.Unlock("test123");

        service.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Configuration_SupportsMultipleServersWithDifferentSettings()
    {
        var service = new ConfigurationService(_testDir);
        service.Storage.Unlock("test123");

        service.AddServer(new IrcServer
        {
            Id = "libera",
            Name = "Libera.Chat",
            Hostname = "irc.libera.chat",
            Port = 6697,
            UseSsl = true,
            AutoConnect = true
        });

        service.AddServer(new IrcServer
        {
            Id = "oftc",
            Name = "OFTC",
            Hostname = "irc.oftc.net",
            Port = 6667,
            UseSsl = false,
            AutoConnect = false
        });

        await service.SaveAsync();

        var loadService = new ConfigurationService(_testDir);
        loadService.Storage.Unlock("test123");
        await loadService.LoadAsync();

        var servers = loadService.GetAllServers();
        servers.Should().HaveCount(2);

        var libera = servers.First(s => s.Id == "libera");
        libera.Port.Should().Be(6697);
        libera.UseSsl.Should().BeTrue();
        libera.AutoConnect.Should().BeTrue();

        var oftc = servers.First(s => s.Id == "oftc");
        oftc.Port.Should().Be(6667);
        oftc.UseSsl.Should().BeFalse();
        oftc.AutoConnect.Should().BeFalse();
    }
}
