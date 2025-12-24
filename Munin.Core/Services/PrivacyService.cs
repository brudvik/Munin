using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Munin.Core.Services;

/// <summary>
/// Provides privacy-preserving filename generation for logs and other files.
/// Uses consistent hashing to create anonymous filenames while maintaining
/// the ability to look up the original names when the storage is unlocked.
/// </summary>
/// <remarks>
/// <para>Privacy features:</para>
/// <list type="bullet">
///   <item><description>Server and channel names are hashed to anonymous IDs</description></item>
///   <item><description>Mapping between hashes and real names is stored encrypted</description></item>
///   <item><description>Dates can optionally be obfuscated</description></item>
///   <item><description>Consistent hashing ensures same input = same output</description></item>
/// </list>
/// </remarks>
public class PrivacyService
{
    private readonly SecureStorageService? _storage;
    private readonly Dictionary<string, string> _hashToName = new();
    private readonly Dictionary<string, string> _nameToHash = new();
    private readonly byte[] _hashKey;
    private bool _mappingLoaded;
    
    private const string MappingFileName = "privacy_mapping.json";
    
    /// <summary>
    /// Gets or sets whether privacy mode is enabled.
    /// When enabled, filenames are anonymized.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Gets or sets whether to also obfuscate dates in filenames.
    /// </summary>
    public bool ObfuscateDates { get; set; } = false;
    
    /// <summary>
    /// Gets whether the mappings have been loaded.
    /// </summary>
    public bool IsMappingLoaded => _mappingLoaded;
    
    /// <summary>
    /// Initializes a new instance of the PrivacyService.
    /// </summary>
    /// <param name="storage">The secure storage service for persisting mappings.</param>
    public PrivacyService(SecureStorageService? storage = null)
    {
        _storage = storage;
        
        // Generate a consistent hash key based on machine/user
        // This ensures hashes are consistent across sessions but different per installation
        var keyMaterial = $"{Environment.MachineName}|{Environment.UserName}|IrcClientPrivacy";
        _hashKey = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }
    
    /// <summary>
    /// Anonymizes a server name to a consistent hash.
    /// </summary>
    /// <param name="serverName">The real server name (e.g., "liberachat").</param>
    /// <returns>An anonymized identifier (e.g., "srv_a3f8c2").</returns>
    public string AnonymizeServerName(string serverName)
    {
        if (!IsEnabled || string.IsNullOrEmpty(serverName))
            return SanitizeFileName(serverName);
        
        return GetOrCreateHash("srv", serverName);
    }
    
    /// <summary>
    /// Anonymizes a channel name to a consistent hash.
    /// </summary>
    /// <param name="channelName">The real channel name (e.g., "#norway").</param>
    /// <returns>An anonymized identifier (e.g., "ch_7b4e91").</returns>
    public string AnonymizeChannelName(string channelName)
    {
        if (!IsEnabled || string.IsNullOrEmpty(channelName))
            return SanitizeFileName(channelName);
        
        return GetOrCreateHash("ch", channelName);
    }
    
    /// <summary>
    /// Generates an anonymized log file path.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="channelName">The channel name.</param>
    /// <param name="date">The date for the log file.</param>
    /// <returns>An anonymized relative file path.</returns>
    public string GetAnonymizedLogPath(string serverName, string channelName, DateTime date)
    {
        var anonServer = AnonymizeServerName(serverName);
        var anonChannel = AnonymizeChannelName(channelName);
        var dateStr = ObfuscateDates 
            ? GetObfuscatedDate(date) 
            : date.ToString("yyyy-MM-dd");
        
        return $"logs/{anonServer}/{anonChannel}_{dateStr}.log";
    }
    
    /// <summary>
    /// Looks up the real name for an anonymized hash.
    /// </summary>
    /// <param name="hash">The anonymized hash.</param>
    /// <returns>The real name, or the hash if not found.</returns>
    public string LookupRealName(string hash)
    {
        EnsureMappingLoaded();
        return _hashToName.TryGetValue(hash, out var name) ? name : hash;
    }
    
