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
using RuntimeHelpers = System.Runtime.CompilerServices.RuntimeHelpers;

namespace VrView;

/// <summary>
/// Full-screen VR player. Plays a playlist with MediaPlayer into a SurfaceTexture rendered by
/// <see cref="VrRenderer"/>, and maps gaze menu, touch overlay, gamepad, keyboard and
/// media-button input onto the same set of <see cref="MenuAction"/>s.
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
public class PlayerActivity : Activity, VrRenderer.IHost
{
    private const string ExtraLocations = "vrview.locations";
    private const string ExtraNames = "vrview.names";
    private const string ExtraStart = "vrview.start";
    private const int TickMs = 250;
    private const float HorizonStep = 0.5f;
    private const float YawStep = 5f;

    private enum Source { Vr, Touch, Key }

    private AppSettings _settings = null!;
    private VideoStore _store = null!;
    private SpotThumbnails _thumbs = null!;
    // Snapshot of _state.Spots for the menu model; replaced only when the spots change.
    private long[] _spots = [];
    private int _spotPage;
    private bool _spotDeleteMode;
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
    private bool _ready;
    private bool _prepared, _resumeOnStart;
    private int _videoWidth, _videoHeight, _consecutiveErrors, _ticks;
    private VideoState _state = new();
    private VideoFormat _detected = VideoFormat.Default;
    /// <summary>Format chosen for the video's folder; only used when the file name has no format tags.</summary>
    private VideoFormat? _folderFormat;

    // Touch overlay
    private LinearLayout _overlay = null!, _subRow = null!;
    private HorizontalScrollView _subRowScroll = null!;
    private TextView _titleText = null!, _timeText = null!, _durationText = null!;
    private SeekBar _seekBar = null!;
    private SpotMarksView _spotMarks = null!;
    private Button _spotsButton = null!;
    private string _subRowKey = "";
    private Button _playButton = null!, _shuffleButton = null!, _repeatButton = null!, _formatButton = null!, _viewButton = null!,
        _sizeButton = null!;
    private MenuPage? _subPage;
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
        _thumbs = new SpotThumbnails(this);
        _thumbs.Changed += () => RunOnUiThread(RefreshUi);
        _audio = GetSystemService(AudioService) as AudioManager;
        _playlist = new Playlist(ReadItems(Intent, out int start), start, _settings.Shuffle, _settings.Repeat);
        if (_playlist.Count == 0) { Finish(); return; }

        _tracker = new HeadTracker(this);
        _renderer = new VrRenderer(_tracker, this) { ThumbnailSource = _thumbs.Get };
        PushConfig();

        _glView = new GLSurfaceView(this);
        _glView.SetEGLContextClientVersion(2);
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

