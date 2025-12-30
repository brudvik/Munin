using FluentAssertions;
using Munin.Core.Models;
using Munin.Core.Services;
using Munin.Core.Tests.Helpers;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Integration tests for IrcConnection.
/// Tests actual TCP connection behavior with a mock IRC server.
/// </summary>
[Collection("Sequential")] // Run tests sequentially to avoid port conflicts
public class IrcConnectionTests : IDisposable
{
    private readonly MockIrcServer _mockServer;
    private readonly IrcServer _testServer;

    public IrcConnectionTests()
    {
        _mockServer = new MockIrcServer();
        _testServer = new IrcServer
        {
            Name = "TestServer",
            Hostname = "127.0.0.1",
            Port = _mockServer.Port,
            UseSsl = false,
            Nickname = "TestUser",
            Username = "testuser",
            RealName = "Test User"
        };
    }

    #region Connection Tests

    [Fact]
    public async Task ConnectAsync_SuccessfulConnection_RaisesConnectedEvent()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        var connected = false;

        connection.Connected += (s, e) => connected = true;

        // Start mock server
        await _mockServer.StartAsync();

        // Act
        var connectTask = connection.ConnectAsync();

        // Wait for client to connect
        var clientConnected = await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        clientConnected.Should().BeTrue();
        
        // Give time for CAP negotiation
        await Task.Delay(200);

        // Server sends greeting
        await _mockServer.SendServerGreetingAsync("TestUser");
        
        // Give time for connection to be established
        await Task.Delay(200);

        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        connection.IsConnected.Should().BeTrue();
        connected.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_SendsNickAndUser_Commands()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        await _mockServer.StartAsync();

        // Act
        var connectTask = connection.ConnectAsync();

        // Wait for client to connect
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation and NICK/USER to be sent
        await Task.Delay(200);

        // Check that NICK and USER were sent
        var nickMsg = _mockServer.ReceivedMessages.FirstOrDefault(m => m.StartsWith("NICK "));
        var userMsg = _mockServer.ReceivedMessages.FirstOrDefault(m => m.StartsWith("USER "));

