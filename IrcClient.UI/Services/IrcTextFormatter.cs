using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace IrcClient.UI.Services;

/// <summary>
/// Parses IRC formatting codes (colors, bold, underline, etc.) and converts to WPF Inlines.
/// </summary>
/// <remarks>
/// <para>Supports all standard mIRC formatting codes:</para>
/// <list type="bullet">
///   <item><description>^B (Ctrl+B) - Bold</description></item>
///   <item><description>^C (Ctrl+K) - Color with optional background</description></item>
///   <item><description>^I (Ctrl+I) - Italic</description></item>
///   <item><description>^U (Ctrl+U) - Underline</description></item>
///   <item><description>^S - Strikethrough</description></item>
///   <item><description>^R (Ctrl+R) - Reverse (swap foreground/background)</description></item>
///   <item><description>^O (Ctrl+O) - Reset all formatting</description></item>
/// </list>
/// <para>Also handles URL detection, emoji conversion, and inline image previews.</para>
/// </remarks>
public static partial class IrcTextFormatter
{
    // IRC control characters
    private const char CtrlBold = '\x02';           // Ctrl+B
    private const char CtrlColor = '\x03';          // Ctrl+K
    private const char CtrlItalic = '\x1D';         // Ctrl+I
    private const char CtrlUnderline = '\x1F';      // Ctrl+U
    private const char CtrlStrikethrough = '\x1E';  // Ctrl+S
    private const char CtrlReverse = '\x16';        // Ctrl+R
    private const char CtrlReset = '\x0F';          // Ctrl+O

    // mIRC color palette (16 colors)
    private static readonly System.Windows.Media.Color[] IrcColors = new[]
    {
        System.Windows.Media.Color.FromRgb(255, 255, 255), // 0: White
        System.Windows.Media.Color.FromRgb(0, 0, 0),       // 1: Black
        System.Windows.Media.Color.FromRgb(0, 0, 127),     // 2: Navy
        System.Windows.Media.Color.FromRgb(0, 147, 0),     // 3: Green
        System.Windows.Media.Color.FromRgb(255, 0, 0),     // 4: Red
        System.Windows.Media.Color.FromRgb(127, 0, 0),     // 5: Maroon
        System.Windows.Media.Color.FromRgb(156, 0, 156),   // 6: Purple
        System.Windows.Media.Color.FromRgb(252, 127, 0),   // 7: Orange
        System.Windows.Media.Color.FromRgb(255, 255, 0),   // 8: Yellow
        System.Windows.Media.Color.FromRgb(0, 252, 0),     // 9: Lime
        System.Windows.Media.Color.FromRgb(0, 147, 147),   // 10: Teal
        System.Windows.Media.Color.FromRgb(0, 255, 255),   // 11: Cyan
        System.Windows.Media.Color.FromRgb(0, 0, 252),     // 12: Blue
        System.Windows.Media.Color.FromRgb(255, 0, 255),   // 13: Magenta
        System.Windows.Media.Color.FromRgb(127, 127, 127), // 14: Gray
        System.Windows.Media.Color.FromRgb(210, 210, 210), // 15: Light Gray
    };

    // Regex for color codes: ^C followed by optional foreground,background colors
    [GeneratedRegex(@"\x03(?:(\d{1,2})(?:,(\d{1,2}))?)?")]
    private static partial Regex ColorCodeRegex();