    private List<MediaItem> ReadItems(Intent? intent, out int start)
    {
        start = 0;
        var items = new List<MediaItem>();
        if (intent is null) return items;
        var locations = intent.GetStringArrayExtra(ExtraLocations);
        var names = intent.GetStringArrayExtra(ExtraNames);
        if (locations is not null)
        {
            for (int i = 0; i < locations.Length; i++)
                items.Add(new MediaItem(locations[i], names is not null && i < names.Length ? names[i] : $"vídeo {i + 1}"));
            start = intent.GetIntExtra(ExtraStart, 0);
        }
        else if (intent.Data is { } uri)
        {
            // Opened from a file manager / gallery ("Abrir con VrView").
            items.Add(new MediaItem(uri.ToString()!, FolderScanner.DisplayName(ContentResolver!, uri)));
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

    public void OnMenuAction(MenuAction action, float fraction, int slot) =>
        RunOnUiThread(() => HandleAction(action, fraction, Source.Vr, slot));

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

    private VideoFormat CurrentFormat => _state.FormatOverride ?? _folderFormat ?? _detected;

    private string FormatNote => _state.FormatOverride is not null ? "" : _folderFormat is not null ? "carpeta" : "auto";

    private void OpenCurrent()
    {
        SaveCurrentPosition();
        ReleasePlayer();
        var item = Current;
        if (item is null) { Finish(); return; }

        _settings.LastVideoUri = item.Location;
        _settings.LastVideoName = item.Name;
        _state = _store.Get(item);
        _videoWidth = _videoHeight = 0;
        _detected = FormatDetector.Detect(item.Name);
        _folderFormat = FormatDetector.HasTags(item.Name) ? null : _store.GetFolderFormat(item);
        _spotPage = 0;
        _spotDeleteMode = false;
        SpotsChanged();
        if (_store.IsReadOnlyFolder(item))
            Toast.MakeText(this, "Para guardar vrview.json en la carpeta, vuelve a elegirla en la lista.", ToastLength.Long)!.Show();
        _prepared = false;
        PushConfig();

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

    private void OnPrepared(MediaPlayer mp)
    {
        _prepared = true;
        _consecutiveErrors = 0;
        OnVideoSize(mp.VideoWidth, mp.VideoHeight);
        long resume = MediaUtil.ResumePosition(_state.PositionMs, mp.Duration);
        if (resume > 0) mp.SeekTo(resume, MediaPlayerSeekMode.Closest);
        mp.Start();
        RefreshUi();
    }

    private void OnVideoSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == _videoWidth && height == _videoHeight)) return;
        _videoWidth = width;
        _videoHeight = height;
        _detected = FormatDetector.Detect(Current?.Name ?? "", width, height);
        SpotsChanged(); // the thumbnail crop depends on the format
        PushConfig();
        RefreshUi();
    }

