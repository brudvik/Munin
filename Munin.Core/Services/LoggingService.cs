using Munin.Core.Models;
using Serilog;
using Serilog.Events;
using System.Collections.Concurrent;
using System.Text;

namespace Munin.Core.Services;

/// <summary>
/// Handles logging of IRC messages to files using Serilog.
/// </summary>
/// <remarks>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Per-channel log files</description></item>
///   <item><description>Structured logging with Serilog</description></item>
///   <item><description>Legacy plain-text format support</description></item>
///   <item><description>Rolling file retention (90 days)</description></item>
///   <item><description>History retrieval for backlog display</description></item>
///   <item><description>Optional encryption via SecureStorageService</description></item>
/// </list>
/// <para>Log files are stored in %APPDATA%\IrcClient\logs\</para>
/// </remarks>
public class LoggingService : IDisposable
{
    private static LoggingService? _instance;
    private static SecureStorageService? _storageService;
    private static PrivacyService? _privacyService;
    
    /// <summary>
    /// Gets the singleton instance of the logging service.
    /// </summary>
    public static LoggingService Instance => _instance ??= new LoggingService();
    
    /// <summary>
    /// Initializes the logging service with a secure storage service for encryption support.
    /// Must be called before first access to Instance if encryption is desired.
    /// </summary>
    /// <param name="storage">The secure storage service to use for encrypted logs.</param>
    public static async Task InitializeAsync(SecureStorageService storage)
    {
        _storageService = storage;
        _privacyService = new PrivacyService(storage);
        
        // Enable privacy mode when encryption is enabled
        if (_privacyService != null)
        {
            _privacyService.IsEnabled = storage.IsEncryptionEnabled;
            
            // Load privacy mappings asynchronously
            await _privacyService.InitializeAsync();
        }
        
        _instance = new LoggingService();
    }

    private readonly string _logPath;
    private readonly ConcurrentDictionary<string, ILogger> _channelLoggers = new();
    private readonly ConcurrentDictionary<string, StreamWriter> _legacyWriters = new();
    private readonly ConcurrentDictionary<string, List<string>> _encryptedLogBuffers = new();
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Gets or sets whether logging is enabled.
    /// </summary>
    public bool EnableLogging { get; set; } = true;
    
    /// <summary>
    /// Whether to use structured Serilog logging (true) or plain text files (false).
    /// </summary>
    /// <remarks>
    /// Structured logging provides better querying and analysis capabilities,
    /// while plain text is more human-readable and compatible with other Munins.
    /// Note: When encryption is enabled, plain text mode is used with encrypted storage.
    /// </remarks>
    public bool UseStructuredLogging { get; set; } = true;
    
    /// <summary>
    /// Gets whether encryption is enabled for log files.
    /// </summary>
    public bool IsEncryptionEnabled => _storageService?.IsEncryptionEnabled ?? false;
    
    /// <summary>
    /// Gets whether privacy mode (anonymous filenames) is enabled.
    /// </summary>
    public bool IsPrivacyEnabled => _privacyService?.IsEnabled ?? false;
    
    /// <summary>
    /// Gets the privacy service for looking up real names from hashes.
    /// </summary>
    public static PrivacyService? PrivacyService => _privacyService;
    
    /// <summary>
    /// Gets the secure delete service for secure log cleanup.
    /// </summary>
    public static SecureDeleteService SecureDelete { get; } = new SecureDeleteService();

    private LoggingService()
    {
        _logPath = PortableMode.LogPath;
        Directory.CreateDirectory(_logPath);
        _logger = SerilogConfig.ForContext<LoggingService>();
    }
    
    /// <summary>
    /// Deletes log files older than the specified number of days.
    /// Uses secure deletion if enabled.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain logs.</param>
    /// <param name="useSecureDelete">Whether to use secure deletion.</param>
    /// <returns>Number of files deleted.</returns>
    public async Task<int> DeleteOldLogsAsync(int retentionDays, bool useSecureDelete)
    {
        if (retentionDays <= 0)
        {
            _logger.Warning("Invalid retention days: {Days}", retentionDays);
            return 0;
        }
        
        SecureDelete.IsEnabled = useSecureDelete;
        var cutoffDate = DateTime.Now.AddDays(-retentionDays);
        var deletedCount = 0;
        
        try
        {
            // Delete unencrypted logs
            if (Directory.Exists(_logPath))
            {
                foreach (var file in Directory.GetFiles(_logPath, "*.log", SearchOption.AllDirectories))
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        if (await SecureDelete.DeleteFileAsync(file))
                        {
                            deletedCount++;
                        }
                    }
                }
            }
            
