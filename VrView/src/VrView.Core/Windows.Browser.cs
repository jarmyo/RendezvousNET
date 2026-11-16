namespace VrView.Core;

/// <summary>What the VR browser's window shows.</summary>
public enum BrowserPane { Bar, Sites, Formats }

/// <summary>State of the VR browser, for its window.</summary>
public sealed record BrowserUiModel(
    BrowserPane Pane,
    string Title,
    string Url,
    bool CanGoBack = false,
    bool CanGoForward = false,
    int Progress = 100,
    int VideoCount = 0,
    bool SiteSaved = false,
    bool Fullscreen = false,
    VideoFormat Format = default,
    bool BigScreen = false,
    bool Playing = false,
    IReadOnlyList<SiteBookmark>? Sites = null,
    int SitesPage = 0)
{
    public const int SitesPerPage = 8;

    /// <summary>Formats offered for a video in VR fullscreen.</summary>
    public static IReadOnlyList<VideoFormat> Formats => VideoFormat.ManualChoices;
}

public static partial class Windows
{
    /// <summary>The VR browser's window: the bar under the page, the sites list or the format choices.</summary>
    public static UiWindow Browser(BrowserUiModel m) => m.Pane switch
    {
        BrowserPane.Sites => BrowserSites(m),
        BrowserPane.Formats => BrowserFormats(m),
        _ => m.Fullscreen ? BrowserFullscreenBar(m) : BrowserBar(m),
    };

    private static UiWindow BrowserBar(BrowserUiModel m)
    {
        const float aspect = 4.4f, width = 2.4f;
        var e = new List<UiElement>();
        e.AddRange(Row(0.36f, 0.64f, 0.02f, 0.98f,
            Btn(MenuAction.WebBack, T("◀ Back")) with { Enabled = m.CanGoBack },
            Btn(MenuAction.WebForward, T("Forward ▶")) with { Enabled = m.CanGoForward },
            Btn(MenuAction.WebReload, "⟳"),
            Btn(MenuAction.WebScrollUp, T("▲ Up"), repeat: true), Btn(MenuAction.WebScrollDown, T("▼ Down"), repeat: true),
            Btn(MenuAction.WebZoomOut, "−"), Btn(MenuAction.WebZoomIn, "＋"),
            Btn(MenuAction.Recenter, T("Recenter"))));
        e.AddRange(Row(0.69f, 0.95f, 0.02f, 0.98f,
            Btn(MenuAction.WebSites, T("★ Favorites")),
            Btn(MenuAction.WebStar, m.SiteSaved ? T("★ Site") : T("☆ Site")),
            Btn(MenuAction.WebFullscreen, T("⛶ Video in VR")),
            Btn(MenuAction.WebPlayer, m.VideoCount > 0 ? F("🎬 Player ({0})", m.VideoCount) : T("🎬 Player")) with { Enabled = m.VideoCount > 0 },
            Btn(MenuAction.WebPhone, T("📱 Phone screen")),
            Btn(MenuAction.Exit, T("Exit"))));
        string sub = m.Progress < 100 ? F("Loading… {0}%", m.Progress) + "   ·   " + m.Url : m.Url;
        return new UiWindow(WindowKind.Browser, m.Title.Length > 0 ? m.Title : T("VR browser"), sub, aspect, e) { Width = width };
    }

    private static UiWindow BrowserFullscreenBar(BrowserUiModel m)
    {
        const float aspect = 3.6f, width = 2.2f;
        var e = new List<UiElement>();
        e.AddRange(Row(0.36f, 0.64f, 0.03f, 0.97f,
            Btn(MenuAction.WebBack10, "◀◀ 10 s", repeat: true),
            Btn(MenuAction.WebPlayPause, m.Playing ? T("II Pause") : T("▶ Play")),
            Btn(MenuAction.WebFwd10, "10 s ▶▶", repeat: true),
            Btn(MenuAction.Recenter, T("Recenter")),
            new UiElement(UiKind.Toggle, MenuAction.WebBigScreen, default)
            {
                Text = T("Big screen"), Active = m.BigScreen, Enabled = m.Format.Projection == Projection.Flat,
            }));
        e.AddRange(Row(0.69f, 0.95f, 0.03f, 0.97f,
            Btn(MenuAction.WebFormats, F("Format: {0}", m.Format.ShortLabel)),
            Btn(MenuAction.WebPlayer, T("🎬 Player")) with { Enabled = m.VideoCount > 0 },
            Btn(MenuAction.WebExitFullscreen, T("✕ Exit fullscreen"))));
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Browser, m.Title.Length > 0 ? m.Title : T("VR browser"),
            T("Tap to show or hide this bar"), aspect, e) { Width = width };
    }

    private static UiWindow BrowserFormats(BrowserUiModel m)
    {
        const float aspect = 2.2f, width = 2.2f;
        var formats = BrowserUiModel.Formats;
        var e = new List<UiElement>();
        const int perRow = 5;
        for (int row = 0; row * perRow < formats.Count; row++)
        {
            var tiles = Enumerable.Range(row * perRow, Math.Min(perRow, formats.Count - row * perRow))
                .Select(i => Tile(MenuAction.WebFormat, formats[i].ShortLabel, i, formats[i] == m.Format)).ToArray();
            // Rows keep the same column width, also the last, shorter one.
            float right = 0.03f + (0.94f + 0.012f) * tiles.Length / perRow - 0.012f;
            e.AddRange(Row(0.2f + row * 0.19f, 0.35f + row * 0.19f, 0.03f, right, tiles));
        }
        e.AddRange(Row(0.82f, 0.95f, 0.65f, 0.97f, Btn(MenuAction.Back, T("Back"))));
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Browser, T("Video format"), T("How the video was filmed: flat, 180° or 360°, and in 3D"), aspect, e) { Width = width };
    }

    private static UiWindow BrowserSites(BrowserUiModel m)
    {
        const float aspect = 1.9f, width = 2.2f;
        var sites = m.Sites ?? [];
        int pages = Math.Max(1, (sites.Count + BrowserUiModel.SitesPerPage - 1) / BrowserUiModel.SitesPerPage);
        int page = Math.Clamp(m.SitesPage, 0, pages - 1);
        var e = new List<UiElement>();
        var shown = sites.Skip(page * BrowserUiModel.SitesPerPage).Take(BrowserUiModel.SitesPerPage).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            int col = i % 2, row = i / 2;
            float x0 = 0.03f + col * 0.475f, y0 = 0.17f + row * 0.155f;
            string host = System.Uri.TryCreate(shown[i].Url, UriKind.Absolute, out var u) ? u.Host : shown[i].Url;
            e.Add(new UiElement(UiKind.Button, MenuAction.WebSite, new UiRect(x0, y0, x0 + 0.465f, y0 + 0.135f))
            {
                Text = shown[i].Title, Detail = host, Arg = page * BrowserUiModel.SitesPerPage + i,
            });
        }
        if (shown.Count == 0)
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.05f, 0.3f, 0.95f, 0.5f))
            {
                Text = T("No favorite sites yet"),
                Detail = T("Save a site with “☆ Site”, here or in the phone's browser."),
            });
        e.AddRange(Row(0.82f, 0.95f, 0.03f, 0.97f,
            Btn(MenuAction.WebSitesPage, T("◀ Page"), -1) with { Enabled = page > 0 },
            Btn(MenuAction.WebSitesPage, T("Page ▶"), 1) with { Enabled = page < pages - 1 },
            Btn(MenuAction.Back, T("Back"))));
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Browser, T("★ Favorites"), F("{0} of {1}", page + 1, pages), aspect, e) { Width = width };
    }
}
