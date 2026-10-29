using System.Numerics;
using Android.Graphics;
using Android.Opengl;
using Javax.Microedition.Khronos.Opengles;
using VrView.Core;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;
using Mesh = VrView.Core.Mesh;

namespace VrView.Gl;

/// <summary>
/// Renders the video onto a sphere / dome / fisheye / flat screen, split side-by-side for a
/// phone VR viewer, with head tracking, a gaze-controlled menu and optional lens pre-distortion.
/// All members prefixed "Gl" and the private state run on the GL thread; the public
/// volatile fields and <see cref="RequestRecenter"/> may be touched from any thread, and the
/// menu methods must be called through <c>GLSurfaceView.QueueEvent</c>.
/// </summary>
internal sealed class VrRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
{
    public interface IHost
    {
        /// <summary>Called on the GL thread when the decoder target is (re)created.</summary>
        void OnVideoSurfaceCreated(SurfaceTexture surfaceTexture);
        /// <summary>Called on the GL thread when the user clicks a menu button by gaze or trigger.</summary>
        /// <param name="slot">For spot thumbnails, which one on the current page.</param>
        void OnMenuAction(MenuAction action, float fraction, int slot);
        /// <summary>Called on the GL thread when "set horizon with the head" captured a new correction (degrees).</summary>
        void OnHorizonCaptured(float pitch, float roll, float yaw);
        /// <summary>Called on the GL thread when the capture was cancelled or timed out.</summary>
        void OnHorizonCaptureCancelled();
    }

    private const float SphereRadius = 50f;
    private const float FlatDistance = 4f;
    private const float FlatWidth = 4.6f;
    private const float PanelDistance = 2f;
    private const float PanelWidth = 2.2f;
    private const float PanelHeight = PanelWidth / VrMenuLayout.Aspect;
    private const float LookDownOpenAngle = -55f * VrMath.Deg2Rad;
    private const float MenuIdleHideSeconds = 6f;
    private const float GuideWidth = 6f;
    private const float GuideHeight = GuideWidth / 8f;
    private const float HorizonCaptureTimeoutSeconds = 30f;

    private readonly HeadTracker _tracker;
    private readonly IHost _host;

    /// <summary>Current rendering parameters (replaced as a whole by the UI thread).</summary>
    public volatile RenderConfig Config = RenderConfig.Default;
    /// <summary>Current menu contents (replaced as a whole by the UI thread).</summary>
    public volatile MenuModel Menu = MenuModel.Empty;
    /// <summary>Touch panning offsets in radians (used without a headset).</summary>
    public volatile float TouchYaw, TouchPitch;
    /// <summary>Published menu state for the UI thread (gamepad mapping).</summary>
    public volatile bool MenuVisible;
    public volatile MenuPage Page;
    /// <summary>Published "set horizon with the head" state for the UI thread.</summary>
    public volatile bool Capturing;
    /// <summary>Spot thumbnails for the "Marcas" page (set by the UI thread per video).</summary>
    public volatile Func<long, Android.Graphics.Bitmap?>? ThumbnailSource;

    private volatile bool _recenterRequested;
    private volatile bool _frameAvailable;

    // GL resources
    private int _videoProgram, _texProgram, _reticleProgram, _distortProgram;
    private int _oesTexture, _panelTexture, _guideTexture, _nadirTexture, _reticleVbo;
    private int _fbo, _fboTexture, _fboWidth, _fboHeight;
    private SurfaceTexture? _surfaceTexture;
    private GlMesh? _videoMesh, _panelMesh, _guideMesh, _nadirMesh, _screenQuad;
    private (VideoFormat, float) _videoMeshKey;
    private float _nadirMeshDeg;
    private MenuPainter? _painter;
    private int _width = 1, _height = 1;

    // Per-frame state (GL thread only)
    private readonly float[] _stMatrix = new float[16];
    private readonly float[] _mvp = new float[16];
    private readonly float[] _rotation = new float[9];
    private Matrix4x4 _headView = Matrix4x4.Identity;
    private bool _hasPose, _autoRecenterDone;
    private float _recenterYaw;
    private long _lastFrameNanos;