            // Delete encrypted logs if storage is available
            if (_storageService != null && _storageService.IsUnlocked)
            {
                var encryptedLogPath = Path.Combine(_storageService.BasePath, "logs");
                if (Directory.Exists(encryptedLogPath))
                {
                    foreach (var file in Directory.GetFiles(encryptedLogPath, "*", SearchOption.AllDirectories))
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTime < cutoffDate)
                        {
                            if (await SecureDelete.DeleteFileAsync(file))
                            {
                                deletedCount++;
                            }
                        }
                    }
                }
            }
            
            _logger.Information("Deleted {Count} old log files (retention: {Days} days, secure: {Secure})", 
                deletedCount, retentionDays, useSecureDelete);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete old logs");
        }
        
        return deletedCount;
    }

    /// <summary>
    /// Logs a message to the appropriate log file.
    /// </summary>
    /// <param name="serverName">The server name for organizing logs.</param>
    /// <param name="channelName">The channel name for the log file.</param>
    /// <param name="message">The message to log.</param>
    public void LogMessage(string serverName, string channelName, IrcMessage message)
    {
        if (!EnableLogging) 
        {
            _logger.Debug("LogMessage: Logging is disabled");
            return;
        }

        try
        {
            var line = FormatLogLine(message);
            _logger.Debug("LogMessage: Logging to {Server}/{Channel}: {Line}", serverName, channelName, line);
            
            if (IsEncryptionEnabled)
            {
                // Use encrypted logging
                _logger.Debug("LogMessage: Using encrypted logging");
                LogEncrypted(serverName, channelName, line);
            }
            else if (UseStructuredLogging)
            {
                var channelLogger = GetChannelLogger(serverName, channelName);
                channelLogger.Information(
                    "[{MessageType}] <{Source}> {Content}",
                    message.Type,
                    message.Source ?? "*",
                    message.Content);
            }
            else
            {
                // Legacy plain text logging
                var writer = GetLegacyWriter(serverName, channelName);
                writer.WriteLine(line);
                writer.Flush();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to log message for {Server}/{Channel}", serverName, channelName);
        }
    }
    
    /// <summary>
    /// Logs a line to an encrypted log file.
    /// </summary>
    private void LogEncrypted(string serverName, string channelName, string line)
    {
        if (_storageService == null || !_storageService.IsUnlocked)
        {
            _logger.Warning("Cannot log encrypted: storage is null={StorageNull}, unlocked={Unlocked}", 
                _storageService == null, _storageService?.IsUnlocked);
            return;
        }
        
        var key = $"{serverName}|{channelName}|{DateTime.Today:yyyy-MM-dd}";
        var buffer = _encryptedLogBuffers.GetOrAdd(key, _ => new List<string>());
        _logger.Debug("LogEncrypted: Adding to buffer for {Key}, buffer size: {Size}", key, buffer.Count);
        
        lock (buffer)
        {
            buffer.Add(line);
            
            // Flush to disk every 10 messages or if buffer is getting large
            if (buffer.Count >= 10)
            {
                _logger.Debug("LogEncrypted: Flushing buffer for {Key}", key);
                FlushEncryptedBuffer(serverName, channelName, buffer);
            }
        }
    }
    
    /// <summary>
    /// Flushes an encrypted log buffer to disk.
    /// </summary>
    private void FlushEncryptedBuffer(string serverName, string channelName, List<string> buffer)
    {
        if (_storageService == null || !_storageService.IsUnlocked || buffer.Count == 0)
            return;
        
        try
        {
            var relativePath = GetEncryptedLogPath(serverName, channelName, DateTime.Today);
            
            // Read existing content if file exists (use sync to avoid deadlock)
            var existingContent = "";
            if (_storageService.FileExists(relativePath))
            {
                existingContent = _storageService.ReadTextSync(relativePath) ?? "";
            }
            
            // Append new lines (use sync to avoid deadlock)
            var newContent = existingContent + string.Join(Environment.NewLine, buffer) + Environment.NewLine;
            _storageService.WriteTextSync(relativePath, newContent);
            
            buffer.Clear();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to flush encrypted log buffer");
        }
    }
    
    private string GetEncryptedLogPath(string serverName, string channelName, DateTime date)
    {
        // Use privacy service for anonymous filenames if enabled
        if (_privacyService != null && _privacyService.IsEnabled)
        {
            return _privacyService.GetAnonymizedLogPath(serverName, channelName, date);
        }
        
        return $"logs/{SanitizeFileName(serverName)}/{SanitizeFileName(channelName)}_{date:yyyy-MM-dd}.log";
    }

    /// <summary>
    /// Logs a raw IRC message.
    /// </summary>
    /// <param name="serverName">The server name for organizing logs.</param>
    /// <param name="direction">Direction indicator (&gt;&gt;&gt; for sent, &lt;&lt;&lt; for received).</param>
    /// <param name="rawMessage">The raw IRC protocol message.</param>
    public void LogRaw(string serverName, string direction, string rawMessage)
    {
        if (!EnableLogging) return;

        // Always filter sensitive data before logging to file
        var maskedMessage = SensitiveDataFilter.MaskSensitiveData(rawMessage);

        try
        {
            if (UseStructuredLogging)
            {
                var rawLogger = GetChannelLogger(serverName, "_raw");
                rawLogger.Debug("{Direction} {RawMessage}", direction, maskedMessage);
            }
            else
            {
                var writer = GetLegacyWriter(serverName, "_raw");
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                writer.WriteLine($"[{timestamp}] {direction} {maskedMessage}");
                writer.Flush();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to log raw message for {Server}", serverName);
        }
    }

    /// <summary>
    /// Logs a system event.
    /// </summary>
    /// <param name="serverName">The server name for organizing logs.</param>
    /// <param name="channelName">The channel name for the log file.</param>
    /// <param name="eventType">Type of event (e.g., JOIN, PART, KICK).</param>
    /// <param name="message">The event message to log.</param>
    public void LogEvent(string serverName, string channelName, string eventType, string message)
    {
        if (!EnableLogging) return;

        try
        {
            var channelLogger = GetChannelLogger(serverName, channelName);
            channelLogger.Information("[{EventType}] {Message}", eventType, message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to log event for {Server}/{Channel}", serverName, channelName);
        }
    }

    private ILogger GetChannelLogger(string serverName, string channelName)
    {
        var key = $"{serverName}|{channelName}";
        return _channelLoggers.GetOrAdd(key, _ =>
        {
            var sanitizedServer = SanitizeFileName(serverName);
            var sanitizedChannel = SanitizeFileName(channelName);
            var logPath = Path.Combine(_logPath, sanitizedServer, $"{sanitizedChannel}-.log");
            
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            // Create a dedicated file sink for this channel
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Server", serverName)
                .Enrich.WithProperty("Channel", channelName)
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Message:lj}{NewLine}",
                    shared: true)
                .CreateLogger();
        });
    }

    /// <summary>
    /// Reads the last N lines from a channel log file.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="channelName">The channel name.</param>
    /// <param name="lineCount">Number of lines to retrieve (default: 100).</param>
    /// <returns>The last N lines from today's log file.</returns>
    public IEnumerable<string> ReadLastLines(string serverName, string channelName, int lineCount = 100)
    {
        if (IsEncryptionEnabled)
        {
            return ReadEncryptedLastLines(serverName, channelName, lineCount);
        }
        
        var filePath = GetLogFilePath(serverName, channelName, DateTime.Today);
        if (!File.Exists(filePath))
            return Enumerable.Empty<string>();

        try
        {
            var lines = File.ReadAllLines(filePath);
            return lines.TakeLast(lineCount);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
    
    /// <summary>
    /// Reads the last N lines from an encrypted log file.
    /// </summary>
    private IEnumerable<string> ReadEncryptedLastLines(string serverName, string channelName, int lineCount)
    {
        if (_storageService == null || !_storageService.IsUnlocked)
            return Enumerable.Empty<string>();
        
        // First flush any pending writes
        var key = $"{serverName}|{channelName}|{DateTime.Today:yyyy-MM-dd}";
        if (_encryptedLogBuffers.TryGetValue(key, out var buffer))
        {
            lock (buffer)
            {
                FlushEncryptedBuffer(serverName, channelName, buffer);
            }
        }
        
        try
        {
            var relativePath = GetEncryptedLogPath(serverName, channelName, DateTime.Today);
            if (!_storageService.FileExists(relativePath))
                return Enumerable.Empty<string>();
            
            // Use sync method to avoid deadlock on UI thread
            var content = _storageService.ReadTextSync(relativePath);
            if (string.IsNullOrEmpty(content))
                return Enumerable.Empty<string>();
            
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.TakeLast(lineCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to read encrypted log");
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Searches for messages containing a pattern.
    /// </summary>
    /// <param name="serverName">The server name to search in.</param>
    /// <param name="channelName">The channel name to search in.</param>
    /// <param name="pattern">The text pattern to search for (case-insensitive).</param>
    /// <param name="maxResults">Maximum number of results to return (default: 100).</param>
    /// <returns>A collection of matching lines with their dates.</returns>
    public IEnumerable<(DateTime date, string line)> Search(string serverName, string channelName, string pattern, int maxResults = 100)
    {
        var results = new List<(DateTime, string)>();
        var serverLogPath = Path.Combine(_logPath, SanitizeFileName(serverName));

        if (!Directory.Exists(serverLogPath))
            return results;

        var channelPrefix = SanitizeFileName(channelName);
        var logFiles = Directory.GetFiles(serverLogPath, $"{channelPrefix}_*.log")
            .OrderByDescending(f => f);

        foreach (var file in logFiles)
        {
            if (results.Count >= maxResults)
                break;

            try
            {
                // Extract date from filename
                var fileName = Path.GetFileNameWithoutExtension(file);
                var datePart = fileName.Split('_').LastOrDefault();
                if (!DateTime.TryParse(datePart, out var date))
                    date = File.GetCreationTime(file);

                var lines = File.ReadAllLines(file);
                foreach (var line in lines.AsEnumerable().Reverse())
                {
                    if (results.Count >= maxResults)
                        break;

                    if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add((date, line));
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        return results;
    }

    private StreamWriter GetLegacyWriter(string serverName, string channelName)
    {
        var key = $"{serverName}|{channelName}|{DateTime.Today:yyyy-MM-dd}";

        return _legacyWriters.GetOrAdd(key, _ =>
        {
            var filePath = GetLogFilePath(serverName, channelName, DateTime.Today);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            return new StreamWriter(filePath, append: true, Encoding.UTF8);
        });
    }

    private string GetLogFilePath(string serverName, string channelName, DateTime date)
    {
        var serverDir = Path.Combine(_logPath, SanitizeFileName(serverName));
        var fileName = $"{SanitizeFileName(channelName)}_{date:yyyy-MM-dd}.log";
        return Path.Combine(serverDir, fileName);
    }

    private static string FormatLogLine(IrcMessage message)
    {
        var timestamp = message.Timestamp.ToString("HH:mm:ss");
        
        return message.Type switch
        {
            MessageType.Normal => $"[{timestamp}] <{message.Source}> {message.Content}",
            MessageType.Action => $"[{timestamp}] * {message.Source} {message.Content}",
            MessageType.Notice => $"[{timestamp}] -{message.Source}- {message.Content}",
            MessageType.Join => $"[{timestamp}] --> {message.Source} has joined",
            MessageType.Part => $"[{timestamp}] <-- {message.Source} has left",
            MessageType.Quit => $"[{timestamp}] <-- {message.Source} has quit",
            MessageType.Kick => $"[{timestamp}] <<< {message.Content}",
            MessageType.Mode => $"[{timestamp}] *** {message.Content}",
            MessageType.Topic => $"[{timestamp}] *** {message.Content}",
            MessageType.Nick => $"[{timestamp}] *** {message.Content}",
            _ => $"[{timestamp}] {message.Content}"
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            sanitized.Append(invalid.Contains(c) ? '_' : c);
        }

        return sanitized.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // Flush all encrypted buffers before disposing
        foreach (var kvp in _encryptedLogBuffers)
        {
            var parts = kvp.Key.Split('|');
            if (parts.Length >= 2)
            {
                lock (kvp.Value)
                {
                    FlushEncryptedBuffer(parts[0], parts[1], kvp.Value);
                }
            }
        }
        _encryptedLogBuffers.Clear();

        // Dispose legacy writers
        foreach (var writer in _legacyWriters.Values)
        {
            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch { }
        }
        _legacyWriters.Clear();

        // Dispose Serilog channel loggers
        foreach (var logger in _channelLoggers.Values)
        {
            if (logger is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
        }
        _channelLoggers.Clear();

        GC.SuppressFinalize(this);
    }
}
