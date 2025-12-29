using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Munin.Agent.Configuration;
using Munin.Agent.Services;
using Munin.Core.Services;

namespace Munin.Agent.Commands;

/// <summary>
/// Interactive setup wizard for initial agent configuration.
/// </summary>
public static class SetupWizard
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("===========================================");
        Console.WriteLine("  Munin Agent Setup Wizard");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        // Get agent name
        Console.Write("Agent name [MuninAgent]: ");
        var name = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(name)) name = "MuninAgent";

        // Get control server settings
        Console.Write("Control server port [5550]: ");
        var portStr = Console.ReadLine();
        var port = 5550;
        if (!string.IsNullOrWhiteSpace(portStr) && int.TryParse(portStr, out var parsedPort))
            port = parsedPort;

        // Generate auth token
        var authToken = AgentSecurity.GenerateAuthToken();
        Console.WriteLine($"Generated auth token: {authToken}");
        Console.WriteLine("IMPORTANT: Store this token securely - you'll need it to connect from Munin UI!");

        // Create configuration
        var config = new AgentConfiguration
        {
            Name = name,
            ControlServer = new ControlServerConfiguration
            {
                Enabled = true,
                Port = port,
                AuthToken = new EncryptedValue { Data = authToken }  // Will be encrypted later
            }
        };

        // Get master password for encryption
        Console.WriteLine();
        Console.Write("Master password for encryption: ");
        var password = ReadPassword();
        Console.WriteLine();
        
        Console.Write("Confirm password: ");
        var confirmPassword = ReadPassword();
        Console.WriteLine();

        if (password != confirmPassword)
        {
            Console.WriteLine("Passwords do not match!");
            return 1;
        }

        // Save configuration
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "agent.json");
        var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        await File.WriteAllTextAsync(configPath, configJson);

        Console.WriteLine();
        Console.WriteLine($"Configuration saved to: {configPath}");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("1. Run 'MuninAgent gencert' to generate a TLS certificate");
        Console.WriteLine("2. Run 'MuninAgent encrypt agent.json' to encrypt sensitive data");
        Console.WriteLine("3. Edit agent.json to add IRC server configurations");
        Console.WriteLine("4. Run 'MuninAgent' to start the agent");

        return 0;
    }

    private static string ReadPassword()
    {
        var password = new List<char>();
        ConsoleKeyInfo key;
        
        do
        {
            key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Backspace && password.Count > 0)
            {
                password.RemoveAt(password.Count - 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Add(key.KeyChar);
                Console.Write("*");
            }
        } while (key.Key != ConsoleKey.Enter);

        return new string(password.ToArray());
    }
}

/// <summary>
/// Encrypts sensitive values in a configuration file.
/// </summary>
public static class EncryptConfigCommand
{
    public static async Task<int> RunAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Configuration file not found: {configPath}");
            return 1;
        }

        Console.Write("Master password: ");
        var password = ReadPassword();
        Console.WriteLine();

        try
        {
            var configService = new AgentConfigurationService(configPath);
            await configService.LoadAsync();
            
            if (!configService.Unlock(password))
            {
                // New encryption - set up the password
            }

            await configService.EnableEncryptionAsync(password);
            await configService.SaveAsync();

            Console.WriteLine("Configuration encrypted successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string ReadPassword()
    {
        var password = new List<char>();
        ConsoleKeyInfo key;
        
        do
        {
            key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Backspace && password.Count > 0)
            {
                password.RemoveAt(password.Count - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Add(key.KeyChar);
                Console.Write("*");
            }
        } while (key.Key != ConsoleKey.Enter);

        return new string(password.ToArray());
    }
}

/// <summary>
/// Decrypts and shows a configuration file (for debugging).
/// </summary>
public static class DecryptConfigCommand
{
    public static async Task<int> RunAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Configuration file not found: {configPath}");
            return 1;
        }

        Console.Write("Master password: ");
        var password = ReadPassword();
        Console.WriteLine();

        try
        {
            var configService = new AgentConfigurationService(configPath);
            await configService.LoadAsync();

            if (!configService.Unlock(password))
            {
                Console.WriteLine("Invalid password or configuration is not encrypted.");
                return 1;
            }

            // Show decrypted values
            var config = configService.Configuration;
            Console.WriteLine();
            Console.WriteLine("Decrypted configuration:");
            Console.WriteLine("========================");
            Console.WriteLine($"Agent ID: {config.AgentId}");
            Console.WriteLine($"Name: {config.Name}");
            Console.WriteLine($"Control Port: {config.ControlServer.Port}");
            
            var authToken = configService.GetDecryptedValue(config.ControlServer.AuthToken);
            Console.WriteLine($"Auth Token: {authToken}");

            foreach (var server in config.Servers)
            {
                Console.WriteLine();
                Console.WriteLine($"Server: {server.Name} ({server.Address}:{server.Port})");
                var nickservPw = configService.GetDecryptedValue(server.NickservPasswordEncrypted);
                if (!string.IsNullOrEmpty(nickservPw))
                    Console.WriteLine($"  NickServ Password: {nickservPw}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string ReadPassword()
    {
        var password = new List<char>();
        ConsoleKeyInfo key;
        
        do
        {
            key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Backspace && password.Count > 0)
            {
                password.RemoveAt(password.Count - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Add(key.KeyChar);
                Console.Write("*");
            }
        } while (key.Key != ConsoleKey.Enter);

        return new string(password.ToArray());
    }
}

/// <summary>
/// Generates a self-signed TLS certificate for the control server.
/// </summary>
public static class GenerateCertificateCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var outputPath = args.Length > 0 ? args[0] : "agent.pfx";
        var password = args.Length > 1 ? args[1] : null;

        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Certificate password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        try
        {
            Console.WriteLine("Generating self-signed certificate...");

            var subject = new X500DistinguishedName("CN=MuninAgent,O=Munin,C=NO");

            using var rsa = RSA.Create(4096);
            var request = new CertificateRequest(
                subject,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Add extensions
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                    false));

            // Subject Alternative Names
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(Environment.MachineName);
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
            sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(sanBuilder.Build());

            // Create self-signed certificate valid for 10 years
            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(10));

            // Export to PFX
            var pfxBytes = cert.Export(X509ContentType.Pfx, password);
            await File.WriteAllBytesAsync(outputPath, pfxBytes);

            Console.WriteLine($"Certificate saved to: {outputPath}");
            Console.WriteLine($"Valid until: {cert.NotAfter:yyyy-MM-dd}");
            Console.WriteLine($"Thumbprint: {cert.Thumbprint}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating certificate: {ex.Message}");
            return 1;
        }
    }

    private static string ReadPassword()
    {
        var password = new List<char>();
        ConsoleKeyInfo key;
        
        do
        {
            key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Backspace && password.Count > 0)
            {
                password.RemoveAt(password.Count - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Add(key.KeyChar);
                Console.Write("*");
            }
        } while (key.Key != ConsoleKey.Enter);

        return new string(password.ToArray());
    }
}
