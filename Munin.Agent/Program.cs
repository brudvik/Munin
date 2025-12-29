using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Munin.Agent.Commands;
using Munin.Agent.Configuration;
using Munin.Agent.Services;
using Munin.Agent.UserDatabase;
using Serilog;
using Serilog.Events;

namespace Munin.Agent;

/// <summary>
/// Entry point for the Munin Agent application.
/// Supports running as console application or Windows Service.
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "agent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("========================================");
            Log.Information("  Munin Agent v{Version}", typeof(Program).Assembly.GetName().Version);
            Log.Information("========================================");

            // Handle special commands
            if (args.Length > 0)
            {
                return await HandleCommandAsync(args);
            }

            // Normal startup
            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Agent terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Handles command-line commands like setup, encrypt, etc.
    /// </summary>
    private static async Task<int> HandleCommandAsync(string[] args)
    {
        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "setup":
                return await SetupWizard.RunAsync();

            case "encrypt":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: MuninAgent encrypt <config-file>");
                    return 1;
                }
                return await EncryptConfigCommand.RunAsync(args[1]);

            case "decrypt":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: MuninAgent decrypt <config-file>");
                    return 1;
                }
                return await DecryptConfigCommand.RunAsync(args[1]);

            case "gentoken":
                var token = AgentSecurity.GenerateAuthToken();
                Console.WriteLine($"Generated auth token: {token}");
                Console.WriteLine("Store this securely - it cannot be recovered!");
                return 0;

            case "gencert":
                return await GenerateCertificateCommand.RunAsync(args.Skip(1).ToArray());

            case "version":
                Console.WriteLine($"Munin Agent v{typeof(Program).Assembly.GetName().Version}");
                return 0;

            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                return 0;

            default:
                Console.WriteLine($"Unknown command: {command}");
                PrintHelp();
                return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
Munin Agent - Autonomous IRC Bot

Usage: MuninAgent [command]

Commands:
  (none)          Start the agent normally
  setup           Run the interactive setup wizard
  encrypt <file>  Encrypt a configuration file
  decrypt <file>  Decrypt a configuration file (requires master password)
  gentoken        Generate a new authentication token
  gencert         Generate a self-signed TLS certificate
  version         Show version information
  help            Show this help message

Environment Variables:
  MUNIN_AGENT_PASSWORD    Master password for encrypted config (avoid if possible)
  MUNIN_AGENT_CONFIG      Path to configuration file (default: agent.json)
");
    }

    /// <summary>
    /// Creates the host builder with all services configured.
    /// </summary>
    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "MuninAgent";
            })
            .UseSystemd()
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                // Configuration
                services.AddSingleton<AgentConfigurationService>();
                
                // Core services
                services.AddSingleton<AgentSecurityService>();
                services.AddSingleton<Munin.Core.Services.EncryptionService>();
                services.AddSingleton<AgentUserDatabaseService>(sp =>
                {
                    var configService = sp.GetRequiredService<AgentConfigurationService>();
                    var encryptionService = sp.GetRequiredService<Munin.Core.Services.EncryptionService>();
                    var dbPath = configService.Configuration.Users.FilePath;
                    if (!Path.IsPathRooted(dbPath))
                        dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);
                    return new AgentUserDatabaseService(dbPath, encryptionService);
                });
                
                // IRC Bot
                services.AddSingleton<IrcBotService>();
                
                // Scripting
                services.AddSingleton<Scripting.AgentScriptManager>();
                
                // Botnet
                services.AddSingleton<Botnet.BotnetService>();
                
                // Channel protection
                services.AddSingleton<Protection.ChannelProtectionService>();
                
                // Channel stats
                services.AddSingleton<Stats.ChannelStatsService>();
                
                // Control server for remote management
                services.AddSingleton<ControlServer>();
                services.AddHostedService(sp => sp.GetRequiredService<ControlServer>());
                
                // Main agent host service
                services.AddHostedService<AgentHostService>();
            });
}