        // Send greeting to complete connection
        await _mockServer.SendServerGreetingAsync("TestUser");

        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        nickMsg.Should().Be("NICK TestUser");
        userMsg.Should().StartWith("USER testuser");
    }

    [Fact]
    public async Task ConnectAsync_WithPassword_SendsPassCommand()
    {
        // Arrange
        _testServer.Password = "secret123";
        var connection = new IrcConnection(_testServer);
        await _mockServer.StartAsync();

        // Start waiting for PASS command before connecting to avoid race condition
        var passTask = Task.Run(async () =>
        {
            // Wait a bit for connection to be established
            await Task.Delay(100);
            return await _mockServer.WaitForMessageAsync(
                msg => msg.StartsWith("PASS "),
                TimeSpan.FromSeconds(5));
        });

        // Act
        var connectTask = connection.ConnectAsync();

        // Wait for client to connect
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));

        var passMsg = await passTask;

        await _mockServer.SendServerGreetingAsync("TestUser");

        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        passMsg.Should().Be("PASS secret123");
    }

    [Fact]
    public async Task ConnectAsync_ServerNotListening_ThrowsException()
    {
        // Arrange - Don't start mock server, use a port that's not listening
        var badServer = new IrcServer
        {
            Name = "BadServer",
            Hostname = "127.0.0.1",
            Port = 19999, // Random high port that should not be in use
            UseSsl = false,
            Nickname = "TestUser",
            Username = "testuser",
            RealName = "Test User"
        };
        var connection = new IrcConnection(badServer);

        // Act & Assert
        var act = async () => await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task IsConnected_WhenNotConnected_ReturnsFalse()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);

        // Assert
        connection.IsConnected.Should().BeFalse();
    }

    #endregion

    #region Disconnection Tests

    [Fact]
    public async Task DisconnectAsync_WhenConnected_SendsQuitCommand()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        await _mockServer.StartAsync();

        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - use WaitForMessageAsync to avoid race conditions with message buffering
        var quitTask = _mockServer.WaitForMessageAsync(
            msg => msg.StartsWith("QUIT"),
            TimeSpan.FromSeconds(5));
            
        await connection.DisconnectAsync("Goodbye!");

        var quitMsg = await quitTask;

        // Assert
        quitMsg.Should().Contain("Goodbye!");
        connection.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task DisconnectAsync_RaisesDisconnectedEvent()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        var disconnected = false;

        connection.Disconnected += (s, e) => disconnected = true;

        await _mockServer.StartAsync();
        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await connection.DisconnectAsync();

        // Assert
        disconnected.Should().BeTrue();
    }

    #endregion

    #region Message Sending Tests

    [Fact]
    public async Task SendRawAsync_SendsMessageToServer()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        await _mockServer.StartAsync();

        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - use WaitForMessageAsync to avoid race conditions with message buffering
        var messageTask = _mockServer.WaitForMessageAsync(
            msg => msg == "PING :test",
            TimeSpan.FromSeconds(5));
            
        await connection.SendRawAsync("PING :test");

        var receivedMsg = await messageTask;

        // Assert
        receivedMsg.Should().Be("PING :test");
    }

    [Fact]
    public async Task SendMessageAsync_SendsPrivmsgCommand()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        await _mockServer.StartAsync();

        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - use WaitForMessageAsync to avoid race conditions with message buffering
        var messageTask = _mockServer.WaitForMessageAsync(
            msg => msg == "PRIVMSG #test :Hello, world!",
            TimeSpan.FromSeconds(5));
            
        await connection.SendMessageAsync("#test", "Hello, world!");

        var receivedMsg = await messageTask;

        // Assert
        receivedMsg.Should().Be("PRIVMSG #test :Hello, world!");
    }

    [Fact]
    public async Task JoinChannelAsync_SendsJoinCommand()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        await _mockServer.StartAsync();

        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - use WaitForMessageAsync to avoid race conditions with message buffering
        var messageTask = _mockServer.WaitForMessageAsync(
            msg => msg == "JOIN #testchannel",
            TimeSpan.FromSeconds(5));
            
        await connection.JoinChannelAsync("#testchannel");

        var receivedMsg = await messageTask;

        // Assert
        receivedMsg.Should().Be("JOIN #testchannel");
    }

    [Fact]
    public async Task JoinChannelAsync_WithKey_SendsJoinWithKey()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        await _mockServer.StartAsync();

        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - use WaitForMessageAsync to avoid race conditions with message buffering
        var messageTask = _mockServer.WaitForMessageAsync(
            msg => msg == "JOIN #secret password123",
            TimeSpan.FromSeconds(5));
            
        await connection.JoinChannelAsync("#secret", "password123");

        var receivedMsg = await messageTask;

        // Assert
        receivedMsg.Should().Be("JOIN #secret password123");
    }

    #endregion

    #region Message Receiving Tests

    [Fact]
    public async Task ReceiveMessage_PrivmsgFromUser_RaisesChannelMessageEvent()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        string? receivedMessage = null;
        string? receivedChannel = null;

        connection.ChannelMessage += (s, e) =>
        {
            receivedMessage = e.Message.Content;
            receivedChannel = e.Channel.Name;
        };

        await _mockServer.StartAsync();
        var connectTask = connection.ConnectAsync();
        
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Simulate joining #test channel first
        await _mockServer.SendAsync(":TestUser!testuser@host JOIN #test");
        await Task.Delay(200);

        // Act
        await _mockServer.SendAsync(":alice!user@host PRIVMSG #test :Hello everyone!");
        await Task.Delay(700); // Give time for message processing

        // Assert
        receivedMessage.Should().Be("Hello everyone!");
        receivedChannel.Should().Be("#test");
    }

    [Fact]
    public async Task ReceiveMessage_PrivateMessage_RaisesPrivateMessageEvent()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        string? receivedMessage = null;
        string? receivedNick = null;

        connection.PrivateMessage += (s, e) =>
        {
            receivedMessage = e.Message.Content;
            receivedNick = e.From;
        };

        await _mockServer.StartAsync();
        var connectTask = connection.ConnectAsync();
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await _mockServer.SendAsync(":bob!user@host PRIVMSG TestUser :Private message");
        await Task.Delay(700); // Give time for message processing

        // Assert
        receivedMessage.Should().Be("Private message");
        receivedNick.Should().Be("bob");
    }

    [Fact]
    public async Task ReceiveMessage_JoinMessage_RaisesUserJoinedEvent()
    {
        // Arrange
        var connection = new IrcConnection(_testServer);
        string? joinedNick = null;
        string? joinedChannel = null;

        connection.UserJoined += (s, e) =>
        {
            joinedNick = e.User.Nickname;
            joinedChannel = e.Channel?.Name;
        };

        await _mockServer.StartAsync();
        var connectTask = connection.ConnectAsync();
        
        await _mockServer.WaitForClientAsync(TimeSpan.FromSeconds(5));
        
        // Give time for CAP negotiation
        await Task.Delay(200);
        
        await _mockServer.SendServerGreetingAsync("TestUser");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // First, user must join the channel to track it
        await _mockServer.SendAsync(":TestUser!testuser@host JOIN #general");
        await Task.Delay(200);

        // Act - Another user joins
        await _mockServer.SendAsync(":charlie!user@host JOIN #general");
        await Task.Delay(700); // Give time for event processing

        // Assert
        joinedNick.Should().Be("charlie");
        joinedChannel.Should().Be("#general");
    }

    #endregion

    public void Dispose()
    {
        _mockServer?.Dispose();
    }
}
