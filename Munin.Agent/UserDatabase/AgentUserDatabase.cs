using System.Text.Json;
using Munin.Agent.Services;

namespace Munin.Agent.UserDatabase;

/// <summary>
/// Eggdrop-style user flags for access control.
/// </summary>
[Flags]
public enum UserFlags : uint
{
    None = 0,
    
    // ======== Global Flags ========
    
    /// <summary>
    /// Bot owner - full access to everything.
    /// Eggdrop: n
    /// </summary>
    Owner = 1 << 0,
    
    /// <summary>
    /// Bot master - can add/remove users, change settings.
    /// Eggdrop: m
    /// </summary>
    Master = 1 << 1,
    
    /// <summary>
    /// Global operator - op in all channels.
    /// Eggdrop: o
    /// </summary>
    Operator = 1 << 2,
    
    /// <summary>
    /// Global voice - voice in all channels.
    /// Eggdrop: v
    /// </summary>
    Voice = 1 << 3,
    
    /// <summary>
    /// Party line access.
    /// Eggdrop: p
    /// </summary>
    Party = 1 << 4,
    
    /// <summary>
    /// File area access.
    /// Eggdrop: x
    /// </summary>
    File = 1 << 5,
    
    /// <summary>
    /// Janitor - can kick/ban but not op others.
    /// Eggdrop: j
    /// </summary>
    Janitor = 1 << 6,
    
    /// <summary>
    /// Friend - won't be banned/kicked by bot.
    /// Eggdrop: f
    /// </summary>
    Friend = 1 << 7,
    
    /// <summary>
    /// Auto-op on join.
    /// Eggdrop: a (channel flag)
    /// </summary>
    AutoOp = 1 << 8,
    
    /// <summary>
    /// Auto-voice on join.
    /// Eggdrop: g (channel flag)
    /// </summary>
    AutoVoice = 1 << 9,
    
    /// <summary>
    /// Bot can be controlled via DCC.
    /// Eggdrop: t
    /// </summary>
    Botnet = 1 << 10,
    
    /// <summary>
    /// User is another bot.
    /// Eggdrop: b
    /// </summary>
    Bot = 1 << 11,
    
    /// <summary>
    /// User is ignored.
    /// Eggdrop: d
    /// </summary>
    Deop = 1 << 12,
    
    /// <summary>
    /// User is banned.
    /// Eggdrop: k
    /// </summary>
    Kick = 1 << 13
}

/// <summary>
/// Represents a user in the agent's user database.
/// </summary>
public class AgentUser
{
    /// <summary>
    /// Unique handle/username.
    /// </summary>
    public string Handle { get; set; } = "";

    /// <summary>
    /// Optional encrypted password for DCC/partyline access.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Hostmasks that identify this user (e.g., "*!*@*.example.com").
    /// </summary>
    public List<string> Hostmasks { get; set; } = new();

    /// <summary>
    /// Global flags for this user.
    /// </summary>
    public UserFlags GlobalFlags { get; set; }

    /// <summary>
    /// Per-channel flags. Key is channel name (lowercase).
    /// </summary>
    public Dictionary<string, UserFlags> ChannelFlags { get; set; } = new();

