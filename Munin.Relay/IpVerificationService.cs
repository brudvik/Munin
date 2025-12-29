using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Munin.Relay;

/// <summary>
/// Service for verifying the public IP address and ensuring VPN is active.
/// Uses multiple methods: external API, GeoIP lookup, and comparison.
/// </summary>
public class IpVerificationService
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly RelayConfiguration _config;
    private string? _lastKnownIp;
    private string? _lastKnownCountry;
    private string? _lastKnownOrg;

    /// <summary>
    /// The current public IP address.
    /// </summary>
    public string? CurrentIp => _lastKnownIp;

    /// <summary>
    /// The country of the current IP.
    /// </summary>
    public string? CurrentCountry => _lastKnownCountry;

    /// <summary>
    /// The organization/ISP of the current IP.
    /// </summary>
    public string? CurrentOrganization => _lastKnownOrg;

    /// <summary>
    /// Event raised when the IP address changes.
    /// </summary>
    public event EventHandler<IpChangedEventArgs>? IpChanged;

    public IpVerificationService(RelayConfiguration config)
    {
        _config = config;
        _logger = Log.ForContext<IpVerificationService>();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>
    /// Performs a comprehensive IP verification check.
    /// </summary>
    /// <returns>Verification result with details.</returns>
    public async Task<IpVerificationResult> VerifyAsync()
    {
        var result = new IpVerificationResult();

        try
        {
            // Method 1: Get IP from multiple sources for reliability
            var ipFromIpify = await GetIpFromServiceAsync("https://api.ipify.org");
            var ipFromIfconfig = await GetIpFromServiceAsync("https://ifconfig.me/ip");

            // Verify consistency
            if (!string.IsNullOrEmpty(ipFromIpify) && !string.IsNullOrEmpty(ipFromIfconfig))
            {
                if (ipFromIpify != ipFromIfconfig)
                {
                    _logger.Warning("IP mismatch between services: ipify={Ip1}, ifconfig={Ip2}", 
                        ipFromIpify, ipFromIfconfig);
                }
            }

            result.IpAddress = ipFromIpify ?? ipFromIfconfig;

            if (string.IsNullOrEmpty(result.IpAddress))
            {
                result.Success = false;
                result.ErrorMessage = "Could not determine public IP from any service";
                return result;
            }

            // Method 2: GeoIP lookup for country and organization
            var geoInfo = await GetGeoInfoAsync(result.IpAddress);
            if (geoInfo != null)
            {
                result.Country = geoInfo.Country;
                result.CountryCode = geoInfo.CountryCode;
                result.Organization = geoInfo.Org;
                result.City = geoInfo.City;
                result.Region = geoInfo.Region;

                // Check if it looks like a VPN
                result.IsLikelyVpn = IsLikelyVpn(geoInfo.Org);
            }

            // Method 3: Verify expected country if configured
            if (!string.IsNullOrEmpty(_config.ExpectedCountryCode))
            {
                result.CountryMatches = string.Equals(
                    result.CountryCode, 
                    _config.ExpectedCountryCode, 
                    StringComparison.OrdinalIgnoreCase);

                if (!result.CountryMatches)
                {
                    _logger.Warning("Country mismatch! Expected {Expected}, got {Actual}",
                        _config.ExpectedCountryCode, result.CountryCode);
                }
            }
            else
            {
                result.CountryMatches = true; // No verification requested
            }

            // Check if IP changed
            if (_lastKnownIp != null && _lastKnownIp != result.IpAddress)
            {
                // Don't log here - let the event handler do it to avoid duplicate messages
                IpChanged?.Invoke(this, new IpChangedEventArgs(_lastKnownIp, result.IpAddress));
            }

            // Update cached values
            _lastKnownIp = result.IpAddress;
            _lastKnownCountry = result.Country;
            _lastKnownOrg = result.Organization;

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "IP verification failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Gets the public IP from a simple text-returning service.
    /// </summary>
    private async Task<string?> GetIpFromServiceAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.Debug("Failed to get IP from {Url}: {Error}", url, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Gets GeoIP information from ip-api.com.
    /// </summary>
    private async Task<GeoIpInfo?> GetGeoInfoAsync(string ip)
    {
        try
        {
            // ip-api.com is free and doesn't require API key
            var url = $"http://ip-api.com/json/{ip}?fields=status,message,country,countryCode,region,city,org";
            var response = await _httpClient.GetFromJsonAsync<GeoIpInfo>(url);
            
            if (response?.Status == "success")
            {
                return response;
            }
            
            _logger.Warning("GeoIP lookup failed: {Message}", response?.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug("GeoIP lookup failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Checks if the organization name suggests a VPN provider.
    /// </summary>
    private static bool IsLikelyVpn(string? org)
    {
        if (string.IsNullOrEmpty(org)) return false;

        // Full names and partial matches for common VPN providers
        // Some GeoIP services truncate organization names, so we check for prefixes too
        var vpnKeywords = new[]
        {
            // VPN providers (including partial matches for truncated names)
            "VPN", "Express", "Nord", "Surfshark", "Proton", "CyberGhost",
            "Private Internet", "PIA", "Mullvad", "Windscribe", "IPVanish",
            "HideMyAss", "HMA", "TunnelBear", "Hotspot Shield", "ZenMate", "TorGuard",
            "Hide.me", "VyprVPN", "Vypr", "StrongVPN", "PureVPN", "Astrill",
            
            // Infrastructure providers commonly used for VPN
            "M247", "Choopa", "Vultr", "DigitalOcean", "Linode", "AWS", "Azure",
            "Google Cloud", "OVH", "Hetzner", "Contabo", "Datacamp", "Leaseweb",
            "QuadraNet", "Cogent", "GTT", "Zenlayer", "HostWinds", "DataWeb",
            "Voxility", "HostHatch", "ColoCrossing", "Psychz", "FDCServers"
        };

        return vpnKeywords.Any(keyword => 
            org.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Result of an IP verification check.
/// </summary>
public class IpVerificationResult
{
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Organization { get; set; }
    public bool IsLikelyVpn { get; set; }
    public bool CountryMatches { get; set; }
    public string? ErrorMessage { get; set; }

    public override string ToString()
    {
        if (!Success)
            return $"Verification failed: {ErrorMessage}";

        return $"IP: {IpAddress} | Location: {City}, {Country} ({CountryCode}) | Org: {Organization} | VPN: {(IsLikelyVpn ? "Yes" : "Unknown")}";
    }
}

/// <summary>
/// Event args for IP change events.
/// </summary>
public class IpChangedEventArgs : EventArgs
{
    public string OldIp { get; }
    public string NewIp { get; }

    public IpChangedEventArgs(string oldIp, string newIp)
    {
        OldIp = oldIp;
        NewIp = newIp;
    }
}

/// <summary>
/// GeoIP information from ip-api.com.
/// </summary>
internal class GeoIpInfo
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("org")]
    public string? Org { get; set; }
}