    // Regex for URLs
    [GeneratedRegex(@"(https?://[^\s<>""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // Image file extensions
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg"
    };
    
    /// <summary>
    /// Enable or disable inline image previews.
    /// </summary>
    public static bool EnableImagePreviews { get; set; } = true;
    
    /// <summary>
    /// Maximum width for inline image previews.
    /// </summary>
    public static double MaxImageWidth { get; set; } = 300;
    
    /// <summary>
    /// Maximum height for inline image previews.
    /// </summary>
    public static double MaxImageHeight { get; set; } = 200;

    // Emoji shortcode mapping
    private static readonly Dictionary<string, string> EmojiMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [":)"] = "😊", ["(:"] = "😊", [":-)"] = "😊",
        [":("] = "😞", [":-("] = "😞",
        [":D"] = "😃", [":-D"] = "😃",
        [";)"] = "😉", [";-)"] = "😉",
        [":P"] = "😛", [":-P"] = "😛", [":p"] = "😛",
        [":O"] = "😮", [":-O"] = "😮", [":o"] = "😮",
        ["<3"] = "❤️",
        [":heart:"] = "❤️",
        [":smile:"] = "😊",
        [":grin:"] = "😃",
        [":wink:"] = "😉",
        [":sad:"] = "😞",
        [":cry:"] = "😢",
        [":angry:"] = "😠",
        [":thumbsup:"] = "👍", [":+1:"] = "👍",
        [":thumbsdown:"] = "👎", [":-1:"] = "👎",
        [":fire:"] = "🔥",
        [":star:"] = "⭐",
        [":check:"] = "✅",
        [":x:"] = "❌",
        [":warning:"] = "⚠️",
        [":info:"] = "ℹ️",
        [":question:"] = "❓",
        [":exclamation:"] = "❗",
        [":coffee:"] = "☕",
        [":beer:"] = "🍺",
        [":pizza:"] = "🍕",
        [":rocket:"] = "🚀",
        [":tada:"] = "🎉",
        [":clap:"] = "👏",
        [":eyes:"] = "👀",
        [":thinking:"] = "🤔",
        [":shrug:"] = "🤷",
        [":wave:"] = "👋",
        [":ok:"] = "👌",
        [":100:"] = "💯",
        [":poop:"] = "💩",
        [":lol:"] = "😂", [":joy:"] = "😂",
        [":rofl:"] = "🤣",
        [":cool:"] = "😎",
        [":sunglasses:"] = "😎",
        [":nerd:"] = "🤓",
        [":skull:"] = "💀",
        [":ghost:"] = "👻",
        [":alien:"] = "👽",
        [":robot:"] = "🤖",
        [":cat:"] = "🐱",
        [":dog:"] = "🐶",
        [":penguin:"] = "🐧",
        [":bug:"] = "🐛",
        [":bee:"] = "🐝",
    };

    /// <summary>
    /// Parses IRC-formatted text and returns WPF Inlines.
    /// </summary>
    public static IEnumerable<Inline> Parse(string text, Brush defaultForeground)
    {
        var inlines = new List<Inline>();
        if (string.IsNullOrEmpty(text))
            return inlines;

        var currentText = new System.Text.StringBuilder();
        var isBold = false;
        var isItalic = false;
        var isUnderline = false;
        var isStrikethrough = false;
        Brush? foreground = null;
        Brush? background = null;

        void FlushText()
        {
            if (currentText.Length == 0) return;

            var run = new Run(currentText.ToString())
            {
                Foreground = foreground ?? defaultForeground
            };

            if (background != null)
                run.Background = background;
            if (isBold)
                run.FontWeight = FontWeights.Bold;
            if (isItalic)
                run.FontStyle = FontStyles.Italic;
            if (isUnderline)
                run.TextDecorations.Add(TextDecorations.Underline);
            if (isStrikethrough)
                run.TextDecorations.Add(TextDecorations.Strikethrough);

            inlines.Add(run);
            currentText.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];

            switch (c)
            {
                case CtrlBold:
                    FlushText();
                    isBold = !isBold;
                    break;

                case CtrlItalic:
                    FlushText();
                    isItalic = !isItalic;
                    break;

                case CtrlUnderline:
                    FlushText();
                    isUnderline = !isUnderline;
                    break;

                case CtrlStrikethrough:
                    FlushText();
                    isStrikethrough = !isStrikethrough;
                    break;

                case CtrlReverse:
                    FlushText();
                    // Swap foreground and background
                    (foreground, background) = (background ?? defaultForeground, foreground);
                    break;

                case CtrlReset:
                    FlushText();
                    isBold = false;
                    isItalic = false;
                    isUnderline = false;
                    isStrikethrough = false;
                    foreground = null;
                    background = null;
                    break;

                case CtrlColor:
                    FlushText();
                    // Parse color code
                    var remaining = text.AsSpan(i);
                    var match = ColorCodeRegex().Match(remaining.ToString());
                    if (match.Success)
                    {
                        i += match.Length - 1; // -1 because loop will increment

                        if (match.Groups[1].Success)
                        {
                            var fg = int.Parse(match.Groups[1].Value);
                            foreground = GetColorBrush(fg);
                        }
                        else
                        {
                            // ^C without number resets colors
                            foreground = null;
                            background = null;
                        }

                        if (match.Groups[2].Success)
                        {
                            var bg = int.Parse(match.Groups[2].Value);
                            background = GetColorBrush(bg);
                        }
                    }
                    break;

                default:
                    currentText.Append(c);
                    break;
            }
        }

