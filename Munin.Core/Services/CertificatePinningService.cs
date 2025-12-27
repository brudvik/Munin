using Serilog;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Munin.Core.Services;

/// <summary>
/// Manages SSL certificate pinning for IRC servers.
/// Stores certificate fingerprints and alerts when they change.
/// </summary>
/// <remarks>
/// <para>Certificate pinning helps detect man-in-the-middle attacks by alerting
/// when a server's SSL certificate changes unexpectedly.</para>
/// </remarks>
public class CertificatePinningService
{
    private static readonly Lazy<CertificatePinningService> _instance = new(() => new CertificatePinningService());
    
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static CertificatePinningService Instance => _instance.Value;
    
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, CertificatePin> _pins = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _pinsFilePath;
    
    /// <summary>
    /// Raised when a certificate change is detected.
    /// </summary>
    public event EventHandler<CertificateChangedEventArgs>? CertificateChanged;
    
    /// <summary>
    /// Raised when a new certificate is seen for the first time.
    /// </summary>
    public event EventHandler<CertificateChangedEventArgs>? NewCertificateSeen;
    
    private CertificatePinningService()
    {
        _logger = SerilogConfig.ForContext<CertificatePinningService>();
        _pinsFilePath = Path.Combine(
            PortableMode.BasePath,
            "certificate_pins.json");
        
        LoadPins();
    }
    
    /// <summary>
    /// Validates a server's certificate and checks if it has changed.
    /// </summary>
    /// <param name="serverHost">The server hostname.</param>
    /// <param name="certificate">The server's certificate.</param>
    /// <returns>A validation result indicating if the certificate is OK, new, or changed.</returns>
    public CertificateValidationResult ValidateCertificate(string serverHost, X509Certificate2 certificate)
    {
        var fingerprint = GetFingerprint(certificate);
        var now = DateTime.UtcNow;
        
        if (_pins.TryGetValue(serverHost, out var existingPin))
        {
            if (existingPin.Fingerprint == fingerprint)
            {
                // Same certificate - all good
                existingPin.LastSeen = now;
                SavePins();
                return CertificateValidationResult.Valid;
            }
            else
            {
                // Certificate changed!
                _logger.Warning("Certificate changed for {Host}! Old: {Old}, New: {New}",
                    serverHost, existingPin.Fingerprint[..16], fingerprint[..16]);
                
                var args = new CertificateChangedEventArgs(
                    serverHost,
                    existingPin.Fingerprint,
                    fingerprint,
                    existingPin.FirstSeen,
                    certificate.Subject,
                    certificate.NotAfter);
                
                CertificateChanged?.Invoke(this, args);
                
                return CertificateValidationResult.Changed;
            }
        }
        else
        {
            // First time seeing this server
            var newPin = new CertificatePin
            {
                ServerHost = serverHost,
                Fingerprint = fingerprint,
                FirstSeen = now,
                LastSeen = now,
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                ExpiresAt = certificate.NotAfter
            };
            
            _pins[serverHost] = newPin;
            SavePins();
            
            _logger.Information("New certificate pinned for {Host}: {Fingerprint}",
                serverHost, fingerprint[..16]);
            
            NewCertificateSeen?.Invoke(this, new CertificateChangedEventArgs(
                serverHost, null, fingerprint, now, certificate.Subject, certificate.NotAfter));
            
            return CertificateValidationResult.NewCertificate;
        }
    }
    
    /// <summary>
    /// Updates the pinned certificate for a server (after user accepts change).
    /// </summary>
    /// <param name="serverHost">The server hostname.</param>
    /// <param name="certificate">The new certificate to pin.</param>
    public void UpdatePin(string serverHost, X509Certificate2 certificate)
    {
        var fingerprint = GetFingerprint(certificate);
        var now = DateTime.UtcNow;
        
        var pin = new CertificatePin
        {
            ServerHost = serverHost,
            Fingerprint = fingerprint,
            FirstSeen = now,
            LastSeen = now,
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            ExpiresAt = certificate.NotAfter
        };
        
        _pins[serverHost] = pin;
        SavePins();
        
        _logger.Information("Certificate pin updated for {Host}", serverHost);
    }
    