    private void OnCompletion()
    {
        if (Current is { } item)
        {
            _state.PositionMs = 0;
            _store.Save(item, _state);
        }
        int before = _playlist.CurrentIndex;
        if (!_playlist.AdvanceOnCompletion())
        {
            // End of the list with repeat off: stay on the last frame with the menus open.
            RefreshUi();
            _glView.QueueEvent(() => _renderer.ShowMenu(MenuPage.Main));
            ShowOverlay(true);
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
        if (_player is null || !_prepared || Current is not { } item) return;
        try { _state.PositionMs = _player.CurrentPosition; }
        catch (Java.Lang.IllegalStateException) { return; }
        _store.Save(item, _state);
    }

    private void SaveVideoState()
    {
        if (Current is { } item) _store.Save(item, _state, flush: true);
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

    // ---- Actions ----

    private void HandleAction(MenuAction action, float fraction, Source source, int slot = 0)
    {
        _overlayShownAt = Java.Lang.JavaSystem.CurrentTimeMillis();
        switch (action)
        {
            case MenuAction.Seek: SeekTo((long)(fraction * Duration)); break;
            case MenuAction.PlayPause: TogglePlay(); break;
            case MenuAction.Back10: SeekTo(Position - 10_000); break;
            case MenuAction.Fwd10: SeekTo(Position + 10_000); break;
            case MenuAction.Prev:
                if (Position > 5_000) SeekTo(0);
                else { SaveCurrentPosition(); _playlist.Previous(); OpenCurrent(); }
                break;
            case MenuAction.Next:
                SaveCurrentPosition();
                _playlist.Next();
                OpenCurrent();
                break;
            case MenuAction.VolDown:
            case MenuAction.VolUp:
                _audio?.AdjustStreamVolume(Android.Media.Stream.Music,
                    action == MenuAction.VolUp ? Adjust.Raise : Adjust.Lower,
                    source == Source.Touch ? VolumeNotificationFlags.ShowUi : 0);
                break;
            case MenuAction.Shuffle:
                _playlist.Shuffle = !_playlist.Shuffle;
                _settings.Shuffle = _playlist.Shuffle;
                break;
            case MenuAction.Repeat:
                _playlist.Repeat = Playlist.NextRepeatMode(_playlist.Repeat);
                _settings.Repeat = _playlist.Repeat;
                break;
            case MenuAction.Format: CycleFormat(); break;
            case MenuAction.Recenter:
                _renderer.TouchPitch = 0;
                _renderer.RequestRecenter();
                break;
            case MenuAction.Horizon:
            case MenuAction.Settings:
                var page = action == MenuAction.Horizon ? MenuPage.Horizon : MenuPage.Settings;
                if (source == Source.Touch) ShowSubRow(_subPage == page ? null : page);
                else if (source == Source.Key) _glView.QueueEvent(() => _renderer.ShowMenu(page));
                break;
            case MenuAction.Back:
                if (source == Source.Touch) ShowSubRow(null);
                else if (source == Source.Key) _glView.QueueEvent(() => _renderer.ShowMenu(MenuPage.Main));
                break;
            case MenuAction.Exit: Finish(); return;
            case MenuAction.Hide:
                if (source == Source.Touch) ShowOverlay(false);
                break;
            case MenuAction.SetHorizon:
                ShowOverlay(false);
                _glView.QueueEvent(() => _renderer.StartHorizonCapture());
                if (!_settings.StereoView)
                    Toast.MakeText(this, "Inclina el teléfono hasta alinear la línea con el horizonte y quédate quieto", ToastLength.Long)!.Show();
                break;
            case MenuAction.Spots:
                if (source == Source.Touch) ShowSubRow(_subPage == MenuPage.Spots ? null : MenuPage.Spots);
                else if (source == Source.Key) _glView.QueueEvent(() => _renderer.ShowMenu(MenuPage.Spots));
                break;
            case MenuAction.SpotAdd: AddSpot(); break;
            case MenuAction.SpotPrev: JumpToSpot(SpotList.Previous(_state.Spots, Position), "No hay marcas antes"); break;
            case MenuAction.SpotNext: JumpToSpot(SpotList.Next(_state.Spots, Position), "No hay marcas después"); break;
            case MenuAction.SpotPagePrev: _spotPage = Math.Max(0, _spotPage - 1); break;
            case MenuAction.SpotPageNext: _spotPage = Math.Min(BuildMenuModel().SpotPageCount - 1, _spotPage + 1); break;
            case MenuAction.SpotDeleteMode: _spotDeleteMode = !_spotDeleteMode; break;
            case MenuAction.SpotSlot: UseSpot(_spotPage * VrMenuLayout.SpotsPerPage + slot, source); break;
            case MenuAction.Tune:
                if (source == Source.Touch) ShowSubRow(_subPage == MenuPage.Tune ? null : MenuPage.Tune);
                else if (source == Source.Key) _glView.QueueEvent(() => _renderer.ShowMenu(MenuPage.Tune));
                break;
            case MenuAction.TuneHMinus or MenuAction.TuneHPlus or MenuAction.TuneVMinus or MenuAction.TuneVPlus or
                MenuAction.TuneLeft or MenuAction.TuneRight or MenuAction.TuneUp or MenuAction.TuneDown or
                MenuAction.TuneRadiusMinus or MenuAction.TuneRadiusPlus or MenuAction.TuneLens or
                MenuAction.TuneNadir or MenuAction.TuneReset:
                ApplyTuning(action);
                break;
            case MenuAction.Opacity:
                _settings.ControlsOpacity = ControlSizes.NextOpacity(_settings.ControlsOpacity);
                ApplyOverlaySize();
                break;
            case MenuAction.MenuSize:
                _settings.ControlsSize = (_settings.ControlsSize + 1) % ControlSizes.Count;
                ApplyOverlaySize();
                break;
            case MenuAction.PitchUp: AdjustHorizon(HorizonStep, 0, 0); break;
            case MenuAction.PitchDown: AdjustHorizon(-HorizonStep, 0, 0); break;
            case MenuAction.RollLeft: AdjustHorizon(0, HorizonStep, 0); break;
            case MenuAction.RollRight: AdjustHorizon(0, -HorizonStep, 0); break;
            case MenuAction.YawLeft: AdjustHorizon(0, 0, YawStep); break;
            case MenuAction.YawRight: AdjustHorizon(0, 0, -YawStep); break;
            case MenuAction.ResetHorizon:
                _state.Pitch = _state.Roll = _state.Yaw = 0;
                SaveVideoState();
                break;
            case MenuAction.ZoomOut: _settings.Fov = Math.Clamp(_settings.Fov + 5f, 40f, 130f); break;
            case MenuAction.ZoomIn: _settings.Fov = Math.Clamp(_settings.Fov - 5f, 40f, 130f); break;
            case MenuAction.IpdMinus: _settings.IpdShift = Math.Clamp(MathF.Round(_settings.IpdShift - 0.01f, 2), -0.3f, 0.3f); break;
            case MenuAction.IpdPlus: _settings.IpdShift = Math.Clamp(MathF.Round(_settings.IpdShift + 0.01f, 2), -0.3f, 0.3f); break;
            case MenuAction.Lens: _settings.LensLevel = (_settings.LensLevel + 1) % 3; break;
            case MenuAction.Dwell: _settings.GazeClick = !_settings.GazeClick; break;
        }
        PushConfig();
        RefreshUi();
    }

    private void AdjustHorizon(float pitch, float roll, float yaw)
    {
        _state.Pitch = Math.Clamp(_state.Pitch + pitch, -90f, 90f);
        _state.Roll = VideoState.NormalizeAngle(_state.Roll + roll);
        _state.Yaw = VideoState.NormalizeAngle(_state.Yaw + yaw);
        SaveVideoState();
    }

    /// <summary>
    /// "Ajuste fino": the tuned format becomes this video's format and, like a format chosen by hand,
    /// the default for the other untagged videos of its folder.
    /// </summary>
    private void ApplyTuning(MenuAction action)
    {
        if (FineTuning.Apply(CurrentFormat, action) is not { } tuned || Current is not { } item) return;
        // Buttons auto-repeat while looked at, so the folder file is written by the periodic flush.
        _state.FormatOverride = tuned;
        _store.Save(item, _state);
        _folderFormat = tuned;
        _store.SetFolderFormat(item, tuned, flush: false);
    }

    // ---- Spots ("marcas") ----

    private void AddSpot()
    {
        if (!_prepared) return;
        long pos = Position;
        if (!SpotList.Add(_state.Spots, pos))
        {
            Toast.MakeText(this, "Ya hay una marca aquí", ToastLength.Short)!.Show();
            return;
        }
        SaveVideoState();
        SpotsChanged();
        // Show the page that contains the new spot.
        _spotPage = Array.IndexOf(_spots, pos) / VrMenuLayout.SpotsPerPage;
        Toast.MakeText(this, "Marca en " + MediaUtil.FormatTime(pos), ToastLength.Short)!.Show();
    }

    private void JumpToSpot(long? spot, string noneMessage)
    {
        if (spot is { } ms) SeekTo(ms);
        else Toast.MakeText(this, _state.Spots.Count == 0 ? "Aún no hay marcas" : noneMessage, ToastLength.Short)!.Show();
    }

    /// <summary>A spot thumbnail was chosen: jump to it, or delete it in delete mode.</summary>
    private void UseSpot(int index, Source source)
    {
        if (index < 0 || index >= _state.Spots.Count) return;
        long ms = _state.Spots[index];
        if (_spotDeleteMode)
        {
            _state.Spots.RemoveAt(index);
            SaveVideoState();
            SpotsChanged();
            _spotPage = Math.Min(_spotPage, BuildMenuModel().SpotPageCount - 1);
            if (_state.Spots.Count == 0) _spotDeleteMode = false;
            Toast.MakeText(this, "Marca borrada (" + MediaUtil.FormatTime(ms) + ")", ToastLength.Short)!.Show();
            return;
        }
        SeekTo(ms);
        if (source == Source.Vr) _glView.QueueEvent(() => _renderer.HideMenu());
    }

    /// <summary>Refreshes the spot snapshot and asks for any missing thumbnails.</summary>
    private void SpotsChanged()
    {
        _spots = _state.Spots.ToArray();
        if (Current is { } item)
        {
            _thumbs.SetVideo(item.Location, CurrentFormat);
            _thumbs.Request(_spots);
        }
    }

    /// <summary>Auto → each manual format in turn → back to auto.</summary>
    private void CycleFormat()
    {
        var choices = VideoFormat.ManualChoices;
        if (_state.FormatOverride is not { } current)
        {
            int idx = IndexOfFormat(choices, CurrentFormat);
            _state.FormatOverride = choices[(idx + 1) % choices.Count];
        }
        else
        {
            int idx = IndexOfFormat(choices, current);
            _state.FormatOverride = idx == choices.Count - 1 ? null : choices[idx + 1];
        }
        SaveVideoState();

        // Also remember it for the other videos of this folder whose names carry no format tags;
        // going back to "auto" forgets it.
        if (Current is { } item)
        {
            _folderFormat = _state.FormatOverride;
            _store.SetFolderFormat(item, _state.FormatOverride);
        }
        string note = _state.FormatOverride is null ? " (auto)" : " · se usará en esta carpeta";
        Toast.MakeText(this, "Formato: " + CurrentFormat.Label + note, ToastLength.Short)!.Show();
        SpotsChanged();
    }

    private static int IndexOfFormat(IReadOnlyList<VideoFormat> list, VideoFormat f)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].Projection == f.Projection && list[i].Stereo == f.Stereo) return i;
        return -1;
    }

    private void ToggleStereoView()
    {
        _settings.StereoView = !_settings.StereoView;
        _renderer.TouchYaw = 0;
        _renderer.TouchPitch = 0;
        _renderer.RequestRecenter();
        PushConfig();
        RefreshUi();
    }

    private void PushConfig()
    {
        _renderer.Config = new RenderConfig(CurrentFormat, _videoWidth, _videoHeight,
            _state.Pitch, _state.Roll, _state.Yaw, _settings.StereoView,
            _settings.Fov, _settings.IpdShift, _settings.LensLevel, _settings.GazeClick,
            ControlSizes.MenuScale(_settings.ControlsSize), _settings.ControlsOpacity);
    }

    private MenuModel BuildMenuModel()
    {
        int vol = 0;
        if (_audio is not null)
        {
            int max = _audio.GetStreamMaxVolume(Android.Media.Stream.Music);
            vol = max > 0 ? _audio.GetStreamVolume(Android.Media.Stream.Music) * 100 / max : 0;
        }
        // Round the position to whole seconds so the panel is only repainted when the text changes.
        return new MenuModel(Current?.Name ?? "", Position / 1000 * 1000, Duration, IsPlaying, _playlist.Shuffle,
            _playlist.Repeat, vol, CurrentFormat, CurrentFormat.Label, FormatNote, _state.Pitch, _state.Roll,
            _state.Yaw, _settings.Fov, _settings.IpdShift, _settings.LensLevel, _settings.GazeClick,
            Math.Max(0, _playlist.Order.ToList().IndexOf(_playlist.CurrentIndex)), _playlist.Count,
            _settings.ControlsSize, _settings.ControlsOpacity, _spots, _spotPage, _spotDeleteMode, _thumbs.Version);
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
        if (_overlay.Visibility == ViewStates.Visible && IsPlaying && _subPage is null && !_seekDragging &&
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

    private void RefreshUi()
    {
        var model = BuildMenuModel();
        _renderer.Menu = model;
        UpdateSessionState();
        if (_overlay is null) return;
        _titleText.Text = $"{model.Index + 1}/{model.Count}  ·  {model.Title}";
        _timeText.Text = MediaUtil.FormatTime(Position);
        _durationText.Text = MediaUtil.FormatTime(Duration);
        if (!_seekDragging)
        {
            long d = Duration;
            _seekBar.Progress = d > 0 ? (int)(Position * 1000 / d) : 0;
        }
        _playButton.Text = Labels.For(MenuAction.PlayPause, model);
        _shuffleButton.Text = Labels.For(MenuAction.Shuffle, model);
        _repeatButton.Text = Labels.For(MenuAction.Repeat, model);
        _formatButton.Text = "Formato: " + model.FormatText;
        _viewButton.Text = _settings.StereoView ? "Vista: visor VR" : "Vista: pantalla";
        _sizeButton.Text = Labels.For(MenuAction.MenuSize, model);
        _spotsButton.Text = Labels.For(MenuAction.Spots, model);
        _spotMarks.SetSpots(model.Spots, model.DurationMs);
        if (_subPage is { } page) FillSubRow(page, model);
    }

    // ---- Touch overlay ----

    private int Dp(float v) => (int)(v * Resources!.DisplayMetrics!.Density + 0.5f);

    private View BuildLayout()
    {
        var root = new FrameLayout(this);
        root.SetBackgroundColor(Color.Black);
        root.AddView(_glView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _overlay = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _overlay.SetBackgroundColor(Color.Argb(190, 0, 0, 0));
        _overlay.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        _overlay.Clickable = true; // swallow touches so they do not reach the GL view

        var top = ButtonRow(_overlay);
        top.AddView(OverlayButton("← Lista", (_, _) => Finish()));
        _viewButton = OverlayButton("", (_, _) => ToggleStereoView());
        top.AddView(_viewButton);
        _formatButton = OverlayButton("", (_, _) => HandleAction(MenuAction.Format, 0, Source.Touch));
        top.AddView(_formatButton);
        top.AddView(OverlayButton("Horizonte", (_, _) => HandleAction(MenuAction.Horizon, 0, Source.Touch)));
        top.AddView(OverlayButton("Recentrar", (_, _) => HandleAction(MenuAction.Recenter, 0, Source.Touch)));
        _spotsButton = OverlayButton("", (_, _) => HandleAction(MenuAction.Spots, 0, Source.Touch));
        top.AddView(_spotsButton);
        top.AddView(OverlayButton("Ajustes", (_, _) => HandleAction(MenuAction.Settings, 0, Source.Touch)));
        _sizeButton = OverlayButton("", (_, _) => HandleAction(MenuAction.MenuSize, 0, Source.Touch));
        top.AddView(_sizeButton);
        top.AddView(OverlayButton("✕ Ocultar", (_, _) => HandleAction(MenuAction.Hide, 0, Source.Touch)));

        _titleText = new TextView(this) { Ellipsize = Android.Text.TextUtils.TruncateAt.Middle };
        _titleText.SetSingleLine(true);
        _titleText.SetTextColor(Color.White);
        _titleText.SetPadding(Dp(4), Dp(2), Dp(4), Dp(2));
        _overlay.AddView(_titleText);

        _subRowScroll = new HorizontalScrollView(this) { Visibility = ViewStates.Gone };
        _subRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _subRowScroll.AddView(_subRow);
        _overlay.AddView(_subRowScroll);

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
        _shuffleButton = OverlayButton("", (_, _) => HandleAction(MenuAction.Shuffle, 0, Source.Touch));
        controls.AddView(_shuffleButton);
        _repeatButton = OverlayButton("", (_, _) => HandleAction(MenuAction.Repeat, 0, Source.Touch));
        controls.AddView(_repeatButton);
        controls.AddView(OverlayButton("Vol −", (_, _) => HandleAction(MenuAction.VolDown, 0, Source.Touch)));
        controls.AddView(OverlayButton("Vol +", (_, _) => HandleAction(MenuAction.VolUp, 0, Source.Touch)));

        root.AddView(_overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));

        // Small corner button, out of reach of the viewer's touch lever, to bring up touch controls.
        var corner = new Button(this) { Text = "☰" };
        corner.SetTextColor(Color.Argb(160, 255, 255, 255));
        corner.SetBackgroundColor(Color.Argb(60, 0, 0, 0));
        corner.Click += (_, _) => ShowOverlay(_overlay.Visibility != ViewStates.Visible);
        root.AddView(corner, new FrameLayout.LayoutParams(Dp(48), Dp(48), GravityFlags.Top | GravityFlags.End));

        ApplyOverlaySize();
        RefreshUi();
        return root;
    }

    /// <summary>Applies the "Controles: chicos / medianos / grandes" setting to the touch overlay.</summary>
    private void ApplyOverlaySize()
    {
        if (_overlay is null) return;
        _overlay.Alpha = _settings.ControlsOpacity;
        int size = _settings.ControlsSize;
        float textSp = size switch { 0 => 11f, 1 => 13f, _ => 15f };
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
        else ShowSubRow(null);
    }

    private void ShowSubRow(MenuPage? page)
    {
        _subPage = page;
        _subRowScroll.Visibility = page is null ? ViewStates.Gone : ViewStates.Visible;
        if (page is { } p) FillSubRow(p, BuildMenuModel());
    }

    private void FillSubRow(MenuPage page, MenuModel model)
    {
        if (page == MenuPage.Spots) { FillSpotsRow(model); return; }
        var actions = VrMenuLayout.Buttons(page)
            .Select(b => b.Action)
            .Where(a => a is not (MenuAction.Seek or MenuAction.Recenter or MenuAction.Format or MenuAction.Hide))
            .ToList();
        if (_subRowKey != page.ToString())
        {
            _subRowKey = page.ToString();
            _subRow.RemoveAllViews();
            _subRow.Tag = page.ToString();
            var header = new TextView(this);
            header.SetTextColor(Color.Rgb(176, 190, 197));
            header.SetPadding(Dp(4), 0, Dp(8), 0);
            _subRow.AddView(header);
            foreach (var a in actions)
                _subRow.AddView(OverlayButton("", (_, _) => HandleAction(a, 0, Source.Touch)));
        }
        ((TextView)_subRow.GetChildAt(0)!).Text = Labels.Header(page, model).Split(" · ", 2) is { Length: 2 } parts ? parts[1] : "";
        for (int i = 0; i < actions.Count; i++)
        {
            var button = (Button)_subRow.GetChildAt(i + 1)!;
            button.Text = actions[i] == MenuAction.Back ? "Cerrar" : Labels.For(actions[i], model);
            button.Enabled = FineTuning.Applies(actions[i], model.Format.Projection);
        }
    }

    /// <summary>
    /// Touch version of the "Marcas" page: add / previous / next / delete mode, then every spot as a
    /// thumbnail (tap to jump, or to delete in delete mode).
    /// </summary>
    private void FillSpotsRow(MenuModel model)
    {
        string key = $"spots:{RuntimeHelpers.GetHashCode(model.Spots)}:{model.ThumbsVersion}:{model.SpotDeleteMode}:{_settings.ControlsSize}";
        if (key == _subRowKey) return;
        _subRowKey = key;
        _subRow.RemoveAllViews();
        _subRow.Tag = null;
        foreach (var a in new[] { MenuAction.SpotAdd, MenuAction.SpotDeleteMode })
            _subRow.AddView(OverlayButton(Labels.For(a, model), (_, _) => HandleAction(a, 0, Source.Touch)));
        if (model.Spots.Length == 0)
        {
            var hint = new TextView(this) { Text = "Aún no hay marcas: «＋ Marca» guarda el momento actual." };
            hint.SetTextColor(Color.Rgb(176, 190, 197));
            hint.SetPadding(Dp(8), 0, Dp(8), 0);
            _subRow.AddView(hint);
        }

        int thumbHeight = Dp(_settings.ControlsSize switch { 0 => 44, 1 => 56, _ => 72 });
        for (int i = 0; i < model.Spots.Length; i++)
        {
            int index = i;
            long ms = model.Spots[i];
            var cell = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
            cell.SetPadding(Dp(3), Dp(2), Dp(3), Dp(2));
            cell.SetBackgroundColor(model.SpotDeleteMode ? Color.Argb(120, 229, 57, 53) : Color.Transparent);
            var image = new ImageView(this);
            image.SetScaleType(ImageView.ScaleType.CenterCrop!);
            image.SetBackgroundColor(Color.Rgb(30, 38, 46));
            if (_thumbs.Get(ms) is { } bmp) image.SetImageBitmap(bmp);
            cell.AddView(image, new LinearLayout.LayoutParams(thumbHeight * 16 / 9, thumbHeight));
            var label = new TextView(this) { Text = (model.SpotDeleteMode ? "✕ " : "") + MediaUtil.FormatTime(ms), Gravity = GravityFlags.Center };
            label.SetTextColor(Color.White);
            cell.AddView(label);
            cell.Click += (_, _) => UseSpot(index, Source.Touch);
            _subRow.AddView(cell);
        }
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
            // In the headset a tap is the viewer's button: open the gaze menu or click the reticle target.
            if (_overlay.Visibility == ViewStates.Visible) ShowOverlay(false);
            _glView.QueueEvent(() => _renderer.Trigger());
        }
        else ShowOverlay(_overlay.Visibility != ViewStates.Visible);
    }

    private sealed class ScaleListener : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        private readonly PlayerActivity _owner;
        public ScaleListener(PlayerActivity owner) => _owner = owner;

        public override bool OnScaleBegin(ScaleGestureDetector? detector)
        {
            _owner._scaling = true;
            return !_owner._settings.StereoView;
        }

        public override bool OnScale(ScaleGestureDetector? detector)
        {
            if (detector is null || _owner._settings.StereoView) return false;
            _owner._settings.Fov = Math.Clamp(_owner._settings.Fov / detector.ScaleFactor, 40f, 130f);
            _owner.PushConfig();
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
        if (_renderer.Capturing)
            _glView.QueueEvent(() => _renderer.Trigger());
        else if (_renderer.MenuVisible)
        {
            var page = _renderer.Page;
            _glView.QueueEvent(() =>
            {
                if (page != MenuPage.Main) _renderer.ShowMenu(MenuPage.Main);
                else _renderer.HideMenu();
            });
        }
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
        Keycode.H or Keycode.R or Keycode.S or Keycode.V or Keycode.M;

    private bool HandleKey(Keycode code)
    {
        bool menu = _renderer.MenuVisible;
        bool horizon = menu && _renderer.Page == MenuPage.Horizon;
        MenuAction Act(MenuAction normal, MenuAction inHorizon) => horizon ? inHorizon : normal;

        switch (code)
        {
            case Keycode.ButtonA:
            case Keycode.DpadCenter:
            case Keycode.Enter:
            case Keycode.NumpadEnter:
                if (menu || _renderer.Capturing) _glView.QueueEvent(() => _renderer.Trigger());
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
                _glView.QueueEvent(() => _renderer.ToggleMenu());
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
                HandleAction(MenuAction.Format, 0, Source.Key);
                return true;
            case Keycode.H:
                HandleAction(MenuAction.Horizon, 0, Source.Key);
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

    private sealed class BackCallback : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        private readonly Action _onBack;
        public BackCallback(Action onBack) => _onBack = onBack;
        public void OnBackInvoked() => _onBack();
    }

    private sealed class SessionCallback : MediaSession.Callback
    {
        private readonly PlayerActivity _a;
        public SessionCallback(PlayerActivity a) => _a = a;

        public override void OnPlay() { if (!_a.IsPlaying) _a.HandleAction(MenuAction.PlayPause, 0, Source.Key); }
        public override void OnPause() { if (_a.IsPlaying) _a.HandleAction(MenuAction.PlayPause, 0, Source.Key); }
        public override void OnSkipToNext() => _a.HandleAction(MenuAction.Next, 0, Source.Key);
        public override void OnSkipToPrevious() => _a.HandleAction(MenuAction.Prev, 0, Source.Key);
        public override void OnFastForward() => _a.HandleAction(MenuAction.Fwd10, 0, Source.Key);
        public override void OnRewind() => _a.HandleAction(MenuAction.Back10, 0, Source.Key);
        public override void OnSeekTo(long pos) => _a.SeekTo(pos);
    }
}
