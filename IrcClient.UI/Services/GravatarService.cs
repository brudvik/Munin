using System.Security.Cryptography;
using System.Text;

namespace IrcClient.UI.Services;

/// <summary>
/// Service for generating Gravatar/Libravatar URLs from IRC user idents.
/// </summary>
/// <remarks>
/// <para>Generates consistent avatar images for IRC users based on their user@host mask.</para>
/// <para>Uses the "identicon" fallback style for users without a registered Gravatar,
/// which generates a unique geometric pattern based on the email hash.</para>
/// </remarks>
public static class GravatarService
{
    private const string GravatarBaseUrl = "https://www.gravatar.com/avatar/";
    private const string LibravatarBaseUrl = "https://seccdn.libravatar.org/avatar/";
    private const int DefaultSize = 32;

    /// <summary>
    /// Gets a Gravatar URL for the given email or IRC ident.
    /// Uses the user@host as a pseudo-email for IRC users.
    /// </summary>
    public static string GetGravatarUrl(string? username, string? hostname, int size = DefaultSize)
    {
        // Create a pseudo-email from IRC ident
        var email = CreatePseudoEmail(username, hostname);
        var hash = GetMd5Hash(email.ToLowerInvariant());
        
        // d=identicon gives us a generated geometric pattern for users without gravatar
        // d=retro gives us 8-bit style avatars
        // d=monsterid gives us monster-style avatars
        // d=wavatar gives us face-like avatars
        return $"{GravatarBaseUrl}{hash}?s={size}&d=identicon&r=g";
    }

    /// <summary>
    /// Gets a Libravatar URL (federated, privacy-friendly alternative).
    /// </summary>
    public static string GetLibravatarUrl(string? username, string? hostname, int size = DefaultSize)
    {
        var email = CreatePseudoEmail(username, hostname);
        var hash = GetMd5Hash(email.ToLowerInvariant());
        return $"{LibravatarBaseUrl}{hash}?s={size}&d=identicon";
    }

    /// <summary>
    /// Creates a pseudo-email from IRC username and hostname.
    /// </summary>
    private static string CreatePseudoEmail(string? username, string? hostname)
    {
        // If we have both, use user@host
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(hostname))
        {
            return $"{username}@{hostname}";
        }
        
        // Fall back to using just the username or hostname
        if (!string.IsNullOrEmpty(username))
        {
            return $"{username}@irc.local";
        }
        
        if (!string.IsNullOrEmpty(hostname))
        {
            return $"user@{hostname}";
        }
        
        // Last resort - use a placeholder
        return "anonymous@irc.local";
    }

    /// <summary>
    /// Computes MD5 hash for Gravatar URL.
    /// </summary>
    private static string GetMd5Hash(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = MD5.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