    /// <summary>
    /// When the user was added.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time the user was seen.
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Optional info line (like Eggdrop's user info).
    /// </summary>
    public string? Info { get; set; }

    /// <summary>
    /// Additional user-defined data.
    /// </summary>
    public Dictionary<string, string> UserData { get; set; } = new();

    /// <summary>
    /// Checks if user has a specific global flag.
    /// </summary>
    public bool HasFlag(UserFlags flag)
    {
        return GlobalFlags.HasFlag(flag);
    }

    /// <summary>
    /// Checks if user has a specific flag for a channel.
    /// Global flags cascade to channels.
    /// </summary>
    public bool HasFlag(UserFlags flag, string? channel)
    {
        if (string.IsNullOrEmpty(channel))
            return HasFlag(flag);

        // Global flags apply everywhere
        if (GlobalFlags.HasFlag(flag))
            return true;

        // Check channel-specific flags
        var key = channel.ToLowerInvariant();
        return ChannelFlags.TryGetValue(key, out var channelFlags) && channelFlags.HasFlag(flag);
    }

    /// <summary>
    /// Gets flags as Eggdrop-style string.
    /// </summary>
    public string GetFlagString()
    {
        var flags = new List<char>();
        
        if (HasFlag(UserFlags.Owner)) flags.Add('n');
        if (HasFlag(UserFlags.Master)) flags.Add('m');
        if (HasFlag(UserFlags.Operator)) flags.Add('o');
        if (HasFlag(UserFlags.Voice)) flags.Add('v');
        if (HasFlag(UserFlags.Party)) flags.Add('p');
        if (HasFlag(UserFlags.File)) flags.Add('x');
        if (HasFlag(UserFlags.Janitor)) flags.Add('j');
        if (HasFlag(UserFlags.Friend)) flags.Add('f');
        if (HasFlag(UserFlags.AutoOp)) flags.Add('a');
        if (HasFlag(UserFlags.AutoVoice)) flags.Add('g');
        if (HasFlag(UserFlags.Botnet)) flags.Add('t');
        if (HasFlag(UserFlags.Bot)) flags.Add('b');
        if (HasFlag(UserFlags.Deop)) flags.Add('d');
        if (HasFlag(UserFlags.Kick)) flags.Add('k');

        return flags.Count > 0 ? new string(flags.ToArray()) : "-";
    }

    /// <summary>
    /// Parses Eggdrop-style flag string and returns UserFlags.
    /// </summary>
    public static UserFlags ParseFlags(string flagString)
    {
        var flags = UserFlags.None;

        foreach (var c in flagString)
        {
            flags |= c switch
            {
                'n' => UserFlags.Owner,
                'm' => UserFlags.Master,
                'o' => UserFlags.Operator,
                'v' => UserFlags.Voice,
                'p' => UserFlags.Party,
                'x' => UserFlags.File,
                'j' => UserFlags.Janitor,
                'f' => UserFlags.Friend,
                'a' => UserFlags.AutoOp,
                'g' => UserFlags.AutoVoice,
                't' => UserFlags.Botnet,
                'b' => UserFlags.Bot,
                'd' => UserFlags.Deop,
                'k' => UserFlags.Kick,
                _ => UserFlags.None
            };
        }

        return flags;
    }
}

/// <summary>
/// User database wrapper.
/// </summary>
public class UserDatabase
{
    public List<AgentUser> Users { get; set; } = new();
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Service for managing the agent's user database.
/// </summary>
public class AgentUserDatabaseService
{
    private readonly Serilog.ILogger _logger;
    private readonly string _databasePath;
    private readonly Munin.Core.Services.EncryptionService _encryptionService;
    private UserDatabase _database = new();
    private bool _isLoaded;

    public AgentUserDatabaseService(string databasePath, Munin.Core.Services.EncryptionService encryptionService)
    {
        _logger = Serilog.Log.ForContext<AgentUserDatabaseService>();
        _databasePath = databasePath;
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Loads the user database from disk.
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(_databasePath))
        {
            _logger.Information("User database not found, starting with empty database");
            _database = new UserDatabase();
            _isLoaded = true;
            return;
        }

        try
        {
            var content = await File.ReadAllBytesAsync(_databasePath);

            // Check if encrypted (starts with magic bytes)
            if (Munin.Core.Services.EncryptionService.IsEncrypted(content))
            {
                if (!_encryptionService.IsUnlocked)
                    throw new InvalidOperationException("User database is encrypted but encryption service is not unlocked");

                content = _encryptionService.Decrypt(content);
            }

            var json = System.Text.Encoding.UTF8.GetString(content);
            _database = JsonSerializer.Deserialize<UserDatabase>(json) ?? new UserDatabase();
            _isLoaded = true;

            _logger.Information("Loaded {Count} users from database", _database.Users.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load user database");
            throw;
        }
    }

