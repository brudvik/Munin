using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Runtime.InteropServices;

namespace MuninRelay;

/// <summary>
/// MuninRelay - IRC Traffic Relay for VPN routing
/// 
/// This tool allows Munin IRC Client to route traffic through a VPN
/// running on a different machine. It provides:
/// - Secure SSL/TLS connection between Munin and the relay
/// - Token-based authentication
/// - IP verification to ensure VPN is active
/// - Support for running as Windows Service or Console app
/// </summary>
public class Program
{
    private const string ServiceName = "MuninRelay";
    private const string ServiceDisplayName = "Munin IRC Relay";
    private const string ServiceDescription = "Routes IRC traffic through VPN for the Munin IRC Client";

    public static async Task<int> Main(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var configDir = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;

        // Parse command-line arguments
        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--install":
                    return InstallService();

                case "--uninstall":
                    return UninstallService();
                    
                case "--setup-password":
                    return SetupMasterPassword(configDir);
                    
                case "--change-password":
                    return ChangeMasterPassword(configPath, configDir);

                case "--generate-token":
                    return GenerateNewToken(configPath, configDir);

                case "--generate-cert":
                    return GenerateCertificate(configPath, configDir);

                case "--verify-ip":
                    return await VerifyIpAsync(configPath, configDir);

                case "--list-servers":
                    return ListServers(configPath, configDir);

                case "--add-server":
                    return AddServer(configPath, configDir, args.Skip(1).ToArray());

                case "--remove-server":
                    return RemoveServer(configPath, configDir, args.Skip(1).ToArray());

                case "--help":
                case "-h":
                case "/?":
                    PrintHelp();
                    return 0;

                case "--console":
                    // Run in console mode (explicit)
                    return await RunConsoleAsync(configPath, configDir, args);
            }
        }

        // Determine if running as service or console
        if (!Environment.UserInteractive)
        {
            // Running as Windows Service
            return await RunAsServiceAsync(configPath, configDir);
        }
        else
        {
            // Running in console mode
            return await RunConsoleAsync(configPath, configDir, args);
        }
    }

    /// <summary>
    /// Runs the relay as a console application.
    /// </summary>
    private static async Task<int> RunConsoleAsync(string configPath, string configDir, string[] args)
    {
        Console.WriteLine("╔═══════════════════════════════════════════╗");
        Console.WriteLine("║         MuninRelay IRC Traffic Relay      ║");
        Console.WriteLine("╚═══════════════════════════════════════════╝");
        Console.WriteLine();

        // Get or create master password
        var password = GetOrSetupMasterPassword(configDir);
        if (password == null)
        {
            return 1;
        }

        // Load or create configuration
        var config = LoadOrCreateConfiguration(configPath, password);

        // Configure logging
        ConfigureLogging(config, isService: false);

        Log.Information("Starting MuninRelay in console mode...");

        // Ensure certificate exists
        if (string.IsNullOrEmpty(config.CertificatePath))
        {
            CertificateGenerator.EnsureCertificateExists(config);
        }

        // Display configuration
        DisplayConfiguration(config);

        // Build and run the host
        var host = Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddHostedService<RelayService>();
            })
            .Build();

        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop the relay...");
        Console.WriteLine();

        try
        {
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error running relay");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Runs the relay as a Windows Service.
    /// </summary>
    private static async Task<int> RunAsServiceAsync(string configPath, string configDir)
    {
        // For Windows Service, password must be stored in environment variable
        var password = Environment.GetEnvironmentVariable("MUNINRELAY_PASSWORD");
        
        if (string.IsNullOrEmpty(password))
        {
            // Try to read from a secure file (created during --install)
            var passwordFile = Path.Combine(configDir, ".service_password");
            if (File.Exists(passwordFile))
            {
                try
                {
                    var encryptedPassword = File.ReadAllBytes(passwordFile);
                    var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                        encryptedPassword,
                        System.Text.Encoding.UTF8.GetBytes("MuninRelay.ServicePassword"),
                        System.Security.Cryptography.DataProtectionScope.LocalMachine);
                    password = System.Text.Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    // Fall through to error
                }
            }
        }
        
        if (string.IsNullOrEmpty(password) || !MasterPassword.Verify(configDir, password))
        {
            Log.Fatal("Cannot start service: Master password not configured or invalid. " +
                     "Run 'MuninRelay --install' interactively to set up the service.");
            return 1;
        }
        
        var config = LoadOrCreateConfiguration(configPath, password);
        ConfigureLogging(config, isService: true);

        Log.Information("Starting MuninRelay as Windows Service...");

        if (string.IsNullOrEmpty(config.CertificatePath))
        {
            CertificateGenerator.EnsureCertificateExists(config);
        }

        var host = Host.CreateDefaultBuilder()
            .UseWindowsService(options =>
            {
                options.ServiceName = ServiceName;
            })
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddHostedService<RelayService>();
            })
            .Build();

        try
        {
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error running service");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Installs the relay as a Windows Service.
    /// </summary>
    private static int InstallService()
    {
        if (!IsAdministrator())
        {
            Console.WriteLine("ERROR: Administrator privileges required to install service.");
            Console.WriteLine("Please run this command as Administrator.");
            return 1;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.WriteLine("ERROR: Could not determine executable path.");
            return 1;
        }

        Console.WriteLine($"Installing service '{ServiceDisplayName}'...");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"create {ServiceName} binPath= \"{exePath}\" start= auto DisplayName= \"{ServiceDisplayName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = Process.Start(startInfo);
            process?.WaitForExit();

            if (process?.ExitCode == 0)
            {
                // Set description
                var descStart = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"description {ServiceName} \"{ServiceDescription}\"",
                    UseShellExecute = false
                };
                Process.Start(descStart)?.WaitForExit();

                Console.WriteLine("Service installed successfully!");
                Console.WriteLine();
                Console.WriteLine("To start the service, run:");
                Console.WriteLine($"  sc start {ServiceName}");
                Console.WriteLine();
                Console.WriteLine("Or use:");
                Console.WriteLine("  net start MuninRelay");
                return 0;
            }
            else
            {
                Console.WriteLine($"ERROR: Failed to install service. Exit code: {process?.ExitCode}");
                Console.WriteLine(process?.StandardError.ReadToEnd());
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Uninstalls the Windows Service.
    /// </summary>
    private static int UninstallService()
    {
        if (!IsAdministrator())
        {
            Console.WriteLine("ERROR: Administrator privileges required to uninstall service.");
            Console.WriteLine("Please run this command as Administrator.");
            return 1;
        }

        Console.WriteLine($"Uninstalling service '{ServiceDisplayName}'...");

        try
        {
            // Stop the service first
            var stopStart = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"stop {ServiceName}",
                UseShellExecute = false
            };
            Process.Start(stopStart)?.WaitForExit();

            // Give it a moment to stop
            Thread.Sleep(1000);

            // Delete the service
            var deleteStart = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete {ServiceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = Process.Start(deleteStart);
            process?.WaitForExit();

            if (process?.ExitCode == 0)
            {
                Console.WriteLine("Service uninstalled successfully!");
                return 0;
            }
            else
            {
                Console.WriteLine($"ERROR: Failed to uninstall service. Exit code: {process?.ExitCode}");
                Console.WriteLine(process?.StandardError.ReadToEnd());
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Generates a new authentication token.
    /// </summary>
    private static int GenerateNewToken(string configPath, string configDir)
    {
        // Get or verify master password
        var password = GetOrSetupMasterPassword(configDir);
        if (password == null)
        {
            return 1;
        }
        
        RelayConfiguration config;
        
        // Load existing config or create new
        if (File.Exists(configPath))
        {
            try
            {
                config = RelayConfiguration.Load(configPath, password, out _);
            }
            catch
            {
                config = new RelayConfiguration { ConfigDirectory = configDir };
            }
        }
        else
        {
            config = new RelayConfiguration { ConfigDirectory = configDir };
        }

        // Generate new token
        var newToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        
        // Store encrypted token in config file
        config.AuthToken = newToken;
        config.Save(configPath, password);

        Console.WriteLine("New authentication token generated!");
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  IMPORTANT: Copy this token now - it cannot be retrieved!     ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Token: {newToken}  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("The token has been encrypted and saved to the configuration file.");
        Console.WriteLine("It is protected with your master password + Windows DPAPI.");
        Console.WriteLine();
        Console.WriteLine("You will need to enter this token in your Munin client settings.");

        return 0;
    }

    /// <summary>
    /// Generates a new SSL certificate.
    /// </summary>
    private static int GenerateCertificate(string configPath, string configDir)
    {
        var password = GetOrSetupMasterPassword(configDir);
        if (password == null)
        {
            return 1;
        }
        
        var config = LoadOrCreateConfiguration(configPath, password);
        ConfigureLogging(config, isService: false);

        Console.WriteLine("Generating new SSL certificate...");
        CertificateGenerator.EnsureCertificateExists(config);

        Console.WriteLine();
        Console.WriteLine($"Certificate saved to: {config.CertificatePath}");
        Console.WriteLine("Configuration updated with certificate settings.");

        return 0;
    }

    /// <summary>
    /// Performs an IP verification check.
    /// </summary>
    private static async Task<int> VerifyIpAsync(string configPath, string configDir)
    {
        var password = GetOrSetupMasterPassword(configDir);
        if (password == null)
        {
            return 1;
        }
        
        var config = LoadOrCreateConfiguration(configPath, password);
        ConfigureLogging(config, isService: false);

        Console.WriteLine("Performing IP verification...");
        Console.WriteLine();

        var verifier = new IpVerificationService(config);
        var result = await verifier.VerifyAsync();

        if (result.Success)
        {
            Console.WriteLine($"IP Address:   {result.IpAddress}");
            Console.WriteLine($"Country:      {result.Country} ({result.CountryCode})");
            Console.WriteLine($"City:         {result.City}, {result.Region}");
            Console.WriteLine($"Organization: {result.Organization}");
            Console.WriteLine($"Likely VPN:   {(result.IsLikelyVpn ? "Yes" : "Unknown")}");

            if (!string.IsNullOrEmpty(config.ExpectedCountryCode))
            {
                Console.WriteLine();
                Console.WriteLine($"Expected country: {config.ExpectedCountryCode}");
                Console.WriteLine($"Country matches:  {(result.CountryMatches ? "Yes ✓" : "No ✗")}");
            }

            return result.CountryMatches ? 0 : 2;
        }
        else
        {
            Console.WriteLine($"ERROR: {result.ErrorMessage}");
            return 1;
        }
    }

    /// <summary>
    /// Displays help information.
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine("MuninRelay - IRC Traffic Relay for VPN Routing");
        Console.WriteLine();
        Console.WriteLine("Usage: MuninRelay [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --console          Run in console mode (default when interactive)");
        Console.WriteLine("  --install          Install as Windows Service (requires Admin)");
        Console.WriteLine("  --uninstall        Uninstall Windows Service (requires Admin)");
        Console.WriteLine("  --setup-password   Set up or reset the master password");
        Console.WriteLine("  --change-password  Change the master password");
        Console.WriteLine("  --generate-token   Generate a new authentication token");
        Console.WriteLine("  --generate-cert    Generate a new SSL certificate");
        Console.WriteLine("  --verify-ip        Verify current IP and VPN status");
        Console.WriteLine("  --list-servers     List allowed IRC servers");
        Console.WriteLine("  --add-server       Add an allowed IRC server");
        Console.WriteLine("  --remove-server    Remove an allowed IRC server");
        Console.WriteLine("  --help             Display this help message");
        Console.WriteLine();
        Console.WriteLine("Master Password:");
        Console.WriteLine("  A master password is required to access encrypted configuration.");
        Console.WriteLine("  It must be at least 8 characters and is set on first run.");
        Console.WriteLine("  The password hash is protected with DPAPI (machine-bound).");
        Console.WriteLine();
        Console.WriteLine("Server Management:");
        Console.WriteLine("  --add-server <host> <port> [ssl]");
        Console.WriteLine("    Example: --add-server irc.example.com 6697 ssl");
        Console.WriteLine("    Example: --add-server irc.example.com 6667");
        Console.WriteLine();
        Console.WriteLine("  --remove-server <host> [port]");
        Console.WriteLine("    Example: --remove-server irc.example.com");
        Console.WriteLine("    Example: --remove-server irc.example.com 6697");
        Console.WriteLine();
        Console.WriteLine("Security:");
        Console.WriteLine("  - Master password encrypts sensitive config (token, servers)");
        Console.WriteLine("  - Password hash is protected with DPAPI (machine-bound)");
        Console.WriteLine("  - Double protection: password + machine binding");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  MuninRelay                    # Run in console mode");
        Console.WriteLine("  MuninRelay --install          # Install as service");
        Console.WriteLine("  MuninRelay --verify-ip        # Check VPN status");
        Console.WriteLine("  MuninRelay --list-servers     # Show allowed servers");
    }

    /// <summary>
    /// Lists allowed IRC servers.
    /// </summary>
    private static int ListServers(string configPath, string configDir)
    {
        try
        {
            var password = GetOrSetupMasterPassword(configDir);
            if (password == null)
            {
                return 1;
            }
            
            var config = RelayConfiguration.Load(configPath, password, out _);
            
            Console.WriteLine("Allowed IRC Servers:");
            Console.WriteLine("════════════════════════════════════════════════════════");
            
            if (config.AllowedServers.Count == 0)
            {
                Console.WriteLine("  (No servers configured - all servers allowed)");
            }
            else
            {
                foreach (var server in config.AllowedServers)
                {
                    var ssl = server.UseSsl ? " [SSL]" : "";
                    Console.WriteLine($"  • {server.Hostname}:{server.Port}{ssl}");
                }
            }
            
            Console.WriteLine();
            Console.WriteLine($"Total: {config.AllowedServers.Count} server(s)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Adds an allowed IRC server.
    /// </summary>
    private static int AddServer(string configPath, string configDir, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: --add-server <hostname> <port> [ssl]");
            Console.WriteLine("Example: --add-server irc.private.net 6697 ssl");
            return 1;
        }

        var hostname = args[0];
        if (!int.TryParse(args[1], out var port))
        {
            Console.WriteLine($"ERROR: Invalid port number: {args[1]}");
            return 1;
        }

        var useSsl = args.Length > 2 && args[2].Equals("ssl", StringComparison.OrdinalIgnoreCase);

        try
        {
            var password = GetOrSetupMasterPassword(configDir);
            if (password == null)
            {
                return 1;
            }
            
            var config = RelayConfiguration.Load(configPath, password, out _);
            
            // Check if already exists
            if (config.AllowedServers.Any(s => 
                s.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase) && s.Port == port))
            {
                Console.WriteLine($"Server already exists: {hostname}:{port}");
                return 0;
            }

            config.AllowedServers.Add(new AllowedServer
            {
                Hostname = hostname,
                Port = port,
                UseSsl = useSsl
            });

            config.Save(configPath, password);

            Console.WriteLine($"Added server: {hostname}:{port}{(useSsl ? " [SSL]" : "")}");
            Console.WriteLine("Server list is encrypted in config.json");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Removes an allowed IRC server.
    /// </summary>
    private static int RemoveServer(string configPath, string configDir, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: --remove-server <hostname> [port]");
            Console.WriteLine("Example: --remove-server irc.private.net");
            Console.WriteLine("Example: --remove-server irc.private.net 6697");
            return 1;
        }

        var hostname = args[0];
        int? port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : null;

        try
        {
            var password = GetOrSetupMasterPassword(configDir);
            if (password == null)
            {
                return 1;
            }
            
            var config = RelayConfiguration.Load(configPath, password, out _);

            var toRemove = config.AllowedServers
                .Where(s => s.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase) &&
                           (port == null || s.Port == port))
                .ToList();

            if (toRemove.Count == 0)
            {
                Console.WriteLine($"Server not found: {hostname}{(port.HasValue ? $":{port}" : "")}");
                return 1;
            }

            foreach (var server in toRemove)
            {
                config.AllowedServers.Remove(server);
                Console.WriteLine($"Removed: {server.Hostname}:{server.Port}");
            }

            config.Save(configPath, password);
            Console.WriteLine("Server list updated and encrypted");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }
    
    /// <summary>
    /// Sets up a new master password.
    /// </summary>
    private static int SetupMasterPassword(string configDir)
    {
        if (MasterPassword.IsConfigured(configDir))
        {
            Console.WriteLine("A master password is already configured.");
            Console.WriteLine("Use --change-password to change it.");
            return 1;
        }
        
        Console.WriteLine("Setting up Master Password");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.WriteLine("The master password encrypts sensitive configuration data.");
        Console.WriteLine("It must be at least 8 characters.");
        Console.WriteLine();
        
        var password = MasterPassword.ReadPassword("Enter master password: ");
        
        if (password.Length < 8)
        {
            Console.WriteLine("ERROR: Password must be at least 8 characters.");
            return 1;
        }
        
        var confirm = MasterPassword.ReadPassword("Confirm password: ");
        
        if (password != confirm)
        {
            Console.WriteLine("ERROR: Passwords do not match.");
            return 1;
        }
        
        try
        {
            MasterPassword.Setup(configDir, password);
            Console.WriteLine();
            Console.WriteLine("Master password configured successfully!");
            Console.WriteLine("The password hash is stored with DPAPI protection (machine-bound).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }
    
    /// <summary>
    /// Changes the master password.
    /// </summary>
    private static int ChangeMasterPassword(string configPath, string configDir)
    {
        if (!MasterPassword.IsConfigured(configDir))
        {
            Console.WriteLine("No master password is configured yet.");
            Console.WriteLine("Run the application to set up a password.");
            return 1;
        }
        
        Console.WriteLine("Change Master Password");
        Console.WriteLine("══════════════════════════════════════════════════════");
        
        var currentPassword = MasterPassword.ReadPassword("Enter current password: ");
        
        if (!MasterPassword.Verify(configDir, currentPassword))
        {
            Console.WriteLine("ERROR: Current password is incorrect.");
            return 1;
        }
        
        var newPassword = MasterPassword.ReadPassword("Enter new password: ");
        
        if (newPassword.Length < 8)
        {
            Console.WriteLine("ERROR: New password must be at least 8 characters.");
            return 1;
        }
        
        var confirm = MasterPassword.ReadPassword("Confirm new password: ");
        
        if (newPassword != confirm)
        {
            Console.WriteLine("ERROR: Passwords do not match.");
            return 1;
        }
        
        try
        {
            // Re-encrypt configuration with new password
            MasterPassword.ChangePassword(configDir, currentPassword, newPassword, (oldPwd, newPwd) =>
            {
                if (File.Exists(configPath))
                {
                    var config = RelayConfiguration.Load(configPath, oldPwd, out _);
                    config.Save(configPath, newPwd);
                    Console.WriteLine("Configuration re-encrypted with new password.");
                }
            });
            
            Console.WriteLine();
            Console.WriteLine("Master password changed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }
    
    /// <summary>
    /// Gets the master password, prompting for setup if not configured.
    /// </summary>
    /// <returns>The master password, or null if setup failed or was cancelled.</returns>
    private static string? GetOrSetupMasterPassword(string configDir)
    {
        if (!MasterPassword.IsConfigured(configDir))
        {
            Console.WriteLine("First-time setup: Master Password Required");
            Console.WriteLine("══════════════════════════════════════════════════════");
            Console.WriteLine("A master password is needed to encrypt sensitive configuration.");
            Console.WriteLine("This password will be required each time MuninRelay starts.");
            Console.WriteLine("Minimum 8 characters.");
            Console.WriteLine();
            
            var password = MasterPassword.ReadPassword("Create master password: ");
            
            if (password.Length < 8)
            {
                Console.WriteLine("ERROR: Password must be at least 8 characters.");
                return null;
            }
            
            var confirm = MasterPassword.ReadPassword("Confirm password: ");
            
            if (password != confirm)
            {
                Console.WriteLine("ERROR: Passwords do not match.");
                return null;
            }
            
            try
            {
                MasterPassword.Setup(configDir, password);
                Console.WriteLine();
                Console.WriteLine("Master password configured!");
                Console.WriteLine();
                return password;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return null;
            }
        }
        
        // Password exists, prompt for it
        var enteredPassword = MasterPassword.ReadPassword("Enter master password: ");
        
        if (!MasterPassword.Verify(configDir, enteredPassword))
        {
            Console.WriteLine("ERROR: Incorrect password.");
            return null;
        }
        
        return enteredPassword;
    }

    /// <summary>
    /// Loads or creates the configuration.
    /// </summary>
    private static RelayConfiguration LoadOrCreateConfiguration(string path, string masterPassword)
    {
        var config = RelayConfiguration.Load(path, masterPassword, out var isNewSetup);
        
        if (isNewSetup)
        {
            Console.WriteLine($"Configuration saved to: {path}");
            Console.WriteLine();
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  IMPORTANT: Copy this token now - it cannot be retrieved!     ║");
            Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
            Console.WriteLine($"║  Token: {config.AuthToken}  ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("The token is encrypted with your master password + DPAPI.");
            Console.WriteLine("Enter this token in your Munin client's relay settings.");
            Console.WriteLine();
            Console.WriteLine("Default allowed servers (also encrypted):");
            foreach (var server in config.AllowedServers)
            {
                Console.WriteLine($"  • {server.Hostname}:{server.Port}{(server.UseSsl ? " [SSL]" : "")}");
            }
            Console.WriteLine();
            Console.WriteLine("Use --add-server and --remove-server to manage the server list.");
            Console.WriteLine("Use --list-servers to view current allowed servers.");
            Console.WriteLine();
        }
        
        return config;
    }

    /// <summary>
    /// Configures Serilog logging.
    /// </summary>
    private static void ConfigureLogging(RelayConfiguration config, bool isService)
    {
        var logPath = config.LogFilePath ?? Path.Combine(AppContext.BaseDirectory, "logs", "muninrelay-.log");

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Is(config.VerboseLogging ? LogEventLevel.Verbose : LogEventLevel.Information)
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

        if (!isService)
        {
            logConfig.WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        Log.Logger = logConfig.CreateLogger();
    }

    /// <summary>
    /// Displays the current configuration.
    /// </summary>
    private static void DisplayConfiguration(RelayConfiguration config)
    {
        Console.WriteLine("Configuration:");
        Console.WriteLine($"  Listen Port:       {config.ListenPort}");
        Console.WriteLine($"  Max Connections:   {config.MaxConnections}");
        Console.WriteLine($"  IP Verification:   {(config.EnableIpVerification ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  Expected Country:  {config.ExpectedCountryCode ?? "(not set)"}");
        Console.WriteLine($"  Certificate:       {config.CertificatePath ?? "(not set)"}");
        Console.WriteLine($"  Log File:          {config.LogFilePath}");

        if (config.AllowedServers.Count > 0)
        {
            Console.WriteLine($"  Allowed Servers:");
            foreach (var server in config.AllowedServers)
            {
                Console.WriteLine($"    - {server.Hostname}:{server.Port} (SSL: {server.UseSsl})");
            }
        }
        else
        {
            Console.WriteLine($"  Allowed Servers:   (all allowed)");
        }
    }

    /// <summary>
    /// Checks if running with administrator privileges.
    /// </summary>
    private static bool IsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
