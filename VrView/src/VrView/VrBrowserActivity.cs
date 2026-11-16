using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using VrView.Core;
using VrView.Gl;

namespace VrView;

/// <summary>
/// The browser inside the headset: the page floats in front of the user as a big screen, with a bar
/// under it. Links are clicked with the reticle and the viewer button (or a tap). A video of the page
/// can be watched in VR fullscreen: the page's own fullscreen, or "⛶ Video in VR" for any video, fills
/// the whole picture and is drawn with the same engine as the player — as a cinema screen or as a
/// 180°/360° video, also in 3D. The page is drawn by <see cref="WebSurface"/> into the renderer's
/// texture, so even videos the system player cannot open (protected or in-page streams) can be seen.
/// </summary>
[Activity(Label = "VrView", Exported = false,
    Name = "net.rendezvous.vrview.VrBrowserActivity",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.Navigation | ConfigChanges.UiMode)]
public class VrBrowserActivity : Activity, VrRenderer.IHost
{
    private const string ExtraUrl = "vrview.url";
    private const string HomeUrl = "https://www.google.com";
    private const int TickMs = 500;
    // The page as a 16:10 screen; at this density sites use their desktop-like layout with readable text.
    private const int PageWidth = 1280, PageHeight = 800, PageDpi = 180;
    private const int VideoLongSide = 2048, VideoDpi = 240;
    /// <summary>Pitch of the bar, under the page (the page is about 40° tall).</summary>
    private const float BarPitch = -27f;
    private const float BigScreenScale = 1.8f;
    private const long BarHideMs = 8000;

    // Finds the video to show in fullscreen: the page's fullscreen element, else the playing / biggest
    // video, else the biggest frame (a video embedded from another site cannot be reached from the page).
    private const string FindVideoJs =
        "function vrvTarget(){var fe=document.fullscreenElement||document.webkitFullscreenElement;" +
        "function area(e){var r=e.getBoundingClientRect();return r.width*r.height;}" +
        "var m=document.querySelector('[data-vrview-fs]');if(m)return m;" +
        "if(fe)return fe.tagName==='VIDEO'?fe:(fe.querySelector('video')||fe);" +
        "var vs=[].slice.call(document.querySelectorAll('video'));" +
        "vs.sort(function(a,b){return ((b.paused?0:1)-(a.paused?0:1))||(area(b)-area(a));});" +
        "var best=vs[0]||null;var fr=[].slice.call(document.querySelectorAll('iframe'));" +
        "fr.sort(function(a,b){return area(b)-area(a);});" +
        "if(fr[0]&&area(fr[0])>(best?area(best)*1.5:400*200))best=fr[0];return best;}" +
        "function vrvVideo(){var t=vrvTarget();if(!t)return null;if(t.tagName==='VIDEO')return t;" +
        "try{return t.querySelector('video');}catch(e){return null;}}";

    // Makes the target fill the page (when the page has no fullscreen of its own) and returns its size.
    private const string EnterJs = "(function(){" + FindVideoJs +
        "var t=vrvTarget();if(!t)return '';var fe=document.fullscreenElement||document.webkitFullscreenElement;" +
        "if(!fe&&!document.getElementById('vrview-fs')){t.setAttribute('data-vrview-fs','1');" +
        "var s=document.createElement('style');s.id='vrview-fs';s.textContent=" +
        "'html,body{overflow:hidden!important}body *{visibility:hidden!important}" +
        "[data-vrview-fs],[data-vrview-fs] *{visibility:visible!important}" +
        "[data-vrview-fs]{position:fixed!important;left:0!important;top:0!important;right:0!important;bottom:0!important;" +
        "width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;margin:0!important;" +
        "padding:0!important;border:0!important;transform:none!important;z-index:2147483647!important;" +
        "background:#000!important;object-fit:contain!important}';document.head.appendChild(s);}" +
        "var v=vrvVideo();var w=v&&v.videoWidth?v.videoWidth:0,h=v&&v.videoHeight?v.videoHeight:0;" +
        "return w+'x'+h;})()";

