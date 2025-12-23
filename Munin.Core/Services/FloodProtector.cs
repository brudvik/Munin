using System.Collections.Concurrent;
using Serilog;

namespace Munin.Core.Services;

/// <summary>
/// Flood protection to prevent being disconnected for excess flood.
/// Implements token bucket algorithm for rate limiting.
/// </summary>
/// <remarks>
/// <para>Uses a token bucket algorithm where:</para>
/// <list type="bullet">
///   <item><description>Each message sent consumes one token</description></item>
///   <item><description>Tokens are refilled at a configurable rate</description></item>
///   <item><description>Messages queue when tokens are exhausted</description></item>
/// </list>
/// <para>Default settings: 5 token burst, 1 token/second refill.</para>
/// </remarks>
public class FloodProtector
{
    private readonly ILogger _logger;
    private readonly int _maxTokens;
    private readonly int _refillRate;
    private readonly TimeSpan _refillInterval;
    private readonly ConcurrentQueue<(string Command, TaskCompletionSource<bool> Completion)> _queue = new();
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    
    private int _tokens;
    private DateTime _lastRefill;
    private bool _isProcessing;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Whether flood protection is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Current queue depth.
    /// </summary>
    public int QueueDepth => _queue.Count;

    /// <summary>
    /// Callback to send a command (set by IrcConnection).
    /// </summary>
    public Func<string, Task>? SendCommandCallback { get; set; }

    /// <summary>
    /// Creates a new flood protector.
    /// </summary>
    /// <param name="maxTokens">Maximum tokens (burst capacity)</param>
    /// <param name="refillRate">Tokens to add per interval</param>
    /// <param name="refillIntervalMs">Interval in milliseconds</param>
    public FloodProtector(int maxTokens = 5, int refillRate = 1, int refillIntervalMs = 1000)
    {
        _logger = SerilogConfig.ForContext<FloodProtector>();
        _maxTokens = maxTokens;
        _refillRate = refillRate;
        _refillInterval = TimeSpan.FromMilliseconds(refillIntervalMs);
        _tokens = maxTokens;
        _lastRefill = DateTime.UtcNow;
    }

    /// <summary>
    /// Queues a command for sending with flood protection.
    /// Returns when the command has been sent.
    /// </summary>
    public async Task SendAsync(string command)
    {
        if (!IsEnabled)
        {
            if (SendCommandCallback != null)
                await SendCommandCallback(command);
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        _queue.Enqueue((command, tcs));
        
        StartProcessing();
        
        await tcs.Task;
    }

    /// <summary>
    /// Queues a command without waiting for it to be sent.
    /// </summary>
    public void QueueSend(string command)
    {
        if (!IsEnabled)
        {
            SendCommandCallback?.Invoke(command);
            return;
        }

        _queue.Enqueue((command, new TaskCompletionSource<bool>()));
        StartProcessing();
    }

    private void StartProcessing()
    {
        if (_isProcessing) return;
        
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _isProcessing = true;
        _ = ProcessQueueAsync(_cts.Token);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_queue.IsEmpty)
            {
                RefillTokens();

                if (_tokens > 0 && _queue.TryDequeue(out var item))
                {
                    _tokens--;
                    
                    try
                    {
                        if (SendCommandCallback != null)
                            await SendCommandCallback(item.Command);
                        item.Completion.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        item.Completion.TrySetException(ex);
                    }
                }
                else
                {
                    // Wait for token refill
                    var waitTime = _refillInterval - (DateTime.UtcNow - _lastRefill);
                    if (waitTime > TimeSpan.Zero)
                    {
                        await Task.Delay(waitTime, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancel
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastRefill;
        
        if (elapsed >= _refillInterval)
        {
            var intervalsElapsed = (int)(elapsed / _refillInterval);
            _tokens = Math.Min(_maxTokens, _tokens + (intervalsElapsed * _refillRate));
            _lastRefill = now;
        }
    }

    /// <summary>
    /// Clears the queue and resets tokens.
    /// </summary>
    public void Reset()
    {
        _cts?.Cancel();
        while (_queue.TryDequeue(out var item))
        {
            item.Completion.TrySetCanceled();
        }
        _tokens = _maxTokens;
        _lastRefill = DateTime.UtcNow;
    }

    /// <summary>
    /// Stops processing and clears the queue.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _isProcessing = false;
    }
}
