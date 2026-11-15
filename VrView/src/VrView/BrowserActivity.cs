using System.Text.Json;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>
/// A simple browser for sites with VR videos. While a page plays a video it notices the address of the
/// stream or file (network requests and the page's video elements), so it can be watched in VrView's own
/// viewer instead of the site's, saved as a web video (also listed in the headset's library) or, for
/// whole files, downloaded. Sites can be saved as favorites. Videos protected with DRM cannot be played.
/// </summary>
[Activity(Label = "Navegador", Exported = false,
    Name = "net.rendezvous.vrview.BrowserActivity",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden)]
public class BrowserActivity : Activity
{
    public const string ExtraUrl = "vrview.url";
    private const int RequestStorage = 7;
    private const int ScanMs = 2500;

    // Video elements and links to videos in the page (requests made inside other frames are seen by the client).
    private const string ScanScript =
        "(function(){var r=[];function add(u){if(u&&typeof u==='string'&&u.indexOf('blob:')!==0&&u.indexOf('data:')!==0)r.push(u);}" +
        "document.querySelectorAll('video,audio,source').forEach(function(v){add(v.currentSrc);add(v.src);});" +
        "document.querySelectorAll('a[href]').forEach(function(a){if(/\\.(mp4|m4v|webm|mov|mkv|m3u8|mpd)([?#]|$)/i.test(a.href||''))add(a.href);});" +
        "return JSON.stringify({t:document.title,s:r.slice(0,60)});})()";

    private const string PauseScript = "document.querySelectorAll('video,audio').forEach(function(v){try{v.pause()}catch(e){}})";

    private AppSettings _settings = null!;
    private WebView _web = null!;
    private EditText _address = null!;
    private ProgressBar _progress = null!;
    private Button _starButton = null!, _videosButton = null!;
    private readonly DetectedMediaList _media = new();
    private readonly Handler _handler = new(Looper.MainLooper!);
    private string _pageUrl = "", _pageTitle = "";
    private bool _resumed;
    private Action? _afterPermission;

    public static Intent CreateIntent(Context context, string? url = null)
    {
        var intent = new Intent(context, typeof(BrowserActivity));
        if (url is not null) intent.PutExtra(ExtraUrl, url);
        return intent;
    }

