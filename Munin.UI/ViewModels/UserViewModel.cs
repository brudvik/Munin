using CommunityToolkit.Mvvm.ComponentModel;
using Munin.Core.Models;
using Munin.UI.Services;
using System.Windows.Media;

namespace Munin.UI.ViewModels;

/// <summary>
/// ViewModel for displaying an IRC user in the user list.
/// </summary>
/// <remarks>
/// <para>Provides UI-bindable properties for:</para>
/// <list type="bullet">
///   <item><description>Display name with mode prefix</description></item>
///   <item><description>Mode-based coloring</description></item>
///   <item><description>Avatar with initials fallback</description></item>
///   <item><description>Away status indication</description></item>
/// </list>
/// </remarks>
public partial class UserViewModel : ObservableObject
{
    /// <summary>
    /// The underlying IRC user model.
    /// </summary>
    [ObservableProperty]
    private IrcUser _user;

    /// <summary>
    /// Display name with mode prefix (e.g., "@Operator").
    /// </summary>
    public string DisplayName => User.DisplayName;
    
    /// <summary>
    /// The user's nickname without prefix.
    /// </summary>
    public string Nickname => User.Nickname;
    
    /// <summary>
    /// The user's channel privilege mode.
    /// </summary>
    public UserMode Mode => User.Mode;
    
    /// <summary>
    /// Whether the user is currently away.
    /// </summary>
    public bool IsAway => User.IsAway;

    /// <summary>
    /// URL to the user's Gravatar avatar (based on ident/host hash).
    /// </summary>
    public string AvatarUrl => GravatarService.GetGravatarUrl(User.Username, User.Hostname, 24);

    /// <summary>
    /// Gets the user's initials (first 1-2 characters of nickname).
    /// </summary>
    public string Initials
    {
        get
        {
            if (string.IsNullOrEmpty(Nickname))
                return "?";
            
            // Get first letter, or first two if nickname is long enough
            var nick = Nickname.TrimStart('@', '+', '%', '&', '~');
            if (nick.Length >= 2)
                return nick[..2].ToUpperInvariant();
            return nick.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Background color for the initials avatar based on nickname hash.
    /// </summary>
    public Brush InitialsBackground
    {
        get
        {
            // Generate a consistent color based on nickname hash
            var hash = Nickname.GetHashCode();
            var hue = Math.Abs(hash) % 360;
            
            // Use HSL to RGB conversion for nice colors
            var color = HslToRgb(hue, 0.5, 0.35);
            return new SolidColorBrush(color);
        }
    }

    /// <summary>
    /// Status indicator color (green=online, yellow=away, gray=unknown).
    /// </summary>
    public Brush StatusColor => IsAway 
        ? new SolidColorBrush(Color.FromRgb(210, 153, 34))  // Warning/Away color
        : new SolidColorBrush(Color.FromRgb(63, 185, 80));  // Success/Online color

    /// <summary>
    /// Status tooltip text.
    /// </summary>
    public string StatusTooltip => IsAway 
        ? (User.AwayMessage ?? "Away") 
        : "Online";

    /// <summary>
    /// Opacity for display (dimmed when away).
    /// </summary>
    public double AwayOpacity => IsAway ? 0.6 : 1.0;

    /// <summary>
    /// Color brush based on user mode for visual distinction.
    /// </summary>
    public Brush ModeColor => Mode switch
    {
        UserMode.Owner => Brushes.Gold,
        UserMode.Admin => Brushes.OrangeRed,
        UserMode.Operator => Brushes.LimeGreen,
        UserMode.HalfOperator => Brushes.CornflowerBlue,
        UserMode.Voice => Brushes.MediumPurple,
        _ => Brushes.LightGray
    };

    /// <summary>
    /// Gets the sort order based on user mode for grouping.
    /// </summary>
    public int SortOrder => User.SortOrder;

    /// <summary>
    /// Gets the group name for this user based on mode.
    /// </summary>
    public string GroupName => Mode switch
    {
        UserMode.Owner => "Owners",
        UserMode.Admin => "Admins",
        UserMode.Operator => "Operators",
        UserMode.HalfOperator => "Half-Operators",
        UserMode.Voice => "Voiced",
        _ => "Users"
    };

    public UserViewModel(IrcUser user)
    {
        _user = user;
    }

    /// <summary>
    /// Converts HSL color values to RGB Color.
    /// </summary>
    private static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;

        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
            g = HueToRgb(p, q, h / 360.0);
            b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);
        }

        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
