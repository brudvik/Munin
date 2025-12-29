using System.Security.Cryptography;
using System.Text;

namespace Munin.Agent.Services;

/// <summary>
/// Provides security utilities for the agent.
/// </summary>
public static class AgentSecurity
{
    private const int AuthTokenLength = 32;
    private const string AllowedTokenChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>
    /// Generates a cryptographically secure authentication token.
    /// </summary>
    /// <returns>A random authentication token.</returns>
    public static string GenerateAuthToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(AuthTokenLength);
        var sb = new StringBuilder(AuthTokenLength);
        
        foreach (var b in bytes)
        {
            sb.Append(AllowedTokenChars[b % AllowedTokenChars.Length]);
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Verifies an authentication token using constant-time comparison.
    /// </summary>
    /// <param name="provided">The token provided by the client.</param>
    /// <param name="expected">The expected token.</param>
    /// <returns>True if tokens match.</returns>
    public static bool VerifyToken(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    /// <summary>
    /// Creates a challenge for authentication.
    /// </summary>
    /// <returns>Random challenge bytes.</returns>
    public static byte[] CreateChallenge()
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// Creates an HMAC-SHA256 response to a challenge.
    /// </summary>
    /// <param name="challenge">The challenge bytes.</param>
    /// <param name="authToken">The authentication token.</param>
    /// <returns>The HMAC response.</returns>
    public static byte[] CreateChallengeResponse(byte[] challenge, string authToken)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(authToken);
        using var hmac = new HMACSHA256(tokenBytes);
        return hmac.ComputeHash(challenge);
    }

    /// <summary>
    /// Verifies a challenge response.
    /// </summary>
    /// <param name="challenge">The original challenge.</param>
    /// <param name="response">The response from the client.</param>
    /// <param name="authToken">The expected authentication token.</param>
    /// <returns>True if the response is valid.</returns>
    public static bool VerifyChallengeResponse(byte[] challenge, byte[] response, string authToken)
    {
        var expected = CreateChallengeResponse(challenge, authToken);
        return CryptographicOperations.FixedTimeEquals(response, expected);
    }

    /// <summary>
    /// Validates a hostmask pattern against a host.
    /// Supports wildcards (* and ?).
    /// </summary>
    /// <param name="pattern">The hostmask pattern (e.g., "*!*@*.example.com").</param>
    /// <param name="host">The host to match against (e.g., "nick!user@host.example.com").</param>
    /// <returns>True if the host matches the pattern.</returns>
    public static bool MatchHostmask(string pattern, string host)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(host))
            return false;

        if (pattern == "*")
            return true;

        return WildcardMatch(pattern.ToLowerInvariant(), host.ToLowerInvariant());
    }

    /// <summary>
    /// Simple wildcard matching (* and ?).
    /// </summary>
    private static bool WildcardMatch(string pattern, string text)
    {
        int pIndex = 0, tIndex = 0;
        int pStar = -1, tStar = -1;

        while (tIndex < text.Length)
        {
            if (pIndex < pattern.Length && (pattern[pIndex] == '?' || pattern[pIndex] == text[tIndex]))
            {
                pIndex++;
                tIndex++;
            }
            else if (pIndex < pattern.Length && pattern[pIndex] == '*')
            {
                pStar = pIndex++;
                tStar = tIndex;
            }
            else if (pStar >= 0)
            {
                pIndex = pStar + 1;
                tIndex = ++tStar;
            }
            else
            {
                return false;
            }
        }

        while (pIndex < pattern.Length && pattern[pIndex] == '*')
            pIndex++;

        return pIndex == pattern.Length;
    }

    /// <summary>
    /// Validates that an IP address is in an allowed list.
    /// Supports wildcards and CIDR notation.
    /// </summary>
    /// <param name="ip">The IP address to check.</param>
    /// <param name="allowedList">List of allowed IPs/patterns.</param>
    /// <returns>True if IP is allowed.</returns>
    public static bool IsIpAllowed(string ip, IEnumerable<string> allowedList)
    {
        var list = allowedList.ToList();
        
        // Empty list or "*" allows all
        if (list.Count == 0 || list.Contains("*"))
            return true;

        foreach (var pattern in list)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            // Exact match
            if (pattern.Equals(ip, StringComparison.OrdinalIgnoreCase))
                return true;

            // Wildcard match
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                if (WildcardMatch(pattern, ip))
                    return true;
            }

            // CIDR notation (simplified - only /8, /16, /24 for IPv4)
            if (pattern.Contains('/'))
            {
                if (MatchCidr(ip, pattern))
                    return true;
            }
        }

        return false;
    }

    private static bool MatchCidr(string ip, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2)
                return false;

            var network = parts[0];
            var prefix = int.Parse(parts[1]);

            var ipBytes = System.Net.IPAddress.Parse(ip).GetAddressBytes();
            var networkBytes = System.Net.IPAddress.Parse(network).GetAddressBytes();

            if (ipBytes.Length != networkBytes.Length)
                return false;

            var bytesToCheck = prefix / 8;
            var remainingBits = prefix % 8;

            for (int i = 0; i < bytesToCheck; i++)
            {
                if (ipBytes[i] != networkBytes[i])
                    return false;
            }

            if (remainingBits > 0 && bytesToCheck < ipBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - remainingBits));
                if ((ipBytes[bytesToCheck] & mask) != (networkBytes[bytesToCheck] & mask))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
