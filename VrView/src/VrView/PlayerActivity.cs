using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Android.Widget;
using VrView.Core;
using VrView.Gl;
using Uri = Android.Net.Uri;
using Orientation = Android.Widget.Orientation;

namespace VrView;

/// <summary>
/// Full-screen VR player. Plays a playlist with MediaPlayer into a SurfaceTexture rendered by
/// <see cref="VrRenderer"/>, and maps the VR windows, touch screen, gamepad, keyboard and
/// media-button input onto the same set of <see cref="MenuAction"/>s. This file has the lifecycle,
/// playback and input; PlayerActivity.Windows.cs has the windows and what their buttons do.
/// </summary>
[Activity(Label = "VrView", Exported = true,
    Name = "net.rendezvous.vrview.PlayerActivity",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.Navigation | ConfigChanges.UiMode)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "video/*")]
public partial class PlayerActivity : Activity, VrRenderer.IHost
{
    private const string ExtraLocations = "vrview.locations";
    private const string ExtraNames = "vrview.names";
    private const string ExtraSizes = "vrview.sizes";
    private const string ExtraStart = "vrview.start";
    private const int TickMs = 250;

    private enum Source { Vr, Touch, Key }

    private AppSettings _settings = null!;
    private VideoStore _store = null!;
    private VideoFrames _frames = null!;
    private LibraryThumbs _libThumbs = null!;
    private Playlist _playlist = null!;
    private HeadTracker _tracker = null!;
    private VrRenderer _renderer = null!;
    private GLSurfaceView _glView = null!;
    private AudioManager? _audio;
    private MediaSession? _session;
    private readonly Handler _handler = new(Looper.MainLooper!);

    private MediaPlayer? _player;
    private Surface? _surface;
    private SurfaceTexture? _videoTexture;
    private bool _ready, _prepared, _resumeOnStart, _openedOnce;
    private int _videoWidth, _videoHeight, _consecutiveErrors, _ticks;
    private VideoState _state = new();
    // The video _state belongs to. Saves always go here, never to Current: the playlist moves to the
    // next video before the old one's state is saved.
    private MediaItem? _stateItem;
    private SphericalInfo? _spherical;
    private MediaFacts? _facts;
    private int _openGeneration;

    // Touch overlay
    private FrameLayout _root = null!;
    private LinearLayout _overlay = null!;
    private WindowView _windowView = null!;
    private TextView _titleText = null!, _timeText = null!, _durationText = null!;
    private SeekBar _seekBar = null!;
    private SpotMarksView _spotMarks = null!;
    private Button _playButton = null!, _viewButton = null!;
    private bool _seekDragging;
    private long _overlayShownAt;

    // Touch gestures
    private ScaleGestureDetector _scaleDetector = null!;
    private float _downX, _downY, _lastX, _lastY;
    private long _downTime;
    private bool _dragging, _scaling;
    private int _hatX, _hatY;

    public static Intent CreateIntent(Context context, IList<MediaItem> items, int startIndex)
    {
        var intent = new Intent(context, typeof(PlayerActivity));
        intent.PutExtra(ExtraLocations, items.Select(i => i.Location).ToArray());
        intent.PutExtra(ExtraNames, items.Select(i => i.Name).ToArray());
        intent.PutExtra(ExtraSizes, items.Select(i => i.SizeBytes).ToArray());
        intent.PutExtra(ExtraStart, startIndex);
        return intent;
    }

    // ---- Lifecycle ----

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            Window.Attributes!.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
        VolumeControlStream = Android.Media.Stream.Music;

        _settings = new AppSettings(this);
        _store = new VideoStore(this, _settings);
        _frames = new VideoFrames(this);
        _frames.Changed += () => RunOnUiThread(PicturesChanged);
        _libThumbs = new LibraryThumbs(this);
        _libThumbs.Changed += () => RunOnUiThread(PicturesChanged);
        _audio = GetSystemService(AudioService) as AudioManager;
        _playlist = new Playlist(ReadItems(Intent, out int start), start, _settings.Shuffle, _settings.Repeat);
        if (_playlist.Count == 0) { Finish(); return; }

        _tracker = new HeadTracker(this) { Mode = _settings.Tracking };
        _renderer = new VrRenderer(_tracker, this)
        {
            PictureSource = Picture,
            SeekPreviewSource = SeekPreview,
        };
        _tracker.ReferenceChanged += () => _renderer.RequestRecenter(yawOnly: true);
        PushConfig();

        _glView = new GLSurfaceView(this);
        _glView.SetEGLContextClientVersion(2);
        _glView.SetEGLConfigChooser(new MultisampleConfigChooser());
        _glView.PreserveEGLContextOnPause = true;
        _glView.SetRenderer(_renderer);
        _glView.RenderMode = Rendermode.Continuously;
        _glView.Touch += OnGlTouch;
        _scaleDetector = new ScaleGestureDetector(this, new ScaleListener(this));

