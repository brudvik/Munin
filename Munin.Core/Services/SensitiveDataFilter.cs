using System.Text.RegularExpressions;

namespace Munin.Core.Services;

/// <summary>
/// Filters sensitive data from IRC messages to prevent credential leakage in logs.
/// </summary>
/// <remarks>
/// This service masks passwords and authentication tokens in:
/// - PASS commands (server passwords)
/// - AUTHENTICATE commands (SASL tokens)
/// - NickServ IDENTIFY commands
/// - NS IDENTIFY commands
/// </remarks>
public static partial class SensitiveDataFilter
{
    private const string MaskedValue = "********";

    // Compiled regex patterns for performance
    [GeneratedRegex(@"^PASS\s+.+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PassCommandRegex();
    
    [GeneratedRegex(@"^AUTHENTICATE\s+(?!PLAIN$|SCRAM-SHA-256$|EXTERNAL$|\*$|\+$).+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AuthenticateDataRegex();
    
    [GeneratedRegex(@"^PRIVMSG\s+NickServ\s+:IDENTIFY\s+.+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NickServIdentifyRegex();
    
    [GeneratedRegex(@"^PRIVMSG\s+NickServ\s+:REGISTER\s+.+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NickServRegisterRegex();
    
    [GeneratedRegex(@"^NS\s+IDENTIFY\s+.+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NsIdentifyRegex();
    
    [GeneratedRegex(@"^NS\s+REGISTER\s+.+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NsRegisterRegex();

    /// <summary>
    /// Masks sensitive data in an IRC message for safe logging.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <returns>The message with sensitive data replaced by asterisks.</returns>
    public static string MaskSensitiveData(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        // PASS password -> PASS ********
        if (PassCommandRegex().IsMatch(message))
        {
            return "PASS " + MaskedValue;
        }

        // AUTHENTICATE <base64data> -> AUTHENTICATE ********
        // But allow AUTHENTICATE PLAIN, AUTHENTICATE SCRAM-SHA-256, AUTHENTICATE *, AUTHENTICATE +
        if (AuthenticateDataRegex().IsMatch(message))
        {
            return "AUTHENTICATE " + MaskedValue;
        }

        // PRIVMSG NickServ :IDENTIFY password -> PRIVMSG NickServ :IDENTIFY ********
        if (NickServIdentifyRegex().IsMatch(message))
        {
            return "PRIVMSG NickServ :IDENTIFY " + MaskedValue;
        }

        // PRIVMSG NickServ :REGISTER password email -> PRIVMSG NickServ :REGISTER ********
        if (NickServRegisterRegex().IsMatch(message))
        {
            return "PRIVMSG NickServ :REGISTER " + MaskedValue;
        }

        // NS IDENTIFY password -> NS IDENTIFY ********
        if (NsIdentifyRegex().IsMatch(message))
        {
            return "NS IDENTIFY " + MaskedValue;
        }

        // NS REGISTER password email -> NS REGISTER ********
        if (NsRegisterRegex().IsMatch(message))
        {
            return "NS REGISTER " + MaskedValue;
        }

        return message;
    }

    /// <summary>
    /// Checks if a message contains sensitive data that should be masked.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <returns>True if the message contains sensitive data.</returns>
    public static bool ContainsSensitiveData(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return PassCommandRegex().IsMatch(message) ||
               AuthenticateDataRegex().IsMatch(message) ||
               NickServIdentifyRegex().IsMatch(message) ||
               NickServRegisterRegex().IsMatch(message) ||
               NsIdentifyRegex().IsMatch(message) ||
               NsRegisterRegex().IsMatch(message);
    }
}
