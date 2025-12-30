using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Munin.Core.Tests.Helpers;

/// <summary>
/// A simple mock IRC server for integration testing.
/// Listens on a local port and can send/receive IRC messages.
/// </summary>
public class MockIrcServer : IDisposable
{
    private readonly TcpListener _listener;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly List<string> _receivedMessages = new();
    private readonly object _lock = new();
    private TaskCompletionSource? _clientConnectedTcs;

    public int Port { get; }
    public bool IsConnected => _client?.Connected == true;
    public IReadOnlyList<string> ReceivedMessages
    {
        get { lock (_lock) return _receivedMessages.ToList(); }
    }

    /// <summary>
    /// Event raised when a client connects to the mock server.
    /// </summary>
    public event EventHandler? ClientConnected;

    /// <summary>
    /// Event raised when a client disconnects from the mock server.
    /// </summary>
    public event EventHandler? ClientDisconnected;

    /// <summary>
    /// Event raised when a message is received from the client.
    /// </summary>
    public event EventHandler<string>? MessageReceived;

    public MockIrcServer(int port = 0)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Starts accepting client connections.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _clientConnectedTcs = new TaskCompletionSource();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptTask = AcceptClientAsync(_cts.Token);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Waits for a client to connect.
    /// </summary>
    public async Task<bool> WaitForClientAsync(TimeSpan timeout)
    {
        if (_clientConnectedTcs == null)
            return false;

        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_clientConnectedTcs.Task, timeoutTask);
        return completedTask == _clientConnectedTcs.Task;
    }

    private async Task AcceptClientAsync(CancellationToken cancellationToken)
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync(cancellationToken);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\r\n" };

            _clientConnectedTcs?.TrySetResult();
            ClientConnected?.Invoke(this, EventArgs.Empty);

            // Start reading messages
            _ = Task.Run(() => ReadMessagesAsync(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
            _clientConnectedTcs?.TrySetCanceled();
        }
        catch (Exception ex)
        {
            _clientConnectedTcs?.TrySetException(ex);
        }
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line == null)
                    break; // Connection closed

                lock (_lock)
                {
                    _receivedMessages.Add(line);
                }

                // Auto-handle CAP negotiation synchronously
                if (line.StartsWith("CAP LS") && _writer != null)
                {
                    await _writer.WriteLineAsync(":server CAP * LS :");
                }

                MessageReceived?.Invoke(this, line);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (IOException)
        {
            // Connection closed
        }
        finally
        {
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sends a raw IRC message to the connected client.
    /// </summary>
    public async Task SendAsync(string message)
    {
        if (_writer == null)
            throw new InvalidOperationException("No client connected");

        await _writer.WriteLineAsync(message);
    }

    /// <summary>
    /// Sends multiple raw IRC messages to the connected client.
    /// </summary>
    public async Task SendAsync(params string[] messages)
    {
        foreach (var message in messages)
        {
            await SendAsync(message);
        }
    }

    /// <summary>
    /// Waits for a specific message to be received from the client.
    /// </summary>
    public async Task<string?> WaitForMessageAsync(Predicate<string> predicate, TimeSpan timeout)
    {
        // Check if message already exists in received messages
        lock (_lock)
        {
            var existing = _receivedMessages.FirstOrDefault(m => predicate(m));
            if (existing != null)
                return existing;
        }

        var cts = new CancellationTokenSource(timeout);
        var tcs = new TaskCompletionSource<string?>();

        void OnMessageReceived(object? sender, string message)
        {
            if (predicate(message))
            {
                tcs.TrySetResult(message);
            }
        }

        MessageReceived += OnMessageReceived;
        cts.Token.Register(() => tcs.TrySetResult(null));

        try
        {
            return await tcs.Task;
        }
        finally
        {
            MessageReceived -= OnMessageReceived;
            cts.Dispose();
        }
    }

    /// <summary>
    /// Waits for any message to be received.
    /// </summary>
    public Task<string?> WaitForAnyMessageAsync(TimeSpan timeout) =>
        WaitForMessageAsync(_ => true, timeout);

    /// <summary>
    /// Sends a standard IRC server greeting (001-004).
    /// </summary>
    public async Task SendServerGreetingAsync(string nickname = "TestUser")
    {
        await SendAsync(
            $":irc.example.com 001 {nickname} :Welcome to the Test IRC Network",
            $":irc.example.com 002 {nickname} :Your host is irc.example.com",
            $":irc.example.com 003 {nickname} :This server was created Mon Jan 1 2024",
            $":irc.example.com 004 {nickname} irc.example.com ircd-test iosw biklmnopstv"
        );
    }

    /// <summary>
    /// Clears the received messages list.
    /// </summary>
    public void ClearReceivedMessages()
    {
        lock (_lock)
        {
            _receivedMessages.Clear();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _acceptTask?.Wait(TimeSpan.FromSeconds(1));
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _listener.Stop();
        _cts?.Dispose();
    }
}
