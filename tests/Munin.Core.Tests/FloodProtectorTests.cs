using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for FloodProtector service.
/// Verifies token bucket algorithm, rate limiting, and queue management.
/// </summary>
public class FloodProtectorTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var protector = new FloodProtector();
        
        protector.IsEnabled.Should().BeTrue();
        protector.QueueDepth.Should().Be(0);
    }

    [Fact]
    public void Constructor_AcceptsCustomParameters()
    {
        var protector = new FloodProtector(maxTokens: 10, refillRate: 2, refillIntervalMs: 500);
        
        protector.IsEnabled.Should().BeTrue();
        protector.QueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenDisabled_SendsImmediately()
    {
        var protector = new FloodProtector();
        protector.IsEnabled = false;
        
        var commandSent = "";
        protector.SendCommandCallback = async (cmd) =>
        {
            commandSent = cmd;
            await Task.CompletedTask;
        };
        
        await protector.SendAsync("PRIVMSG #test :Hello");
        
        commandSent.Should().Be("PRIVMSG #test :Hello");
        protector.QueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WithinBurst_SendsImmediately()
    {
        var protector = new FloodProtector(maxTokens: 5);
        var commandsSent = new List<string>();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            commandsSent.Add(cmd);
            await Task.CompletedTask;
        };
        
        // Send within burst capacity
        await protector.SendAsync("COMMAND1");
        await protector.SendAsync("COMMAND2");
        await protector.SendAsync("COMMAND3");
        
        commandsSent.Should().HaveCount(3);
        commandsSent.Should().Contain("COMMAND1");
        commandsSent.Should().Contain("COMMAND2");
        commandsSent.Should().Contain("COMMAND3");
    }

    [Fact]
    public async Task SendAsync_ExceedingBurst_QueuesAndWaits()
    {
        var protector = new FloodProtector(maxTokens: 2, refillRate: 1, refillIntervalMs: 100);
        var commandsSent = new List<string>();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            commandsSent.Add(cmd);
            await Task.CompletedTask;
        };
        
        // Send more than burst capacity
        var task1 = protector.SendAsync("CMD1");
        var task2 = protector.SendAsync("CMD2");
        var task3 = protector.SendAsync("CMD3");
        
        await Task.WhenAll(task1, task2, task3);
        
        commandsSent.Should().HaveCount(3);
    }

    [Fact]
    public void QueueSend_AddsToQueue()
    {
        var protector = new FloodProtector(maxTokens: 2);
        var commandsSent = new List<string>();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            commandsSent.Add(cmd);
            await Task.CompletedTask;
        };
        
        protector.QueueSend("CMD1");
        protector.QueueSend("CMD2");
        protector.QueueSend("CMD3");
        
        // Allow time for processing
        Thread.Sleep(50);
        
        commandsSent.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void QueueSend_WhenDisabled_SendsImmediately()
    {
        var protector = new FloodProtector();
        protector.IsEnabled = false;
        
        var commandSent = "";
        protector.SendCommandCallback = async (cmd) =>
        {
            commandSent = cmd;
            await Task.CompletedTask;
        };
        
        protector.QueueSend("TEST");
        
        Thread.Sleep(10);
        commandSent.Should().Be("TEST");
    }

    [Fact]
    public async Task SendAsync_MultipleCommands_RespectsRateLimit()
    {
        var protector = new FloodProtector(maxTokens: 3, refillRate: 1, refillIntervalMs: 50);
        var commandsSent = new List<string>();
        var timestamps = new List<DateTime>();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            commandsSent.Add(cmd);
            timestamps.Add(DateTime.UtcNow);
            await Task.CompletedTask;
        };
        
        // Send 5 commands (exceeds burst of 3)
        var tasks = Enumerable.Range(1, 5).Select(i => protector.SendAsync($"CMD{i}")).ToArray();
        await Task.WhenAll(tasks);
        
        commandsSent.Should().HaveCount(5);
        
        // Verify rate limiting occurred (timestamps should be spaced out)
        if (timestamps.Count >= 4)
        {
            var timeDiff = timestamps[3] - timestamps[2];
            timeDiff.TotalMilliseconds.Should().BeGreaterThan(30); // Some delay should exist
        }
    }

    [Fact]
    public void QueueDepth_TracksQueuedCommands()
    {
        var protector = new FloodProtector(maxTokens: 1);
        var sendCount = 0;
        
        protector.SendCommandCallback = async (cmd) =>
        {
            await Task.Delay(100); // Slow callback to accumulate queue
            sendCount++;
        };
        
        protector.QueueSend("CMD1");
        protector.QueueSend("CMD2");
        protector.QueueSend("CMD3");
        
        Thread.Sleep(10);
        protector.QueueDepth.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Reset_ClearsQueueAndResetTokens()
    {
        var protector = new FloodProtector(maxTokens: 1);
        
        protector.SendCommandCallback = async (cmd) =>
        {
            await Task.Delay(1000); // Long delay to keep in queue
        };
        
        protector.QueueSend("CMD1");
        protector.QueueSend("CMD2");
        protector.QueueSend("CMD3");
        
        Thread.Sleep(10);
        var queueBefore = protector.QueueDepth;
        
        protector.Reset();
        
        queueBefore.Should().BeGreaterThan(0);
        protector.QueueDepth.Should().Be(0);
    }

    [Fact]
    public void Stop_StopsProcessing()
    {
        var protector = new FloodProtector(maxTokens: 1);
        var sendCount = 0;
        
        protector.SendCommandCallback = async (cmd) =>
        {
            await Task.Delay(50);
            sendCount++;
        };
        
        protector.QueueSend("CMD1");
        protector.QueueSend("CMD2");
        protector.QueueSend("CMD3");
        
        Thread.Sleep(10);
        protector.Stop();
        Thread.Sleep(200);
        
        // After stop, not all commands should be sent
        sendCount.Should().BeLessThan(3);
    }

    [Fact]
    public async Task SendAsync_WithNullCallback_DoesNotThrow()
    {
        var protector = new FloodProtector();
        protector.SendCommandCallback = null;
        
        var act = async () => await protector.SendAsync("TEST");
        
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_CallbackThrowsException_CompletesWithException()
    {
        var protector = new FloodProtector();
        protector.SendCommandCallback = async (cmd) =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("Test error");
        };
        
        var act = async () => await protector.SendAsync("TEST");
        
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendAsync_TokenRefill_AllowsMoreCommands()
    {
        var protector = new FloodProtector(maxTokens: 2, refillRate: 1, refillIntervalMs: 100);
        var commandsSent = new List<string>();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            commandsSent.Add(cmd);
            await Task.CompletedTask;
        };
        
        // Use up burst
        await protector.SendAsync("CMD1");
        await protector.SendAsync("CMD2");
        
        commandsSent.Should().HaveCount(2);
        
        // Wait for refill
        await Task.Delay(150);
        
        // Should be able to send more
        await protector.SendAsync("CMD3");
        
        commandsSent.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendAsync_ConcurrentCalls_HandledSafely()
    {
        var protector = new FloodProtector(maxTokens: 10, refillRate: 5, refillIntervalMs: 100);
        var commandsSent = new List<string>();
        var lockObj = new object();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            lock (lockObj)
            {
                commandsSent.Add(cmd);
            }
            await Task.Delay(1);
        };
        
        // Send 20 commands concurrently
        var tasks = Enumerable.Range(1, 20).Select(i => protector.SendAsync($"CMD{i}")).ToArray();
        await Task.WhenAll(tasks);
        
        commandsSent.Should().HaveCount(20);
        commandsSent.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SendAsync_LargeQueue_ProcessesInOrder()
    {
        var protector = new FloodProtector(maxTokens: 5, refillRate: 10, refillIntervalMs: 50);
        var commandsSent = new List<string>();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            commandsSent.Add(cmd);
            await Task.CompletedTask;
        };
        
        var commands = Enumerable.Range(1, 15).Select(i => $"CMD{i}").ToList();
        var tasks = commands.Select(cmd => protector.SendAsync(cmd)).ToArray();
        
        await Task.WhenAll(tasks);
        
        commandsSent.Should().HaveCount(15);
        // Commands should be processed in order
        for (int i = 0; i < commands.Count; i++)
        {
            commandsSent[i].Should().Be(commands[i]);
        }
    }

    [Fact]
    public void IsEnabled_CanBeToggled()
    {
        var protector = new FloodProtector();
        
        protector.IsEnabled.Should().BeTrue();
        
        protector.IsEnabled = false;
        protector.IsEnabled.Should().BeFalse();
        
        protector.IsEnabled = true;
        protector.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_HighThroughput_HandlesGracefully()
    {
        var protector = new FloodProtector(maxTokens: 20, refillRate: 50, refillIntervalMs: 10);
        var commandsSent = 0;
        
        protector.SendCommandCallback = async (cmd) =>
        {
            Interlocked.Increment(ref commandsSent);
            await Task.CompletedTask;
        };
        
        // Send 100 commands rapidly
        var tasks = Enumerable.Range(1, 100).Select(i => protector.SendAsync($"CMD{i}")).ToArray();
        await Task.WhenAll(tasks);
        
        commandsSent.Should().Be(100);
    }

    [Fact]
    public async Task SendAsync_ZeroRefillRate_OnlyAllowsBurst()
    {
        var protector = new FloodProtector(maxTokens: 3, refillRate: 0, refillIntervalMs: 100);
        var commandsSent = new List<string>();
        
        protector.SendCommandCallback = async (cmd) =>
        {
            commandsSent.Add(cmd);
            await Task.CompletedTask;
        };
        
        // Send burst
        await protector.SendAsync("CMD1");
        await protector.SendAsync("CMD2");
        await protector.SendAsync("CMD3");
        
        commandsSent.Should().HaveCount(3);
        
        // Start 4th command (should queue forever with no refill)
        var task4 = protector.SendAsync("CMD4");
        
        // Wait a bit
        var completedInTime = await Task.WhenAny(task4, Task.Delay(200)) != task4;
        
        completedInTime.Should().BeTrue("4th command should still be queued");
    }
}
