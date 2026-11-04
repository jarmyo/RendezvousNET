using System.Numerics;
using Android.Graphics;
using Android.Opengl;
using Javax.Microedition.Khronos.Opengles;
using VrView.Core;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;
using Mesh = VrView.Core.Mesh;

namespace VrView.Gl;

/// <summary>
/// Renders the video onto a sphere / dome / fisheye / cubemap / flat screen, split side-by-side for
/// a phone VR viewer, with head tracking, the gaze-controlled windows and optional lens
/// pre-distortion. The UI thread owns which window is open (<see cref="Window"/>); the GL thread
/// places it, finds what the reticle points at and reports clicks through <see cref="IHost"/>.
/// </summary>
internal sealed class VrRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
{
    public interface IHost
    {
        /// <summary>Called on the GL thread when the decoder target is (re)created.</summary>
        void OnVideoSurfaceCreated(SurfaceTexture surfaceTexture);
        /// <summary>Called on the GL thread when an element was clicked by gaze or trigger.</summary>
        void OnMenuAction(MenuAction action, float fraction, int arg);
        /// <summary>Called on the GL thread when the viewer button or a look down asks for the menu.</summary>
        void OnMenuRequested();
        /// <summary>Called on the GL thread when "set horizon with the head" captured a new correction (degrees).</summary>
        void OnHorizonCaptured(float pitch, float roll, float yaw);
        /// <summary>Called on the GL thread when the capture was cancelled or timed out.</summary>
        void OnHorizonCaptureCancelled();
    }

    private const float SphereRadius = 50f;
    private const float FlatDistance = 4f;
    private const float FlatWidth = 4.6f;
    private const float PanelDistance = 2f;
    private const float LookDownOpenAngle = -55f * VrMath.Deg2Rad;
    private const float MenuIdleHideSeconds = 6f;
    private const float GuideWidth = 6f;
    private const float GuideHeight = GuideWidth / 8f;
    private const float HorizonCaptureTimeoutSeconds = 30f;
    /// <summary>Size of the previous/next buttons (about 10° at the panel distance).</summary>
    private const float EdgeSize = 0.36f;
    private const float PreviewWidth = 0.5f;

    private readonly HeadTracker _tracker;
    private readonly IHost _host;

    /// <summary>Current rendering parameters (replaced as a whole by the UI thread).</summary>
    public volatile RenderConfig Config = RenderConfig.Default;
    /// <summary>The open window, or null (replaced as a whole by the UI thread).</summary>
    public volatile UiWindow? Window;
    /// <summary>Bumped by the UI thread when a picture used by the window arrived.</summary>
    public volatile int PicturesVersion;
    /// <summary>Pictures for thumbnails and the raw frame preview.</summary>
    public volatile Func<UiElement, Bitmap?>? PictureSource;
    /// <summary>Picture of the video at a time, for the preview above the seek bar.</summary>
    public volatile Func<long, Bitmap?>? SeekPreviewSource;
    /// <summary>Touch panning offsets in radians (used without a headset).</summary>
    public volatile float TouchYaw, TouchPitch;
    /// <summary>Published "set horizon with the head" state for the UI thread.</summary>
    public volatile bool Capturing;
    /// <summary>Where the reticle is on the seek bar (0..1), or -1.</summary>
    public volatile float SeekHover = -1f;
    /// <summary>Decoded video frames per second, measured.</summary>
    public volatile float VideoFps;
    /// <summary>Duration of the video, for the seek preview time.</summary>
    public volatile int DurationMsForPreview;

    private volatile bool _recenterRequested, _recenterPitchToo;
    private volatile bool _frameAvailable;

    // GL resources
    private int _videoProgram, _videoProgramHq, _texProgram, _reticleProgram, _distortProgram;
    private int _oesTexture, _panelTexture, _guideTexture, _nadirTexture, _prevTexture, _nextTexture, _reticleVbo;
    private int _previewTexture, _crossLeftTexture, _crossRightTexture, _gridTexture, _subtitleTexture;
    private int _fbo, _fboTexture, _fboWidth, _fboHeight;
    private SurfaceTexture? _surfaceTexture;
    private GlMesh? _videoMesh, _videoMeshRight, _panelMesh, _guideMesh, _nadirMesh, _edgeMesh, _screenQuad, _previewMesh,
        _crossMesh, _gridMesh, _subtitleMesh;
    private object? _videoMeshKey;
    private float _nadirMeshDeg;
    private (float W, float H) _panelSize;
    private MenuPainter? _painter;
    private int _width = 1, _height = 1;

    // Per-frame state (GL thread only)
    private readonly float[] _stMatrix = new float[16];
    private readonly float[] _mvp = new float[16];
    private readonly float[] _rotation = new float[9];
    private Matrix4x4 _headView = Matrix4x4.Identity;
    private bool _hasPose, _autoRecenterDone;
    private float _recenterYaw, _recenterPitch;
    private long _lastFrameNanos;
    private int _frameCount;
    private float _fpsSeconds;

    // Window state (GL thread only)
    private UiWindow? _shownWindow, _paintedWindow;
    private float _menuYaw, _menuPitch;
    private int _hovered = -1, _paintedHover = -2, _paintedPictures = -1;
    private float _hoverFraction, _paintedFraction = -1;
    private readonly GazeDwell _dwell = new();
    private float _idleSeconds, _lookDownSeconds;

