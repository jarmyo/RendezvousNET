namespace VrView.Core;

/// <summary>What the VR browser's window shows.</summary>
public enum BrowserPane { Bar, Sites, Formats, Keyboard, Options, Dialog }

/// <summary>Where the VR keyboard writes: the focused field of the page, or the browser's address.</summary>
public enum KeyboardTarget { Page, Address }

/// <summary>Keys shown: letters (with ñ) or numbers, symbols and accented letters.</summary>
public enum KeyboardLayer { Letters, Symbols }

/// <summary>An option of a list of the page (a &lt;select&gt;), which the VR browser shows in its own window.</summary>
public sealed record PageOption(string Text, bool Selected, bool Enabled = true);

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
    int SitesPage = 0,
    KeyboardTarget Target = KeyboardTarget.Page,
    KeyboardLayer Layer = KeyboardLayer.Letters,
    bool Shift = false,
    string Typed = "",
    string FieldHint = "",
    bool Password = false,
    int Caret = -1,
    IReadOnlyList<PageOption>? Options = null,
    bool MultipleChoice = false,
    int OptionsPage = 0,
    string DialogText = "",
    bool DialogAsks = false)
{
    public const int SitesPerPage = 8;
    public const int OptionsPerPage = 12;

    /// <summary>The page of the options list that shows the selected option.</summary>
    public static int PageOfSelected(IReadOnlyList<PageOption> options)
    {
        int i = -1;
        for (int k = 0; k < options.Count && i < 0; k++) if (options[k].Selected) i = k;
        return Math.Max(0, i) / OptionsPerPage;
    }

    /// <summary>The keys of a layer, row by row (special keys are added around them).</summary>
    public static IReadOnlyList<string> KeyRows(KeyboardLayer layer) => layer == KeyboardLayer.Letters
        ? ["1234567890", "qwertyuiop", "asdfghjkl\u00f1", "zxcvbnm"]
        : ["1234567890", "@#$%&*-+()", "!\"':;/?_=~", "\u00e1\u00e9\u00ed\u00f3\u00fa\u00fc\u00bf\u00a1"];

    /// <summary>The character a key types, with shift applied.</summary>
    public static string KeyText(char key, bool shift) => shift ? char.ToUpperInvariant(key).ToString() : key.ToString();

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
        BrowserPane.Keyboard => BrowserKeyboard(m),
        BrowserPane.Options => BrowserOptions(m),
        BrowserPane.Dialog => BrowserDialog(m),
        _ => m.Fullscreen ? BrowserFullscreenBar(m) : BrowserBar(m),
    };

    private static UiWindow BrowserBar(BrowserUiModel m)
    {
        const float aspect = 3.3f, width = 2.4f;
        var e = new List<UiElement>();
        e.AddRange(Row(0.27f, 0.47f, 0.02f, 0.98f,
            Btn(MenuAction.WebBack, T("◀ Back")) with { Enabled = m.CanGoBack },
            Btn(MenuAction.WebForward, T("Forward ▶")) with { Enabled = m.CanGoForward },
            Btn(MenuAction.WebReload, "⟳"),
            Btn(MenuAction.WebAddress, T("🔍 Address")),
            Btn(MenuAction.WebKeyboard, T("⌨ Keyboard")),
            Btn(MenuAction.Recenter, T("Recenter"))));
        e.AddRange(Row(0.51f, 0.71f, 0.02f, 0.98f,
            Btn(MenuAction.WebScrollUp, T("▲ Up"), repeat: true), Btn(MenuAction.WebScrollDown, T("▼ Down"), repeat: true),
            Btn(MenuAction.WebZoomOut, "−"), Btn(MenuAction.WebZoomIn, "＋"),
            Btn(MenuAction.WebSites, T("★ Favorites")),
            Btn(MenuAction.WebStar, m.SiteSaved ? T("★ Site") : T("☆ Site"))));
        e.AddRange(Row(0.75f, 0.95f, 0.02f, 0.98f,
            Btn(MenuAction.WebFullscreen, T("⛶ Video in VR")),
            Btn(MenuAction.WebPlayer, m.VideoCount > 0 ? F("🎬 Player ({0})", m.VideoCount) : T("🎬 Player")) with { Enabled = m.VideoCount > 0 },
            Btn(MenuAction.WebPhone, T("📱 Phone screen")),
            Btn(MenuAction.Exit, T("Exit"))));
        string sub = m.Progress < 100 ? F("Loading… {0}%", m.Progress) + "   ·   " + m.Url : m.Url;
        return new UiWindow(WindowKind.Browser, m.Title.Length > 0 ? m.Title : T("VR browser"), sub, aspect, e) { Width = width };
    }

    private static UiWindow BrowserKeyboard(BrowserUiModel m)
    {
        const float aspect = 2.3f, width = 2.5f;
        const float x0 = 0.02f, x1 = 0.98f, top = 0.33f, rowH = 0.115f, gap = 0.012f;
        var e = new List<UiElement>();
        // What is being written, with the caret (around the caret when it is long).
        string shown = WebInput.WithCaret(m.Password ? new string('•', m.Typed.Length) : m.Typed, m.Caret);
        if (shown.Length > 72)
        {
            int at = Math.Clamp(shown.IndexOf('▏') - 50, 0, shown.Length - 71);
            shown = (at > 0 ? "…" : "") + shown.Substring(at, 71) + (at + 71 < shown.Length ? "…" : "");
        }
        string label = m.Target == KeyboardTarget.Address ? T("Address or search")
            : m.FieldHint.Length > 0 ? m.FieldHint : T("Text field of the page");
        e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(x0, 0.16f, x1, 0.30f)) { Text = shown, Detail = label });

        var rows = BrowserUiModel.KeyRows(m.Layer);
        UiElement Key(char c) => Btn(MenuAction.WebKey, BrowserUiModel.KeyText(c, m.Shift), c);
        float Y(int row) => top + row * (rowH + gap);
        // Every key the same width as in the 10-key rows; the shorter rows are centered with their special keys.
        float keyW = (x1 - x0 - gap * 9) / 10;
        for (int r = 0; r < 3; r++)
            e.AddRange(Row(Y(r), Y(r) + rowH, x0, x1, rows[r].Select(Key).ToArray()));
        var row4 = new List<(UiElement El, float Units)>();
        row4.Add((Tile(MenuAction.WebShift, m.Layer == KeyboardLayer.Letters ? "⇧" : "⇧ \u00c1\u00c9", 0, m.Shift), 1.5f));
        row4.AddRange(rows[3].Select(c => (Key(c), 1f)));
        row4.Add((Btn(MenuAction.WebBackspace, "⌫", repeat: true), 1.5f));
        e.AddRange(Weighted(Y(3), Y(3) + rowH, x0, x1, keyW, row4));
        var row5 = new List<(UiElement El, float Units)>
        {
            (Btn(MenuAction.WebKeyLayer, m.Layer == KeyboardLayer.Letters ? "?123" : "ABC",
                (int)(m.Layer == KeyboardLayer.Letters ? KeyboardLayer.Symbols : KeyboardLayer.Letters)), 1.5f),
            (Key(','), 1f),
            (Btn(MenuAction.WebKey, T("space"), ' '), 3f),
            (Key('.'), 1f),
            (m.Target == KeyboardTarget.Address ? Btn(MenuAction.WebKeyText, ".com") : Key('@'), 1f),
            (Btn(MenuAction.WebCaretLeft, "←", repeat: true), 1f),
            (Btn(MenuAction.WebCaretRight, "→", repeat: true), 1f),
            (Btn(MenuAction.WebEnter, m.Target == KeyboardTarget.Address ? T("Go") : "⏎"), 1.5f),
        };
        e.AddRange(Weighted(Y(4), Y(4) + rowH, x0, x1, keyW, row5));
        e.Add(Close(aspect, width));
        string title = m.Target == KeyboardTarget.Address ? T("🔍 Address") : T("⌨ Keyboard");
        string sub = m.Target == KeyboardTarget.Address ? T("Type an address or words to search, then Go")
            : T("Point at a key and tap; tap the page to choose another field");
        return new UiWindow(WindowKind.Browser, title, sub, aspect, e) { Width = width };
    }

    /// <summary>A row of keys with widths in key units, centered, keeping the size of the full rows' keys.</summary>
    private static IEnumerable<UiElement> Weighted(float y0, float y1, float left, float right, float keyW,
        IReadOnlyList<(UiElement El, float Units)> items)
    {
        const float gap = 0.012f;
        float total = items.Sum(i => i.Units * keyW) + gap * (items.Count - 1);
        float scale = total > right - left ? (right - left - gap * (items.Count - 1)) / (total - gap * (items.Count - 1)) : 1f;
        float used = items.Sum(i => i.Units * keyW * scale) + gap * (items.Count - 1);
        float x = left + (right - left - used) / 2;
        foreach (var (el, units) in items)
        {
            float w = units * keyW * scale;
            yield return el with { Rect = new UiRect(x, y0, x + w, y1) };
            x += w + gap;
        }
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

    /// <summary>A message of the page (alert), or a question (confirm) with Cancel.</summary>
    private static UiWindow BrowserDialog(BrowserUiModel m)
    {
        const float aspect = 2.4f, width = 2.2f;
        string text = m.DialogText.Trim();
        text = text.Length > 400 ? text[..399] + "…" : text;
        // A short message in one bright line; a long one wrapped.
        bool oneLine = text.Length <= 48 && !text.Contains('\n');
        var e = new List<UiElement>
        {
            new(UiKind.Text, MenuAction.None, new UiRect(0.05f, 0.2f, 0.95f, 0.68f))
            {
                Text = oneLine ? text : "", Detail = oneLine ? "" : text, Active = true,
            },
        };
        e.AddRange(m.DialogAsks
            ? Row(0.74f, 0.94f, 0.3f, 0.95f, Btn(MenuAction.WebDialogCancel, T("Cancel")), Btn(MenuAction.WebDialogOk, T("OK")))
            : Row(0.74f, 0.94f, 0.6f, 0.95f, Btn(MenuAction.WebDialogOk, T("OK"))));
        return new UiWindow(WindowKind.Browser, T("Message from the page"), m.Url, aspect, e) { Width = width };
    }

    private static UiWindow BrowserOptions(BrowserUiModel m)
    {
        const float aspect = 1.9f, width = 2.2f;
        var options = m.Options ?? [];
        int pages = Math.Max(1, (options.Count + BrowserUiModel.OptionsPerPage - 1) / BrowserUiModel.OptionsPerPage);
        int page = Math.Clamp(m.OptionsPage, 0, pages - 1);
        var e = new List<UiElement>();
        int first = page * BrowserUiModel.OptionsPerPage;
        int count = Math.Min(BrowserUiModel.OptionsPerPage, options.Count - first);
        for (int i = 0; i < count; i++)
        {
            int col = i % 2, row = i / 2;
            float x0 = 0.03f + col * 0.475f, y0 = 0.16f + row * 0.108f;
            var o = options[first + i];
            e.Add(Tile(MenuAction.WebOption, o.Text.Length > 0 ? o.Text : "—", first + i, o.Selected) with
            {
                Rect = new UiRect(x0, y0, x0 + 0.465f, y0 + 0.095f), Enabled = o.Enabled,
            });
        }
        e.AddRange(Row(0.82f, 0.95f, 0.03f, 0.97f,
            Btn(MenuAction.WebOptionsPage, T("◀ Page"), -1) with { Enabled = page > 0 },
            Btn(MenuAction.WebOptionsPage, T("Page ▶"), 1) with { Enabled = page < pages - 1 },
            Btn(MenuAction.Back, m.MultipleChoice ? T("Done") : T("Back"))));
        e.Add(Close(aspect, width));
        string title = m.FieldHint.Length > 0 ? m.FieldHint : T("Choose an option");
        string sub = pages > 1 ? F("{0} of {1}", page + 1, pages) : m.MultipleChoice ? T("Choose one or more") : "";
        return new UiWindow(WindowKind.Browser, title, sub, aspect, e) { Width = width };
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