    /// <summary>
    /// Saves the user database to disk.
    /// </summary>
    public async Task SaveAsync(bool encrypt = true)
    {
        if (!_isLoaded)
            return;

        _database.LastModified = DateTime.UtcNow;

        try
        {
            var json = JsonSerializer.Serialize(_database, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            var content = System.Text.Encoding.UTF8.GetBytes(json);

            // Encrypt if encryption service is unlocked
            if (encrypt && _encryptionService.IsUnlocked)
            {
                content = _encryptionService.Encrypt(content);
            }

            await File.WriteAllBytesAsync(_databasePath, content);
            _logger.Debug("Saved user database");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save user database");
            throw;
        }
    }

    /// <summary>
    /// Gets all users.
    /// </summary>
    public IReadOnlyList<AgentUser> GetUsers()
    {
        return _database.Users.AsReadOnly();
    }

    /// <summary>
    /// Gets a user by handle.
    /// </summary>
    public AgentUser? GetUser(string handle)
    {
        return _database.Users.FirstOrDefault(u => 
            u.Handle.Equals(handle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Matches a hostmask to a user.
    /// </summary>
    public AgentUser? MatchUser(string hostmask)
    {
        foreach (var user in _database.Users)
        {
            foreach (var pattern in user.Hostmasks)
            {
                if (AgentSecurity.MatchHostmask(pattern, hostmask))
                {
                    return user;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Adds or updates a user.
    /// </summary>
    public void SetUser(AgentUser user)
    {
        var existing = GetUser(user.Handle);
        if (existing != null)
        {
            _database.Users.Remove(existing);
        }
        _database.Users.Add(user);
        _logger.Information("Set user: {Handle}", user.Handle);
    }

    /// <summary>
    /// Removes a user.
    /// </summary>
    public bool RemoveUser(string handle)
    {
        var user = GetUser(handle);
        if (user != null)
        {
            _database.Users.Remove(user);
            _logger.Information("Removed user: {Handle}", handle);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sets global flags for a user.
    /// </summary>
    public bool SetFlags(string handle, UserFlags flags)
    {
        var user = GetUser(handle);
        if (user != null)
        {
            user.GlobalFlags = flags;
            _logger.Information("Set flags for {Handle}: {Flags}", handle, user.GetFlagString());
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sets channel flags for a user.
    /// </summary>
    public bool SetChannelFlags(string handle, string channel, UserFlags flags)
    {
        var user = GetUser(handle);
        if (user != null)
        {
            user.ChannelFlags[channel.ToLowerInvariant()] = flags;
            _logger.Information("Set channel flags for {Handle} in {Channel}: {Flags}", 
                handle, channel, flags);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Adds flags to a user (keeps existing flags).
    /// </summary>
    public bool AddFlags(string handle, string flagString, string? channel = null)
    {
        var user = GetUser(handle);
        if (user == null) return false;

        var newFlags = AgentUser.ParseFlags(flagString);

        if (string.IsNullOrEmpty(channel))
        {
            user.GlobalFlags |= newFlags;
        }
        else
        {
            var key = channel.ToLowerInvariant();
            if (!user.ChannelFlags.ContainsKey(key))
                user.ChannelFlags[key] = UserFlags.None;
            user.ChannelFlags[key] |= newFlags;
        }

        return true;
    }

    /// <summary>
    /// Removes flags from a user.
    /// </summary>
    public bool RemoveFlags(string handle, string flagString, string? channel = null)
    {
        var user = GetUser(handle);
        if (user == null) return false;

        var removeFlags = AgentUser.ParseFlags(flagString);

        if (string.IsNullOrEmpty(channel))
        {
            user.GlobalFlags &= ~removeFlags;
        }
        else
        {
            var key = channel.ToLowerInvariant();
            if (user.ChannelFlags.ContainsKey(key))
                user.ChannelFlags[key] &= ~removeFlags;
        }

        return true;
    }

    /// <summary>
    /// Adds a hostmask to a user.
    /// </summary>
    public bool AddHostmask(string handle, string hostmask)
    {
        var user = GetUser(handle);
        if (user != null && !user.Hostmasks.Contains(hostmask, StringComparer.OrdinalIgnoreCase))
        {
            user.Hostmasks.Add(hostmask);
            _logger.Information("Added hostmask {Hostmask} to {Handle}", hostmask, handle);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a hostmask from a user.
    /// </summary>
    public bool RemoveHostmask(string handle, string hostmask)
    {
        var user = GetUser(handle);
        if (user != null)
        {
            var removed = user.Hostmasks.RemoveAll(h => 
                h.Equals(hostmask, StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }
        return false;
    }

    /// <summary>
    /// Updates last seen time for a user.
    /// </summary>
    public void UpdateLastSeen(string handle)
    {
        var user = GetUser(handle);
        if (user != null)
        {
            user.LastSeen = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Checks if a user has permission to perform an action.
    /// Owners and masters can do anything.
    /// </summary>
    public bool CheckPermission(string hostmask, UserFlags requiredFlags, string? channel = null)
    {
        var user = MatchUser(hostmask);
        if (user == null) return false;

        // Owners and masters have all permissions
        if (user.HasFlag(UserFlags.Owner) || user.HasFlag(UserFlags.Master))
            return true;

        return user.HasFlag(requiredFlags, channel);
    }
}
