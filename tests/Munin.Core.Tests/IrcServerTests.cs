using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for IrcServer - connection configuration, state management, runtime properties
/// </summary>
public class IrcServerTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        // Act
        var server = new IrcServer();

        // Assert
        server.Id.Should().NotBeNullOrEmpty();
        server.Name.Should().BeEmpty();
        server.Hostname.Should().BeEmpty();
        server.Port.Should().Be(6667);
        server.UseSsl.Should().BeFalse();
        server.State.Should().Be(ConnectionState.Disconnected);
        server.Channels.Should().BeEmpty();
        server.AutoJoinChannels.Should().BeEmpty();
        server.ConnectedAt.Should().BeNull();
    }

    [Fact]
    public void Id_IsUniqueForEachInstance()
    {
        // Act
        var server1 = new IrcServer();
        var server2 = new IrcServer();

        // Assert
        server1.Id.Should().NotBe(server2.Id);
    }

    [Fact]
    public void ConnectionProperties_CanBeConfigured()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.Name = "Libera.Chat";
        server.Hostname = "irc.libera.chat";
        server.Port = 6697;
        server.UseSsl = true;

        // Assert
        server.Name.Should().Be("Libera.Chat");
        server.Hostname.Should().Be("irc.libera.chat");
        server.Port.Should().Be(6697);
        server.UseSsl.Should().BeTrue();
    }

    [Fact]
    public void UserIdentity_CanBeConfigured()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.Nickname = "TestUser";
        server.Username = "testuser";
        server.RealName = "Test User";

        // Assert
        server.Nickname.Should().Be("TestUser");
        server.Username.Should().Be("testuser");
        server.RealName.Should().Be("Test User");
    }

    [Fact]
    public void Passwords_CanBeSet()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.Password = "server_password";
        server.NickServPassword = "nickserv_password";

        // Assert
        server.Password.Should().Be("server_password");
        server.NickServPassword.Should().Be("nickserv_password");
    }

    [Fact]
    public void AutoJoinChannels_CanBeManaged()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.AutoJoinChannels.Add("#general");
        server.AutoJoinChannels.Add("#help");
        server.AutoJoinChannels.Add("#dev");

        // Assert
        server.AutoJoinChannels.Should().HaveCount(3);
        server.AutoJoinChannels.Should().Contain(new[] { "#general", "#help", "#dev" });
    }

    [Fact]
    public void State_CanBeUpdated()
    {
        // Arrange
        var server = new IrcServer();

        // Act & Assert
        server.State = ConnectionState.Connecting;
        server.State.Should().Be(ConnectionState.Connecting);

        server.State = ConnectionState.Connected;
        server.State.Should().Be(ConnectionState.Connected);

        server.State = ConnectionState.Reconnecting;
        server.State.Should().Be(ConnectionState.Reconnecting);

        server.State = ConnectionState.Disconnected;
        server.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public void ConnectedAt_TracksConnectionTime()
    {
        // Arrange
        var server = new IrcServer();
        var beforeConnect = DateTime.Now;

        // Act
        server.ConnectedAt = DateTime.Now;

        // Assert
        server.ConnectedAt.Should().NotBeNull();
        server.ConnectedAt.Should().BeOnOrAfter(beforeConnect);
    }

    [Fact]
    public void Channels_CanBeManaged()
    {
        // Arrange
        var server = new IrcServer();
        var channel1 = new IrcChannel { Name = "#channel1" };
        var channel2 = new IrcChannel { Name = "#channel2" };

        // Act
        server.Channels.Add(channel1);
        server.Channels.Add(channel2);

        // Assert
        server.Channels.Should().HaveCount(2);
        server.Channels.Should().Contain(channel1);
        server.Channels.Should().Contain(channel2);
    }

    [Fact]
    public void ISupport_IsInitialized()
    {
        // Arrange & Act
        var server = new IrcServer();

        // Assert
        server.ISupport.Should().NotBeNull();
    }

    [Fact]
    public void Capabilities_IsInitialized()
    {
        // Arrange & Act
        var server = new IrcServer();

        // Assert
        server.Capabilities.Should().NotBeNull();
    }

    [Fact]
    public void SaslCredentials_CanBeSet()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.SaslUsername = "sasluser";
        server.SaslPassword = "saslpass";

        // Assert
        server.SaslUsername.Should().Be("sasluser");
        server.SaslPassword.Should().Be("saslpass");
    }

    [Fact]
    public void ClientCertificate_CanBeConfigured()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.UseClientCertificate = true;
        server.ClientCertificatePath = "/path/to/cert.pfx";
        server.ClientCertificatePassword = "certpass";

        // Assert
        server.UseClientCertificate.Should().BeTrue();
        server.ClientCertificatePath.Should().Be("/path/to/cert.pfx");
        server.ClientCertificatePassword.Should().Be("certpass");
    }

    [Fact]
    public void ProxySettings_CanBeConfigured()
    {
        // Arrange
        var server = new IrcServer();
        var proxy = new ProxySettings
        {
            Type = ProxyType.SOCKS5,
            Host = "proxy.example.com",
            Port = 1080,
            Username = "proxyuser",
            Password = "proxypass"
        };

        // Act
        server.Proxy = proxy;

        // Assert
        server.Proxy.Should().NotBeNull();
        server.Proxy!.Type.Should().Be(ProxyType.SOCKS5);
        server.Proxy.Host.Should().Be("proxy.example.com");
        server.Proxy.Port.Should().Be(1080);
        server.Proxy.Username.Should().Be("proxyuser");
        server.Proxy.Password.Should().Be("proxypass");
    }

    [Fact]
    public void IPv6_PreferenceCanBeSet()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.PreferIPv6 = true;

        // Assert
        server.PreferIPv6.Should().BeTrue();
        server.IsIPv6Connected.Should().BeFalse(); // Not connected yet
    }

    [Fact]
    public void IPv6Connected_TracksActiveConnectionType()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.IsIPv6Connected = true;

        // Assert
        server.IsIPv6Connected.Should().BeTrue();
    }

    [Fact]
    public void GroupAndSortOrder_CanBeSet()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.Group = "Personal";
        server.SortOrder = 5;

        // Assert
        server.Group.Should().Be("Personal");
        server.SortOrder.Should().Be(5);
    }

    [Fact]
    public void Bouncer_CanBeConfigured()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.IsBouncer = true;
        server.SuppressPlaybackNotifications = true;

        // Assert
        server.IsBouncer.Should().BeTrue();
        server.SuppressPlaybackNotifications.Should().BeTrue();
        server.IsBouncerDetected.Should().BeFalse();
        server.IsReceivingPlayback.Should().BeFalse();
    }

    [Fact]
    public void BouncerDetection_CanBeSet()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.IsBouncerDetected = true;
        server.IsReceivingPlayback = true;

        // Assert
        server.IsBouncerDetected.Should().BeTrue();
        server.IsReceivingPlayback.Should().BeTrue();
    }

    [Fact]
    public void RelaySettings_CanBeConfigured()
    {
        // Arrange
        var server = new IrcServer();
        var relay = new RelaySettings
        {
            Enabled = true,
            Host = "relay.example.com",
            Port = 6900,
            AuthToken = "secret_token",
            UseSsl = true,
            AcceptInvalidCertificates = false
        };

        // Act
        server.Relay = relay;

        // Assert
        server.Relay.Should().NotBeNull();
        server.Relay!.Enabled.Should().BeTrue();
        server.Relay.Host.Should().Be("relay.example.com");
        server.Relay.Port.Should().Be(6900);
        server.Relay.AuthToken.Should().Be("secret_token");
        server.Relay.UseSsl.Should().BeTrue();
        server.Relay.AcceptInvalidCertificates.Should().BeFalse();
    }

    [Fact]
    public void AcceptInvalidCertificates_DefaultsFalse()
    {
        // Act
        var server = new IrcServer();

        // Assert
        server.AcceptInvalidCertificates.Should().BeFalse();
    }

    [Fact]
    public void AutoConnect_CanBeEnabled()
    {
        // Arrange
        var server = new IrcServer();

        // Act
        server.AutoConnect = true;

        // Assert
        server.AutoConnect.Should().BeTrue();
    }

    [Fact]
    public void ProxySettings_DefaultsToNone()
    {
        // Act
        var proxy = new ProxySettings();

        // Assert
        proxy.Type.Should().Be(ProxyType.None);
        proxy.Host.Should().BeEmpty();
        proxy.Port.Should().Be(0);
    }

    [Fact]
    public void RelaySettings_HasDefaultValues()
    {
        // Act
        var relay = new RelaySettings();

        // Assert
        relay.Enabled.Should().BeFalse();
        relay.Host.Should().BeEmpty();
        relay.Port.Should().Be(6900);
        relay.UseSsl.Should().BeTrue();
        relay.AcceptInvalidCertificates.Should().BeTrue();
    }

    [Fact]
    public void ConnectionState_AllStatesAvailable()
    {
        // Assert - Verify all states exist
        var states = Enum.GetValues<ConnectionState>();
        states.Should().Contain(ConnectionState.Disconnected);
        states.Should().Contain(ConnectionState.Connecting);
        states.Should().Contain(ConnectionState.Connected);
        states.Should().Contain(ConnectionState.Reconnecting);
    }

    [Fact]
    public void ProxyType_AllTypesAvailable()
    {
        // Assert - Verify all proxy types exist
        var types = Enum.GetValues<ProxyType>();
        types.Should().Contain(ProxyType.None);
        types.Should().Contain(ProxyType.SOCKS4);
        types.Should().Contain(ProxyType.SOCKS5);
        types.Should().Contain(ProxyType.HTTP);
    }
}
