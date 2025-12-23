using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Munin.Core.Services;

/// <summary>
/// Centralized Serilog configuration for the Munin.
/// </summary>
/// <remarks>
/// <para>Provides a single point of configuration for all logging in the application.</para>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Console output with colored level indicators</description></item>
///   <item><description>Rolling daily log files (30 day retention)</description></item>
///   <item><description>Thread ID enrichment for debugging</description></item>
///   <item><description>Contextual logging per class/component</description></item>
/// </list>
/// <para>Default log location: %APPDATA%\IrcClient\logs</para>
/// </remarks>
public static class SerilogConfig
{
    private static bool _isInitialized;
    private static readonly object _lock = new();
    private static string _logDirectory = GetDefaultLogDirectory();

    /// <summary>
    /// Gets the current log directory.
    /// </summary>
    public static string LogDirectory => _logDirectory;

    /// <summary>
    /// Initializes Serilog with default configuration.
    /// </summary>
    /// <param name="logDirectory">Optional custom log directory. Uses default if null.</param>
    /// <param name="minimumLevel">Minimum log level to capture. Default is Information.</param>
    public static void Initialize(string? logDirectory = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        lock (_lock)
        {
            if (_isInitialized)
            {
                Log.Warning("Serilog is already initialized. Call CloseAndFlush() first to reconfigure.");
                return;
            }

            _logDirectory = logDirectory ?? GetDefaultLogDirectory();
            Directory.CreateDirectory(_logDirectory);

            var logPath = Path.Combine(_logDirectory, "ircclient-.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "Munin")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                    shared: true)
                .CreateLogger();

            _isInitialized = true;
            Log.Information("Serilog initialized. Log directory: {LogDirectory}", _logDirectory);
        }
    }

    /// <summary>
    /// Creates a logger for a specific context (class name).
    /// </summary>
    public static ILogger ForContext<T>() => Log.ForContext<T>();

    /// <summary>
    /// Creates a logger for a specific context.
    /// </summary>
    public static ILogger ForContext(string sourceContext) => Log.ForContext("SourceContext", sourceContext);

    /// <summary>
    /// Creates a logger for IRC message logging (server/channel specific).
    /// </summary>
    public static ILogger ForIrcChannel(string serverName, string channelName)
    {
        return Log.ForContext("Server", serverName)
                  .ForContext("Channel", channelName)
                  .ForContext("SourceContext", $"IRC.{serverName}.{channelName}");
    }

    /// <summary>
    /// Closes and flushes the logger. Call this on application shutdown.
    /// </summary>
    public static void CloseAndFlush()
    {
        lock (_lock)
        {
            Log.CloseAndFlush();
            _isInitialized = false;
        }
    }

    private static string GetDefaultLogDirectory()
    {
        return PortableMode.LogPath;
    }
}
