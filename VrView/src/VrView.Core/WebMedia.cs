using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrView.Core;

/// <summary>Kind of video a web page plays.</summary>
public enum StreamKind
{
    /// <summary>A whole file (mp4, webm, mov…): plays and can be downloaded.</summary>
    File,
    /// <summary>HLS playlist (.m3u8): plays in the system player.</summary>
    Hls,
    /// <summary>MPEG-DASH manifest (.mpd): detected, but the system player cannot play it.</summary>
    Dash,
}

/// <summary>A video found while browsing a page.</summary>
public sealed record DetectedMedia(string Url, StreamKind Kind, string PageUrl, string PageTitle)
{
    public bool CanPlay => Kind != StreamKind.Dash;
    public bool CanDownload => Kind == StreamKind.File;

    public string KindLabel => Kind switch { StreamKind.Hls => "HLS (stream)", StreamKind.Dash => "DASH (no compatible)", _ => "Archivo" };

    /// <summary>File name of the URL (without query), for display and format tags.</summary>
    public string FileName => MediaSniffer.FileName(Url);
}

/// <summary>Recognizes video URLs among the requests and elements of a page.</summary>
public static class MediaSniffer
{
    private static readonly string[] FileExtensions = [".mp4", ".m4v", ".webm", ".mov", ".mkv"];

    /// <summary>The kind of video an URL points to (by extension or MIME type), or null when it is not one.</summary>
    public static StreamKind? Classify(string url, string? mimeType = null)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        string mime = (mimeType ?? "").ToLowerInvariant();
        if (mime.Contains("mpegurl")) return StreamKind.Hls;
        if (mime.Contains("dash+xml")) return StreamKind.Dash;
        string path = PathOf(url).ToLowerInvariant();
        if (path.EndsWith(".m3u8")) return StreamKind.Hls;
        if (path.EndsWith(".mpd")) return StreamKind.Dash;
        if (FileExtensions.Any(path.EndsWith) || mime.StartsWith("video/"))
            return LooksLikeSegment(path) ? null : StreamKind.File;
        return null;
    }

    /// <summary>
    /// Pieces of a stream ("seg-12.mp4", "chunk_00042.m4v", "init.mp4", "/frag/5.mp4") are not videos on
    /// their own; a real file name rarely is just a number or has segment words in it.
    /// </summary>
    public static bool LooksLikeSegment(string path)
    {
        string name = Path.GetFileNameWithoutExtension(FileName(path)).ToLowerInvariant();
        if (name.Length == 0 || name.All(char.IsDigit)) return true;
        if (name is "init" || name.StartsWith("init-") || name.StartsWith("init_")) return true;
        foreach (var word in new[] { "seg", "segment", "chunk", "frag", "fragment", "part" })
        {
            int i = name.IndexOf(word, StringComparison.Ordinal);
            if (i < 0) continue;
            // "seg-12", "chunk_00042", "segment3"
            string rest = name[(i + word.Length)..].TrimStart('-', '_', '.');
            if (rest.Length > 0 && rest.All(char.IsDigit)) return true;
        }
        return false;
    }

    /// <summary>The URL without query and fragment.</summary>
    public static string PathOf(string url)
    {
        int cut = url.IndexOfAny(['?', '#']);
        return cut >= 0 ? url[..cut] : url;
    }

    /// <summary>Last path part of the URL, decoded ("playa_180_LR.mp4").</summary>
    public static string FileName(string url)
    {
        string path = PathOf(url).TrimEnd('/');
        string name = path[(path.LastIndexOf('/') + 1)..];
        try { return Uri.UnescapeDataString(name); }
        catch (UriFormatException) { return name; }
    }

    /// <summary>
    /// A display name for a video of a page: the file name when it carries format tags (so "_180_LR" keeps
    /// working), otherwise the page title (or the file name when there is no title).
    /// </summary>
    public static string DisplayName(string mediaUrl, string? pageTitle)
    {
        string file = FileName(mediaUrl);
        if (FormatDetector.HasTags(file) || string.IsNullOrWhiteSpace(pageTitle)) return file.Length > 0 ? file : "video web";
        string ext = Path.GetExtension(file);
        string title = pageTitle!.Trim();
        return ext.Length is > 1 and <= 5 && !title.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? title + ext : title;
    }
}

/// <summary>
/// The videos found on the current page, newest first. The same video asked for again (e.g. with another
/// range or token in the query) replaces its older entry.
/// </summary>
public sealed class DetectedMediaList
{
    public const int MaxEntries = 30;
    private readonly List<DetectedMedia> _items = [];

    public IReadOnlyList<DetectedMedia> Items => _items;
    public int Version { get; private set; }

