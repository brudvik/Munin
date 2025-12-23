using CommunityToolkit.Mvvm.ComponentModel;
using Munin.Core.Models;
using Munin.UI.Services;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Munin.UI.ViewModels;

/// <summary>
/// ViewModel for displaying an IRC message in the UI.
/// </summary>
/// <remarks>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Formatted timestamp display</description></item>
///   <item><description>Nickname coloring and alignment</description></item>
///   <item><description>URL detection and link previews</description></item>
///   <item><description>Highlight detection</description></item>
///   <item><description>Search result matching</description></item>
/// </list>
/// </remarks>
public partial class MessageViewModel : ObservableObject
{
    /// <summary>
    /// Standard IRC max nickname length (some servers allow more).
    /// </summary>
    public const int MaxNicknameLength = 16;
    
    /// <summary>
    /// Current user's nickname for "own message" detection.
    /// </summary>
    public static string? CurrentNickname { get; set; }
    
    /// <summary>
    /// Regex pattern for extracting URLs from messages.
    /// </summary>
    private static readonly Regex UrlRegex = new(@"(https?://[^\s<>""]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    /// <summary>
    /// The underlying IRC message model.
    /// </summary>
    [ObservableProperty]
    private IrcMessage _message;
    
    /// <summary>
    /// Link previews for URLs in this message.
    /// </summary>
    public ObservableCollection<LinkPreview> LinkPreviews { get; } = new();
    
    /// <summary>
    /// Whether link previews are currently being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingPreviews;

    public string Timestamp => $"[{Message.FormattedTime}]";
    public string? Source => Message.Source;
    public string Content => Message.Content;
    public MessageType Type => Message.Type;
    public bool IsHighlight => Message.IsHighlight;

    [ObservableProperty]
    private bool _isSearchMatch;
    
    /// <summary>
    /// True if this message was sent by the current user.
    /// </summary>
    public bool IsOwnMessage => !string.IsNullOrEmpty(Source) && 
        !string.IsNullOrEmpty(CurrentNickname) &&
        Source.Equals(CurrentNickname, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the nickname padded to fixed width for alignment.
    /// Format: "Nickname        " (right-padded) or "        Nickname" (left-padded)
    /// </summary>
    public string PaddedNickname
    {
        get
        {
            if (string.IsNullOrEmpty(Source)) return new string(' ', MaxNicknameLength);
            var nick = Source.Length > MaxNicknameLength 
                ? Source[..MaxNicknameLength] 
                : Source;
            return nick.PadLeft(MaxNicknameLength);
        }
    }

    public string FormattedMessage => Type switch
    {
        MessageType.Action => $"* {Source} {Content}",
        MessageType.Join or MessageType.Part or MessageType.Quit or 
        MessageType.Kick or MessageType.Nick or MessageType.Mode or 
        MessageType.Topic => Content,
        MessageType.Notice => $"-{Source}- {Content}",
        MessageType.System or MessageType.Error => Content,
        _ => Content
    };

    public bool ShowNickname => Type == MessageType.Normal || Type == MessageType.Notice;

    public Brush NicknameColor
    {
        get
        {
            if (string.IsNullOrEmpty(Source)) return Brushes.Gray;
            
            // Generate consistent color from nickname hash
            var hash = Source.GetHashCode();
            var colors = new[]
            {
                Color.FromRgb(231, 76, 60),   // Red
                Color.FromRgb(46, 204, 113),  // Green
                Color.FromRgb(52, 152, 219),  // Blue
                Color.FromRgb(155, 89, 182),  // Purple
                Color.FromRgb(241, 196, 15),  // Yellow
                Color.FromRgb(230, 126, 34),  // Orange
                Color.FromRgb(26, 188, 156),  // Teal
                Color.FromRgb(236, 240, 241), // Light gray
                Color.FromRgb(149, 165, 166), // Gray
                Color.FromRgb(243, 156, 18),  // Gold
            };
            
            return new SolidColorBrush(colors[Math.Abs(hash) % colors.Length]);
        }
    }

    // Colors for different message types
    private static readonly Brush OwnMessageColor = new SolidColorBrush(Color.FromRgb(88, 166, 255));     // Bright blue for own messages
    private static readonly Brush JoinColor = new SolidColorBrush(Color.FromRgb(63, 185, 80));            // Green - positive
    private static readonly Brush PartColor = new SolidColorBrush(Color.FromRgb(139, 148, 158));          // Gray - neutral departure
    private static readonly Brush QuitColor = new SolidColorBrush(Color.FromRgb(201, 123, 20));           // Orange - unexpected departure  
    private static readonly Brush KickColor = new SolidColorBrush(Color.FromRgb(248, 81, 73));            // Red - serious/forced
    private static readonly Brush NickColor = new SolidColorBrush(Color.FromRgb(168, 133, 255));          // Purple - identity change
    private static readonly Brush ModeColor = new SolidColorBrush(Color.FromRgb(210, 153, 34));           // Gold - permission change
    private static readonly Brush TopicColor = new SolidColorBrush(Color.FromRgb(56, 189, 248));          // Cyan - informational

    public Brush MessageColor => Type switch
    {
        MessageType.Error => Brushes.IndianRed,
        MessageType.System => Brushes.DimGray,
        MessageType.Notice => Brushes.CornflowerBlue,
        MessageType.Action => Brushes.MediumPurple,
        MessageType.Join => JoinColor,
        MessageType.Part => PartColor,
        MessageType.Quit => QuitColor,
        MessageType.Kick => KickColor,
        MessageType.Nick => NickColor,
        MessageType.Mode => ModeColor,
        MessageType.Topic => TopicColor,
        _ when IsHighlight => Brushes.Gold,
        _ when IsOwnMessage => OwnMessageColor,
        _ => Brushes.White
    };

    public MessageViewModel(IrcMessage message)
    {
        _message = message;
        
        // Start loading link previews asynchronously
        _ = LoadLinkPreviewsAsync();
    }
    
    private async Task LoadLinkPreviewsAsync()
    {
        if (!LinkPreviewService.Instance.Enabled)
            return;
            
        var matches = UrlRegex.Matches(Content);
        if (matches.Count == 0)
            return;
            
        IsLoadingPreviews = true;
        
        try
        {
            foreach (Match match in matches.Take(2)) // Limit to 2 previews per message
            {
                var url = match.Value;
                
                // Skip image URLs (they're shown inline already)
                if (IrcTextFormatter.IsImageUrl(url))
                    continue;
                    
                var preview = await LinkPreviewService.Instance.GetPreviewAsync(url);
                if (preview != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        LinkPreviews.Add(preview);
                    });
                }
            }
        }
        catch
        {
            // Ignore preview loading failures
        }
        finally
        {
            IsLoadingPreviews = false;
        }
    }
}