    /// <summary>
    /// Gets all known server names (for UI display).
    /// </summary>
    /// <returns>Dictionary of hash to real name for servers.</returns>
    public Dictionary<string, string> GetServerMappings()
    {
        EnsureMappingLoaded();
        return _hashToName
            .Where(kvp => kvp.Key.StartsWith("srv_"))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
    
    /// <summary>
    /// Gets all known channel names for a server.
    /// </summary>
    /// <returns>Dictionary of hash to real name for channels.</returns>
    public Dictionary<string, string> GetChannelMappings()
    {
        EnsureMappingLoaded();
        return _hashToName
            .Where(kvp => kvp.Key.StartsWith("ch_"))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
    
    /// <summary>
    /// Persists the privacy mappings to storage.
    /// </summary>
    public async Task SaveMappingsAsync()
    {
        if (_storage == null || !_storage.IsUnlocked)
            return;
        
        var mapping = new PrivacyMapping
        {
            HashToName = new Dictionary<string, string>(_hashToName),
            Version = 1
        };
        
        await _storage.WriteJsonAsync(MappingFileName, mapping, JsonSourceGenerationContext.Default.PrivacyMapping);
    }
    
    /// <summary>
    /// Loads the privacy mappings from storage.
    /// </summary>
    public async Task LoadMappingsAsync()
    {
        if (_storage == null || !_storage.IsUnlocked)
            return;
        
        try
        {
            var mapping = await _storage.ReadJsonAsync(MappingFileName, JsonSourceGenerationContext.Default.PrivacyMapping);
            if (mapping?.HashToName != null)
            {
                _hashToName.Clear();
                _nameToHash.Clear();
                
                foreach (var kvp in mapping.HashToName)
                {
                    _hashToName[kvp.Key] = kvp.Value;
                    _nameToHash[kvp.Value.ToLowerInvariant()] = kvp.Key;
                }
            }
            _mappingLoaded = true;
        }
        catch
        {
            _mappingLoaded = true; // Mark as loaded even on error to avoid retrying
        }
    }
    
    private string GetOrCreateHash(string prefix, string name)
    {
        EnsureMappingLoaded();
        
        var normalizedName = name.ToLowerInvariant();
        
        if (_nameToHash.TryGetValue(normalizedName, out var existingHash))
            return existingHash;
        
        // Create a new hash
        var hash = ComputeHash(prefix, name);
        
        // Store mapping
        _hashToName[hash] = name;
        _nameToHash[normalizedName] = hash;
        
        // Save mappings asynchronously (fire and forget)
        _ = SaveMappingsAsync();
        
        return hash;
    }
    
    private string ComputeHash(string prefix, string name)
    {
        // Use HMAC-SHA256 with our installation-specific key
        using var hmac = new HMACSHA256(_hashKey);
        var inputBytes = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
        var hashBytes = hmac.ComputeHash(inputBytes);
        
        // Take first 6 characters of hex (24 bits = ~16 million possibilities)
        var hashHex = Convert.ToHexString(hashBytes)[..6].ToLowerInvariant();
        
        return $"{prefix}_{hashHex}";
    }
    
    private string GetObfuscatedDate(DateTime date)
    {
        // Obfuscate date by hashing it with a weekly granularity
        // This groups logs by week instead of exact date
        var weekStart = date.AddDays(-(int)date.DayOfWeek);
        using var hmac = new HMACSHA256(_hashKey);
        var inputBytes = Encoding.UTF8.GetBytes(weekStart.ToString("yyyy-MM-dd"));
        var hashBytes = hmac.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
    }
    
    /// <summary>
    /// Initializes the privacy service by loading mappings.
    /// Call this after secure storage is unlocked.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!_mappingLoaded && _storage != null && _storage.IsUnlocked)
        {
            await LoadMappingsAsync();
        }
    }
    
    private void EnsureMappingLoaded()
    {
        // Just check the flag - mappings should be loaded via InitializeAsync
        // This prevents blocking the UI thread
        if (!_mappingLoaded)
        {
            _mappingLoaded = true; // Mark as loaded to prevent repeated attempts
        }
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
}

/// <summary>
/// Serialization model for privacy mappings.
/// </summary>
internal class PrivacyMapping
{
    public Dictionary<string, string> HashToName { get; set; } = new();
    public int Version { get; set; } = 1;
}