    /// <summary>Adds the URL if it is a video. Returns true when the list changed.</summary>
    public bool Add(string url, string pageUrl, string pageTitle, string? mimeType = null)
    {
        if (MediaSniffer.Classify(url, mimeType) is not { } kind) return false;
        string path = MediaSniffer.PathOf(url);
        int existing = _items.FindIndex(m => MediaSniffer.PathOf(m.Url) == path);
        if (existing == 0 && _items[0].Url == url) return false;
        if (existing >= 0) _items.RemoveAt(existing);
        _items.Insert(0, new DetectedMedia(url, kind, pageUrl, pageTitle));
        if (_items.Count > MaxEntries) _items.RemoveAt(_items.Count - 1);
        Version++;
        return true;
    }

    /// <summary>Playlists and files first (what can be played), then the rest.</summary>
    public IReadOnlyList<DetectedMedia> Playable => _items.Where(m => m.CanPlay).Concat(_items.Where(m => !m.CanPlay)).ToList();

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Version++;
    }
}

/// <summary>A saved web site.</summary>
public sealed class SiteBookmark
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>A saved web video: the page it was found on and the video address (which may expire).</summary>
public sealed class VideoBookmark
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string MediaUrl { get; set; } = "";
    public StreamKind Kind { get; set; }
    public long Added { get; set; }

    /// <summary>Key of the video's remembered state (position, format, spots); stable when the address is renewed.</summary>
    [JsonIgnore] public string StateKey => "web:" + Id;
}

/// <summary>Saved sites and web videos (kept in the app settings as JSON).</summary>
public sealed class WebBookmarks
{
    public List<SiteBookmark> Sites { get; set; } = [];
    public List<VideoBookmark> Videos { get; set; } = [];

    public SiteBookmark? FindSite(string url) => Sites.FirstOrDefault(s => SameUrl(s.Url, url));

    /// <summary>Adds the site or, when it is already saved, removes it. Returns true when it is now saved.</summary>
    public bool ToggleSite(string url, string title)
    {
        if (FindSite(url) is { } existing)
        {
            Sites.Remove(existing);
            return false;
        }
        Sites.Insert(0, new SiteBookmark { Url = url, Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim() });
        return true;
    }

    /// <summary>The saved video for this page (and kind), if any.</summary>
    public VideoBookmark? FindVideo(string pageUrl, StreamKind kind) =>
        Videos.FirstOrDefault(v => SameUrl(v.PageUrl, pageUrl) && v.Kind == kind);

    /// <summary>
    /// Saves a video, or renews the address of the one already saved for the same page (addresses of
    /// streams often expire), keeping its title and remembered state.
    /// </summary>
    public VideoBookmark SaveVideo(DetectedMedia media, string title, long now)
    {
        var existing = Videos.FirstOrDefault(v => SameUrl(v.PageUrl, media.PageUrl) &&
                                                  (v.Kind == media.Kind || MediaSniffer.PathOf(v.MediaUrl) == MediaSniffer.PathOf(media.Url)));
        if (existing is not null)
        {
            existing.MediaUrl = media.Url;
            existing.Kind = media.Kind;
            return existing;
        }
        var b = new VideoBookmark
        {
            Id = now.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + Videos.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Title = string.IsNullOrWhiteSpace(title) ? media.FileName : title.Trim(),
            PageUrl = media.PageUrl,
            MediaUrl = media.Url,
            Kind = media.Kind,
            Added = now,
        };
        Videos.Insert(0, b);
        return b;
    }

    public VideoBookmark? GetVideo(string id) => Videos.FirstOrDefault(v => v.Id == id);

    /// <summary>Same page, ignoring the fragment and a trailing slash.</summary>
    public static bool SameUrl(string a, string b) => Normalize(a) == Normalize(b);

    private static string Normalize(string url)
    {
        int hash = url.IndexOf('#');
        if (hash >= 0) url = url[..hash];
        return url.TrimEnd('/');
    }

    public string ToJson() => JsonSerializer.Serialize(this, WebBookmarksJson.Default.WebBookmarks);

    public static WebBookmarks Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new WebBookmarks();
        try
        {
            var b = JsonSerializer.Deserialize(json, WebBookmarksJson.Default.WebBookmarks) ?? new WebBookmarks();
            b.Sites ??= [];
            b.Videos ??= [];
            b.Videos.RemoveAll(v => string.IsNullOrEmpty(v.Id) || string.IsNullOrEmpty(v.MediaUrl));
            return b;
        }
        catch (JsonException)
        {
            return new WebBookmarks();
        }
    }
}

/// <summary>Turns a typed address into an URL: adds https:// to host names, searches anything else.</summary>
public static class WebAddress
{
    public static string FromInput(string input)
    {
        string text = input.Trim();
        if (text.Length == 0) return "about:blank";
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return text;
        bool looksLikeHost = !text.Contains(' ') && (text.Contains('.') || text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
        return looksLikeHost ? "https://" + text : "https://duckduckgo.com/?q=" + Uri.EscapeDataString(text);
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(WebBookmarks))]
internal sealed partial class WebBookmarksJson : JsonSerializerContext
{
}