    private const string ExitJs =
        "(function(){var s=document.getElementById('vrview-fs');if(s)s.remove();" +
        "document.querySelectorAll('[data-vrview-fs]').forEach(function(e){e.removeAttribute('data-vrview-fs');});})()";

    private const string StateJs = "(function(){" + FindVideoJs +
        "var v=vrvVideo();if(!v)return '-1';return (v.paused?'0':'1')+'x'+(v.videoWidth||0)+'x'+(v.videoHeight||0);})()";

    private const string PlayPauseJs = "(function(){" + FindVideoJs +
        "var v=vrvVideo();if(v){if(v.paused)v.play();else v.pause();}})()";

    private static string SeekJs(int seconds) => "(function(){" + FindVideoJs +
        $"var v=vrvVideo();if(v)v.currentTime=Math.max(0,v.currentTime+({seconds}));}})()";

    private AppSettings _settings = null!;
    private HeadTracker _tracker = null!;
    private VrRenderer _renderer = null!;
    private GLSurfaceView _glView = null!;
    private WebSurface _web = null!;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly DetectedMediaList _media = new();
    private bool _ready;

    private string _pageUrl = "", _pageTitle = "";
    private int _progress = 100;
    private BrowserPane _pane = BrowserPane.Bar;
    private int _sitesPage;

    // VR fullscreen
    private bool _fullscreen, _barVisible = true, _bigScreen, _playing;
    private VideoFormat _format = VideoFormat.Default;
    private int _videoWidth, _videoHeight;
    private WebChromeClient.ICustomViewCallback? _customCallback;
    private long _lastActionAt;

    // Touch without the headset: drag to look around
    private float _downX, _downY, _lastX, _lastY;
    private long _downTime;
    private bool _dragging;

    public static Intent CreateIntent(Context context, string? url = null)
    {
        var intent = new Intent(context, typeof(VrBrowserActivity));
        if (url is not null) intent.PutExtra(ExtraUrl, url);
        return intent;
    }

    // ---- Lifecycle ----

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AppLanguage.Apply(this);
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            Window.Attributes!.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
        VolumeControlStream = Android.Media.Stream.Music;

        _settings = new AppSettings(this);
        _tracker = new HeadTracker(this) { Mode = _settings.Tracking };
        _renderer = new VrRenderer(_tracker, this)
        {
            ContentClick = (u, v) => RunOnUiThread(() => OnPageClick(u, v)),
            AnchoredWindowPitch = BarPitch,
        };
        _tracker.ReferenceChanged += () => _renderer.RequestRecenter(yawOnly: true);

        _web = new WebSurface(this, PageWidth, PageHeight, PageDpi);
        SetupWebView(_web.Web);
        PushConfig();

        _glView = new GLSurfaceView(this);
        _glView.SetEGLContextClientVersion(2);
        _glView.SetEGLConfigChooser(new MultisampleConfigChooser());
        _glView.PreserveEGLContextOnPause = true;
        _glView.SetRenderer(_renderer);
        _glView.RenderMode = Rendermode.Continuously;
        _glView.Touch += OnGlTouch;
        SetContentView(_glView);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(HandleBack));
        _ready = true;

        if (!_tracker.IsAvailable) _settings.StereoView = false;
        string url = Intent?.GetStringExtra(ExtraUrl) ?? _settings.LastWebUrl ?? _settings.Bookmarks.Sites.FirstOrDefault()?.Url ?? HomeUrl;
        Load(url);
        RefreshWindow();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.GetStringExtra(ExtraUrl) is { Length: > 0 } url)
        {
            ExitFullscreen();
            Load(url);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!_ready) return;
        HideSystemUi();
        _tracker.DisplayRotation = CurrentDisplayRotation();
        _tracker.Start();
        _glView.OnResume();
        _web.Web.OnResume();
        _handler.RemoveCallbacks(Tick);
        _handler.PostDelayed(Tick, TickMs);
    }

    protected override void OnPause()
    {
        if (_ready)
        {
            _handler.RemoveCallbacks(Tick);
            _tracker.Stop();
            _glView.OnPause();
            _web.Web.OnPause();
            CookieManager.Instance?.Flush();
            if (_pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) _settings.LastWebUrl = _pageUrl;
        }
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _handler.RemoveCallbacksAndMessages(null);
        if (_ready) _web.Dispose();
        base.OnDestroy();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideSystemUi();
    }

    private void HideSystemUi()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1422
            Window!.SetDecorFitsSystemWindows(false);