    // Seek preview
    private Bitmap? _previewBitmap;
    private (float W, float H) _previewSize;

    // Subtitles
    private string? _subtitleText;
    private float _subtitleScale;
    private (float W, float H) _subtitleSize;

    // Previous / next buttons. A longer dwell than the menu, so just looking around does not change the video.
    private readonly GazeDwell _edgeDwell = new() { DwellSeconds = 2f };
    private MenuAction _hoveredEdge;
    private float _edgeRefYaw, _edgeRefPitch;
    private bool _edgeRefSet;

    // "Set horizon with the head"
    private bool _capturing;
    private float _captureSeconds;
    private readonly StillnessDetector _stillness = new();
    /// <summary>Freshly captured horizon, used until the UI thread pushes it back in <see cref="Config"/>.</summary>
    private (float Pitch, float Roll, float Yaw)? _capturedHorizon;
    private float _capturedSeconds;

    public VrRenderer(HeadTracker tracker, IHost host)
    {
        _tracker = tracker;
        _host = host;
        Android.Opengl.Matrix.SetIdentityM(_stMatrix, 0);
    }

    /// <summary>Puts the front of the video where the user is looking (up/down too unless <paramref name="yawOnly"/>).</summary>
    public void RequestRecenter(bool yawOnly = false)
    {
        _recenterPitchToo = !yawOnly;
        _recenterRequested = true;
    }

    /// <summary>
    /// Starts "set horizon with the head": a level guide line follows the head and, once the head is
    /// held still, the current view becomes the new level, straight-ahead view. GL thread.
    /// </summary>
    public void StartHorizonCapture()
    {
        _capturing = true;
        Capturing = true;
        _captureSeconds = 0;
        _stillness.Reset();
    }

    private void CancelHorizonCapture()
    {
        _capturing = false;
        Capturing = false;
        _host.OnHorizonCaptureCancelled();
    }

    /// <summary>Viewer button / screen tap: open the menu, or click what the reticle points at. GL thread.</summary>
    public void Trigger()
    {
        if (_capturing) { CancelHorizonCapture(); return; }
        var w = _shownWindow;
        if (w is null)
        {
            if (_hoveredEdge != MenuAction.None) _host.OnMenuAction(_hoveredEdge, 0f, 0);
            else _host.OnMenuRequested();
            return;
        }
        var hit = HitWindow(w, VrMath.Forward(CurrentView()), Config);
        _host.OnMenuAction(hit.Action == MenuAction.None ? MenuAction.Hide : hit.Action, hit.Fraction, hit.Arg);
    }

    // ---- GLSurfaceView.IRenderer ----

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        _videoProgram = GlUtil.CreateProgram(Shaders.VideoVertex, Shaders.VideoFragment);
        try
        {
            _videoProgramHq = GlUtil.CreateProgram(Shaders.VideoVertex, Shaders.VideoFragmentSupersampled);
        }
        catch (InvalidOperationException ex)
        {
            // GPUs without derivatives for external textures keep the single-sample shader.
            Android.Util.Log.Warn("VrView", "Supersampling unavailable: " + ex.Message);
            _videoProgramHq = _videoProgram;
        }
        _texProgram = GlUtil.CreateProgram(Shaders.TexturedVertex, Shaders.TexturedFragment);
        _reticleProgram = GlUtil.CreateProgram(Shaders.ReticleVertex, Shaders.ReticleFragment);
        _distortProgram = GlUtil.CreateProgram(Shaders.TexturedVertex, Shaders.DistortionFragment);

