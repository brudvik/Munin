namespace IrcClient.Core.Models;

/// <summary>
/// Manages IRCv3 capability negotiation.
/// </summary>
public class CapabilityManager
{
    /// <summary>
    /// Capabilities that the client wants to request.
    /// </summary>
    public static readonly HashSet<string> WantedCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core capabilities
        "cap-notify",           // Notify when caps change
        "multi-prefix",         // All user prefixes in WHO/NAMES
        "away-notify",          // Away status changes
        "account-notify",       // Account login/logout
        "extended-join",        // Account/realname in JOIN
        "userhost-in-names",    // user@host in NAMES
        "chghost",              // Host changes
        "setname",              // Realname changes
        
        // Message enhancements
        "server-time",          // @time tag
        "message-tags",         // Message tags support
        "account-tag",          // @account tag
        "batch",                // Batched messages
        "echo-message",         // Echo our messages back
        "msgid",                // Message IDs
        "labeled-response",     // Labeled responses
        
        // Authentication
        "sasl",                 // SASL authentication
        
        // Other
        "invite-notify",        // Invite notifications
        "standard-replies",     // Standard reply format
    };

    /// <summary>
    /// Capabilities available from the server.
    /// Key: capability name, Value: capability value (if any)
    /// </summary>
    public Dictionary<string, string?> AvailableCapabilities { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Capabilities that have been enabled (ACKed).
    /// </summary>
    public HashSet<string> EnabledCapabilities { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether CAP negotiation is in progress.
    /// </summary>
    public bool IsNegotiating { get; set; }

    /// <summary>
    /// Whether CAP negotiation has completed.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Whether we're waiting for more CAP LS lines (multiline).
    /// </summary>
    public bool IsMultiline { get; set; }

    /// <summary>
    /// SASL authentication method supported.
    /// </summary>
    public List<string> SaslMethods { get; } = new();

    /// <summary>
    /// Checks if a capability is enabled.
    /// </summary>
    public bool HasCapability(string capability) =>
        EnabledCapabilities.Contains(capability);

    /// <summary>
    /// Parses the CAP LS response and stores available capabilities.
    /// </summary>
    /// <param name="capsLine">The capability list from the server.</param>
    /// <param name="isMultiline">Whether this is a multiline response (*).</param>
    public void ParseAvailableCapabilities(string capsLine, bool isMultiline)
    {
        IsMultiline = isMultiline;

        if (string.IsNullOrWhiteSpace(capsLine)) return;

        foreach (var cap in capsLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = cap.IndexOf('=');
            if (eqIndex > 0)
            {
                var name = cap[..eqIndex];
                var value = cap[(eqIndex + 1)..];
                AvailableCapabilities[name] = value;

                // Parse SASL methods
                if (name.Equals("sasl", StringComparison.OrdinalIgnoreCase))
                {
                    SaslMethods.Clear();
                    SaslMethods.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries));
                }
            }
            else
            {
                AvailableCapabilities[cap] = null;
            }
        }
    }

    /// <summary>
    /// Gets the list of capabilities to request.
    /// </summary>
    public IEnumerable<string> GetCapabilitiesToRequest()
    {
        return WantedCapabilities
            .Where(cap => AvailableCapabilities.ContainsKey(cap))
            .OrderBy(cap => cap);
    }

    /// <summary>
    /// Processes a CAP ACK response.
    /// </summary>
    public void ProcessAck(string capsLine)
    {
        if (string.IsNullOrWhiteSpace(capsLine)) return;

        foreach (var cap in capsLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Remove any modifiers (-, ~, =)
            var name = cap.TrimStart('-', '~', '=');
            if (cap.StartsWith('-'))
            {
                EnabledCapabilities.Remove(name);
            }
            else
            {
                EnabledCapabilities.Add(name);
            }
        }
    }

    /// <summary>
    /// Processes a CAP NAK response.
    /// </summary>
    public void ProcessNak(string capsLine)
    {
        // NAK means the capabilities were rejected
        // We don't add them to enabled
    }

    /// <summary>
    /// Processes a CAP DEL notification (cap-notify).
    /// </summary>
    public void ProcessDel(string capsLine)
    {
        if (string.IsNullOrWhiteSpace(capsLine)) return;

        foreach (var cap in capsLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            AvailableCapabilities.Remove(cap);
            EnabledCapabilities.Remove(cap);
        }
    }

    /// <summary>
    /// Processes a CAP NEW notification (cap-notify).
    /// </summary>
    public void ProcessNew(string capsLine)
    {
        // Parse as available capabilities
        ParseAvailableCapabilities(capsLine, false);
    }

    /// <summary>
    /// Resets the capability manager state.
    /// </summary>
    public void Reset()
    {
        AvailableCapabilities.Clear();
        EnabledCapabilities.Clear();
        SaslMethods.Clear();
        IsNegotiating = false;
        IsComplete = false;
        IsMultiline = false;
    }
}
