using System.Text.RegularExpressions;

namespace AspireApp.Web.Services;

public static partial class UrlSourceTypeClassifier
{
    public const string GenericUrl = "url";
    public const string YouTubeVideo = "youtube_video";
    public const string YouTubeChannel = "youtube_channel";

    private static readonly string[] YouTubeHosts =
    [
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "youtu.be",
        "www.youtu.be"
    ];

    public static string Classify(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return GenericUrl;
        }

        if (!IsYouTubeHost(uri.Host))
        {
            return GenericUrl;
        }

        if (LooksLikeChannel(uri))
        {
            return YouTubeChannel;
        }

        return LooksLikeVideo(uri)
            ? YouTubeVideo
            : GenericUrl;
    }

    public static string GetDefaultMimeType(string sourceType) =>
        sourceType switch
        {
            YouTubeVideo => "text/plain",
            YouTubeChannel => "text/plain",
            _ => "text/html"
        };

    private static bool IsYouTubeHost(string host) =>
        YouTubeHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeChannel(Uri uri)
    {
        var path = uri.AbsolutePath.TrimEnd('/');
        return path.Contains("/channel/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/c/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/user/", StringComparison.OrdinalIgnoreCase)
            || ChannelHandleRegex().IsMatch(path);
    }

    private static bool LooksLikeVideo(Uri uri)
    {
        var path = uri.AbsolutePath.TrimEnd('/');
        if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return path.Length > 1;
        }

        if (path.Contains("/embed/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/watch/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/shorts/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Query.AsSpan().Contains("v=", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^/@[A-Za-z0-9._-]+(?:/videos)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelHandleRegex();
}