    // Menu state (GL thread only)
    private bool _menuVisible;
    private MenuPage _page = MenuPage.Main;
    private float _menuYaw, _menuPitch;
    private MenuAction _hovered;
    private readonly GazeDwell _dwell = new();
    private float _idleSeconds, _lookDownSeconds;
    private MenuModel? _paintedModel;
    private MenuPage _paintedPage;
    private MenuAction _paintedHover;
    private int _hoveredSlot, _paintedSlot;
    private bool _panelDirty = true;

    // "Set horizon with the head" (GL thread only)
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

    public void RequestRecenter() => _recenterRequested = true;

    // ---- Menu control (GL thread, via QueueEvent) ----

    public void ShowMenu(MenuPage page)
    {
        if (_capturing) CancelHorizonCapture();
        if (!_menuVisible)
        {
            var f = VrMath.Forward(CurrentView());
            _menuYaw = VrMath.YawOf(f);
            _menuPitch = Math.Clamp(VrMath.PitchOf(f), -50f * VrMath.Deg2Rad, 50f * VrMath.Deg2Rad);
        }
        SetMenu(true, page);
    }

    public void HideMenu() => SetMenu(false, MenuPage.Main);

    /// <summary>
    /// Starts "set horizon with the head": the menu closes, a level guide line follows the head and,
    /// once the head is held still, the current view becomes the new level, straight-ahead view.
    /// </summary>
    public void StartHorizonCapture()
    {
        HideMenu();
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

    public void ToggleMenu()
    {
        if (_menuVisible) HideMenu();
        else ShowMenu(MenuPage.Main);
    }

    /// <summary>Viewer button / screen tap: open the menu, or click what the reticle points at.</summary>
    public void Trigger()
    {
        if (_capturing) { CancelHorizonCapture(); return; }
        if (!_menuVisible) { ShowMenu(MenuPage.Main); return; }
        var f = VrMath.Forward(CurrentView());
        var hit = HitPanel(f, Config);
        var target = hit is { } h ? VrMenuLayout.HitTest(_page, h.X, h.Y) : MenuHit.None;
        if (target.Action == MenuAction.None) HideMenu();
        else Fire(target);
    }

    private void SetMenu(bool visible, MenuPage page)
    {
        _menuVisible = visible;
        _page = page;
        MenuVisible = visible;
        Page = page;
        _idleSeconds = 0;
        _lookDownSeconds = 0;
        _dwell.Reset();
        _panelDirty = true;
    }

    private void Fire(MenuHit target)
    {
        switch (target.Action)
        {
            case MenuAction.Horizon: SetMenu(true, MenuPage.Horizon); break;
            case MenuAction.Spots: SetMenu(true, MenuPage.Spots); break;
            case MenuAction.Tune: SetMenu(true, MenuPage.Tune); break;
            case MenuAction.Settings: SetMenu(true, MenuPage.Settings); break;
            case MenuAction.Back: SetMenu(true, MenuPage.Main); break;
            case MenuAction.Exit:
            case MenuAction.Hide: SetMenu(false, MenuPage.Main); break;
            case MenuAction.SetHorizon: StartHorizonCapture(); break;
        }
        _idleSeconds = 0;
        _host.OnMenuAction(target.Action, target.Fraction, target.Slot);
    }

    // ---- GLSurfaceView.IRenderer ----

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        _videoProgram = GlUtil.CreateProgram(Shaders.VideoVertex, Shaders.VideoFragment);
        _texProgram = GlUtil.CreateProgram(Shaders.TexturedVertex, Shaders.TexturedFragment);
        _reticleProgram = GlUtil.CreateProgram(Shaders.ReticleVertex, Shaders.ReticleFragment);
        _distortProgram = GlUtil.CreateProgram(Shaders.TexturedVertex, Shaders.DistortionFragment);

        var tex = new int[4];
        GLES20.GlGenTextures(4, tex, 0);
        _oesTexture = tex[0];
        _panelTexture = tex[1];
        _guideTexture = tex[2];
        _nadirTexture = tex[3];
        GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, _oesTexture);
        SetLinearClamp(GLES11Ext.GlTextureExternalOes);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _panelTexture);
        SetLinearClamp(GLES20.GlTexture2d);

        // Panel quad: bitmap row 0 is the top, so flip v.
        var quad = MeshBuilder.Quad(PanelWidth, PanelHeight, 0);
        for (int i = 4; i < quad.Vertices.Length; i += Mesh.FloatsPerVertex) quad.Vertices[i] = 1f - quad.Vertices[i];
        _panelMesh = new GlMesh(quad);

        var guide = MeshBuilder.Quad(GuideWidth, GuideHeight, PanelDistance);
        for (int i = 4; i < guide.Vertices.Length; i += Mesh.FloatsPerVertex) guide.Vertices[i] = 1f - guide.Vertices[i];
        _guideMesh = new GlMesh(guide);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _guideTexture);
        SetLinearClamp(GLES20.GlTexture2d);
        using (var guideBitmap = MenuPainter.PaintHorizonGuide())
        {
            GLUtils.TexImage2D(GLES20.GlTexture2d, 0, guideBitmap, 0);
            guideBitmap.Recycle();
        }
        GLES20.GlBindTexture(GLES20.GlTexture2d, _nadirTexture);
        SetLinearClamp(GLES20.GlTexture2d);
        using (var nadirBitmap = MenuPainter.PaintNadirPatch())
        {
            GLUtils.TexImage2D(GLES20.GlTexture2d, 0, nadirBitmap, 0);
            nadirBitmap.Recycle();
        }
        _nadirMesh = null;
        _screenQuad = new GlMesh(MeshBuilder.Quad(2, 2, 0));

        var ids = new int[1];
        GLES20.GlGenBuffers(1, ids, 0);
        _reticleVbo = ids[0];
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, _reticleVbo);
        GLES20.GlBufferData(GLES20.GlArrayBuffer, 12, GlUtil.ToBuffer(new[] { 0f, 0f, -PanelDistance }), GLES20.GlStaticDraw);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);

        _videoMesh = null;
        _fbo = 0;
        _painter ??= new MenuPainter();
        _panelDirty = true;

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
            try
            {
                _surfaceTexture.UpdateTexImage();
                _surfaceTexture.GetTransformMatrix(_stMatrix);
            }
            catch (Java.Lang.IllegalStateException) { /* surface released during shutdown */ }
        }

        var cfg = Config;
        if (_tracker.TryGetRotation(_rotation))
        {
            _headView = VrMath.HeadView(_rotation, _tracker.DisplayRotation);
            _hasPose = true;
        }
        var view = CurrentView();

        if (_recenterRequested || (!_autoRecenterDone && _hasPose))
        {
            // Put the front of the video where the user is looking (yaw only).
            _recenterYaw = VrMath.YawOf(VrMath.Forward(view));
            _recenterRequested = false;
            _autoRecenterDone = true;
        }

        UpdateHorizonCapture(view, cfg, dt);
        UpdateMenu(view, cfg, dt);

        GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, 0);
        GLES20.GlViewport(0, 0, _width, _height);
        GLES20.GlClearColor(0f, 0f, 0f, 1f);
        GLES20.GlClear(GLES20.GlColorBufferBit);

        EnsureVideoMesh(cfg);
        if (_menuVisible) PaintPanelIfNeeded();

        bool stereo = cfg.StereoView;
        int eyes = stereo ? 2 : 1;
        int eyeWidth = stereo ? _width / 2 : _width;
        bool distort = stereo && cfg.LensLevel > 0;
        var (hPitch, hRoll, hYaw) = _capturedHorizon ?? (cfg.Pitch, cfg.Roll, cfg.Yaw);
        var model = VrMath.VideoModel(hPitch, hRoll, hYaw, _recenterYaw);
        var panelModel = Matrix4x4.CreateScale(cfg.MenuScale) * VrMath.PanelModel(_menuYaw, _menuPitch, PanelDistance);

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

            float shift = stereo ? (eye == 0 ? -cfg.IpdShift : cfg.IpdShift) : 0f;
            var proj = VrMath.Projection(cfg.Fov, (float)eyeWidth / _height, 0.05f, 200f, shift);

            DrawVideo(model * view * proj, cfg.Format.EyeRect(stereo ? eye : 0));
            if (cfg.Format.NadirDeg > 0 && cfg.Format.Projection != Projection.Flat)
                DrawNadir(NadirDown * model * view * proj, cfg.Format.NadirDeg);
            if (_menuVisible)
            {
                DrawPanel(panelModel * view * proj);
                DrawReticle(proj, cfg.GazeClick ? _dwell.Progress : 0f, _hovered != MenuAction.None);
            }
            else if (_capturing)
            {
                DrawGuide(proj);
                DrawReticle(proj, _stillness.Progress, _stillness.Progress > 0);
            }

            if (distort)
            {
                GLES20.GlBindFramebuffer(GLES20.GlFramebuffer, 0);
                GLES20.GlViewport(eye * eyeWidth, 0, eyeWidth, _height);
                DrawDistortion(cfg, (float)eyeWidth / _height);
            }
        }
    }

    // ---- Frame helpers ----

    private Matrix4x4 CurrentView() =>
        Matrix4x4.CreateRotationY(TouchYaw) * _headView * Matrix4x4.CreateRotationX(TouchPitch);

    private Vector2? HitPanel(Vector3 forward, RenderConfig cfg) =>
        VrMath.PanelHit(forward, _menuYaw, _menuPitch, PanelDistance, PanelWidth * cfg.MenuScale, PanelHeight * cfg.MenuScale);

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

        var (p, r, y) = VrMath.HorizonFromView(cfg.Pitch, cfg.Roll, cfg.Yaw, _recenterYaw, view);
        p = Math.Clamp(p, -90f, 90f);
        r = VideoState.NormalizeAngle(r);
        y = VideoState.NormalizeAngle(y);
        _recenterYaw = VrMath.YawOf(VrMath.Forward(view));
        _capturedHorizon = (p, r, y);
        _capturedSeconds = 0;
        _capturing = false;
        Capturing = false;
        _host.OnHorizonCaptured(p, r, y);
    }

    private void UpdateMenu(Matrix4x4 view, RenderConfig cfg, float dt)
    {
        var forward = VrMath.Forward(view);
        if (_capturing) return;
        if (!_menuVisible)
        {
            // Head gesture: looking straight down for a moment opens the menu.
            if (_hasPose && cfg.StereoView && VrMath.PitchOf(forward) < LookDownOpenAngle)
            {
                _lookDownSeconds += dt;
                if (_lookDownSeconds > 0.6f)
                {
                    _menuYaw = VrMath.YawOf(forward);
                    _menuPitch = -25f * VrMath.Deg2Rad;
                    SetMenu(true, MenuPage.Main);
                }
            }
            else _lookDownSeconds = 0;
            return;
        }

        var hit = HitPanel(forward, cfg);
        var target = hit is { } h ? VrMenuLayout.HitTest(_page, h.X, h.Y) : MenuHit.None;
        if (target.Action != _hovered || target.Slot != _hoveredSlot)
        {
            _hovered = target.Action;
            _hoveredSlot = target.Slot;
            _panelDirty = true;
        }

        bool onPanel = hit is { } p && p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1;
        _idleSeconds = onPanel ? 0 : _idleSeconds + dt;
        if (_idleSeconds > MenuIdleHideSeconds)
        {
            HideMenu();
            return;
        }

        _dwell.DwellSeconds = 1.4f;
        int key = target.Action == MenuAction.Seek ? (int)(target.Fraction * 40) : target.Slot;
        if (cfg.GazeClick && _dwell.Update(target.Action, key, target.Repeatable, dt))
            Fire(target);
    }

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
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlUseProgram(_texProgram);
        VrMath.ToGl(mvp, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_texProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_texProgram, "uAlpha"), 1f);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _nadirTexture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_texProgram, "uTexture"), 0);
        _nadirMesh.Draw(GLES20.GlGetAttribLocation(_texProgram, "aPosition"), GLES20.GlGetAttribLocation(_texProgram, "aUv"));
        GLES20.GlDisable(GLES20.GlBlend);
    }

    private void EnsureVideoMesh(RenderConfig cfg)
    {
        var f = cfg.Format;
        float aspect = f.Projection == Projection.Flat ? f.EyeAspect(cfg.VideoWidth, cfg.VideoHeight) : 0f;
        // Only the geometry-relevant part of the format: eye packing and the nadir patch do not change the mesh.
        var key = (f with { Stereo = StereoMode.Mono, SwapEyes = false, NadirDeg = 0 }, aspect);
        if (_videoMesh is not null && key == _videoMeshKey) return;
        _videoMesh?.Dispose();
        _videoMesh = new GlMesh(f.Projection switch
        {
            Projection.Sphere360 => MeshBuilder.Equirect(SphereRadius, f.EquirectHFov, latSpanDeg: f.EquirectVFov),
            Projection.Dome180 => MeshBuilder.Equirect(SphereRadius, f.EquirectHFov, 64, 48, f.EquirectVFov),
            Projection.Fisheye => MeshBuilder.Fisheye(SphereRadius, f.FisheyeFov, lens: f.Lens,
                centerX: f.CenterX, centerY: f.CenterY, radiusScale: f.RadiusScale),
            _ => MeshBuilder.Quad(FlatWidth, FlatWidth / Math.Max(aspect, 0.1f), FlatDistance),
        });
        _videoMeshKey = key;
    }

    private void DrawVideo(Matrix4x4 mvp, UvRect eyeRect)
    {
        if (_videoMesh is null) return;
        GLES20.GlUseProgram(_videoProgram);
        VrMath.ToGl(mvp, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_videoProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_videoProgram, "uStMatrix"), 1, false, _stMatrix, 0);
        GLES20.GlUniform4f(GLES20.GlGetUniformLocation(_videoProgram, "uEyeRect"), eyeRect.ScaleU, eyeRect.ScaleV, eyeRect.OffsetU, eyeRect.OffsetV);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, _oesTexture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_videoProgram, "uTexture"), 0);
        _videoMesh.Draw(GLES20.GlGetAttribLocation(_videoProgram, "aPosition"), GLES20.GlGetAttribLocation(_videoProgram, "aUv"));
    }

    private void PaintPanelIfNeeded()
    {
        var m = Menu;
        if (!_panelDirty && Equals(m, _paintedModel) && _paintedPage == _page && _paintedHover == _hovered &&
            _paintedSlot == _hoveredSlot) return;
        _painter!.Draw(_page, m, _hovered, _hoveredSlot, ThumbnailSource);
        _paintedSlot = _hoveredSlot;
        GLES20.GlBindTexture(GLES20.GlTexture2d, _panelTexture);
        GLUtils.TexImage2D(GLES20.GlTexture2d, 0, _painter.Bitmap, 0);
        _paintedModel = m;
        _paintedPage = _page;
        _paintedHover = _hovered;
        _panelDirty = false;
    }

    private void DrawPanel(Matrix4x4 mvp)
    {
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlUseProgram(_texProgram);
        VrMath.ToGl(mvp, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_texProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_texProgram, "uAlpha"), Config.MenuOpacity);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _panelTexture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_texProgram, "uTexture"), 0);
        _panelMesh!.Draw(GLES20.GlGetAttribLocation(_texProgram, "aPosition"), GLES20.GlGetAttribLocation(_texProgram, "aUv"));
        GLES20.GlDisable(GLES20.GlBlend);
    }

    private void DrawGuide(Matrix4x4 proj)
    {
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlUseProgram(_texProgram);
        // Head-locked: no model or view transform.
        VrMath.ToGl(proj, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_texProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_texProgram, "uAlpha"), 1f);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _guideTexture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_texProgram, "uTexture"), 0);
        _guideMesh!.Draw(GLES20.GlGetAttribLocation(_texProgram, "aPosition"), GLES20.GlGetAttribLocation(_texProgram, "aUv"));
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
        bool hovering = highlight;
        GLES20.GlUniform4f(GLES20.GlGetUniformLocation(_reticleProgram, "uColor"),
            hovering ? 0.31f : 1f, hovering ? 0.76f : 1f, hovering ? 0.97f : 1f, 1f);
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

    private void DrawDistortion(RenderConfig cfg, float aspect)
    {
        var (k1, k2) = cfg.LensCoefficients;
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

    private static void SetLinearClamp(int target)
    {
        GLES20.GlTexParameteri(target, GLES20.GlTextureMinFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(target, GLES20.GlTextureMagFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(target, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
        GLES20.GlTexParameteri(target, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
    }
}
