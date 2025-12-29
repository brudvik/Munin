using System.Security.Cryptography;
using System.Text;

namespace Munin.Relay;

/// <summary>
/// Provides DPAPI-based protection for authentication tokens.
/// </summary>
/// <remarks>
/// <para>Uses Windows Data Protection API (DPAPI) to encrypt tokens.</para>
/// <para>Tokens are bound to the local machine, meaning:</para>
/// <list type="bullet">
///   <item><description>Only this machine can decrypt the token</description></item>
///   <item><description>Copying config.json to another machine won't expose the token</description></item>
///   <item><description>Works with Windows Services running as LocalSystem or specific accounts</description></item>
/// </list>
/// </remarks>
public static class TokenProtection
{
    // Prefix to identify encrypted tokens in config
    private const string EncryptedPrefix = "DPAPI:";
    
    // Additional entropy for DPAPI (adds machine-specific randomness)
    private static readonly byte[] AdditionalEntropy = "MuninRelay.AuthToken.v1"u8.ToArray();

    /// <summary>
    /// Checks if a token string is encrypted.
    /// </summary>
    /// <param name="token">The token to check.</param>
    /// <returns>True if the token is encrypted with DPAPI.</returns>
    public static bool IsEncrypted(string? token)
    {
        return !string.IsNullOrEmpty(token) && token.StartsWith(EncryptedPrefix);
    }

    /// <summary>
    /// Encrypts a token using DPAPI with LocalMachine scope.
    /// </summary>
    /// <param name="plainToken">The plain text token to encrypt.</param>
    /// <returns>The encrypted token with DPAPI: prefix.</returns>
    /// <exception cref="CryptographicException">If encryption fails.</exception>
    public static string Encrypt(string plainToken)
    {
        if (string.IsNullOrEmpty(plainToken))
            throw new ArgumentException("Token cannot be empty", nameof(plainToken));

        // Don't double-encrypt
        if (IsEncrypted(plainToken))
            return plainToken;

        var plainBytes = Encoding.UTF8.GetBytes(plainToken);
        
        // Use LocalMachine scope so the Windows Service can decrypt it
        // regardless of which user account it runs under
        var encryptedBytes = ProtectedData.Protect(
            plainBytes,
            AdditionalEntropy,
            DataProtectionScope.LocalMachine);

        var base64 = Convert.ToBase64String(encryptedBytes);
        return EncryptedPrefix + base64;
    }

    /// <summary>
    /// Decrypts a DPAPI-protected token.
    /// </summary>
    /// <param name="encryptedToken">The encrypted token (with DPAPI: prefix).</param>
    /// <returns>The plain text token.</returns>
    /// <exception cref="CryptographicException">If decryption fails (wrong machine, corrupted data).</exception>
    public static string Decrypt(string encryptedToken)
    {
        if (string.IsNullOrEmpty(encryptedToken))
            throw new ArgumentException("Token cannot be empty", nameof(encryptedToken));

        // If not encrypted, return as-is (for backwards compatibility during migration)
        if (!IsEncrypted(encryptedToken))
            return encryptedToken;

        var base64 = encryptedToken[EncryptedPrefix.Length..];
        var encryptedBytes = Convert.FromBase64String(base64);

        var plainBytes = ProtectedData.Unprotect(
            encryptedBytes,
            AdditionalEntropy,
            DataProtectionScope.LocalMachine);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Attempts to decrypt a token, returning null on failure.
    /// </summary>
    /// <param name="encryptedToken">The potentially encrypted token.</param>
    /// <param name="plainToken">The decrypted token if successful.</param>
    /// <returns>True if decryption succeeded or token was plain text.</returns>
    public static bool TryDecrypt(string? encryptedToken, out string? plainToken)
    {
        plainToken = null;
        
        if (string.IsNullOrEmpty(encryptedToken))
            return false;

        try
        {
            plainToken = Decrypt(encryptedToken);
            return true;
        }
        catch (CryptographicException)
        {
            // Token was encrypted on a different machine or is corrupted
            return false;
        }
        catch (FormatException)
        {
            // Invalid base64
            return false;
        }
    }
}