    // ---- Lifecycle ----

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _settings = new AppSettings(this);
        SetContentView(BuildLayout());
        SetupWebView();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(GoBackOrClose));

        string? url = Intent?.GetStringExtra(ExtraUrl) ?? _settings.LastWebUrl;
        if (url is { Length: > 0 }) Load(url);
        else
        {
            _address.RequestFocus();
            if (_settings.Bookmarks.Sites.Count > 0) ShowFavorites();
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.GetStringExtra(ExtraUrl) is { Length: > 0 } url) Load(url);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _web.OnResume();
        _resumed = true;
        _handler.RemoveCallbacks(Scan);
        _handler.PostDelayed(Scan, ScanMs);
    }

    protected override void OnPause()
    {
        _resumed = false;
        _handler.RemoveCallbacks(Scan);
        _web.OnPause();
        CookieManager.Instance?.Flush();
        if (_pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) _settings.LastWebUrl = _pageUrl;
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _handler.RemoveCallbacks(Scan);
        _web.Destroy();
        base.OnDestroy();
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e?.KeyCode == Keycode.Back && !OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (e.Action == KeyEventActions.Up) GoBackOrClose();
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    private void GoBackOrClose()
    {
        if (_web.CanGoBack()) _web.GoBack();
        else Finish();
    }

    // ---- UI ----

    private int Dp(float v) => (int)(v * Resources!.DisplayMetrics!.Density + 0.5f);

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Rgb(18, 24, 32));

        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(4), Dp(4), Dp(4), 0);
        bar.AddView(SmallButton("◀", (_, _) => GoBackOrClose()));
        bar.AddView(SmallButton("▶", (_, _) => { if (_web.CanGoForward()) _web.GoForward(); }));
        bar.AddView(SmallButton("⟳", (_, _) => _web.Reload()));
        _address = new EditText(this)
        {
            Hint = T("Address or search"),
            InputType = Android.Text.InputTypes.TextVariationUri | Android.Text.InputTypes.ClassText,
            ImeOptions = Android.Views.InputMethods.ImeAction.Go,
        };
        _address.SetSingleLine(true);
        _address.SetTextColor(Color.White);
        _address.SetHintTextColor(Color.Rgb(120, 134, 145));
        _address.SetSelectAllOnFocus(true);
        _address.EditorAction += (_, e) =>
        {
            Load(WebAddress.FromInput(_address.Text ?? ""));
            HideKeyboard();
            e.Handled = true;
        };
        bar.AddView(_address, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        bar.AddView(SmallButton(T("Go"), (_, _) => { Load(WebAddress.FromInput(_address.Text ?? "")); HideKeyboard(); }));
        root.AddView(bar);

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetPadding(Dp(4), 0, Dp(4), 0);
        _starButton = SmallButton(T("☆ Site"), (_, _) => ToggleSiteBookmark());
        _videosButton = SmallButton("", (_, _) => ShowVideos());
        foreach (var b in new[] { _starButton, SmallButton(T("★ Favorites"), (_, _) => ShowFavorites()), _videosButton,
                                  SmallButton(T("✕ Close"), (_, _) => Finish()) })
            actions.AddView(b, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        root.AddView(actions);

        _progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100, Visibility = ViewStates.Gone };
        root.AddView(_progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(3)));

        _web = new WebView(this);
        root.AddView(_web, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        UpdateVideosButton();
        return root;
    }

    private Button SmallButton(string text, EventHandler onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetAllCaps(false);
        b.SetMinWidth(Dp(44));
        b.SetMinimumWidth(Dp(44));
        b.SetPadding(Dp(8), 0, Dp(8), 0);
        b.Click += onClick;
        return b;
    }

    private void HideKeyboard()
    {
        (GetSystemService(InputMethodService) as Android.Views.InputMethods.InputMethodManager)?.HideSoftInputFromWindow(_address.WindowToken, 0);
        _address.ClearFocus();
    }

    private void SetupWebView()
    {
        var s = _web.Settings!;
        s.JavaScriptEnabled = true;
        s.DomStorageEnabled = true;
        s.MediaPlaybackRequiresUserGesture = true;
        s.LoadWithOverviewMode = true;
        s.UseWideViewPort = true;
        s.BuiltInZoomControls = true;
        s.DisplayZoomControls = false;
        s.AllowFileAccess = false;
        var cookies = CookieManager.Instance!;
        cookies.SetAcceptCookie(true);
        cookies.SetAcceptThirdPartyCookies(_web, true);
        _web.SetWebViewClient(new Client(this));
        _web.SetWebChromeClient(new Chrome(this));
        _web.SetDownloadListener(new Downloads(this));
    }

    private void Load(string url)
    {
        _address.Text = url;
        _web.LoadUrl(url);
    }

    private void UpdateVideosButton()
    {
        int n = _media.Items.Count;
        _videosButton.Text = n > 0 ? F("🎬 Videos ({0})", n) : T("🎬 Videos");
        _videosButton.SetTextColor(n > 0 ? Color.Rgb(79, 195, 247) : Color.White);
        _starButton.Text = _settings.Bookmarks.FindSite(_pageUrl) is not null ? T("★ Site") : T("☆ Site");
    }

    // ---- Page events ----

    private void OnPageStarted(string? url)
    {
        if (url is null || WebBookmarks.SameUrl(url, _pageUrl)) return;
        _pageUrl = url;
        _pageTitle = "";
        _address.Text = url;
        _media.Clear();
        UpdateVideosButton();
    }

    private void OnUrlChanged(string? url)
    {
        // Pages that change their address without loading (single-page sites).
        if (url is null || url == _pageUrl) return;
        _pageUrl = url;
        if (!_address.HasFocus) _address.Text = url;
        UpdateVideosButton();
    }

    private void OnTitle(string? title)
    {
        if (!string.IsNullOrWhiteSpace(title)) _pageTitle = title.Trim();
    }

    private void OnProgress(int percent)
    {
        _progress.Progress = percent;
        _progress.Visibility = percent < 100 ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>A request of the page or one of its frames (any thread).</summary>
    private void OnRequest(string url, string? mimeType = null)
    {
        if (MediaSniffer.Classify(url, mimeType) is null) return;
        RunOnUiThread(() =>
        {
            if (_media.Add(url, _pageUrl, _pageTitle, mimeType)) UpdateVideosButton();
        });
    }

    private void Scan()
    {
        if (!_resumed) return;
        if (_pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            _web.EvaluateJavascript(ScanScript, new JsResult(OnScanResult));
        _handler.PostDelayed(Scan, ScanMs);
    }

    private void OnScanResult(string? result)
    {
        if (string.IsNullOrEmpty(result) || result == "null") return;
        try
        {
            // The script returns a JSON text, which arrives encoded as a JSON string.
            string? json = JsonDocument.Parse(result).RootElement.GetString();
            if (json is null) return;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("t", out var t) && t.GetString() is { Length: > 0 } title) _pageTitle = title;
            bool changed = false;
            if (doc.RootElement.TryGetProperty("s", out var list))
                foreach (var u in list.EnumerateArray())
                    if (u.GetString() is { } url) changed |= _media.Add(url, _pageUrl, _pageTitle);
            if (changed) UpdateVideosButton();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Android.Util.Log.Warn("VrView", "Page scan: " + ex.Message);
        }
    }

    // ---- Videos found on the page ----

    private void ShowVideos()
    {
        var items = _media.Playable;
        if (items.Count == 0)
        {
            new AlertDialog.Builder(this)
                .SetTitle(T("No videos found"))!
                .SetMessage(T("Play the video on the page (even for a few seconds) and press “🎬 Videos” again. Protected (DRM) videos cannot be watched outside the site."))!
                .SetPositiveButton(T("Got it"), (_, _) => { })!
                .Show();
            return;
        }
        string[] labels = items.Select(m =>
        {
            string host = System.Uri.TryCreate(m.Url, UriKind.Absolute, out var u) ? u.Host : "";
            return $"{m.KindLabel} · {m.FileName}\n{host}";
        }).ToArray();
        new AlertDialog.Builder(this)
            .SetTitle(T("Videos on this page"))!
            .SetItems(labels, (_, e) => VideoActions(items[e.Which]))!
            .SetNegativeButton(T("Close"), (_, _) => { })!
            .Show();
    }

    private void VideoActions(DetectedMedia media)
    {
        var options = new List<(string Label, Action Run)>();
        if (media.CanPlay)
        {
            options.Add((T("▶ Watch in the VrView viewer"), () => Play(media)));
            options.Add((T("★ Save to Web videos"), () => SaveVideo(media)));
        }
        if (media.CanDownload) options.Add((T("⬇ Download to Download/VrView"), () => Download(media.Url, null, null)));
        options.Add((T("📋 Copy the link"), () => CopyLink(media.Url)));
        string title = media.CanPlay ? MediaSniffer.DisplayName(media.Url, _pageTitle)
            : T("DASH: the system player cannot play this format");
        new AlertDialog.Builder(this)
            .SetTitle(title)!
            .SetItems(options.Select(o => o.Label).ToArray(), (_, e) => options[e.Which].Run())!
            .Show();
    }

    private void Play(DetectedMedia media)
    {
        // A saved video of this page gets its address renewed (they often expire) and keeps its state.
        var bookmarks = _settings.Bookmarks;
        MediaItem item;
        if (bookmarks.FindVideo(media.PageUrl, media.Kind) is { } saved)
        {
            saved.MediaUrl = media.Url;
            _settings.Bookmarks = bookmarks;
            item = WebBookmarkItems.ToItem(saved);
        }
        else
        {
            item = new MediaItem(media.Url, MediaSniffer.DisplayName(media.Url, _pageTitle), 0, media.PageUrl,
                "web:" + MediaSniffer.PathOf(media.Url));
        }
        _web.EvaluateJavascript(PauseScript, null);
        CookieManager.Instance?.Flush();
        StartActivity(PlayerActivity.CreateIntent(this, [item], 0));
    }

    private void SaveVideo(DetectedMedia media)
    {
        var input = new EditText(this) { Text = MediaSniffer.DisplayName(media.Url, _pageTitle) };
        input.SetSingleLine(true);
        new AlertDialog.Builder(this)
            .SetTitle(T("Video name"))!
            .SetMessage(T("Format tags in the name (180, 360, LR, TB…) help show it correctly."))!
            .SetView(input)!
            .SetPositiveButton(T("Save"), (_, _) =>
            {
                var bookmarks = _settings.Bookmarks;
                int before = bookmarks.Videos.Count;
                var saved = bookmarks.SaveVideo(media, input.Text ?? "", Java.Lang.JavaSystem.CurrentTimeMillis());
                _settings.Bookmarks = bookmarks;
                Toast.MakeText(this, bookmarks.Videos.Count > before
                    ? F("Saved “{0}”: it is in “★ Web videos” and in the viewer's Library", saved.Title)
                    : F("Link of “{0}” renewed", saved.Title), ToastLength.Long)!.Show();
            })!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }

    private void CopyLink(string url)
    {
        if (GetSystemService(ClipboardService) is ClipboardManager clipboard) clipboard.PrimaryClip = ClipData.NewPlainText("video", url);
        Toast.MakeText(this, T("Link copied"), ToastLength.Short)!.Show();
    }

    // ---- Downloads ----

    private void Download(string url, string? contentDisposition, string? mimeType)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) &&
            CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
        {
            // Android 8–9 need the storage permission to save in Descargas.
            _afterPermission = () => Download(url, contentDisposition, mimeType);
            RequestPermissions([Manifest.Permission.WriteExternalStorage], RequestStorage);
            return;
        }
        string name = URLUtil.GuessFileName(url, contentDisposition, mimeType) ?? MediaSniffer.FileName(url);
        if (MediaSniffer.Classify(url, mimeType) == StreamKind.File && !string.IsNullOrEmpty(MediaSniffer.FileName(url)))
            name = MediaSniffer.FileName(url);
        name = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || c is ':' or '*' or '?' ? '_' : c));
        try
        {
            var request = new DownloadManager.Request(Uri.Parse(url));
            foreach (var (key, value) in WebHeaders.For(this, url, _pageUrl)) request.AddRequestHeader(key, value);
            request.SetTitle(name);
            request.SetDescription("VrView");
            if (mimeType is { Length: > 0 }) request.SetMimeType(mimeType);
            request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
            request.SetDestinationInExternalPublicDir(Android.OS.Environment.DirectoryDownloads, "VrView/" + name);
            (GetSystemService(DownloadService) as DownloadManager)!.Enqueue(request);
            Toast.MakeText(this, F("Downloading “{0}” to Download/VrView (choose that folder in the list to watch it)", name), ToastLength.Long)!.Show();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or ArgumentException)
        {
            Toast.MakeText(this, F("Could not download: {0}", ex.Message), ToastLength.Long)!.Show();
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != RequestStorage) return;
        var next = _afterPermission;
        _afterPermission = null;
        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted) next?.Invoke();
        else Toast.MakeText(this, T("Without storage permission nothing can be saved to Download"), ToastLength.Long)!.Show();
    }

    // ---- Favorites ----

    private void ToggleSiteBookmark()
    {
        if (!_pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        var bookmarks = _settings.Bookmarks;
        bool saved = bookmarks.ToggleSite(_pageUrl, _pageTitle);
        _settings.Bookmarks = bookmarks;
        Toast.MakeText(this, saved ? T("Site saved to favorites") : T("Site removed from favorites"), ToastLength.Short)!.Show();
        UpdateVideosButton();
    }

    private void ShowFavorites()
    {
        var bookmarks = _settings.Bookmarks;
        var sites = bookmarks.Sites;
        var labels = sites.Select(s => s.Title + "\n" + s.Url).ToList();
        labels.Add(F("★ Saved web videos ({0})…", bookmarks.Videos.Count));
        var dialog = new AlertDialog.Builder(this)
            .SetTitle(sites.Count == 0 ? T("Favorites · save a site with “☆ Site”") : T("Favorites · long-press to delete"))!
            .SetItems(labels.ToArray(), (_, e) =>
            {
                if (e.Which < sites.Count) Load(sites[e.Which].Url);
                else WebVideosDialog.Show(this, _settings, openPage: Load);
            })!
            .SetNegativeButton(T("Close"), (_, _) => { })!
            .Create()!;
        dialog.Show();
        dialog.ListView!.ItemLongClick += (_, e) =>
        {
            if (e.Position >= sites.Count) return;
            var site = sites[e.Position];
            dialog.Dismiss();
            new AlertDialog.Builder(this)
                .SetTitle(site.Title)!
                .SetItems([T("Rename…"), T("Remove from favorites")], (_, a) =>
                {
                    if (a.Which == 1) DeleteSite(site.Url);
                    else RenameSite(site.Url, site.Title);
                })!
                .Show();
        };
    }

    private void DeleteSite(string url)
    {
        var bookmarks = _settings.Bookmarks;
        bookmarks.Sites.RemoveAll(s => s.Url == url);
        _settings.Bookmarks = bookmarks;
        UpdateVideosButton();
    }

    private void RenameSite(string url, string title)
    {
        var input = new EditText(this) { Text = title };
        input.SetSingleLine(true);
        new AlertDialog.Builder(this)
            .SetTitle(T("Site name"))!
            .SetView(input)!
            .SetPositiveButton(T("Save"), (_, _) =>
            {
                var bookmarks = _settings.Bookmarks;
                if (bookmarks.Sites.FirstOrDefault(s => s.Url == url) is { } site && (input.Text ?? "").Trim() is { Length: > 0 } t)
                    site.Title = t;
                _settings.Bookmarks = bookmarks;
            })!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }

    // ---- WebView callbacks ----

    private sealed class Client(BrowserActivity a) : WebViewClient
    {
        public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
        {
            // Only watched, never changed: the page loads exactly as in any browser.
            if (request?.Url?.ToString() is { } url) a.OnRequest(url);
            return null;
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            string? url = request?.Url?.ToString();
            if (url is null) return false;
            // App links (intent:, market:, …) are not opened inside the browser.
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return true;
            // A link straight to a video: offer it instead of opening the bare file.
            if (request!.IsForMainFrame && MediaSniffer.Classify(url) is not null)
            {
                a.OnRequest(url);
                a.ShowVideos();
                return true;
            }
            return false;
        }

        public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            a.OnPageStarted(url);
        }

        public override void DoUpdateVisitedHistory(WebView? view, string? url, bool isReload)
        {
            base.DoUpdateVisitedHistory(view, url, isReload);
            a.OnUrlChanged(url);
        }
    }

    private sealed class Chrome(BrowserActivity a) : WebChromeClient
    {
        public override void OnProgressChanged(WebView? view, int newProgress) => a.OnProgress(newProgress);
        public override void OnReceivedTitle(WebView? view, string? title) => a.OnTitle(title);
    }

    private sealed class Downloads(BrowserActivity a) : Java.Lang.Object, IDownloadListener
    {
        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition, string? mimetype, long contentLength)
        {
            if (url is null) return;
            if (MediaSniffer.Classify(url, mimetype) is { } kind && kind != StreamKind.Dash)
            {
                // A video the site offers to download: it can also be watched right away.
                a._media.Add(url, a._pageUrl, a._pageTitle, mimetype);
                a.UpdateVideosButton();
                var media = a._media.Items[0];
                new AlertDialog.Builder(a)
                    .SetTitle(MediaSniffer.DisplayName(url, a._pageTitle))!
                    .SetItems([T("⬇ Download to Download/VrView"), T("▶ Watch now in the viewer"), T("★ Save to Web videos")], (_, e) =>
                    {
                        if (e.Which == 0) a.Download(url, contentDisposition, mimetype);
                        else if (e.Which == 1) a.Play(media);
                        else a.SaveVideo(media);
                    })!
                    .Show();
                return;
            }
            a.Download(url, contentDisposition, mimetype);
        }
    }

    private sealed class JsResult(Action<string?> done) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => done(value?.ToString());
    }

    private sealed class BackCallback(Action onBack) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => onBack();
    }
}
