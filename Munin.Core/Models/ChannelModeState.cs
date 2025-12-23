namespace Munin.Core.Models;

/// <summary>
/// Represents cached channel mode state.
/// </summary>
public class ChannelModeState
{
    /// <summary>
    /// The channel name.
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// Simple on/off modes (e.g., +nts).
    /// </summary>
    public HashSet<char> SimpleModes { get; } = new();

    /// <summary>
    /// Modes with parameters (e.g., +l 50, +k secret).
    /// </summary>
    public Dictionary<char, string> ParameterModes { get; } = new();

    /// <summary>
    /// Channel limit (+l).
    /// </summary>
    public int? Limit
    {
        get => ParameterModes.TryGetValue('l', out var v) && int.TryParse(v, out var l) ? l : null;
        set
        {
            if (value.HasValue)
                ParameterModes['l'] = value.Value.ToString();
            else
                ParameterModes.Remove('l');
        }
    }

    /// <summary>
    /// Channel key (+k).
    /// </summary>
    public string? Key
    {
        get => ParameterModes.TryGetValue('k', out var v) ? v : null;
        set
        {
            if (value != null)
                ParameterModes['k'] = value;
            else
                ParameterModes.Remove('k');
        }
    }

    /// <summary>
    /// Whether the channel is moderated (+m).
    /// </summary>
    public bool IsModerated => SimpleModes.Contains('m');

    /// <summary>
    /// Whether the channel is secret (+s).
    /// </summary>
    public bool IsSecret => SimpleModes.Contains('s');

    /// <summary>
    /// Whether the channel is private (+p).
    /// </summary>
    public bool IsPrivate => SimpleModes.Contains('p');

    /// <summary>
    /// Whether the channel is invite-only (+i).
    /// </summary>
    public bool IsInviteOnly => SimpleModes.Contains('i');

    /// <summary>
    /// Whether the topic is protected (+t).
    /// </summary>
    public bool TopicProtected => SimpleModes.Contains('t');

    /// <summary>
    /// Whether external messages are blocked (+n).
    /// </summary>
    public bool NoExternalMessages => SimpleModes.Contains('n');

    /// <summary>
    /// Applies a mode change.
    /// </summary>
    /// <param name="adding">True if adding mode, false if removing</param>
    /// <param name="mode">The mode character</param>
    /// <param name="parameter">Optional parameter</param>
    public void ApplyMode(bool adding, char mode, string? parameter = null)
    {
        // Parameter modes
        if (mode is 'l' or 'k' or 'j' or 'f')
        {
            if (adding && parameter != null)
                ParameterModes[mode] = parameter;
            else
                ParameterModes.Remove(mode);
            return;
        }

        // Simple modes
        if (adding)
            SimpleModes.Add(mode);
        else
            SimpleModes.Remove(mode);
    }

    /// <summary>
    /// Gets the mode string (e.g., "+ntsk secret").
    /// </summary>
    public string GetModeString()
    {
        var modes = new List<char>(SimpleModes);
        var parameters = new List<string>();

        foreach (var (mode, param) in ParameterModes)
        {
            modes.Add(mode);
            parameters.Add(param);
        }

        if (modes.Count == 0)
            return "";

        var modeStr = "+" + new string(modes.OrderBy(m => m).ToArray());
        if (parameters.Count > 0)
            modeStr += " " + string.Join(" ", parameters);

        return modeStr;
    }

    /// <summary>
    /// Clears all modes.
    /// </summary>
    public void Clear()
    {
        SimpleModes.Clear();
        ParameterModes.Clear();
    }
}
