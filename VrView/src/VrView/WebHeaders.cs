using Android.Content;
using Android.Webkit;
using VrView.Core;

namespace VrView;

/// <summary>
/// HTTP headers that make a web video load like it does in the browser: the browser's user agent, the
/// page as referrer and the cookies of the site (sessions, age gates). Without them many sites refuse
/// the request.
/// </summary>
internal static class WebHeaders
{
    public static IDictionary<string, string> For(Context context, string url, string? pageUrl)
    {
        var headers = new Dictionary<string, string>();
        try
        {
            if (WebSettings.GetDefaultUserAgent(context) is { Length: > 0 } agent) headers["User-Agent"] = agent;
        }
        catch (Java.Lang.Exception)
        {
            // No WebView installed: the default player agent is used.
        }
        if (!string.IsNullOrEmpty(pageUrl)) headers["Referer"] = pageUrl;
        try
        {
            if (CookieManager.Instance?.GetCookie(url) is { Length: > 0 } cookies) headers["Cookie"] = cookies;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or Android.Util.AndroidRuntimeException)
        {
            // No WebView provider: no cookies.
        }
        return headers;
    }
}

/// <summary>Saved web videos as playlist items.</summary>
internal static class WebBookmarkItems
{
    public static MediaItem ToItem(VideoBookmark b) => new(b.MediaUrl, b.Title, 0, b.PageUrl, b.StateKey);

    /// <summary>The saved videos the system player can play (DASH cannot).</summary>
    public static List<MediaItem> All(WebBookmarks bookmarks) =>
        bookmarks.Videos.Where(v => v.Kind != StreamKind.Dash).Select(ToItem).ToList();
}
