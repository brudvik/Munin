namespace Munin.Core.Models;

/// <summary>
/// Represents a group/folder for organizing IRC servers.
/// </summary>
public class ServerGroup
{
    /// <summary>
    /// Unique identifier for this group.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Display name for the group.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Sort order of this group in the server rail.
    /// </summary>
    public int SortOrder { get; set; } = 0;
    
    /// <summary>
    /// Whether this group is collapsed in the UI.
    /// </summary>
    public bool IsCollapsed { get; set; } = false;
    
    /// <summary>
    /// Optional icon/emoji for the group.
    /// </summary>
    public string? Icon { get; set; }
}