#pragma warning restore CA1422
            var c = Window.InsetsController;
            if (c is null) return;
            c.Hide(WindowInsets.Type.SystemBars());
            c.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
        else
        {
#pragma warning disable CA1422
            Window!.DecorView.SystemUiFlags = SystemUiFlags.ImmersiveSticky |
                SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen | SystemUiFlags.LayoutStable |
                SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
#pragma warning restore CA1422
        }
    }

    private int CurrentDisplayRotation()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) return (int)(Display?.Rotation ?? SurfaceOrientation.Rotation90);
#pragma warning disable CA1422
        return (int)(WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation90);
#pragma warning restore CA1422
    }

    private (float W, float H) ScreenMeters()
    {
        var m = Resources!.DisplayMetrics!;
        float w = Math.Max(m.WidthPixels, m.HeightPixels), h = Math.Min(m.WidthPixels, m.HeightPixels);
        float xdpi = m.Xdpi > 50 ? m.Xdpi : (int)m.DensityDpi, ydpi = m.Ydpi > 50 ? m.Ydpi : (int)m.DensityDpi;
        return (w / xdpi * 0.0254f, h / ydpi * 0.0254f);
    }

    // ---- Renderer callbacks (GL thread) ----

    public void OnVideoSurfaceCreated(SurfaceTexture surfaceTexture) => RunOnUiThread(() =>
    {
        if (IsDestroyed) { surfaceTexture.Release(); return; }
        _web.Attach(surfaceTexture);
    });

    public void OnMenuAction(MenuAction action, float fraction, int arg) => RunOnUiThread(() => HandleAction(action, arg));

    public void OnMenuRequested() => RunOnUiThread(() =>
    {
        // In fullscreen a click (or a look down) brings the bar back.
        _barVisible = true;
        _lastActionAt = SystemClock.UptimeMillis();
        RefreshWindow();
    });

    public void OnHorizonCaptured(float pitch, float roll, float yaw) { }

    public void OnCenterCaptured() { }

    public void OnCaptureCancelled(bool centering) { }

    // ---- Rendering state ----

    private void PushConfig()
    {
        var (sw, sh) = ScreenMeters();
        var format = _fullscreen ? _format : VideoFormat.Default;
        float scale = _fullscreen && _bigScreen && format.Projection == Projection.Flat ? BigScreenScale : 1f;
        // No previous/next buttons at the edges: there is no playlist here.
        var view = _settings.View(sw, sh) with { EdgeButtonsOn = false };
        _renderer.Config = new RenderConfig(format, _web.Width, _web.Height, 0, 0, 0, view, FlatScale: scale);
        // The bar sits under the page; in fullscreen it opens where the user looks.
        _renderer.AnchoredWindowPitch = _fullscreen ? float.NaN : BarPitch;
    }

    private BrowserUiModel Model() => new(_pane, _pageTitle, _pageUrl, _web.Web.CanGoBack(), _web.Web.CanGoForward(), _progress,
        _media.Playable.Count(m => m.CanPlay), _settings.Bookmarks.FindSite(_pageUrl) is not null, _fullscreen, _format,
        _bigScreen, _playing, _settings.Bookmarks.Sites, _sitesPage);

    private void RefreshWindow()
    {
        bool hidden = _fullscreen && !_barVisible && _pane == BrowserPane.Bar;
        _renderer.Window = hidden ? null : Windows.Browser(Model());
    }

    private void Tick()
    {
        if (_fullscreen)
        {
            if (_barVisible && _pane == BrowserPane.Bar && SystemClock.UptimeMillis() - _lastActionAt > BarHideMs)
                _barVisible = false;
            _web.Web.EvaluateJavascript(StateJs, new JsResult(OnVideoState));
        }
        RefreshWindow();
        _handler.PostDelayed(Tick, TickMs);
    }

    private void OnVideoState(string? result)
    {
        string s = Unquote(result);
        var parts = s.Split('x');
        _playing = parts[0] == "1";
        if (parts.Length == 3 && int.TryParse(parts[1], out int w) && int.TryParse(parts[2], out int h) && w > 0 && h > 0 &&
            (w != _videoWidth || h != _videoHeight))
        {
            // The real size arrived after the start of the video (or the stream changed quality).
            _videoWidth = w;
            _videoHeight = h;
            if (_fullscreen) FitPageToVideo();
        }
    }

    // ---- The page ----

    private void SetupWebView(WebView web)
    {
        var s = web.Settings!;
        s.JavaScriptEnabled = true;
        s.DomStorageEnabled = true;
        s.MediaPlaybackRequiresUserGesture = false;
        s.LoadWithOverviewMode = true;
        s.UseWideViewPort = true;
        s.BuiltInZoomControls = true;
        s.DisplayZoomControls = false;
        s.AllowFileAccess = false;
        var cookies = CookieManager.Instance!;
        cookies.SetAcceptCookie(true);
        cookies.SetAcceptThirdPartyCookies(web, true);
        web.SetWebViewClient(new Client(this));
        web.SetWebChromeClient(new Chrome(this));
    }

    private void Load(string url)
    {
        _web.Web.LoadUrl(url);
        _pageUrl = url;
        RefreshWindow();
    }

    private void OnPageStarted(string? url)
    {
        if (url is null || WebBookmarks.SameUrl(url, _pageUrl) && _pageTitle.Length > 0) return;
        _pageUrl = url;
        _pageTitle = "";
        _media.Clear();
        RefreshWindow();
    }

    private void OnUrlChanged(string? url)
    {
        if (url is null || url == _pageUrl) return;
        _pageUrl = url;
        RefreshWindow();
    }

    private void OnRequest(string url, string? mimeType = null)
    {
        if (MediaSniffer.Classify(url, mimeType) is null) return;
        RunOnUiThread(() => { if (_media.Add(url, _pageUrl, _pageTitle, mimeType)) RefreshWindow(); });
    }

    private void OnPageClick(float u, float v)
    {
        _lastActionAt = SystemClock.UptimeMillis();
        if (_pane != BrowserPane.Bar) { _pane = BrowserPane.Bar; RefreshWindow(); return; }
        _web.Tap(u, v);
    }

    // ---- Actions ----

    private void HandleAction(MenuAction action, int arg)
    {
        _lastActionAt = SystemClock.UptimeMillis();
        var web = _web.Web;
        switch (action)
        {
            case MenuAction.WebBack: if (web.CanGoBack()) web.GoBack(); break;
            case MenuAction.WebForward: if (web.CanGoForward()) web.GoForward(); break;
            case MenuAction.WebReload: web.Reload(); break;
            case MenuAction.WebScrollUp: _web.Drag(0.5f, 0.35f, -0.3f); break;
            case MenuAction.WebScrollDown: _web.Drag(0.5f, 0.65f, 0.3f); break;
            case MenuAction.WebZoomIn: web.ZoomIn(); break;
            case MenuAction.WebZoomOut: web.ZoomOut(); break;
            case MenuAction.Recenter: _glView.QueueEvent(() => _renderer.RequestRecenter()); break;
            case MenuAction.WebSites: _pane = BrowserPane.Sites; break;
            case MenuAction.WebSitesPage: _sitesPage = Math.Max(0, _sitesPage + arg); break;
            case MenuAction.WebSite:
                if (arg >= 0 && arg < _settings.Bookmarks.Sites.Count)
                {
                    _pane = BrowserPane.Bar;
                    Load(_settings.Bookmarks.Sites[arg].Url);
                }
                break;
            case MenuAction.WebStar:
            {
                var bookmarks = _settings.Bookmarks;
                bool saved = bookmarks.ToggleSite(_pageUrl, _pageTitle);
                _settings.Bookmarks = bookmarks;
                Toast.MakeText(this, saved ? T("Site saved to favorites") : T("Site removed from favorites"), ToastLength.Short)!.Show();
                break;
            }
            case MenuAction.WebFullscreen: EnterFullscreen(); break;
            case MenuAction.WebExitFullscreen: ExitFullscreen(); break;
            case MenuAction.WebFormats: _pane = BrowserPane.Formats; break;
            case MenuAction.WebFormat:
                if (arg >= 0 && arg < BrowserUiModel.Formats.Count)
                {
                    _format = BrowserUiModel.Formats[arg];
                    if (Host(_pageUrl) is { Length: > 0 } host) _settings.SetWebFormat(host, _format);
                    _pane = BrowserPane.Bar;
                    FitPageToVideo();
                }
                break;
            case MenuAction.WebBigScreen: _bigScreen = !_bigScreen; PushConfig(); break;
            case MenuAction.WebPlayPause: web.EvaluateJavascript(PlayPauseJs, null); _playing = !_playing; break;
            case MenuAction.WebBack10: web.EvaluateJavascript(SeekJs(-10), null); break;
            case MenuAction.WebFwd10: web.EvaluateJavascript(SeekJs(10), null); break;
            case MenuAction.WebPlayer: OpenInPlayer(); break;
            case MenuAction.WebPhone:
                StartActivity(BrowserActivity.CreateIntent(this, _pageUrl));
                Finish();
                return;
            case MenuAction.Exit: Finish(); return;
            case MenuAction.Back: _pane = BrowserPane.Bar; break;
            case MenuAction.Hide:
                // A click outside the window: closes a list or, in fullscreen, the bar. The bar under the page stays.
                if (_pane != BrowserPane.Bar) _pane = BrowserPane.Bar;
                else if (_fullscreen) _barVisible = false;
                break;
        }
        RefreshWindow();
    }

    private void HandleBack()
    {
        if (_pane != BrowserPane.Bar) _pane = BrowserPane.Bar;
        else if (_fullscreen) ExitFullscreen();
        else if (_web.Web.CanGoBack()) _web.Web.GoBack();
        else { Finish(); return; }
        RefreshWindow();
    }

    /// <summary>Plays the video found on the page in VrView's own player (seeking, bookmarks, format editor).</summary>
    private void OpenInPlayer()
    {
        var media = _media.Playable.FirstOrDefault(m => m.CanPlay);
        if (media is null) return;
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
        _web.Web.EvaluateJavascript("document.querySelectorAll('video,audio').forEach(function(v){try{v.pause()}catch(e){}})", null);
        CookieManager.Instance?.Flush();
        StartActivity(PlayerActivity.CreateIntent(this, [item], 0));
    }

    // ---- VR fullscreen ----

    /// <summary>"⛶ Video in VR": the main video of the page fills the page, which fills the picture.</summary>
    private void EnterFullscreen()
    {
        _web.Web.EvaluateJavascript(EnterJs, new JsResult(result =>
        {
            string s = Unquote(result);
            if (s.Length == 0)
            {
                Toast.MakeText(this, T("No video on this page: start it and try again"), ToastLength.Long)!.Show();
                return;
            }
            var parts = s.Split('x');
            int.TryParse(parts[0], out _videoWidth);
            int.TryParse(parts.Length > 1 ? parts[1] : "0", out _videoHeight);
            StartFullscreen();
        }));
    }

    /// <summary>The page's own fullscreen (its player's ⛶ button): shown in VR instead of on the phone screen.</summary>
    private void OnShowCustomView(View view, WebChromeClient.ICustomViewCallback callback)
    {
        _customCallback?.OnCustomViewHidden();
        _customCallback = callback;
        _web.SetCustomView(view);
        _videoWidth = _videoHeight = 0;
        StartFullscreen();
        // The video's size, once the page has switched.
        _handler.PostDelayed(() => _web.Web.EvaluateJavascript(StateJs, new JsResult(OnVideoState)), 300);
    }

    private void OnHideCustomView()
    {
        _web.SetCustomView(null);
        _customCallback = null;
        LeaveFullscreen();
    }

    private void StartFullscreen()
    {
        _fullscreen = true;
        _barVisible = true;
        _pane = BrowserPane.Bar;
        _lastActionAt = SystemClock.UptimeMillis();
        string host = Host(_pageUrl);
        // The format chosen before for this site, else what the page's title or address says.
        string path = System.Uri.TryCreate(_pageUrl, UriKind.Absolute, out var u) ? u.AbsolutePath : "";
        string name = (_pageTitle + " " + path).Replace('/', ' ').Replace('\\', ' ').Replace('.', ' ');
        _format = _settings.WebFormat(host)
                  ?? (FormatDetector.HasTags(name) ? FormatDetector.Detect(name, _videoWidth, _videoHeight) : VideoFormat.Default);
        // No recenter: the video takes the page's place, also when the user recentered lying down.
        FitPageToVideo();
        RefreshWindow();
    }

    private void ExitFullscreen()
    {
        if (!_fullscreen) return;
        if (_customCallback is { } cb)
        {
            // The page leaves its fullscreen and then calls OnHideCustomView.
            _customCallback = null;
            cb.OnCustomViewHidden();
            _web.SetCustomView(null);
        }
        _web.Web.EvaluateJavascript(ExitJs, null);
        LeaveFullscreen();
    }

    private void LeaveFullscreen()
    {
        if (!_fullscreen) return;
        _fullscreen = false;
        _bigScreen = false;
        _pane = BrowserPane.Bar;
        _web.Resize(PageWidth, PageHeight, PageDpi);
        PushConfig();
        RefreshWindow();
    }

    /// <summary>
    /// Gives the page the shape of the video, so the video fills the whole texture and each eye or the
    /// 180°/360° picture lands exactly where the format expects it.
    /// </summary>
    private void FitPageToVideo()
    {
        if (!_fullscreen) return;
        float aspect = _videoWidth > 0 && _videoHeight > 0 ? (float)_videoWidth / _videoHeight : TypicalAspect(_format);
        int w, h;
        if (aspect >= 1) { w = VideoLongSide; h = (int)(VideoLongSide / aspect); }
        else { h = VideoLongSide; w = (int)(VideoLongSide * aspect); }
        _web.Resize(Math.Max(2, w & ~1), Math.Max(2, h & ~1), VideoDpi);
        PushConfig();
    }

    /// <summary>Usual frame shape of a format, when the page does not tell the video's size (videos from other sites).</summary>
    private static float TypicalAspect(VideoFormat f)
    {
        float eye = f.Projection switch
        {
            Projection.Flat => 16f / 9f,
            Projection.Sphere360 => 2f,
            Projection.Eac or Projection.Cubemap => 1.5f,
            _ => 1f,
        };
        return f.Stereo switch
        {
            StereoMode.SideBySide => eye * 2,
            StereoMode.TopBottom => eye / 2,
            _ => eye,
        };
    }

    private static string Host(string url) => System.Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

    private static string Unquote(string? result)
    {
        if (string.IsNullOrEmpty(result) || result == "null") return "";
        try { return JsonDocument.Parse(result).RootElement.GetString() ?? ""; }
        catch (JsonException) { return ""; }
    }

    // ---- Input ----

    private int Dp(float v) => (int)(v * Resources!.DisplayMetrics!.Density + 0.5f);

    private void OnGlTouch(object? sender, View.TouchEventArgs e)
    {
        var ev = e.Event!;
        e.Handled = true;
        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = _lastX = ev.GetX();
                _downY = _lastY = ev.GetY();
                _downTime = ev.EventTime;
                _dragging = false;
                break;
            case MotionEventActions.Move:
                if (_settings.StereoView || ev.PointerCount > 1) break;
                float dx = ev.GetX() - _lastX, dy = ev.GetY() - _lastY;
                _lastX = ev.GetX();
                _lastY = ev.GetY();
                if (!_dragging && Math.Abs(ev.GetX() - _downX) + Math.Abs(ev.GetY() - _downY) > Dp(12)) _dragging = true;
                if (_dragging)
                {
                    float radPerPx = _settings.Fov * VrMath.Deg2Rad / Math.Max(1, _glView.Height);
                    _renderer.TouchYaw -= dx * radPerPx;
                    _renderer.TouchPitch = Math.Clamp(_renderer.TouchPitch - dy * radPerPx, -1.4f, 1.4f);
                }
                break;
            case MotionEventActions.Up:
                // In the headset a tap is the viewer's button: click what the reticle points at.
                if (!_dragging && ev.EventTime - _downTime < 400) _glView.QueueEvent(() => _renderer.Trigger());
                break;
        }
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is null) return base.DispatchKeyEvent(e);
        var code = e.KeyCode;
        if (code is Keycode.VolumeUp or Keycode.VolumeDown or Keycode.VolumeMute) return base.DispatchKeyEvent(e);
        bool down = e.Action == KeyEventActions.Down && e.RepeatCount == 0;
        switch (code)
        {
            case Keycode.Back:
                if (e.Action == KeyEventActions.Up && !OperatingSystem.IsAndroidVersionAtLeast(33)) HandleBack();
                return true;
            case Keycode.ButtonA or Keycode.DpadCenter or Keycode.Enter or Keycode.NumpadEnter or Keycode.ButtonR1 or Keycode.ButtonR2:
                if (down) _glView.QueueEvent(() => _renderer.Trigger());
                return true;
            case Keycode.ButtonB or Keycode.Escape:
                if (down) HandleBack();
                return true;
            case Keycode.DpadUp or Keycode.PageUp:
                if (e.Action == KeyEventActions.Down) HandleAction(MenuAction.WebScrollUp, 0);
                return true;
            case Keycode.DpadDown or Keycode.PageDown:
                if (e.Action == KeyEventActions.Down) HandleAction(MenuAction.WebScrollDown, 0);
                return true;
            case Keycode.Space or Keycode.MediaPlayPause or Keycode.Headsethook:
                if (down) HandleAction(MenuAction.WebPlayPause, 0);
                return true;
            case Keycode.ButtonX or Keycode.ButtonStart or Keycode.Menu:
                if (down) OnMenuRequested();
                return true;
            case Keycode.ButtonL1 or Keycode.F:
                if (down) HandleAction(_fullscreen ? MenuAction.WebExitFullscreen : MenuAction.WebFullscreen, 0);
                return true;
            case Keycode.ButtonY or Keycode.R:
                if (down) HandleAction(MenuAction.Recenter, 0);
                return true;
            case Keycode.DpadLeft or Keycode.DpadRight when _fullscreen:
                if (e.Action == KeyEventActions.Down)
                    HandleAction(code == Keycode.DpadLeft ? MenuAction.WebBack10 : MenuAction.WebFwd10, 0);
                return true;
        }
        return base.DispatchKeyEvent(e);
    }

    // ---- Web callbacks ----

    private sealed class Client(VrBrowserActivity a) : WebViewClient
    {
        public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
        {
            if (request?.Url?.ToString() is { } url) a.OnRequest(url);
            return null;
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            string? url = request?.Url?.ToString();
            // App links (intent:, market:, …) would leave the headset.
            return url is null || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
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

    private sealed class Chrome(VrBrowserActivity a) : WebChromeClient
    {
        public override void OnProgressChanged(WebView? view, int newProgress)
        {
            a._progress = newProgress;
            a.RefreshWindow();
        }

        public override void OnReceivedTitle(WebView? view, string? title)
        {
            if (!string.IsNullOrWhiteSpace(title)) a._pageTitle = title.Trim();
            a.RefreshWindow();
        }

        public override void OnShowCustomView(View? view, ICustomViewCallback? callback)
        {
            if (view is null || callback is null) return;
            a.OnShowCustomView(view, callback);
        }

        public override void OnHideCustomView() => a.OnHideCustomView();
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
