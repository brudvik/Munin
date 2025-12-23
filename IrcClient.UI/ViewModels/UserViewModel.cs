using CommunityToolkit.Mvvm.ComponentModel;
using IrcClient.Core.Models;
using IrcClient.UI.Services;
using System.Windows.Media;

namespace IrcClient.UI.ViewModels;

/// <summary>
/// ViewModel for displaying an IRC user in the user list.
/// </summary>
/// <remarks>
/// <para>Provides UI-bindable properties for:</para>
/// <list type="bullet">
///   <item><description>Display name with mode prefix</description></item>
///   <item><description>Mode-based coloring</description></item>
///   <item><description>Gravatar avatar</description></item>
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
    /// Opacity for display (dimmed when away).
    /// </summary>
    public double AwayOpacity => IsAway ? 0.5 : 1.0;

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

    public UserViewModel(IrcUser user)
    {
        _user = user;
    }
}
