namespace Munin.Core.Models;

/// <summary>
/// Represents an IRCv3 BATCH for grouped messages.
/// </summary>
public class IrcBatch
{
    /// <summary>
    /// The batch reference tag (e.g., "yXNAbvnRHTRBv").
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// The batch type (e.g., "chathistory", "netjoin", "netsplit").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Additional parameters for the batch.
    /// </summary>
    public List<string> Parameters { get; set; } = new();

    /// <summary>
    /// Messages collected in this batch.
    /// </summary>
    public List<ParsedIrcMessage> Messages { get; set; } = new();

    /// <summary>
    /// Whether the batch is complete.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Parent batch reference (for nested batches).
    /// </summary>
    public string? ParentReference { get; set; }
}