        var tex = new int[12];
        GLES20.GlGenTextures(12, tex, 0);
        _oesTexture = tex[0];
        _panelTexture = tex[1];
        _guideTexture = tex[2];
        _nadirTexture = tex[3];
        _prevTexture = tex[4];
        _nextTexture = tex[5];
        _previewTexture = tex[6];
        _crossLeftTexture = tex[7];
        _crossRightTexture = tex[8];
        _gridTexture = tex[9];
        _subtitleTexture = tex[10];
        GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, _oesTexture);
        SetLinearClamp(GLES11Ext.GlTextureExternalOes);
        foreach (int t in new[] { _panelTexture, _previewTexture, _subtitleTexture })
        {
            GLES20.GlBindTexture(GLES20.GlTexture2d, t);
            SetLinearClamp(GLES20.GlTexture2d);
        }
        Upload(_guideTexture, MenuPainter.PaintHorizonGuide());
        Upload(_nadirTexture, MenuPainter.PaintNadirPatch());
        Upload(_prevTexture, MenuPainter.PaintEdgeButton(false));
        Upload(_nextTexture, MenuPainter.PaintEdgeButton(true));
        Upload(_crossLeftTexture, MenuPainter.PaintCross(Color.Argb(255, 255, 82, 82)));
        Upload(_crossRightTexture, MenuPainter.PaintCross(Color.Argb(255, 105, 240, 174)));
        Upload(_gridTexture, MenuPainter.PaintGrid());

        _guideMesh = new GlMesh(FlippedQuad(GuideWidth, GuideHeight, PanelDistance));
        _edgeMesh = new GlMesh(FlippedQuad(EdgeSize, EdgeSize, 0));
        _crossMesh = new GlMesh(FlippedQuad(1f, 1f, 0));
        _gridMesh = new GlMesh(FlippedQuad(4.4f, 4.4f, PanelDistance));
        _screenQuad = new GlMesh(MeshBuilder.Quad(2, 2, 0));
        _panelMesh = _previewMesh = _subtitleMesh = null;
        _panelSize = _previewSize = _subtitleSize = default;
        _subtitleText = null;
        _previewBitmap = null;

        var ids = new int[1];
        GLES20.GlGenBuffers(1, ids, 0);
        _reticleVbo = ids[0];
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, _reticleVbo);
        GLES20.GlBufferData(GLES20.GlArrayBuffer, 12, GlUtil.ToBuffer(new[] { 0f, 0f, -PanelDistance }), GLES20.GlStaticDraw);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);

        _videoMesh = _videoMeshRight = null;
        _videoMeshKey = null;
        _nadirMesh = null;
        _fbo = 0;
        _painter ??= new MenuPainter();
        _paintedWindow = null;

        GLES20.GlDisable(GLES20.GlDepthTest);

        // The previous SurfaceTexture (if the GL context was lost) is released by the host after
        // it has moved the decoder to the new one.
        _surfaceTexture = new SurfaceTexture(_oesTexture);
        _surfaceTexture.FrameAvailable += (_, _) => _frameAvailable = true;
        _host.OnVideoSurfaceCreated(_surfaceTexture);
        GlUtil.CheckError("OnSurfaceCreated");
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        long now = Java.Lang.JavaSystem.NanoTime();
        float dt = _lastFrameNanos == 0 ? 0f : Math.Clamp((now - _lastFrameNanos) / 1e9f, 0f, 0.1f);
        _lastFrameNanos = now;

        if (_frameAvailable && _surfaceTexture is not null)
        {
            _frameAvailable = false;
            _frameCount++;
            try
            {
                _surfaceTexture.UpdateTexImage();
                _surfaceTexture.GetTransformMatrix(_stMatrix);
            }
            catch (Java.Lang.IllegalStateException) { /* surface released during shutdown */ }
        }
        _fpsSeconds += dt;
        if (_fpsSeconds >= 1f)
        {
            VideoFps = _frameCount / _fpsSeconds;
            _frameCount = 0;
            _fpsSeconds = 0;
        }

        var cfg = Config;
        var v = cfg.View;
        if (_tracker.TryGetRotation(_rotation))
        {
            _headView = VrMath.HeadView(_rotation, _tracker.DisplayRotation);
            _hasPose = true;
        }
        var view = CurrentView();

        if (_recenterRequested || (!_autoRecenterDone && _hasPose))
        {
            // Put the front of the video where the user is looking. The automatic recenter only
            // turns (yaw); an explicit "Recentrar" also centers up/down, e.g. when lying down.
            var forward = VrMath.Forward(view);
            _recenterYaw = VrMath.YawOf(forward);
            _recenterPitch = _recenterRequested && _recenterPitchToo ? VrMath.PitchOf(forward) : 0f;
            _recenterRequested = false;
            _autoRecenterDone = true;
            _edgeRefSet = false;
        }

        UpdateHorizonCapture(view, cfg, dt);
        UpdateWindow(view, cfg, dt);
        UpdateEdgeButtons(view, cfg, dt);

        GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, 0);
        GLES20.GlViewport(0, 0, _width, _height);
        GLES20.GlClearColor(0f, 0f, 0f, 1f);
        GLES20.GlClear(GLES20.GlColorBufferBit);

        EnsureVideoMesh(cfg);
        var window = _shownWindow;
        if (window is not null) PaintPanelIfNeeded(window, cfg);
        bool previewVisible = window?.Kind == WindowKind.Player && SeekHover >= 0 && UpdateSeekPreview();
        UpdateSubtitle(cfg);

        DrawEyes(cfg, view, window, previewVisible);
    }

    /// <summary>
    /// Draws both eyes. Kept apart from <see cref="OnDrawFrame"/>: one method with this many matrix
    /// locals makes the arm64 AOT compiler fail (stack offsets too large for a SIMD store).
    /// </summary>
    private void DrawEyes(RenderConfig cfg, Matrix4x4 view, UiWindow? window, bool previewVisible)
    {
        var v = cfg.View;
        bool stereo = v.StereoView;
        int eyes = stereo ? 2 : 1;
        int eyeWidth = stereo ? _width / 2 : _width;
        bool distort = stereo && v.LensLevel > 0;
        var (hPitch, hRoll, hYaw) = _capturedHorizon ?? (cfg.Pitch, cfg.Roll, cfg.Yaw);
        var model = VrMath.VideoModel(hPitch, hRoll, hYaw, _recenterYaw, _recenterPitch);
        float scale = v.MenuScale;
        var panelModel = Matrix4x4.CreateScale(scale) * VrMath.PanelModel(_menuYaw, _menuPitch, PanelDistance);
        // Changing a format or calibration value: the window turns see-through so the effect is visible.
        float panelAlpha = v.Opacity * (window?.Kind is WindowKind.Format or WindowKind.Calibration && IsAdjusting() ? 0.55f : 1f);

        for (int eye = 0; eye < eyes; eye++)
        {
            if (distort)
            {
                EnsureFbo(eyeWidth, _height);
                GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, _fbo);
                GLES20.GlViewport(0, 0, eyeWidth, _height);
                GLES20.GlClear(GLES20.GlColorBufferBit);
            }
            else
            {
                GLES20.GlViewport(eye * eyeWidth, 0, eyeWidth, _height);
            }

            DrawEye(cfg, eye, stereo, (float)eyeWidth / _height, view, model, panelModel, window, previewVisible, panelAlpha, scale);

            if (distort)
            {
                GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, 0);
                GLES20.GlViewport(eye * eyeWidth, 0, eyeWidth, _height);
                DrawDistortion(v, (float)eyeWidth / _height);
            }
        }
    }

    private void DrawEye(RenderConfig cfg, int eye, bool stereo, float aspect, Matrix4x4 view, Matrix4x4 model,
        Matrix4x4 panelModel, UiWindow? window, bool previewVisible, float panelAlpha, float scale)
    {
        var v = cfg.View;
        {
            float side = stereo ? (eye == 0 ? -1f : 1f) : 0f;
            var proj = VrMath.Projection(v.Fov, aspect, 0.05f, 200f, side * v.IpdShift);
            // Menu, buttons and reticle can be lined up separately from the video.
            var menuProj = VrMath.Projection(v.Fov, aspect, 0.05f, 200f, side * (v.IpdShift + v.MenuShift));

            var mesh = eye == 1 && _videoMeshRight is not null ? _videoMeshRight : _videoMesh;
            DrawVideo(mesh, model * view * proj, cfg.Format.EyeRect(stereo ? eye : 0), v.HighQuality);
            if (cfg.Format.NadirDeg > 0 && cfg.Format.Projection != Projection.Flat)
                DrawNadir(NadirDown * model * view * proj, cfg.Format.NadirDeg);
            if (cfg.CalibrationStep > 0) DrawCalibration(cfg.CalibrationStep, eye, stereo, view, proj, menuProj);
            if (v.EdgeButtonsOn && !_capturing && cfg.CalibrationStep == 0)
                DrawEdgeButtons(view * menuProj, v);
            if (_subtitleText is not null && _subtitleMesh is not null)
                DrawTextured(_subtitleMesh, _subtitleTexture, SubtitleModel() * menuProj, 1f);
            if (window is not null)
            {
                DrawTextured(_panelMesh!, _panelTexture, panelModel * view * menuProj, panelAlpha);
                if (previewVisible) DrawTextured(_previewMesh!, _previewTexture, PreviewModel(scale) * view * menuProj, 1f);
                DrawReticle(menuProj, v.GazeClick ? _dwell.Progress : 0f, _hovered >= 0);
            }
            else if (_capturing)
            {
                DrawTextured(_guideMesh!, _guideTexture, menuProj, 1f);
                DrawReticle(menuProj, _stillness.Progress, _stillness.Progress > 0);
            }
            else if (_hoveredEdge != MenuAction.None)
            {
                DrawReticle(menuProj, v.GazeClick ? _edgeDwell.Progress : 0f, true);
            }
        }
    }

    // ---- Frame helpers ----

    private Matrix4x4 CurrentView() =>
        Matrix4x4.CreateRotationY(TouchYaw) * _headView * Matrix4x4.CreateRotationX(TouchPitch);

    private (float W, float H) PanelSize(UiWindow w, RenderConfig cfg) =>
        (w.Width * cfg.View.MenuScale, w.Width / w.Aspect * cfg.View.MenuScale);

    private UiHit HitWindow(UiWindow w, Vector3 forward, RenderConfig cfg)
    {
        var (pw, ph) = PanelSize(w, cfg);
        return VrMath.PanelHit(forward, _menuYaw, _menuPitch, PanelDistance, pw, ph) is { } h ? w.HitTest(h.X, h.Y) : UiHit.None;
    }

    private bool IsAdjusting() => _hovered >= 0 && _shownWindow is { } w && _hovered < w.Elements.Count &&
                                  w.Elements[_hovered].Action is MenuAction.ParamSet or MenuAction.ParamMinus or MenuAction.ParamPlus;

    private void UpdateWindow(Matrix4x4 view, RenderConfig cfg, float dt)
    {
        var forward = VrMath.Forward(view);
        var w = Window;
        if (w is not null && _shownWindow is null)
        {
            // Opening: put the window in front of the user. The calibration wizard sits lower so the
            // targets stay in the middle of the view.
            _menuYaw = VrMath.YawOf(forward);
            float pitch = VrMath.PitchOf(forward);
            _menuPitch = w.Kind == WindowKind.Calibration
                ? -22f * VrMath.Deg2Rad
                : Math.Clamp(pitch < LookDownOpenAngle ? -25f * VrMath.Deg2Rad : pitch, -50f * VrMath.Deg2Rad, 50f * VrMath.Deg2Rad);
            _dwell.Reset();
            _idleSeconds = 0;
        }
        if (w is not null && _shownWindow is not null && w.Kind != _shownWindow.Kind)
        {
            _dwell.Reset();
            if (w.Kind == WindowKind.Calibration) _menuPitch = -22f * VrMath.Deg2Rad;
        }
        _shownWindow = w;

        if (w is null)
        {
            _hovered = -1;
            SeekHover = -1;
            if (_capturing) return;
            // Head gesture: looking straight down for a moment opens the menu.
            if (_hasPose && cfg.View.StereoView && cfg.View.LookDownMenu && VrMath.PitchOf(forward) < LookDownOpenAngle)
            {
                _lookDownSeconds += dt;
                if (_lookDownSeconds > 0.6f)
                {
                    _lookDownSeconds = float.NegativeInfinity; // once per look down
                    _host.OnMenuRequested();
                }
            }
            else if (VrMath.PitchOf(forward) >= LookDownOpenAngle) _lookDownSeconds = 0;
            return;
        }
        _lookDownSeconds = 0;

        var hit = HitWindow(w, forward, cfg);
        _hovered = hit.Index;
        _hoverFraction = hit.Fraction;
        SeekHover = hit.Action == MenuAction.Seek ? hit.Fraction : -1f;

        var (pw, ph) = PanelSize(w, cfg);
        bool onPanel = VrMath.PanelHit(forward, _menuYaw, _menuPitch, PanelDistance, pw, ph) is { } p &&
                       p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1;
        // Only the player bar hides by itself: in the other windows people look at the video to see changes.
        _idleSeconds = onPanel ? 0 : _idleSeconds + dt;
        if (w.Kind == WindowKind.Player && _idleSeconds > MenuIdleHideSeconds)
        {
            _idleSeconds = 0;
            _host.OnMenuAction(MenuAction.Hide, 0, 0);
            return;
        }

        _dwell.DwellSeconds = cfg.View.DwellSeconds;
        _dwell.RepeatSeconds = 0.5f;
        if (cfg.View.GazeClick && _dwell.Update(hit.Action, hit.DwellKey, hit.Repeatable, dt, hit.Accelerate))
        {
            _idleSeconds = 0;
            _host.OnMenuAction(hit.Action, hit.Fraction, hit.Arg);
        }
    }

    private void UpdateHorizonCapture(Matrix4x4 view, RenderConfig cfg, float dt)
    {
        if (_capturedHorizon is { } c)
        {
            // Keep the captured values until the UI thread has pushed them back (or give up after 2 s).
            _capturedSeconds += dt;
            bool applied = MathF.Abs(cfg.Pitch - c.Pitch) < 0.05f && MathF.Abs(cfg.Roll - c.Roll) < 0.05f && MathF.Abs(cfg.Yaw - c.Yaw) < 0.05f;
            if (applied || _capturedSeconds > 2f) _capturedHorizon = null;
        }
        if (!_capturing) return;

        _captureSeconds += dt;
        if (_captureSeconds > HorizonCaptureTimeoutSeconds) { CancelHorizonCapture(); return; }
        if (!_stillness.Update(VrMath.Forward(view), VrMath.Up(view), dt)) return;

        var (p, r, y) = VrMath.HorizonFromView(cfg.Pitch, cfg.Roll, cfg.Yaw, _recenterYaw, view, _recenterPitch);
        p = Math.Clamp(p, -90f, 90f);
        r = VideoState.NormalizeAngle(r);
        y = VideoState.NormalizeAngle(y);
        _recenterYaw = VrMath.YawOf(VrMath.Forward(view));
        _recenterPitch = 0f; // the captured correction already includes the vertical part
        _capturedHorizon = (p, r, y);
        _capturedSeconds = 0;
        _capturing = false;
        Capturing = false;
        _host.OnHorizonCaptured(p, r, y);
    }

    // ---- Previous / next buttons ----

    /// <summary>
    /// Where a previous/next button sits. Fixed: to the sides of the video's front. Following the
    /// head: to the sides of where the user looks, carried along only when the head turns past them,
    /// so they can still be looked at.
    /// </summary>
    private (float Yaw, float Pitch) EdgePlacement(bool next, ViewSettings v)
    {
        float angle = v.EdgeButtonsAngle * VrMath.Deg2Rad;
        if (!v.EdgeFollowHead) return (_recenterYaw + (next ? -1f : 1f) * angle, _recenterPitch);
        return (_edgeRefYaw + (next ? -1f : 1f) * 35f * VrMath.Deg2Rad, _edgeRefPitch);
    }

    private void UpdateEdgeButtons(Matrix4x4 view, RenderConfig cfg, float dt)
    {
        var v = cfg.View;
        var forward = VrMath.Forward(view);
        if (v.EdgeFollowHead)
        {
            float yaw = VrMath.YawOf(forward), pitch = VrMath.PitchOf(forward);
            if (!_edgeRefSet) { _edgeRefYaw = yaw; _edgeRefPitch = pitch; _edgeRefSet = true; }
            float limit = 50f * VrMath.Deg2Rad;
            float d = WrapAngle(yaw - _edgeRefYaw);
            if (d > limit) _edgeRefYaw += d - limit;
            else if (d < -limit) _edgeRefYaw += d + limit;
            float dp = pitch - _edgeRefPitch, plimit = 20f * VrMath.Deg2Rad;
            if (dp > plimit) _edgeRefPitch += dp - plimit;
            else if (dp < -plimit) _edgeRefPitch += dp + plimit;
        }

        var hovered = MenuAction.None;
        // The window wins when the reticle is on it.
        if (v.EdgeButtonsOn && !_capturing && cfg.CalibrationStep == 0 && !(_shownWindow is not null && _hovered >= 0))
        {
            for (int i = 0; i < 2; i++)
            {
                bool next = i == 1;
                var (yaw, pitch) = EdgePlacement(next, v);
                if (VrMath.PanelHit(forward, yaw, pitch, PanelDistance, EdgeSize, EdgeSize) is { } h &&
                    h.X is >= 0 and <= 1 && h.Y is >= 0 and <= 1)
                    hovered = next ? MenuAction.Next : MenuAction.Prev;
            }
        }
        _hoveredEdge = hovered;
        // Not repeatable: holding the gaze after a change of video does not skip again.
        if (v.GazeClick && _edgeDwell.Update(hovered, 0, false, dt))
            _host.OnMenuAction(hovered, 0f, 0);
    }

    private void DrawEdgeButtons(Matrix4x4 viewProj, ViewSettings v)
    {
        for (int i = 0; i < 2; i++)
        {
            bool next = i == 1;
            var (yaw, pitch) = EdgePlacement(next, v);
            bool hovered = _hoveredEdge == (next ? MenuAction.Next : MenuAction.Prev);
            DrawTextured(_edgeMesh!, next ? _nextTexture : _prevTexture, VrMath.PanelModel(yaw, pitch, PanelDistance) * viewProj,
                v.Opacity * (hovered ? 1f : 0.6f));
        }
    }

    private static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    // ---- Calibration targets ----

    private void DrawCalibration(int step, int eye, bool stereo, Matrix4x4 view, Matrix4x4 proj, Matrix4x4 menuProj)
    {
        // Each eye sees its own color: when the lenses are lined up, the two crosses merge into one.
        int texture = !stereo || eye == 0 ? _crossLeftTexture : _crossRightTexture;
        float pitch = _menuPitch + 30f * VrMath.Deg2Rad;
        switch (step)
        {
            case 1:
                // Far away, like the video.
                DrawTextured(_crossMesh!, texture, Matrix4x4.CreateScale(6f) * VrMath.PanelModel(_menuYaw, pitch, 40f) * view * proj, 0.9f);
                break;
            case 2:
                // At the distance of the windows.
                DrawTextured(_crossMesh!, texture, Matrix4x4.CreateScale(0.3f) * VrMath.PanelModel(_menuYaw, pitch, PanelDistance) * view * menuProj, 0.9f);
                break;
            default:
                // Head-locked grid: its lines look straight with the right distortion.
                DrawTextured(_gridMesh!, _gridTexture, menuProj, 0.85f);
                break;
        }
    }

    // ---- Seek preview and subtitles ----

    private bool UpdateSeekPreview()
    {
        float f = SeekHover;
        int duration = DurationMsForPreview;
        if (f < 0 || duration <= 0) return false;
        long ms = (long)(MathF.Round(f * 40) / 40 * duration);
        var bmp = SeekPreviewSource?.Invoke(ms);
        if (bmp is null || bmp.IsRecycled) return _previewBitmap is not null && _previewMesh is not null;
        if (!ReferenceEquals(bmp, _previewBitmap))
        {
            _previewBitmap = bmp;
            GLES20.GlBindTexture(GLES20.GlTexture2d, _previewTexture);
            GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bmp, 0);
            float h = PreviewWidth * bmp.Height / Math.Max(1, bmp.Width);
            if (_previewMesh is null || _previewSize != (PreviewWidth, h))
            {
                _previewMesh?.Dispose();
                _previewMesh = new GlMesh(FlippedQuad(PreviewWidth, h, 0));
                _previewSize = (PreviewWidth, h);
            }
        }
        return _previewMesh is not null;
    }

    /// <summary>Above the seek bar, at the gaze position along it.</summary>
    private Matrix4x4 PreviewModel(float scale)
    {
        var w = _shownWindow!;
        var cfg = Config;
        var (pw, ph) = PanelSize(w, cfg);
        var seek = w.Find(MenuAction.Seek);
        float fx = seek is null ? 0.5f : seek.Rect.X0 + seek.Rect.Width * Math.Max(0, SeekHover);
        float x = (fx - 0.5f) * pw;
        float y = ph / 2 + _previewSize.H * scale / 2 + 0.03f;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(x, y, 0) * VrMath.PanelModel(_menuYaw, _menuPitch, PanelDistance - 0.02f);
    }

    private void UpdateSubtitle(RenderConfig cfg)
    {
        string? text = string.IsNullOrWhiteSpace(cfg.Subtitle) ? null : cfg.Subtitle;
        float scale = cfg.View.SubtitleSize / 100f;
        if (text == _subtitleText && scale == _subtitleScale) return;
        _subtitleText = text;
        _subtitleScale = scale;
        if (text is null) return;
        using var bmp = SubtitlePainter.Paint(text);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _subtitleTexture);
        GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bmp, 0);
        float w = 1.6f * scale, h = w * bmp.Height / bmp.Width;
        bmp.Recycle();
        if (_subtitleMesh is null || _subtitleSize != (w, h))
        {
            _subtitleMesh?.Dispose();
            _subtitleMesh = new GlMesh(FlippedQuad(w, h, 0));
            _subtitleSize = (w, h);
        }
    }

    /// <summary>Head-locked, below the center of the view.</summary>
    private Matrix4x4 SubtitleModel() => VrMath.PanelModel(0, -17f * VrMath.Deg2Rad, PanelDistance);

    // ---- Drawing ----

    /// <summary>Turns a cap around the front (-Z) into one under the viewer (-Y).</summary>
    private static readonly Matrix4x4 NadirDown = Matrix4x4.CreateRotationX(-MathF.PI / 2);

    private void DrawNadir(Matrix4x4 mvp, float angleDeg)
    {
        if (_nadirMesh is null || _nadirMeshDeg != angleDeg)
        {
            _nadirMesh?.Dispose();
            // Slightly inside the video sphere so it is drawn over it.
            _nadirMesh = new GlMesh(MeshBuilder.Cap(SphereRadius * 0.95f, angleDeg));
            _nadirMeshDeg = angleDeg;
        }
        DrawTextured(_nadirMesh, _nadirTexture, mvp, 1f);
    }

    private void EnsureVideoMesh(RenderConfig cfg)
    {
        var f = cfg.Format;
        bool dense = cfg.View.DenseMesh;
        float aspect = f.Projection == Projection.Flat ? f.EyeAspect(cfg.VideoWidth, cfg.VideoHeight) : 0f;
        // Only the geometry-relevant part of the format: eye packing and the nadir patch do not change the mesh.
        object key = (f with { Stereo = StereoMode.Mono, SwapEyes = false, NadirDeg = 0 }, aspect, dense, cfg.EmbeddedLeft, cfg.EmbeddedRight);
        if (_videoMesh is not null && key.Equals(_videoMeshKey)) return;
        _videoMesh?.Dispose();
        _videoMeshRight?.Dispose();
        _videoMeshRight = null;
        int k = dense ? 4 : 3; // density factor (in thirds)
        Mesh mesh;
        switch (f.Projection)
        {
            // Dense meshes keep straight lines of the picture straight (16-bit indices allow ~65k vertices).
            case Projection.Sphere360: mesh = MeshBuilder.Equirect(SphereRadius, f.EquirectHFov, 64 * k, 32 * k, f.EquirectVFov); break;
            case Projection.Dome180: mesh = MeshBuilder.Equirect(SphereRadius, f.EquirectHFov, 44 * k, 32 * k, f.EquirectVFov); break;
            case Projection.Fisheye:
                mesh = MeshBuilder.Fisheye(SphereRadius, f.FisheyeFov, 32 * k, 64 * k, lens: f.Lens,
                    centerX: f.CenterX, centerY: f.CenterY, radiusScale: f.RadiusScale);
                break;
            case Projection.Eac or Projection.Cubemap: mesh = CubemapMesh.Build(SphereRadius, f.Projection == Projection.Eac, 12 * k); break;
            case Projection.Mesh when cfg.EmbeddedLeft is not null:
                mesh = SphericalMetadata.Scale(cfg.EmbeddedLeft, SphereRadius);
                if (cfg.EmbeddedRight is not null) _videoMeshRight = new GlMesh(SphericalMetadata.Scale(cfg.EmbeddedRight, SphereRadius));
                break;
            case Projection.Mesh: mesh = MeshBuilder.Equirect(SphereRadius, 180f, 44 * k, 32 * k); break;
            default: mesh = MeshBuilder.Quad(FlatWidth, FlatWidth / Math.Max(aspect, 0.1f), FlatDistance); break;
        }
        _videoMesh = new GlMesh(mesh);
        _videoMeshKey = key;
    }

    private void DrawVideo(GlMesh? mesh, Matrix4x4 mvp, UvRect eyeRect, bool highQuality)
    {
        if (mesh is null) return;
        int program = highQuality ? _videoProgramHq : _videoProgram;
        GLES20.GlUseProgram(program);
        VrMath.ToGl(mvp, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(program, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(program, "uStMatrix"), 1, false, _stMatrix, 0);
        GLES20.GlUniform4f(GLES20.GlGetUniformLocation(program, "uEyeRect"), eyeRect.ScaleU, eyeRect.ScaleV, eyeRect.OffsetU, eyeRect.OffsetV);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, _oesTexture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(program, "uTexture"), 0);
        mesh.Draw(GLES20.GlGetAttribLocation(program, "aPosition"), GLES20.GlGetAttribLocation(program, "aUv"));
    }

    private void PaintPanelIfNeeded(UiWindow w, RenderConfig cfg)
    {
        int pictures = PicturesVersion;
        bool sameHover = _paintedHover == _hovered &&
                         (_hovered < 0 || w.Elements[_hovered].Kind is not (UiKind.Slider or UiKind.Seek) || MathF.Abs(_paintedFraction - _hoverFraction) < 0.01f);
        if (sameHover && pictures == _paintedPictures && Windows.SameContent(w, _paintedWindow) && _panelMesh is not null) return;
        _painter!.Pictures = PictureSource;
        _painter.PreviewFormat = cfg.Format;
        var bmp = _painter.Draw(w, _hovered, _hoverFraction);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _panelTexture);
        GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bmp, 0);
        _paintedWindow = w;
        _paintedHover = _hovered;
        _paintedFraction = _hoverFraction;
        _paintedPictures = pictures;
        var size = (w.Width, w.Width / w.Aspect);
        if (_panelMesh is null || size != _panelSize)
        {
            _panelMesh?.Dispose();
            _panelMesh = new GlMesh(FlippedQuad(size.Item1, size.Item2, 0));
            _panelSize = size;
        }
    }

    private void DrawTextured(GlMesh mesh, int texture, Matrix4x4 mvp, float alpha)
    {
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlUseProgram(_texProgram);
        VrMath.ToGl(mvp, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_texProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_texProgram, "uAlpha"), alpha);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, texture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_texProgram, "uTexture"), 0);
        mesh.Draw(GLES20.GlGetAttribLocation(_texProgram, "aPosition"), GLES20.GlGetAttribLocation(_texProgram, "aUv"));
        GLES20.GlDisable(GLES20.GlBlend);
    }

    private void DrawReticle(Matrix4x4 proj, float progress, bool highlight)
    {
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlUseProgram(_reticleProgram);
        VrMath.ToGl(proj, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_reticleProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_reticleProgram, "uSize"), Math.Max(24f, _height * 0.04f));
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_reticleProgram, "uProgress"), progress);
        GLES20.GlUniform4f(GLES20.GlGetUniformLocation(_reticleProgram, "uColor"),
            highlight ? 0.31f : 1f, highlight ? 0.76f : 1f, highlight ? 0.97f : 1f, 1f);
        int pos = GLES20.GlGetAttribLocation(_reticleProgram, "aPosition");
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, _reticleVbo);
        GLES20.GlEnableVertexAttribArray(pos);
        GLES20.GlVertexAttribPointer(pos, 3, GLES20.GlFloat, false, 12, 0);
        GLES20.GlDrawArrays(GLES20.GlPoints, 0, 1);
        GLES20.GlDisableVertexAttribArray(pos);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
        GLES20.GlDisable(GLES20.GlBlend);
    }

    private void EnsureFbo(int width, int height)
    {
        if (_fbo != 0 && _fboWidth == width && _fboHeight == height) return;
        if (_fbo != 0)
        {
            GLES20.GlDeleteFramebuffers(1, new[] { _fbo }, 0);
            GLES20.GlDeleteTextures(1, new[] { _fboTexture }, 0);
        }
        var ids = new int[1];
        GLES20.GlGenTextures(1, ids, 0);
        _fboTexture = ids[0];
        GLES20.GlBindTexture(GLES20.GlTexture2d, _fboTexture);
        SetLinearClamp(GLES20.GlTexture2d);
        GLES20.GlTexImage2D(GLES20.GlTexture2d, 0, GLES20.GlRgba, width, height, 0, GLES20.GlRgba, GLES20.GlUnsignedByte, null);
        GLES20.GlGenFramebuffers(1, ids, 0);
        _fbo = ids[0];
        GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, _fbo);
        GLES20.GlFramebufferTexture2D(GLES20.GlFramebuffer, GLES20.GlColorAttachment0, GLES20.GlTexture2d, _fboTexture, 0);
        GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, 0);
        _fboWidth = width;
        _fboHeight = height;
        GlUtil.CheckError("EnsureFbo");
    }

    private void DrawDistortion(ViewSettings v, float aspect)
    {
        var (k1, k2) = v.LensCoefficients;
        GLES20.GlUseProgram(_distortProgram);
        VrMath.ToGl(Matrix4x4.Identity, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_distortProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniform2f(GLES20.GlGetUniformLocation(_distortProgram, "uK"), k1, k2);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_distortProgram, "uAspect"), aspect);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_distortProgram, "uScale"), 1f + k1 + k2);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _fboTexture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_distortProgram, "uTexture"), 0);
        _screenQuad!.Draw(GLES20.GlGetAttribLocation(_distortProgram, "aPosition"), GLES20.GlGetAttribLocation(_distortProgram, "aUv"));
    }

    /// <summary>Quad for a bitmap texture: bitmap row 0 is the top, so v is flipped.</summary>
    private static Mesh FlippedQuad(float width, float height, float distance)
    {
        var quad = MeshBuilder.Quad(width, height, distance);
        for (int i = 4; i < quad.Vertices.Length; i += Mesh.FloatsPerVertex) quad.Vertices[i] = 1f - quad.Vertices[i];
        return quad;
    }

    private static void Upload(int texture, Bitmap bitmap)
    {
        GLES20.GlBindTexture(GLES20.GlTexture2d, texture);
        SetLinearClamp(GLES20.GlTexture2d);
        GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bitmap, 0);
        bitmap.Recycle();
    }

    private static void SetLinearClamp(int target)
    {
        GLES20.GlTexParameteri(target, GLES20.GlTextureMinFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(target, GLES20.GlTextureMagFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(target, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
        GLES20.GlTexParameteri(target, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
    }
}

/// <summary>Draws a subtitle line block: white text with a dark outline on a translucent band.</summary>
internal static class SubtitlePainter
{
    public static Bitmap Paint(string text)
    {
        const int width = 1600, lineH = 64;
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(4).ToArray();
        int height = Math.Max(1, lines.Length) * lineH + 24;
        var bmp = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        var c = new Canvas(bmp);
        using var band = new Paint { AntiAlias = true, Color = Color.Argb(140, 0, 0, 0) };
        using var p = new Paint { AntiAlias = true, Color = Color.White, TextAlign = Android.Graphics.Paint.Align.Center, TextSize = 48 };
        float widest = lines.Length == 0 ? 0 : lines.Max(l => p.MeasureText(l));
        float bw = Math.Min(width, widest + 48);
        c.DrawRoundRect(new RectF((width - bw) / 2, 0, (width + bw) / 2, height), 16, 16, band);
        p.SetShadowLayer(4, 0, 0, Color.Black);
        for (int i = 0; i < lines.Length; i++)
            c.DrawText(StripTags(lines[i]), width / 2f, 12 + lineH * (i + 1) - 16, p);
        return bmp;
    }

    // SRT lines may carry <i>, <b>, <font> tags.
    private static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
}
