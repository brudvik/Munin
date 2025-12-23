using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace IrcClient.UI.Services;

/// <summary>
/// Fetches Open Graph metadata for URL previews.
/// </summary>
/// <remarks>
/// <para>Provides rich link previews by fetching Open Graph (og:) and Twitter Card metadata.</para>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Caching to avoid repeated fetches</description></item>
///   <item><description>Concurrent fetch limiting (max 3 simultaneous)</description></item>
///   <item><description>Content size limiting (max 64KB) for safety</description></item>
///   <item><description>Fallback to &lt;title&gt; tag if OG tags are missing</description></item>
/// </list>
/// </remarks>
public partial class LinkPreviewService
{
    private static readonly Lazy<LinkPreviewService> _instance = new(() => new LinkPreviewService());
    public static LinkPreviewService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, LinkPreview?> _cache = new();
    private readonly SemaphoreSlim _semaphore = new(3); // Limit concurrent fetches

    /// <summary>
    /// Enable or disable link previews.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum age of cached previews.
    /// </summary>
    public TimeSpan CacheExpiry { get; set; } = TimeSpan.FromMinutes(30);

    private LinkPreviewService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "IrcClient/1.0 LinkPreview");
    }

    /// <summary>
    /// Gets a link preview for a URL.
    /// </summary>
    public async Task<LinkPreview?> GetPreviewAsync(string url)
    {
        if (!Enabled || string.IsNullOrEmpty(url))
            return null;

        // Skip image URLs
        if (IrcTextFormatter.IsImageUrl(url))
            return null;

        // Check cache
        if (_cache.TryGetValue(url, out var cached))
            return cached;

        await _semaphore.WaitAsync();
        try
        {
            // Double-check cache after acquiring semaphore
            if (_cache.TryGetValue(url, out cached))
                return cached;

            var preview = await FetchPreviewAsync(url);
            _cache[url] = preview;
            return preview;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<LinkPreview?> FetchPreviewAsync(string url)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == null || !contentType.StartsWith("text/html"))
                return null;

            // Limit content read to prevent memory issues
            var content = await ReadLimitedContentAsync(response.Content, 64 * 1024); // 64KB max
            if (string.IsNullOrEmpty(content))
                return null;

            return ParseOpenGraph(url, content);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ReadLimitedContentAsync(HttpContent content, int maxBytes)
    {
        using var stream = await content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var buffer = new char[maxBytes];
        var memory = new Memory<char>(buffer);
        var read = await reader.ReadBlockAsync(memory);
        return new string(buffer, 0, read);
    }

    private static LinkPreview? ParseOpenGraph(string url, string html)
    {
        var preview = new LinkPreview { Url = url };

        // Try Open Graph tags first
        preview.Title = ExtractMetaContent(html, "og:title") 
                       ?? ExtractMetaContent(html, "twitter:title");
        preview.Description = ExtractMetaContent(html, "og:description") 
                             ?? ExtractMetaContent(html, "twitter:description") 
                             ?? ExtractMetaContent(html, "description");
        preview.ImageUrl = ExtractMetaContent(html, "og:image") 
                          ?? ExtractMetaContent(html, "twitter:image");
        preview.SiteName = ExtractMetaContent(html, "og:site_name");

        // Fallback to <title> tag
        if (string.IsNullOrEmpty(preview.Title))
        {
            var titleMatch = TitleRegex().Match(html);
            if (titleMatch.Success)
            {
                preview.Title = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
            }
        }

        // Only return if we have at least a title
        return !string.IsNullOrEmpty(preview.Title) ? preview : null;
    }

    private static string? ExtractMetaContent(string html, string property)
    {
        // Try property="..." format
        var pattern = $@"<meta[^>]*(?:property|name)=[""']{Regex.Escape(property)}[""'][^>]*content=[""']([^""']*)[""']";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
            return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);

        // Try content="..." first format
        pattern = $@"<meta[^>]*content=[""']([^""']*)[""'][^>]*(?:property|name)=[""']{Regex.Escape(property)}[""']";
        match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
            return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);

        return null;
    }

    [GeneratedRegex(@"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    /// <summary>
    /// Clears the preview cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }
}

/// <summary>
/// Represents Open Graph metadata for a URL.
/// </summary>
public class LinkPreview
{
    /// <summary>Gets or sets the original URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the page title (og:title or &lt;title&gt;).</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the page description (og:description).</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the preview image URL (og:image).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Gets or sets the site name (og:site_name).</summary>
    public string? SiteName { get; set; }

    /// <summary>Gets or sets when this preview was fetched.</summary>
    public DateTime FetchedAt { get; set; } = DateTime.Now;

    /// <summary>Gets a formatted display title including site name if available.</summary>
    public string DisplayTitle => !string.IsNullOrEmpty(SiteName) 
        ? $"{Title} — {SiteName}" 
        : Title ?? Url;
}
