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
[Activity(Label = "@string/app_name", Exported = false,
    Name = "net.rendezvous.vrbrowser.VrBrowserActivity",
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
    /// <summary>Pitch of the keyboard: lower than the bar, so the page stays in view while typing.</summary>
    private const float KeyboardPitch = -40f;

    // The focused element of the page, also inside shadow roots (frames from other sites cannot be read).
    private const string FieldJs =
        "function vrvField(){var e=document.activeElement;while(e&&e.shadowRoot&&e.shadowRoot.activeElement)" +
        "e=e.shadowRoot.activeElement;return e;}" +
        "function vrvSet(e,v){var d=Object.getOwnPropertyDescriptor(Object.getPrototypeOf(e),'value');" +
        "if(d&&d.set)d.set.call(e,v);else e.value=v;}" +
        "function vrvSel(e){var s=e.value.length,n=s;try{if(typeof e.selectionStart==='number'){s=e.selectionStart;n=e.selectionEnd;}}catch(x){}return [s,n];}";

    // What the keyboard writes into: k = f (a text field), i (a frame of another site) or nothing.
    private const string FocusJs = "(function(){" + FieldJs +
        "var e=vrvField();if(!e||e===document.body||e===document.documentElement)return {k:''};" +
        "if(e.tagName==='IFRAME'||e.tagName==='FRAME')return {k:'i'};var t=(e.type||'').toLowerCase();" +
        "var ed=e.isContentEditable||e.tagName==='TEXTAREA'||(e.tagName==='INPUT'&&" +
        "!/^(button|submit|checkbox|radio|range|color|file|image|reset|hidden)$/.test(t));if(!ed)return {k:''};" +
        "var v=e.isContentEditable?e.innerText:(e.value||''),c=-1;if(!e.isContentEditable)c=vrvSel(e)[0];" +
        "var lab='';try{if(e.labels&&e.labels[0])lab=e.labels[0].innerText;}catch(x){}" +
        "return {k:'f',v:String(v).slice(0,2000),c:c,h:String(e.placeholder||e.getAttribute('aria-label')||lab||e.title||'').slice(0,80)," +
        "p:t==='password'};})()";

    /// <summary>Writes text at the caret of the focused field, as typing would (the page sees input events).</summary>
    private static string InsertJs(string text) => "(function(t){" + FieldJs +
        "var e=vrvField();if(!e||e.tagName==='IFRAME'||e.tagName==='FRAME')return 0;" +
        "try{if(document.execCommand('insertText',false,t))return 1;}catch(x){}" +
        "if(!('value' in e))return 0;var r=vrvSel(e);vrvSet(e,e.value.slice(0,r[0])+t+e.value.slice(r[1]));" +
        "try{e.setSelectionRange(r[0]+t.length,r[0]+t.length);}catch(x){}" +
        "e.dispatchEvent(new InputEvent('input',{bubbles:true,data:t,inputType:'insertText'}));return 1;})(" +
        WebInput.JsString(text) + ")";

    private const string BackspaceJs = "(function(){" + FieldJs +
        "var e=vrvField();if(!e||e.tagName==='IFRAME'||e.tagName==='FRAME')return 0;" +
        "try{if(document.execCommand('delete',false))return 1;}catch(x){}" +
        "if(!('value' in e))return 0;var r=vrvSel(e),s=r[0]===r[1]?Math.max(0,r[0]-1):r[0];" +
        "vrvSet(e,e.value.slice(0,s)+e.value.slice(r[1]));try{e.setSelectionRange(s,s);}catch(x){}" +
        "e.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'deleteContentBackward'}));return 1;})()";

    private static string CaretJs(int step) => "(function(){" + FieldJs +
        "var e=vrvField();if(!e||!('value' in e)||e.isContentEditable)return 0;var c=vrvSel(e)[0]+(" + step + ");" +
        "c=Math.max(0,Math.min(e.value.length,c));try{e.setSelectionRange(c,c);return 1;}catch(x){return 0;}})()";

    // A list of the page (<select>) under the point clicked: Android would open its choices in a window
    // that the viewer does not show, so they are shown in the VR window instead (k = s, o = [text, selected, enabled]).
    private const string OptionsJs =
        "function vrvOpts(e){var lab='';try{if(e.labels&&e.labels[0])lab=e.labels[0].innerText;}catch(x){}" +
        "return {k:'s',m:!!e.multiple,h:String(e.getAttribute('aria-label')||lab||e.title||'').slice(0,80)," +
        "o:[].slice.call(e.options,0,500).map(function(p){return [String(p.label||p.text||'').trim().slice(0,80),p.selected,!p.disabled];})};}";

    private static string PickJs(float u, float v) => "(function(u,v){" + OptionsJs +
        "var w=window.visualViewport,x=w?w.offsetLeft+u*w.width:u*innerWidth,y=w?w.offsetTop+v*w.height:v*innerHeight;" +
        "var e=document.elementFromPoint(x,y);while(e&&e.shadowRoot){var n=e.shadowRoot.elementFromPoint(x,y);if(!n||n===e)break;e=n;}" +
        "if(e&&e.tagName==='OPTION')e=e.parentElement;if(e&&e.tagName==='OPTGROUP')e=e.parentElement;" +
        "if(!e||e.tagName!=='SELECT'||e.disabled)return {k:''};window.__vrvSelect=e;try{e.focus();}catch(x){}return vrvOpts(e);})(" +
        Inv(u) + "," + Inv(v) + ")";

    private static string ChooseJs(int index) => "(function(i){" + OptionsJs +
        "var e=window.__vrvSelect;if(!e||!e.options[i])return {k:''};" +
        "if(e.multiple)e.options[i].selected=!e.options[i].selected;else e.selectedIndex=i;" +
        "e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));" +
        "return vrvOpts(e);})(" + index + ")";

    private static string Inv(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

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
    private List<PageOption> _options = [];
    private bool _optionsMultiple;
    private string _optionsHint = "";
    private int _optionsPage;
    /// <summary>The page's alert or confirm being shown; it waits for an answer.</summary>
    private Android.Webkit.JsResult? _dialog;
    private string _dialogText = "";
    private bool _dialogAsks;

    // VR fullscreen
    private bool _fullscreen, _barVisible = true, _bigScreen, _playing;
    private VideoFormat _format = VideoFormat.Default;
    private int _videoWidth, _videoHeight;
    private WebChromeClient.ICustomViewCallback? _customCallback;
    private long _lastActionAt;

    // VR keyboard
    private KeyboardTarget _kbTarget;
    private KeyboardLayer _kbLayer;
    private bool _kbShift;
    private string _address = "";
    private int _addressCaret;
    private PageField _field = PageField.None;
    // Kind of the focused element at the last look: the keyboard opens when a text field gets the focus.
    private string _lastFieldKind = "";

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
        ViewerLink.SyncSettings(this);
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
        _dialog?.Cancel();
        _dialog = null;
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
        _renderer.AnchoredWindowPitch = WindowPitch();
    }

    private BrowserUiModel Model()
    {
        bool address = _kbTarget == KeyboardTarget.Address;
        return new BrowserUiModel(_pane, _pageTitle, _pageUrl, _web.Web.CanGoBack(), _web.Web.CanGoForward(), _progress,
            _media.Playable.Count(m => m.CanPlay), _settings.Bookmarks.FindSite(_pageUrl) is not null, _fullscreen, _format,
            _bigScreen, _playing, _settings.Bookmarks.Sites, _sitesPage,
            _kbTarget, _kbLayer, _kbShift,
            Typed: address ? _address : _field.Value,
            FieldHint: _pane == BrowserPane.Options ? _optionsHint : address ? "" : _field.Hint,
            Password: !address && _field.Password,
            Caret: address ? _addressCaret : _field.Caret,
            Options: _options, MultipleChoice: _optionsMultiple, OptionsPage: _optionsPage,
            DialogText: _dialogText, DialogAsks: _dialogAsks);
    }

    private void RefreshWindow()
    {
        // Any way out of the message (Back, a click outside, ✕…) is a "Cancel", so the page never waits forever.
        if (_dialog is not null && _pane != BrowserPane.Dialog) AnswerDialog(false);
        bool hidden = _fullscreen && !_barVisible && _pane == BrowserPane.Bar;
        _renderer.AnchoredWindowPitch = WindowPitch();
        _renderer.Window = hidden ? null : Windows.Browser(Model());
    }

    /// <summary>The bar and the keyboard sit under the page; in fullscreen windows open where the user looks.</summary>
    private float WindowPitch() => _fullscreen ? float.NaN : _pane == BrowserPane.Keyboard ? KeyboardPitch : BarPitch;

    private void Tick()
    {
        if (_fullscreen)
        {
            if (_barVisible && _pane == BrowserPane.Bar && SystemClock.UptimeMillis() - _lastActionAt > BarHideMs)
                _barVisible = false;
            _web.Web.EvaluateJavascript(StateJs, new JsResult(OnVideoState));
        }
        else PollField();
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
        if (_pane is BrowserPane.Sites or BrowserPane.Formats or BrowserPane.Options) { _pane = BrowserPane.Bar; RefreshWindow(); return; }
        // Typing an address: a click on the page leaves it. Typing in the page: the click may pick another field.
        if (_pane == BrowserPane.Keyboard && _kbTarget == KeyboardTarget.Address) { _pane = BrowserPane.Bar; RefreshWindow(); }
        if (_fullscreen) { Click(u, v); return; }
        // A list of the page opens its choices here; anything else gets the click.
        _web.Web.EvaluateJavascript(PickJs(u, v), new JsResult(result =>
        {
            if (ShowOptions(result, resetPage: true)) return;
            Click(u, v);
        }));
    }

    private void Click(float u, float v)
    {
        _lastFieldKind = ""; // a click on a text field opens the keyboard again, also after closing it
        _web.Tap(u, v);
        if (!_fullscreen) _handler.PostDelayed(PollField, 250);
    }

    /// <summary>Shows the choices of a list of the page (from <see cref="OptionsJs"/>); false when it is not one.</summary>
    private bool ShowOptions(string? json, bool resetPage)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return false;
        try
        {
            var r = JsonDocument.Parse(json).RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("k", out var k) || k.GetString() != "s") return false;
            var options = new List<PageOption>();
            if (r.TryGetProperty("o", out var o) && o.ValueKind == JsonValueKind.Array)
                foreach (var item in o.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 3)
                        options.Add(new PageOption(item[0].GetString() ?? "", item[1].ValueKind == JsonValueKind.True,
                            item[2].ValueKind == JsonValueKind.True));
            _options = options;
            _optionsMultiple = r.TryGetProperty("m", out var m) && m.ValueKind == JsonValueKind.True;
            _optionsHint = r.TryGetProperty("h", out var h) ? (h.GetString() ?? "").Trim() : "";
            if (resetPage) _optionsPage = BrowserUiModel.PageOfSelected(options);
            _pane = BrowserPane.Options;
            RefreshWindow();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    // ---- Typing ----

    private sealed record PageField(string Kind, string Value, int Caret, string Hint, bool Password)
    {
        public static PageField None { get; } = new("", "", -1, "", false);
        /// <summary>A text field of the page itself (not inside a frame of another site).</summary>
        public bool InPage => Kind == "f";
        public bool Typable => Kind is "f" or "i";
    }

    /// <summary>Looks at the focused element: opens the keyboard on a new text field, closes it when the field is left.</summary>
    private void PollField()
    {
        if (_fullscreen || IsDestroyed) return;
        _web.Web.EvaluateJavascript(FocusJs, new JsResult(result =>
        {
            var field = ParseField(result);
            string kind = field.Kind;
            _field = field;
            if (kind == "f" && _lastFieldKind != "f" && _pane == BrowserPane.Bar)
                OpenKeyboard(KeyboardTarget.Page);
            else if (!field.Typable && _pane == BrowserPane.Keyboard && _kbTarget == KeyboardTarget.Page)
                _pane = BrowserPane.Bar;
            _lastFieldKind = kind;
            RefreshWindow();
        }));
    }

    private static PageField ParseField(string? json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return PageField.None;
        try
        {
            var r = JsonDocument.Parse(json).RootElement;
            if (r.ValueKind != JsonValueKind.Object) return PageField.None;
            string S(string n) => r.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            int caret = r.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
            bool password = r.TryGetProperty("p", out var pw) && pw.ValueKind == JsonValueKind.True;
            return new PageField(S("k"), S("v"), caret, S("h").Trim(), password);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return PageField.None;
        }
    }

    private void OpenKeyboard(KeyboardTarget target)
    {
        _kbTarget = target;
        _kbLayer = KeyboardLayer.Letters;
        _kbShift = false;
        if (target == KeyboardTarget.Address)
        {
            _address = _pageUrl;
            _addressCaret = _address.Length;
        }
        _pane = BrowserPane.Keyboard;
    }

    private void TypeText(string text)
    {
        if (_kbTarget == KeyboardTarget.Address)
        {
            _address = _address.Insert(Math.Clamp(_addressCaret, 0, _address.Length), text);
            _addressCaret = Math.Clamp(_addressCaret, 0, _address.Length) + text.Length;
            return;
        }
        // Script for the page's own fields (any character); real key presses for frames of other sites.
        if (_field.InPage)
            _web.Web.EvaluateJavascript(InsertJs(text), new JsResult(r => { if (r != "1") _web.Type(text); PollField(); }));
        else _web.Type(text);
    }

    private void Backspace()
    {
        if (_kbTarget == KeyboardTarget.Address)
        {
            int at = Math.Clamp(_addressCaret, 0, _address.Length);
            if (at > 0) { _address = _address.Remove(at - 1, 1); _addressCaret = at - 1; }
            return;
        }
        if (_field.InPage)
            _web.Web.EvaluateJavascript(BackspaceJs, new JsResult(r => { if (r != "1") _web.Key(Keycode.Del); PollField(); }));
        else _web.Key(Keycode.Del);
    }

    private void MoveCaret(int step)
    {
        if (_kbTarget == KeyboardTarget.Address)
        {
            _addressCaret = Math.Clamp(_addressCaret + step, 0, _address.Length);
            return;
        }
        if (_field.InPage)
            _web.Web.EvaluateJavascript(CaretJs(step), new JsResult(r =>
            {
                if (r != "1") _web.Key(step < 0 ? Keycode.DpadLeft : Keycode.DpadRight);
                PollField();
            }));
        else _web.Key(step < 0 ? Keycode.DpadLeft : Keycode.DpadRight);
    }

    private void Enter()
    {
        if (_kbTarget == KeyboardTarget.Address)
        {
            string url = WebAddress.FromInput(_address);
            _pane = BrowserPane.Bar;
            if (url.Length > 0) Load(url);
            return;
        }
        // A real Enter: sends the form or adds a line, as the page expects.
        _web.Key(Keycode.Enter);
        _handler.PostDelayed(PollField, 300);
    }

    // ---- Messages of the page ----

    /// <summary>Shows an alert or confirm of the page in the viewer (Android's own dialog would not be seen).</summary>
    private void ShowDialog(string? message, Android.Webkit.JsResult result, bool asks)
    {
        _dialog?.Cancel();
        _dialog = result;
        _dialogText = message ?? "";
        _dialogAsks = asks;
        _pane = BrowserPane.Dialog;
        if (_fullscreen) _barVisible = true;
        RefreshWindow();
    }

    private void AnswerDialog(bool ok)
    {
        var dialog = _dialog;
        _dialog = null;
        if (ok) dialog?.Confirm(); else dialog?.Cancel();
        if (_pane == BrowserPane.Dialog) _pane = BrowserPane.Bar;
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
            case MenuAction.WebAddress: OpenKeyboard(KeyboardTarget.Address); break;
            case MenuAction.WebKeyboard:
                if (_field.Typable) OpenKeyboard(KeyboardTarget.Page);
                else Toast.MakeText(this, T("Nothing to type into: tap a text field of the page first"), ToastLength.Short)!.Show();
                break;
            case MenuAction.WebKey:
                TypeText(BrowserUiModel.KeyText((char)arg, _kbShift));
                _kbShift = false;
                break;
            case MenuAction.WebKeyText: TypeText(".com"); break;
            case MenuAction.WebBackspace: Backspace(); break;
            case MenuAction.WebEnter: Enter(); break;
            case MenuAction.WebShift: _kbShift = !_kbShift; break;
            case MenuAction.WebKeyLayer: _kbLayer = (KeyboardLayer)arg; _kbShift = false; break;
            case MenuAction.WebCaretLeft: MoveCaret(-1); break;
            case MenuAction.WebCaretRight: MoveCaret(1); break;
            case MenuAction.WebDialogOk: AnswerDialog(true); break;
            case MenuAction.WebDialogCancel: AnswerDialog(false); break;
            case MenuAction.WebOptionsPage: _optionsPage = Math.Max(0, _optionsPage + arg); break;
            case MenuAction.WebOption:
                web.EvaluateJavascript(ChooseJs(arg), new JsResult(result =>
                {
                    // One choice closes the list; with several choices it stays open until Done.
                    if (!ShowOptions(result, resetPage: false) || !_optionsMultiple) { _pane = BrowserPane.Bar; RefreshWindow(); }
                }));
                break;
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
        ViewerLink.Play(this, [item], 0);
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
        if (TypeFromKeyboard(e)) return true;
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

    /// <summary>
    /// A physical keyboard types where the VR keyboard writes (and opens it on a text field of the page).
    /// Controller buttons do not type: they still click, go back and so on.
    /// </summary>
    private bool TypeFromKeyboard(KeyEvent e)
    {
        bool typing = _pane == BrowserPane.Keyboard || (!_fullscreen && _field.Typable);
        if (!typing || KeyEvent.IsGamepadButton(e.KeyCode)) return false;
        int unicode = e.GetUnicodeChar(e.MetaState);
        bool special = e.KeyCode is Keycode.Del or Keycode.Enter or Keycode.NumpadEnter or Keycode.DpadLeft or Keycode.DpadRight;
        if (unicode == 0 && !special) return false;
        if (e.Action != KeyEventActions.Down) return true;
        if (_pane != BrowserPane.Keyboard) OpenKeyboard(KeyboardTarget.Page);
        switch (e.KeyCode)
        {
            case Keycode.Del: Backspace(); break;
            case Keycode.Enter or Keycode.NumpadEnter: Enter(); break;
            case Keycode.DpadLeft: MoveCaret(-1); break;
            case Keycode.DpadRight: MoveCaret(1); break;
            default: if (unicode >= ' ') TypeText(char.ConvertFromUtf32(unicode)); break;
        }
        RefreshWindow();
        return true;
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

        public override bool OnJsAlert(WebView? view, string? url, string? message, Android.Webkit.JsResult? result)
        {
            if (result is null) return false;
            a.ShowDialog(message, result, asks: false);
            return true;
        }

        public override bool OnJsConfirm(WebView? view, string? url, string? message, Android.Webkit.JsResult? result)
        {
            if (result is null) return false;
            a.ShowDialog(message, result, asks: true);
            return true;
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