        SetContentView(BuildLayout());
        SetupMediaSession();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(HandleBack));
        _ready = true;

        if (!_tracker.IsAvailable)
        {
            _settings.StereoView = false;
            PushConfig();
            Toast.MakeText(this, "Este teléfono no tiene giroscopio: arrastra con el dedo para mirar alrededor.", ToastLength.Long)!.Show();
        }
        ShowOverlay(!_settings.StereoView);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        var items = ReadItems(intent, out int start);
        if (items.Count == 0) return;
        SaveCurrentPosition();
        _playlist = new Playlist(items, start, _settings.Shuffle, _settings.Repeat);
        _library = null;
        if (_surface is not null) OpenCurrent();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!_ready) return;
        HideSystemUi();
        _tracker.DisplayRotation = CurrentDisplayRotation();
        _tracker.Start();
        _glView.OnResume();
        if (_resumeOnStart && _prepared) _player?.Start();
        _resumeOnStart = false;
        _handler.RemoveCallbacks(Tick);
        _handler.PostDelayed(Tick, TickMs);
    }

    protected override void OnPause()
    {
        if (!_ready) { base.OnPause(); return; }
        SaveCurrentPosition();
        VideoStore.Flush();
        _resumeOnStart = _prepared && _player?.IsPlaying == true;
        if (_prepared) _player?.Pause();
        _tracker.Stop();
        _glView.OnPause();
        _handler.RemoveCallbacks(Tick);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        ReleasePlayer();
        _session?.Release();
        _surface?.Release();
        _videoTexture?.Release();
        base.OnDestroy();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideSystemUi();
    }

    private static List<MediaItem> ReadItems(Intent? intent, out int start)
    {
        start = 0;
        var items = new List<MediaItem>();
        if (intent is null) return items;
        var locations = intent.GetStringArrayExtra(ExtraLocations);
        var names = intent.GetStringArrayExtra(ExtraNames);
        var sizes = intent.GetLongArrayExtra(ExtraSizes);
        if (locations is not null)
        {
            for (int i = 0; i < locations.Length; i++)
                items.Add(new MediaItem(locations[i], names is not null && i < names.Length ? names[i] : $"vídeo {i + 1}",
                    sizes is not null && i < sizes.Length ? sizes[i] : 0));
            start = intent.GetIntExtra(ExtraStart, 0);
        }
        else if (intent.Data is { } uri && Application.Context.ContentResolver is { } resolver)
        {
            // Opened from a file manager / gallery ("Abrir con VrView").
            items.Add(new MediaItem(uri.ToString()!, FolderScanner.DisplayName(resolver, uri)));
        }
        return items;
    }

    private void HideSystemUi()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1422 // Edge-to-edge is the default from Android 15; harmless before.
            Window!.SetDecorFitsSystemWindows(false);
