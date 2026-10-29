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
        void OnMenuAction(MenuAction action, float fraction);
    }

    private const float SphereRadius = 50f;
    private const float FlatDistance = 4f;
    private const float FlatWidth = 4.6f;
    private const float PanelDistance = 2f;
    private const float PanelWidth = 2.2f;
    private const float PanelHeight = PanelWidth / VrMenuLayout.Aspect;
    private const float LookDownOpenAngle = -55f * VrMath.Deg2Rad;
    private const float MenuIdleHideSeconds = 6f;

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

    private volatile bool _recenterRequested;
    private volatile bool _frameAvailable;

    // GL resources
    private int _videoProgram, _texProgram, _reticleProgram, _distortProgram;
    private int _oesTexture, _panelTexture, _reticleVbo;
    private int _fbo, _fboTexture, _fboWidth, _fboHeight;
    private SurfaceTexture? _surfaceTexture;
    private GlMesh? _videoMesh, _panelMesh, _screenQuad;
    private (Projection, float, float) _videoMeshKey;
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
    private bool _panelDirty = true;

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
        if (!_menuVisible)
        {
            var f = VrMath.Forward(CurrentView());
            _menuYaw = VrMath.YawOf(f);
            _menuPitch = Math.Clamp(VrMath.PitchOf(f), -50f * VrMath.Deg2Rad, 50f * VrMath.Deg2Rad);
        }
        SetMenu(true, page);
    }

    public void HideMenu() => SetMenu(false, MenuPage.Main);

    public void ToggleMenu()
    {
        if (_menuVisible) HideMenu();
        else ShowMenu(MenuPage.Main);
    }

    /// <summary>Viewer button / screen tap: open the menu, or click what the reticle points at.</summary>
    public void Trigger()
    {
        if (!_menuVisible) { ShowMenu(MenuPage.Main); return; }
        var f = VrMath.Forward(CurrentView());
        var hit = VrMath.PanelHit(f, _menuYaw, _menuPitch, PanelDistance, PanelWidth, PanelHeight);
        var (action, fraction, _) = hit is { } h ? VrMenuLayout.HitTest(_page, h.X, h.Y) : (MenuAction.None, 0f, false);
        if (action == MenuAction.None) HideMenu();
        else Fire(action, fraction);
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

    private void Fire(MenuAction action, float fraction)
    {
        switch (action)
        {
            case MenuAction.Horizon: SetMenu(true, MenuPage.Horizon); break;
            case MenuAction.Settings: SetMenu(true, MenuPage.Settings); break;
            case MenuAction.Back: SetMenu(true, MenuPage.Main); break;
            case MenuAction.Exit: SetMenu(false, MenuPage.Main); break;
        }
        _idleSeconds = 0;
        _host.OnMenuAction(action, fraction);
    }

    // ---- GLSurfaceView.IRenderer ----

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        _videoProgram = GlUtil.CreateProgram(Shaders.VideoVertex, Shaders.VideoFragment);
        _texProgram = GlUtil.CreateProgram(Shaders.TexturedVertex, Shaders.TexturedFragment);
        _reticleProgram = GlUtil.CreateProgram(Shaders.ReticleVertex, Shaders.ReticleFragment);
        _distortProgram = GlUtil.CreateProgram(Shaders.TexturedVertex, Shaders.DistortionFragment);

        var tex = new int[2];
        GLES20.GlGenTextures(2, tex, 0);
        _oesTexture = tex[0];
        _panelTexture = tex[1];
        GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, _oesTexture);
        SetLinearClamp(GLES11Ext.GlTextureExternalOes);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _panelTexture);
        SetLinearClamp(GLES20.GlTexture2d);

        // Panel quad: bitmap row 0 is the top, so flip v.
        var quad = MeshBuilder.Quad(PanelWidth, PanelHeight, 0);
        for (int i = 4; i < quad.Vertices.Length; i += Mesh.FloatsPerVertex) quad.Vertices[i] = 1f - quad.Vertices[i];
        _panelMesh = new GlMesh(quad);
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
        var model = VrMath.VideoModel(cfg.Pitch, cfg.Roll, cfg.Yaw, _recenterYaw);
        var panelModel = VrMath.PanelModel(_menuYaw, _menuPitch, PanelDistance);

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
            if (_menuVisible)
            {
                DrawPanel(panelModel * view * proj);
                DrawReticle(proj);
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

    private void UpdateMenu(Matrix4x4 view, RenderConfig cfg, float dt)
    {
        var forward = VrMath.Forward(view);
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

        var hit = VrMath.PanelHit(forward, _menuYaw, _menuPitch, PanelDistance, PanelWidth, PanelHeight);
        var (action, fraction, repeat) = hit is { } h ? VrMenuLayout.HitTest(_page, h.X, h.Y) : (MenuAction.None, 0f, false);
        if (action != _hovered) { _hovered = action; _panelDirty = true; }

        bool onPanel = hit is { } p && p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1;
        _idleSeconds = onPanel ? 0 : _idleSeconds + dt;
        if (_idleSeconds > MenuIdleHideSeconds)
        {
            HideMenu();
            return;
        }

        _dwell.DwellSeconds = 1.4f;
        int key = action == MenuAction.Seek ? (int)(fraction * 40) : 0;
        if (cfg.GazeClick && _dwell.Update(action, key, repeat, dt))
            Fire(action, fraction);
    }

    private void EnsureVideoMesh(RenderConfig cfg)
    {
        var f = cfg.Format;
        float aspect = f.Projection == Projection.Flat ? f.EyeAspect(cfg.VideoWidth, cfg.VideoHeight) : 0f;
        float fov = f.Projection == Projection.Fisheye ? f.FisheyeFov : 0f;
        var key = (f.Projection, aspect, fov);
        if (_videoMesh is not null && key == _videoMeshKey) return;
        _videoMesh?.Dispose();
        _videoMesh = new GlMesh(f.Projection switch
        {
            Projection.Sphere360 => MeshBuilder.Equirect(SphereRadius, 360f),
            Projection.Dome180 => MeshBuilder.Equirect(SphereRadius, 180f, 64, 48),
            Projection.Fisheye => MeshBuilder.Fisheye(SphereRadius, fov),
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
        if (!_panelDirty && Equals(m, _paintedModel) && _paintedPage == _page && _paintedHover == _hovered) return;
        _painter!.Draw(_page, m, _hovered);
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
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_texProgram, "uAlpha"), 1f);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES20.GlTexture2d, _panelTexture);
        GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_texProgram, "uTexture"), 0);
        _panelMesh!.Draw(GLES20.GlGetAttribLocation(_texProgram, "aPosition"), GLES20.GlGetAttribLocation(_texProgram, "aUv"));
        GLES20.GlDisable(GLES20.GlBlend);
    }

    private void DrawReticle(Matrix4x4 proj)
    {
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlUseProgram(_reticleProgram);
        VrMath.ToGl(proj, _mvp);
        GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_reticleProgram, "uMvp"), 1, false, _mvp, 0);
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_reticleProgram, "uSize"), Math.Max(24f, _height * 0.04f));
        GLES20.GlUniform1f(GLES20.GlGetUniformLocation(_reticleProgram, "uProgress"), Config.GazeClick ? _dwell.Progress : 0f);
        bool hovering = _hovered != MenuAction.None;
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