    /// <summary>
    /// Removes a pinned certificate.
    /// </summary>
    /// <param name="serverHost">The server hostname.</param>
    public void RemovePin(string serverHost)
    {
        if (_pins.TryRemove(serverHost, out _))
        {
            SavePins();
            _logger.Information("Certificate pin removed for {Host}", serverHost);
        }
    }
    
    /// <summary>
    /// Gets the pinned certificate info for a server.
    /// </summary>
    /// <param name="serverHost">The server hostname.</param>
    /// <returns>The certificate pin info, or null if not pinned.</returns>
    public CertificatePin? GetPin(string serverHost)
    {
        return _pins.TryGetValue(serverHost, out var pin) ? pin : null;
    }
    
    /// <summary>
    /// Gets all pinned certificates.
    /// </summary>
    public IReadOnlyDictionary<string, CertificatePin> GetAllPins() => _pins;
    
    /// <summary>
    /// Calculates the SHA-256 fingerprint of a certificate.
    /// </summary>
    private static string GetFingerprint(X509Certificate2 certificate)
    {
        var hash = SHA256.HashData(certificate.RawData);
        return Convert.ToHexString(hash);
    }
    
    private void LoadPins()
    {
        try
        {
            if (File.Exists(_pinsFilePath))
            {
                var json = File.ReadAllText(_pinsFilePath);
                var pins = JsonSerializer.Deserialize(json, JsonSourceGenerationContext.Default.ListCertificatePin);
                
                if (pins != null)
                {
                    foreach (var pin in pins)
                    {
                        _pins[pin.ServerHost] = pin;
                    }
                }
                
                _logger.Information("Loaded {Count} certificate pins", _pins.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load certificate pins");
        }
    }
    
    private void SavePins()
    {
        try
        {
            var pins = _pins.Values.ToList();
            var json = JsonSerializer.Serialize(pins, JsonSourceGenerationContext.Default.ListCertificatePin);
            
            var directory = Path.GetDirectoryName(_pinsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllText(_pinsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save certificate pins");
        }
    }
}

/// <summary>
/// Represents a pinned SSL certificate for a server.
/// </summary>
public class CertificatePin
{
    /// <summary>
    /// The server hostname.
    /// </summary>
    public string ServerHost { get; set; } = string.Empty;
    
    /// <summary>
    /// The SHA-256 fingerprint of the certificate.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;
    
    /// <summary>
    /// When this certificate was first seen.
    /// </summary>
    public DateTime FirstSeen { get; set; }
    
    /// <summary>
    /// When this certificate was last seen.
    /// </summary>
    public DateTime LastSeen { get; set; }
    
    /// <summary>
    /// The certificate subject.
    /// </summary>
    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// The certificate issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;
    
    /// <summary>
    /// When the certificate expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Result of certificate validation.
/// </summary>
public enum CertificateValidationResult
{
    /// <summary>Certificate matches the pinned fingerprint.</summary>
    Valid,
    
    /// <summary>New certificate for a new server.</summary>
    NewCertificate,
    
    /// <summary>Certificate has changed from the pinned fingerprint.</summary>
    Changed
}

/// <summary>
/// Event arguments for certificate change events.
/// </summary>
public class CertificateChangedEventArgs : EventArgs
{
    /// <summary>Gets the server hostname.</summary>
    public string ServerHost { get; }
    
    /// <summary>Gets the previous fingerprint (null for new certificates).</summary>
    public string? PreviousFingerprint { get; }
    
    /// <summary>Gets the new fingerprint.</summary>
    public string NewFingerprint { get; }
    
    /// <summary>Gets when the previous certificate was first seen.</summary>
    public DateTime PreviousFirstSeen { get; }
    
    /// <summary>Gets the certificate subject.</summary>
    public string Subject { get; }
    
    /// <summary>Gets the certificate expiration date.</summary>
    public DateTime ExpiresAt { get; }
    
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public CertificateChangedEventArgs(
        string serverHost,
        string? previousFingerprint,
        string newFingerprint,
        DateTime previousFirstSeen,
        string subject,
        DateTime expiresAt)
    {
        ServerHost = serverHost;
        PreviousFingerprint = previousFingerprint;
        NewFingerprint = newFingerprint;
        PreviousFirstSeen = previousFirstSeen;
        Subject = subject;
        ExpiresAt = expiresAt;
    }
}