#pragma warning restore CA1422
            var c = Window.InsetsController;
            if (c is null) return;
            c.Hide(WindowInsets.Type.SystemBars());
            c.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
        else
        {
#pragma warning disable CA1422 // Legacy immersive flags for Android 8-10.
            Window!.DecorView.SystemUiFlags = SystemUiFlags.ImmersiveSticky |
                SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen | SystemUiFlags.LayoutStable |
                SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
#pragma warning restore CA1422
        }
    }

    // ---- Renderer callbacks (GL thread) ----

    public void OnVideoSurfaceCreated(SurfaceTexture surfaceTexture)
    {
        RunOnUiThread(() =>
        {
            if (IsDestroyed) { surfaceTexture.Release(); return; }
            var oldSurface = _surface;
            var oldTexture = _videoTexture;
            _videoTexture = surfaceTexture;
            _surface = new Surface(surfaceTexture);
            if (_player is not null) _player.SetSurface(_surface);
            else OpenCurrent();
            oldSurface?.Release();
            oldTexture?.Release();
        });
    }

    public void OnMenuAction(MenuAction action, float fraction, int arg) =>
        RunOnUiThread(() => HandleAction(action, fraction, Source.Vr, arg));

    public void OnMenuRequested() => RunOnUiThread(() =>
    {
        if (_windows.Count == 0) OpenWindow(WindowKind.Player, Source.Vr);
    });

    public void OnHorizonCaptured(float pitch, float roll, float yaw) => RunOnUiThread(() =>
    {
        _state.Pitch = pitch;
        _state.Roll = roll;
        _state.Yaw = yaw;
        SaveVideoState();
        PushConfig();
        RefreshUi();
        Toast.MakeText(this, "Horizonte fijado", ToastLength.Short)!.Show();
    });

    public void OnHorizonCaptureCancelled() => RunOnUiThread(() =>
        Toast.MakeText(this, "Ajuste de horizonte cancelado", ToastLength.Short)!.Show());

    // ---- Playback ----

    private MediaItem? Current => _playlist.Current;

    private void OpenCurrent()
    {
        SaveCurrentPosition();
        ReleasePlayer();
        var item = Current;
        if (item is null) { Finish(); return; }

        _settings.LastVideoUri = item.Location;
        _settings.LastVideoName = item.Name;
        _state = _store.Get(item);
        _stateItem = item;
        _videoWidth = _videoHeight = 0;
        _spherical = null;
        _facts = null;
        _editScope = null;
        _spotPage = 0;
        _spotDeleteMode = false;
        _subtitle = null;
        _audioTracks = [];
        _subtitleTracks = [];
        _subtitleFiles = [];
        _addedSubtitleFiles.Clear();
        _selectedAudio = _selectedSubtitle = -1;
        RefreshFormat();
        SpotsChanged();
        if (_store.IsReadOnlyFolder(item))
            Toast.MakeText(this, "Para guardar vrview.json en la carpeta, vuelve a elegirla en la lista.", ToastLength.Long)!.Show();
        if (_openedOnce && _settings.AutoRecenter) _renderer.RequestRecenter(yawOnly: true);
        _openedOnce = true;
        _prepared = false;
        ReadFileInfoAsync(item);

        var mp = new MediaPlayer();
        _player = mp;
        mp.SetAudioAttributes(new AudioAttributes.Builder()!
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Movie)!
            .Build()!);
        mp.SetSurface(_surface);
        mp.Prepared += (_, _) => { if (mp == _player) OnPrepared(mp); };
        mp.Completion += (_, _) => { if (mp == _player) OnCompletion(); };
        mp.VideoSizeChanged += (_, e) => { if (mp == _player) OnVideoSize(e.Width, e.Height); };
        mp.TimedText += (_, e) => { if (mp == _player) OnTimedText(e.Text?.Text); };
        mp.Error += (_, e) =>
        {
            e.Handled = true;
            if (mp == _player) OnPlaybackError($"error {e.What}/{e.Extra}");
        };
        try
        {
            mp.SetDataSource(this, Uri.Parse(item.Location)!);
            mp.PrepareAsync();
        }
        catch (Exception ex)
        {
            OnPlaybackError(ex.Message);
        }
        UpdateSessionMetadata(item);
        RefreshUi();
    }

    /// <summary>Spherical metadata (format, embedded mesh), file facts and subtitle files, in the background.</summary>
    private void ReadFileInfoAsync(MediaItem item)
    {
        int generation = ++_openGeneration;
        Task.Run(() =>
        {
            var spherical = MediaInfoReader.ReadSpherical(this, item.Location);
            RunOnUiThread(() =>
            {
                if (generation != _openGeneration) return;
                _spherical = spherical;
                RefreshFormat();
                SpotsChanged();
                RefreshUi();
            });
            var facts = MediaInfoReader.Read(this, item, spherical);
            var subtitles = MediaUtil.TreeOf(item.Location) is { } tree
                ? FolderScanner.FindCompanions(ContentResolver!, Uri.Parse(tree)!, item.Name, ".srt")
                : [];
            RunOnUiThread(() =>
            {
                if (generation != _openGeneration) return;
                _facts = facts;
                _subtitleFiles = subtitles;
                if (_prepared) AddSubtitleFiles();
                RefreshUi();
            });
        });
    }

    private void OnPrepared(MediaPlayer mp)
    {
        _prepared = true;
        _consecutiveErrors = 0;
        OnVideoSize(mp.VideoWidth, mp.VideoHeight);
        long resume = MediaUtil.ResumePosition(_state.PositionMs, mp.Duration);
        if (resume > 0) mp.SeekTo(resume, MediaPlayerSeekMode.Closest);
        AddSubtitleFiles();
        ReadTracks();
        mp.Start();
        RefreshUi();
    }

    private void OnVideoSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == _videoWidth && height == _videoHeight)) return;
        _videoWidth = width;
        _videoHeight = height;
        RefreshFormat();
        SpotsChanged(); // the thumbnail crop depends on the format
        RefreshUi();
    }

    private void OnCompletion()
    {
        if (_stateItem is { } item)
        {
            _state.PositionMs = 0;
            _store.Save(item, _state);
        }
        int before = _playlist.CurrentIndex;
        if (!_playlist.AdvanceOnCompletion())
        {
            // End of the list with repeat off: stay on the last frame with the controls open.
            OpenWindow(WindowKind.Player, _settings.StereoView ? Source.Vr : Source.Touch);
            ShowOverlay(!_settings.StereoView);
            RefreshUi();
            return;
        }
        if (_playlist.CurrentIndex == before && _player is not null && _prepared)
        {
            _player.SeekTo(0);
            _player.Start();
            RefreshUi();
        }
        else OpenCurrent();
    }

    private void OnPlaybackError(string detail)
    {
        _prepared = false;
        _consecutiveErrors++;
        Toast.MakeText(this, $"No se puede reproducir «{Current?.Name}» ({detail})", ToastLength.Long)!.Show();
        if (_playlist.Count > 1 && _consecutiveErrors < _playlist.Count)
        {
            _handler.PostDelayed(() =>
            {
                _playlist.Next();
                OpenCurrent();
            }, 1500);
        }
        else
        {
            ReleasePlayer();
            ShowOverlay(true);
        }
    }

    private void ReleasePlayer()
    {
        var mp = _player;
        _player = null;
        _prepared = false;
        if (mp is null) return;
        try { mp.Reset(); } catch (Java.Lang.IllegalStateException) { }
        mp.Release();
    }

    private void SaveCurrentPosition()
    {
        if (_player is null || !_prepared || _stateItem is not { } item) return;
        try { _state.PositionMs = _player.CurrentPosition; }
        catch (Java.Lang.IllegalStateException) { return; }
        _store.Save(item, _state);
    }

    private void SaveVideoState()
    {
        if (_stateItem is { } item) _store.Save(item, _state, flush: true);
    }

    private long Position => _prepared && _player is not null ? _player.CurrentPosition : 0;
    private long Duration => _prepared && _player is not null ? _player.Duration : 0;
    private bool IsPlaying => _prepared && _player?.IsPlaying == true;

    private void SeekTo(long ms)
    {
        if (!_prepared || _player is null) return;
        long d = Duration;
        ms = Math.Max(0, d > 0 ? Math.Min(ms, d - 500) : ms);
        _player.SeekTo(ms, MediaPlayerSeekMode.Closest);
        _subtitle = null;
        RefreshUi();
    }

    private void TogglePlay()
    {
        if (_player is null) { OpenCurrent(); return; }
        if (!_prepared) return;
        if (_player.IsPlaying) _player.Pause();
        else _player.Start();
        RefreshUi();
    }

    private void PlayItems(IList<MediaItem> items, int index)
    {
        if (items.Count == 0) return;
        SaveCurrentPosition();
        _playlist = new Playlist(items, Math.Clamp(index, 0, items.Count - 1), _settings.Shuffle, _settings.Repeat);
        OpenCurrent();
    }

    // ---- Periodic update ----

    private void Tick()
    {
        _ticks++;
        RefreshUi();
        // Display rotation can flip between landscape and reverse landscape without a config change.
        _tracker.DisplayRotation = CurrentDisplayRotation();
        if (_ticks % (5000 / TickMs) == 0 && IsPlaying) SaveCurrentPosition();
        if (_ticks % (30_000 / TickMs) == 0) VideoStore.Flush();
        if (_overlay.Visibility == ViewStates.Visible && IsPlaying && !_seekDragging &&
            Java.Lang.JavaSystem.CurrentTimeMillis() - _overlayShownAt > 5000)
            ShowOverlay(false);
        _handler.PostDelayed(Tick, TickMs);
    }

    private int CurrentDisplayRotation()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) return (int)(Display?.Rotation ?? SurfaceOrientation.Rotation90);
