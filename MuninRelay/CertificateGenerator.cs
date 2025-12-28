using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace MuninRelay;

/// <summary>
/// Utility for generating self-signed SSL certificates for the relay.
/// </summary>
public static class CertificateGenerator
{
    private static readonly ILogger Logger = Log.ForContext(typeof(CertificateGenerator));

    /// <summary>
    /// Generates a self-signed certificate for the relay server.
    /// </summary>
    /// <param name="subjectName">The subject name (e.g., "MuninRelay").</param>
    /// <param name="validityDays">Number of days the certificate is valid.</param>
    /// <returns>The generated certificate.</returns>
    public static X509Certificate2 GenerateSelfSigned(string subjectName = "MuninRelay", int validityDays = 365)
    {
        Logger.Information("Generating self-signed certificate for {Subject}", subjectName);

        using var rsa = RSA.Create(4096);

        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add key usage extensions
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                critical: true));

        // Add Subject Alternative Name (SAN)
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddDays(validityDays);

        var certificate = request.CreateSelfSigned(notBefore, notAfter);

        Logger.Information("Certificate generated. Valid from {From} to {To}",
            notBefore.ToString("yyyy-MM-dd"),
            notAfter.ToString("yyyy-MM-dd"));

        return certificate;
    }

    /// <summary>
    /// Saves a certificate to a PFX file.
    /// </summary>
    /// <param name="certificate">The certificate to save.</param>
    /// <param name="filePath">Path to save the PFX file.</param>
    /// <param name="password">Password to protect the PFX file.</param>
    public static void SaveToPfx(X509Certificate2 certificate, string filePath, string password)
    {
        var pfxData = certificate.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(filePath, pfxData);
        Logger.Information("Certificate saved to {Path}", filePath);
    }

    /// <summary>
    /// Generates a certificate and saves it to the default location.
    /// </summary>
    /// <param name="config">The relay configuration.</param>
    public static void EnsureCertificateExists(RelayConfiguration config)
    {
        if (!string.IsNullOrEmpty(config.CertificatePath) && File.Exists(config.CertificatePath))
        {
            Logger.Information("Using existing certificate: {Path}", config.CertificatePath);
            return;
        }

        var defaultPath = Path.Combine(
            Path.GetDirectoryName(typeof(CertificateGenerator).Assembly.Location) ?? ".",
            "relay-cert.pfx");

        if (File.Exists(defaultPath))
        {
            Logger.Information("Using existing certificate: {Path}", defaultPath);
            config.CertificatePath = defaultPath;
            config.CertificatePassword = "MuninRelay"; // Default password
            return;
        }

        // Generate new certificate
        Logger.Information("No certificate found, generating new self-signed certificate...");

        var password = GenerateRandomPassword();
        var cert = GenerateSelfSigned();
        SaveToPfx(cert, defaultPath, password);

        config.CertificatePath = defaultPath;
        config.CertificatePassword = password;

        // Save the updated config
        config.Save(Path.Combine(
            Path.GetDirectoryName(typeof(CertificateGenerator).Assembly.Location) ?? ".",
            "config.json"));

        Logger.Information("Certificate generated and configuration updated");
    }

    /// <summary>
    /// Generates a random password for the certificate.
    /// </summary>
    private static string GenerateRandomPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes);
    }
}
