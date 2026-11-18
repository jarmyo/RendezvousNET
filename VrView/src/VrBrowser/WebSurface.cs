using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace VrView;

/// <summary>
/// A WebView drawn into a texture instead of the screen: it lives in a <see cref="Presentation"/> on a
/// private virtual display whose output is the renderer's <see cref="SurfaceTexture"/>. The system
/// composes everything the page shows there, video included (also hardware-decoded and fullscreen video),
/// so the page can be drawn as a floating screen inside the headset. Touches are sent to it as events.
/// UI thread only.
/// </summary>
internal sealed class WebSurface : IDisposable
{
    /// <summary>KeyCharacterMap.VIRTUAL_KEYBOARD: the device id of keys that no real keyboard sent.</summary>
    private const int VirtualKeyboard = -1;
    private readonly VirtualDisplay _display;
    private readonly Presentation _presentation;
    private readonly FrameLayout _root;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private SurfaceTexture? _texture;
    private Surface? _surface;
    private View? _custom;

    public WebView Web { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public WebSurface(Context context, int width, int height, int dpi)
    {
        Width = width;
        Height = height;
        var displays = (DisplayManager)context.GetSystemService(Context.DisplayService)!;
        // Private display of this app: no permission needed, nothing else can show windows on it.
        _display = displays.CreateVirtualDisplay("VrView browser", width, height, dpi, null,
            VirtualDisplayFlags.OwnContentOnly | VirtualDisplayFlags.Presentation)!;
        _presentation = new Presentation(context, _display.Display!);
        _root = new FrameLayout(_presentation.Context);
        _root.SetBackgroundColor(Color.White);
        Web = new WebView(_presentation.Context);
        _root.AddView(Web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _presentation.SetContentView(_root);
        _presentation.SetCancelable(false);
        // The phone's on-screen keyboard must not open over the headset view: typing comes from the VR keyboard.
        _presentation.Window?.AddFlags(WindowManagerFlags.AltFocusableIm);
        Web.Focusable = true;
        Web.FocusableInTouchMode = true;
        _presentation.Show();
    }

    /// <summary>Starts drawing into the renderer's texture (again after the GL context was recreated).</summary>
    public void Attach(SurfaceTexture texture)
    {
        var oldSurface = _surface;
        var oldTexture = _texture;
        _texture = texture;
        _texture.SetDefaultBufferSize(Width, Height);
        _surface = new Surface(texture);
        _display.Surface = _surface;
        oldSurface?.Release();
        if (!ReferenceEquals(oldTexture, texture)) oldTexture?.Release();
    }

    /// <summary>Changes the page size in pixels (the page lays itself out again).</summary>
    public void Resize(int width, int height, int dpi)
    {
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        _texture?.SetDefaultBufferSize(width, height);
        _display.Resize(width, height, dpi);
    }

    /// <summary>The page's own fullscreen view (a video player), shown over the page; null removes it.</summary>
    public void SetCustomView(View? view)
    {
        if (_custom is not null) _root.RemoveView(_custom);
        _custom = view;
        if (view is not null)
            _root.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    public bool HasCustomView => _custom is not null;

    /// <summary>A tap at a point of the page (fractions of the width and height, origin top-left).</summary>
    public void Tap(float u, float v)
    {
        float x = u * Width, y = v * Height;
        long t = SystemClock.UptimeMillis();
        Send(t, t, MotionEventActions.Down, x, y);
        _handler.PostDelayed(() => Send(t, SystemClock.UptimeMillis(), MotionEventActions.Up, x, y), 60);
    }

    /// <summary>
    /// Scrolls like a finger drag at a point of the page (any scrolling box, not only the page itself).
    /// <paramref name="fraction"/> is the distance as a fraction of the height; positive shows what is below.
    /// </summary>
    public void Drag(float u, float v, float fraction)
    {
        const int steps = 8, stepMs = 16;
        float x = u * Width, y0 = v * Height;
        float dy = -fraction * Height;
        long t = SystemClock.UptimeMillis();
        Send(t, t, MotionEventActions.Down, x, y0);
        for (int i = 1; i <= steps; i++)
        {
            float y = y0 + dy * i / steps;
            _handler.PostDelayed(() => Send(t, SystemClock.UptimeMillis(), MotionEventActions.Move, x, y), i * stepMs);
        }
        // Held still a moment before lifting, so it stops there instead of flinging on.
        float yEnd = y0 + dy;
        _handler.PostDelayed(() => Send(t, SystemClock.UptimeMillis(), MotionEventActions.Move, x, yEnd), steps * stepMs + 60);
        _handler.PostDelayed(() => Send(t, SystemClock.UptimeMillis(), MotionEventActions.Up, x, yEnd), steps * stepMs + 80);
    }

    /// <summary>A key press (down and up), for the focused element of the page.</summary>
    public void Key(Keycode code)
    {
        long t = SystemClock.UptimeMillis();
        DispatchKey(new KeyEvent(t, t, KeyEventActions.Down, code, 0));
        DispatchKey(new KeyEvent(t, SystemClock.UptimeMillis(), KeyEventActions.Up, code, 0));
    }

    /// <summary>Types text with key presses (for frames of other sites, which scripts cannot reach).</summary>
    public void Type(string text)
    {
        var map = KeyCharacterMap.Load(VirtualKeyboard);
        var events = map?.GetEvents(text.ToCharArray());
        if (events is not null)
        {
            foreach (var ev in events) DispatchKey(ev);
            return;
        }
        // Characters without a key (ñ, á…): one event that carries the text.
#pragma warning disable CA1422 // ACTION_MULTIPLE is still how a view receives text without an input method.
        DispatchKey(new KeyEvent(SystemClock.UptimeMillis(), text, VirtualKeyboard, 0));
#pragma warning restore CA1422
    }

    private void DispatchKey(KeyEvent ev)
    {
        if (!Web.HasFocus) Web.RequestFocus();
        _presentation.DispatchKeyEvent(ev);
    }

    private void Send(long downTime, long eventTime, MotionEventActions action, float x, float y)
    {
        var ev = MotionEvent.Obtain(downTime, eventTime, action, x, y, 0)!;
        ev.SetSource(InputSourceType.Touchscreen);
        _presentation.DispatchTouchEvent(ev);
        ev.Recycle();
    }

    public void Dispose()
    {
        _handler.RemoveCallbacksAndMessages(null);
        SetCustomView(null);
        Web.StopLoading();
        Web.Destroy();
        _presentation.Dismiss();
        _display.Release();
        _surface?.Release();
        _texture?.Release();
    }
}