#pragma warning disable CA1422
        return (int)(WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation90);
#pragma warning restore CA1422
    }

    /// <summary>Physical size of the screen in meters (landscape), for the scanned viewer profile.</summary>
    private (float W, float H) ScreenMeters()
    {
        var m = Resources!.DisplayMetrics!;
        float w = Math.Max(m.WidthPixels, m.HeightPixels), h = Math.Min(m.WidthPixels, m.HeightPixels);
        float xdpi = m.Xdpi > 50 ? m.Xdpi : (int)m.DensityDpi, ydpi = m.Ydpi > 50 ? m.Ydpi : (int)m.DensityDpi;
        return (w / xdpi * 0.0254f, h / ydpi * 0.0254f);
    }

    // ---- Touch overlay ----

    private int Dp(float v) => (int)(v * Resources!.DisplayMetrics!.Density + 0.5f);

    private View BuildLayout()
    {
        _root = new FrameLayout(this);
        _root.SetBackgroundColor(Color.Black);
        _root.AddView(_glView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // Windows on the touch screen: the same ones as in the headset.
        _windowView = new WindowView(this)
        {
            Visibility = ViewStates.Gone,
            Pictures = Picture,
            Clicked = (action, fraction, arg) => HandleAction(action, fraction, Source.Touch, arg),
        };
        _windowView.SetBackgroundColor(Color.Argb(120, 0, 0, 0));
        _root.AddView(_windowView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _overlay = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _overlay.SetBackgroundColor(Color.Argb(190, 0, 0, 0));
        _overlay.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        _overlay.Clickable = true; // swallow touches so they do not reach the GL view

        var top = ButtonRow(_overlay);
        top.AddView(OverlayButton("← Lista", (_, _) => Finish()));
        _viewButton = OverlayButton("", (_, _) => ToggleStereoView());
        top.AddView(_viewButton);
        foreach (var (kind, label) in new[]
                 {
                     (WindowKind.Format, "Formato"), (WindowKind.Spots, "Marcas"), (WindowKind.Horizon, "Horizonte"),
                     (WindowKind.Tracks, "Audio y subt."), (WindowKind.Info, "Info"), (WindowKind.Library, "Biblioteca"),
                     (WindowKind.Settings, "⚙ Ajustes"),
                 })
            top.AddView(OverlayButton(label, (_, _) => OpenWindow(kind, Source.Touch)));
        top.AddView(OverlayButton("✎ Nombrar marca", (_, _) => NameSpot()));
        top.AddView(OverlayButton("✕ Ocultar", (_, _) => ShowOverlay(false)));

        _titleText = new TextView(this) { Ellipsize = Android.Text.TextUtils.TruncateAt.Middle };
        _titleText.SetSingleLine(true);
        _titleText.SetTextColor(Color.White);
        _titleText.SetPadding(Dp(4), Dp(2), Dp(4), Dp(2));
        _overlay.AddView(_titleText);

        var seekRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        seekRow.SetGravity(GravityFlags.CenterVertical);
        _timeText = new TextView(this);
        _timeText.SetTextColor(Color.White);
        _durationText = new TextView(this);
        _durationText.SetTextColor(Color.White);
        _seekBar = new SeekBar(this) { Max = 1000 };
        _seekBar.StartTrackingTouch += (_, _) => _seekDragging = true;
        _seekBar.StopTrackingTouch += (_, _) =>
        {
            _seekDragging = false;
            SeekTo(Duration * _seekBar.Progress / 1000);
        };
        _seekBar.ProgressChanged += (_, e) =>
        {
            if (e.FromUser) _timeText.Text = MediaUtil.FormatTime(Duration * e.Progress / 1000);
            _overlayShownAt = Java.Lang.JavaSystem.CurrentTimeMillis();
        };
        seekRow.AddView(_timeText);
        // The spot ticks are drawn over the seek bar, using its padding to line up with the track.
        var seekFrame = new FrameLayout(this);
        seekFrame.AddView(_seekBar, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.CenterVertical));
        _spotMarks = new SpotMarksView(this, _seekBar);
        seekFrame.AddView(_spotMarks, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        seekRow.AddView(seekFrame, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        seekRow.AddView(_durationText);
        _overlay.AddView(seekRow);

        var controls = ButtonRow(_overlay);
        controls.AddView(OverlayButton("|◀", (_, _) => HandleAction(MenuAction.Prev, 0, Source.Touch)));
        controls.AddView(OverlayButton("◀◀ 10", (_, _) => HandleAction(MenuAction.Back10, 0, Source.Touch)));
        _playButton = OverlayButton("", (_, _) => HandleAction(MenuAction.PlayPause, 0, Source.Touch));
        controls.AddView(_playButton);
        controls.AddView(OverlayButton("10 ▶▶", (_, _) => HandleAction(MenuAction.Fwd10, 0, Source.Touch)));
        controls.AddView(OverlayButton("▶|", (_, _) => HandleAction(MenuAction.Next, 0, Source.Touch)));
        controls.AddView(OverlayButton("◀ Marca", (_, _) => HandleAction(MenuAction.SpotPrev, 0, Source.Touch)));
        controls.AddView(OverlayButton("＋ Marca", (_, _) => HandleAction(MenuAction.SpotAdd, 0, Source.Touch)));
        controls.AddView(OverlayButton("Marca ▶", (_, _) => HandleAction(MenuAction.SpotNext, 0, Source.Touch)));
        controls.AddView(OverlayButton("Recentrar", (_, _) => HandleAction(MenuAction.Recenter, 0, Source.Touch)));
        controls.AddView(OverlayButton("Vol −", (_, _) => HandleAction(MenuAction.VolDown, 0, Source.Touch)));
        controls.AddView(OverlayButton("Vol +", (_, _) => HandleAction(MenuAction.VolUp, 0, Source.Touch)));

        _root.AddView(_overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));

        // Small corner button, out of reach of the viewer's touch lever, to bring up touch controls.
        var corner = new Button(this) { Text = "☰" };
        corner.SetTextColor(Color.Argb(160, 255, 255, 255));
        corner.SetBackgroundColor(Color.Argb(60, 0, 0, 0));
        corner.Click += (_, _) => ShowOverlay(_overlay.Visibility != ViewStates.Visible);
        _root.AddView(corner, new FrameLayout.LayoutParams(Dp(48), Dp(48), GravityFlags.Top | GravityFlags.End));

        ApplyOverlaySize();
        return _root;
    }

    /// <summary>Applies the "Controles: chicos / medianos / grandes" and opacity settings to the touch overlay.</summary>
    private void ApplyOverlaySize()
    {
        if (_overlay is null) return;
        _overlay.Alpha = _settings.ControlsOpacity;
        float textSp = _settings.ControlsSize switch { 0 => 11f, 1 => 13f, _ => 15f };
        _titleText.SetTextSize(Android.Util.ComplexUnitType.Sp, textSp);
        _timeText.SetTextSize(Android.Util.ComplexUnitType.Sp, textSp);
        _durationText.SetTextSize(Android.Util.ComplexUnitType.Sp, textSp);
        StyleButtons(_overlay);

        void StyleButtons(ViewGroup group)
        {
            for (int i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is Button b) StyleOverlayButton(b);
                else if (child is ViewGroup g) StyleButtons(g);
            }
        }
    }

    private void StyleOverlayButton(Button b)
    {
        int size = _settings.ControlsSize;
        float textSp = size switch { 0 => 11f, 1 => 13f, _ => 15f };
        int minHeight = Dp(size switch { 0 => 30, 1 => 38, _ => 48 });
        int padH = Dp(size switch { 0 => 6, 1 => 10, _ => 14 });
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, textSp);
        b.SetMinHeight(minHeight);
        b.SetMinimumHeight(minHeight);
        b.SetMinWidth(Dp(size switch { 0 => 36, 1 => 48, _ => 56 }));
        b.SetMinimumWidth(0);
        b.SetPadding(padH, 0, padH, 0);
    }

    private LinearLayout ButtonRow(ViewGroup parent)
    {
        var scroll = new HorizontalScrollView(this);
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        scroll.AddView(row);
        parent.AddView(scroll);
        return row;
    }

    private Button OverlayButton(string text, EventHandler onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetAllCaps(false);
        StyleOverlayButton(b);
        b.Click += onClick;
        return b;
    }

    private void ShowOverlay(bool show)
    {
        _overlay.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
        if (show) _overlayShownAt = Java.Lang.JavaSystem.CurrentTimeMillis();
    }

    private void UpdateOverlay(PlayerStatus p)
    {
        _titleText.Text = $"{p.Index + 1}/{p.Count}  ·  {p.Title}  ·  {p.FormatText}";
        _timeText.Text = MediaUtil.FormatTime(Position);
        _durationText.Text = MediaUtil.FormatTime(Duration);
        if (!_seekDragging)
        {
            long d = Duration;
            _seekBar.Progress = d > 0 ? (int)(Position * 1000 / d) : 0;
        }
        _playButton.Text = p.Playing ? "II Pausa" : "▶ Reproducir";
        _viewButton.Text = _settings.StereoView ? "Vista: visor VR" : "Vista: pantalla";
        _spotMarks.SetSpots(_spots, Duration);
    }

    // ---- Touch on the video ----

    private void OnGlTouch(object? sender, View.TouchEventArgs e)
    {
        var ev = e.Event!;
        e.Handled = true;
        _scaleDetector.OnTouchEvent(ev);
        bool stereo = _settings.StereoView;
        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = _lastX = ev.GetX();
                _downY = _lastY = ev.GetY();
                _downTime = ev.EventTime;
                _dragging = false;
                break;
            case MotionEventActions.Move:
                if (stereo || _scaling || ev.PointerCount > 1) break;
                float dx = ev.GetX() - _lastX, dy = ev.GetY() - _lastY;
                _lastX = ev.GetX();
                _lastY = ev.GetY();
                if (!_dragging && Math.Abs(ev.GetX() - _downX) + Math.Abs(ev.GetY() - _downY) > Dp(12)) _dragging = true;
                if (_dragging)
                {
                    // Drag the picture with the finger.
                    float radPerPx = _settings.Fov * VrMath.Deg2Rad / Math.Max(1, _glView.Height);
                    _renderer.TouchYaw -= dx * radPerPx;
                    _renderer.TouchPitch = Math.Clamp(_renderer.TouchPitch - dy * radPerPx, -1.4f, 1.4f);
                }
                break;
            case MotionEventActions.Up:
                if (!_dragging && !_scaling && ev.EventTime - _downTime < 400) OnTap();
                _scaling = false;
                break;
        }
    }

    private void OnTap()
    {
        if (_renderer.Capturing)
        {
            _glView.QueueEvent(() => _renderer.Trigger()); // cancels the horizon capture
            return;
        }
        if (_settings.StereoView)
        {
            // In the headset a tap is the viewer's button: open the menu or click the reticle target.
            if (_overlay.Visibility == ViewStates.Visible) ShowOverlay(false);
            _glView.QueueEvent(() => _renderer.Trigger());
        }
        else ShowOverlay(_overlay.Visibility != ViewStates.Visible);
    }

    private sealed class ScaleListener(PlayerActivity owner) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScaleBegin(ScaleGestureDetector? detector)
        {
            owner._scaling = true;
            return !owner._settings.StereoView;
        }

        public override bool OnScale(ScaleGestureDetector? detector)
        {
            if (detector is null || owner._settings.StereoView) return false;
            owner._settings.Fov = Math.Clamp(owner._settings.Fov / detector.ScaleFactor, 40f, 130f);
            owner.PushConfig();
            return true;
        }
    }

    // ---- Keys: gamepad, keyboard, Bluetooth remotes, media buttons ----

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is null) return base.DispatchKeyEvent(e);
        var code = e.KeyCode;
        if (code is Keycode.VolumeUp or Keycode.VolumeDown or Keycode.VolumeMute)
            return base.DispatchKeyEvent(e);

        if (code == Keycode.Back)
        {
            if (e.Action == KeyEventActions.Up) HandleBack();
            return true;
        }
        if (e.Action == KeyEventActions.Down)
            return HandleKey(code) || base.DispatchKeyEvent(e);
        if (e.Action == KeyEventActions.Up && IsMappedKey(code))
            return true;
        return base.DispatchKeyEvent(e);
    }

    private void HandleBack()
    {
        if (_renderer.Capturing) _glView.QueueEvent(() => _renderer.Trigger());
        else if (_windows.Count > 0) HandleAction(MenuAction.Back, 0, Source.Key);
        else if (_overlay.Visibility == ViewStates.Visible) ShowOverlay(false);
        else Finish();
    }

    private static bool IsMappedKey(Keycode code) => code is
        Keycode.ButtonA or Keycode.ButtonB or Keycode.ButtonX or Keycode.ButtonY or Keycode.ButtonL1 or Keycode.ButtonR1 or
        Keycode.ButtonL2 or Keycode.ButtonR2 or Keycode.ButtonStart or Keycode.ButtonSelect or Keycode.DpadCenter or
        Keycode.DpadLeft or Keycode.DpadRight or Keycode.DpadUp or Keycode.DpadDown or Keycode.Enter or
        Keycode.NumpadEnter or Keycode.Space or Keycode.Escape or Keycode.Menu or Keycode.MediaPlayPause or
        Keycode.MediaPlay or Keycode.MediaPause or Keycode.Headsethook or Keycode.MediaNext or Keycode.MediaPrevious or
        Keycode.MediaFastForward or Keycode.MediaRewind or Keycode.PageUp or Keycode.PageDown or Keycode.F or
        Keycode.H or Keycode.R or Keycode.S or Keycode.V or Keycode.M or Keycode.I or Keycode.L or Keycode.K or
        Keycode.ButtonThumbl;

    private bool HandleKey(Keycode code)
    {
        bool menu = _windows.Count > 0;
        bool horizon = menu && _windows[^1] == WindowKind.Horizon;
        MenuAction Act(MenuAction normal, MenuAction inHorizon) => horizon ? inHorizon : normal;

        switch (code)
        {
            case Keycode.ButtonA:
            case Keycode.DpadCenter:
            case Keycode.Enter:
            case Keycode.NumpadEnter:
                if ((menu && !_touchWindows) || _renderer.Capturing) _glView.QueueEvent(() => _renderer.Trigger());
                else HandleAction(MenuAction.PlayPause, 0, Source.Key);
                return true;
            case Keycode.Space:
            case Keycode.MediaPlayPause:
            case Keycode.Headsethook:
                HandleAction(MenuAction.PlayPause, 0, Source.Key);
                return true;
            case Keycode.MediaPlay:
                if (!IsPlaying) HandleAction(MenuAction.PlayPause, 0, Source.Key);
                return true;
            case Keycode.MediaPause:
                if (IsPlaying) HandleAction(MenuAction.PlayPause, 0, Source.Key);
                return true;
            case Keycode.ButtonB:
            case Keycode.Escape:
                if (menu || _renderer.Capturing) HandleBack();
                return true;
            case Keycode.ButtonX:
            case Keycode.ButtonStart:
            case Keycode.Menu:
            case Keycode.M:
                if (menu) HandleAction(MenuAction.Hide, 0, Source.Key);
                else OpenWindow(WindowKind.Player, Source.Key);
                return true;
            case Keycode.ButtonY:
            case Keycode.R:
                HandleAction(MenuAction.Recenter, 0, Source.Key);
                return true;
            case Keycode.DpadLeft:
                HandleAction(Act(MenuAction.Back10, MenuAction.RollLeft), 0, Source.Key);
                return true;
            case Keycode.DpadRight:
                HandleAction(Act(MenuAction.Fwd10, MenuAction.RollRight), 0, Source.Key);
                return true;
            case Keycode.DpadUp:
                HandleAction(Act(MenuAction.VolUp, MenuAction.PitchUp), 0, Source.Key);
                return true;
            case Keycode.DpadDown:
                HandleAction(Act(MenuAction.VolDown, MenuAction.PitchDown), 0, Source.Key);
                return true;
            case Keycode.MediaRewind:
                HandleAction(MenuAction.Back10, 0, Source.Key);
                return true;
            case Keycode.MediaFastForward:
                HandleAction(MenuAction.Fwd10, 0, Source.Key);
                return true;
            case Keycode.ButtonL1:
            case Keycode.MediaPrevious:
            case Keycode.PageUp:
                HandleAction(Act(MenuAction.Prev, MenuAction.YawLeft), 0, Source.Key);
                return true;
            case Keycode.ButtonR1:
            case Keycode.MediaNext:
            case Keycode.PageDown:
                HandleAction(Act(MenuAction.Next, MenuAction.YawRight), 0, Source.Key);
                return true;
            case Keycode.ButtonL2:
                if (_state.Spots.Count > 0) HandleAction(MenuAction.SpotPrev, 0, Source.Key);
                else SeekTo(Position - 60_000);
                return true;
            case Keycode.ButtonR2:
                if (_state.Spots.Count > 0) HandleAction(MenuAction.SpotNext, 0, Source.Key);
                else SeekTo(Position + 60_000);
                return true;
            case Keycode.ButtonThumbl:
            case Keycode.K:
                HandleAction(MenuAction.SpotAdd, 0, Source.Key);
                return true;
            case Keycode.ButtonSelect:
            case Keycode.F:
                OpenWindow(WindowKind.Format, Source.Key);
                return true;
            case Keycode.H:
                OpenWindow(WindowKind.Horizon, Source.Key);
                return true;
            case Keycode.I:
                OpenWindow(WindowKind.Info, Source.Key);
                return true;
            case Keycode.L:
                OpenWindow(WindowKind.Library, Source.Key);
                return true;
            case Keycode.S:
                HandleAction(MenuAction.Shuffle, 0, Source.Key);
                return true;
            case Keycode.V:
                ToggleStereoView();
                return true;
        }
        return false;
    }

    /// <summary>Many gamepads report the D-pad as hat axes instead of key events.</summary>
    public override bool DispatchGenericMotionEvent(MotionEvent? ev)
    {
        if (ev is not null && (ev.Source & InputSourceType.Joystick) == InputSourceType.Joystick &&
            ev.Action == MotionEventActions.Move)
        {
            int hx = Math.Sign(MathF.Round(ev.GetAxisValue(Axis.HatX)));
            int hy = Math.Sign(MathF.Round(ev.GetAxisValue(Axis.HatY)));
            if (hx != _hatX && hx != 0) HandleKey(hx < 0 ? Keycode.DpadLeft : Keycode.DpadRight);
            if (hy != _hatY && hy != 0) HandleKey(hy < 0 ? Keycode.DpadUp : Keycode.DpadDown);
            _hatX = hx;
            _hatY = hy;
            return true;
        }
        return base.DispatchGenericMotionEvent(ev);
    }

    // ---- Media session (Bluetooth headset / remote buttons) ----

    private void SetupMediaSession()
    {
        _session = new MediaSession(this, "VrView");
        _session.SetCallback(new SessionCallback(this));
        _session.Active = true;
    }

    private void UpdateSessionMetadata(MediaItem item)
    {
        _session?.SetMetadata(new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, item.Name)!
            .Build());
    }

    private PlaybackStateCode _lastSessionState = PlaybackStateCode.None;

    private void UpdateSessionState()
    {
        if (_session is null) return;
        var code = IsPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
        if (code == _lastSessionState && _ticks % 8 != 0) return;
        _lastSessionState = code;
        _session.SetPlaybackState(new PlaybackState.Builder()
            .SetActions(PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause |
                        PlaybackState.ActionSkipToNext | PlaybackState.ActionSkipToPrevious |
                        PlaybackState.ActionFastForward | PlaybackState.ActionRewind | PlaybackState.ActionSeekTo)!
            .SetState(code, Position, IsPlaying ? 1f : 0f)!
            .Build());
    }

    private sealed class BackCallback(Action onBack) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => onBack();
    }

    private sealed class SessionCallback(PlayerActivity a) : MediaSession.Callback
    {
        public override void OnPlay() { if (!a.IsPlaying) a.HandleAction(MenuAction.PlayPause, 0, Source.Key); }
        public override void OnPause() { if (a.IsPlaying) a.HandleAction(MenuAction.PlayPause, 0, Source.Key); }
        public override void OnSkipToNext() => a.HandleAction(MenuAction.Next, 0, Source.Key);
        public override void OnSkipToPrevious() => a.HandleAction(MenuAction.Prev, 0, Source.Key);
        public override void OnFastForward() => a.HandleAction(MenuAction.Fwd10, 0, Source.Key);
        public override void OnRewind() => a.HandleAction(MenuAction.Back10, 0, Source.Key);
        public override void OnSeekTo(long pos) => a.SeekTo(pos);
    }
}
