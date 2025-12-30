using FluentAssertions;
using Munin.Core.Models;
using Munin.Core.Services;
using Munin.Core.Tests.Helpers;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for MockIrcServer to verify it works correctly.
/// </summary>
public class MockIrcServerTests : IDisposable
{
    private MockIrcServer? _mockServer;

    public void Dispose()
    {
        _mockServer?.Dispose();
    }

    [Fact]
    public async Task MockIrcServer_AcceptsConnection()
    {
        // Arrange
        _mockServer = new MockIrcServer();
        await _mockServer.StartAsync();

        // Act
        using var tcpClient = new System.Net.Sockets.TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", _mockServer.Port);

        // Assert
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(1));
        _mockServer.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task MockIrcServer_ReceivesMessages()
    {
        // Arrange
        _mockServer = new MockIrcServer();
        await _mockServer.StartAsync();

        using var tcpClient = new System.Net.Sockets.TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", _mockServer.Port);
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(1));

        var stream = tcpClient.GetStream();
        var writer = new StreamWriter(stream, System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
            NewLine = "\r\n"
        };

        // Act
        await writer.WriteLineAsync("NICK TestUser");
        await Task.Delay(100);

        // Assert
        _mockServer.ReceivedMessages.Should().Contain("NICK TestUser");
    }

    [Fact]
    public async Task MockIrcServer_WaitForMessageAsync_FindsExistingMessage()
    {
        // Arrange
        _mockServer = new MockIrcServer();
        await _mockServer.StartAsync();

        using var tcpClient = new System.Net.Sockets.TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", _mockServer.Port);
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(1));

        var stream = tcpClient.GetStream();
        var writer = new StreamWriter(stream, System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync("NICK TestUser");
        await Task.Delay(200); // Wait for message to be received

        // Act
        var nickMsg = await _mockServer.WaitForMessageAsync(
            msg => msg.StartsWith("NICK "),
            TimeSpan.FromSeconds(2));

        // Assert
        nickMsg.Should().Be("NICK TestUser");
    }

    [Fact]
    public async Task IrcConnection_SendsMessages_ToMockServer()
    {
        // Arrange
        _mockServer = new MockIrcServer();
        await _mockServer.StartAsync();

        var server = new IrcServer
        {
            Hostname = "127.0.0.1",
            Port = _mockServer.Port,
            Nickname = "TestUser",
            Username = "testuser",
            RealName = "Test User",
            UseSsl = false
        };

        var connection = new IrcConnection(server);

        // Act
        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));

        // Wait a bit for messages to arrive
        await Task.Delay(500);

        Console.WriteLine($"Received {_mockServer.ReceivedMessages.Count} messages:");
        foreach (var msg in _mockServer.ReceivedMessages)
        {
            Console.WriteLine($"  {msg}");
        }

        // Send CAP response and greeting
        await _mockServer.SendAsync(":server CAP * LS :");
        await _mockServer.SendAsync(":server CAP TestUser END");
        await _mockServer.SendServerGreetingAsync("TestUser");

        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        var nickMsg = _mockServer.ReceivedMessages.FirstOrDefault(m => m.StartsWith("NICK "));
        var userMsg = _mockServer.ReceivedMessages.FirstOrDefault(m => m.StartsWith("USER "));

        nickMsg.Should().NotBeNull("NICK command should be sent");
        userMsg.Should().NotBeNull("USER command should be sent");
        
        Console.WriteLine($"NICK: {nickMsg}");
        Console.WriteLine($"USER: {userMsg}");
    }
}