        FlushText();
        return inlines;
    }

    /// <summary>
    /// Parses text and creates Inlines with clickable URLs, emojis, and IRC formatting.
    /// </summary>
    public static IEnumerable<Inline> ParseWithLinks(string text, Brush defaultForeground)
    {
        // First apply emoji conversion
        text = ConvertEmojis(text);

        // Split by URLs and process each part
        var matches = UrlRegex().Matches(text);
        if (matches.Count == 0)
        {
            return Parse(text, defaultForeground);
        }

        var inlines = new List<Inline>();
        var lastIndex = 0;

        foreach (Match match in matches)
        {
            // Add text before the URL
            if (match.Index > lastIndex)
            {
                var beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                inlines.AddRange(Parse(beforeText, defaultForeground));
            }

            // Add the hyperlink
            var url = match.Value;
            var hyperlink = new Hyperlink(new Run(url))
            {
                NavigateUri = new Uri(url),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 166, 255)), // Accent blue
                TextDecorations = null // Remove underline for cleaner look
            };
            hyperlink.RequestNavigate += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                }
                catch { }
                e.Handled = true;
            };
            hyperlink.MouseEnter += (s, e) => hyperlink.TextDecorations = TextDecorations.Underline;
            hyperlink.MouseLeave += (s, e) => hyperlink.TextDecorations = null;

            inlines.Add(hyperlink);
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after last URL
        if (lastIndex < text.Length)
        {
            var afterText = text.Substring(lastIndex);
            inlines.AddRange(Parse(afterText, defaultForeground));
        }

        return inlines;
    }

    /// <summary>
    /// Converts emoji shortcodes to actual emojis.
    /// </summary>
    public static string ConvertEmojis(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        foreach (var (shortcode, emoji) in EmojiMap)
        {
            text = text.Replace(shortcode, emoji, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    /// <summary>
    /// Strips all IRC formatting codes from text.
    /// </summary>
    public static string StripFormatting(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove color codes
        text = ColorCodeRegex().Replace(text, "");

        // Remove other control characters
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c != CtrlBold && c != CtrlItalic && c != CtrlUnderline && 
                c != CtrlStrikethrough && c != CtrlReverse && c != CtrlReset)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // Extended IRC colors (99 colors) 
    private static readonly Dictionary<int, System.Windows.Media.Color> ExtendedColors = new()
    {
        [16] = System.Windows.Media.Color.FromRgb(71, 0, 0),
        [17] = System.Windows.Media.Color.FromRgb(71, 33, 0),
        [18] = System.Windows.Media.Color.FromRgb(71, 71, 0),
        [19] = System.Windows.Media.Color.FromRgb(50, 71, 0),
    };

    private static SolidColorBrush GetColorBrush(int colorIndex)
    {
        if (colorIndex >= 0 && colorIndex < IrcColors.Length)
        {
            return new SolidColorBrush(IrcColors[colorIndex]);
        }

        if (ExtendedColors.TryGetValue(colorIndex, out var extColor))
        {
            return new SolidColorBrush(extColor);
        }

        // Default to white for unknown colors
        return Brushes.White;
    }
    
    /// <summary>
    /// Checks if a URL points to an image file.
    /// </summary>
    public static bool IsImageUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
            
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            
            // Check file extension
            foreach (var ext in ImageExtensions)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            // Check for known image hosting patterns
            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("imgur.com") || 
                host.Contains("i.redd.it") ||
                host.Contains("pbs.twimg.com") ||
                host.Contains("cdn.discordapp.com"))
            {
                // These hosts often serve images even without extension
                var query = uri.Query.ToLowerInvariant();
                if (string.IsNullOrEmpty(query) || !query.Contains("format="))
                    return true;
            }
        }
        catch
        {
            return false;
        }
        
        return false;
    }
    
    /// <summary>
    /// Gets image URLs from text.
    /// </summary>
    public static IEnumerable<string> ExtractImageUrls(string text)
    {
        if (!EnableImagePreviews || string.IsNullOrEmpty(text))
            yield break;
            
        var matches = UrlRegex().Matches(text);
        foreach (Match match in matches)
        {
            var url = match.Value;
            if (IsImageUrl(url))
            {
                yield return url;
            }
        }
    }
}
