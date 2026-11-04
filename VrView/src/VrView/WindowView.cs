using Android.Content;
using Android.Graphics;
using Android.Views;
using VrView.Core;
using VrView.Gl;

namespace VrView;

/// <summary>
/// Shows the same windows as the headset on the touch screen: the window is painted with
/// <see cref="MenuPainter"/>, fitted into the view, and taps and slider drags are turned into actions.
/// </summary>
internal sealed class WindowView : View
{
    private readonly MenuPainter _painter = new();
    private UiWindow? _window;
    private int _pressed = -1;
    private float _pressedFraction;
    private RectF _dest = new();
    private bool _dirty = true;

    /// <summary>Called with the action, fraction (sliders) and argument of a touched element.</summary>
    public Action<MenuAction, float, int>? Clicked { get; set; }

    public WindowView(Context context) : base(context) { }

    public Func<UiElement, Bitmap?>? Pictures
    {
        get => _painter.Pictures;
        set => _painter.Pictures = value;
    }

    public UiWindow? Window
    {
        get => _window;
        set
        {
            if (Windows.SameContent(value, _window)) return;
            _window = value;
            _dirty = true;
            Invalidate();
        }
    }

    public void SetPreviewFormat(VideoFormat f)
    {
        if (_painter.PreviewFormat == f) return;
        _painter.PreviewFormat = f;
        Repaint();
    }

    /// <summary>Pictures changed: repaint.</summary>
    public void Repaint()
    {
        _dirty = true;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_window is null) return;
        if (_dirty || _painter.Bitmap is null)
        {
            _painter.Draw(_window, _pressed, _pressedFraction);
            _dirty = false;
        }
        var bmp = _painter.Bitmap!;
        float scale = Math.Min((float)Width / bmp.Width, (float)Height / bmp.Height);
        float w = bmp.Width * scale, h = bmp.Height * scale;
        _dest = new RectF((Width - w) / 2, (Height - h) / 2, (Width + w) / 2, (Height + h) / 2);
        using var paint = new Paint { FilterBitmap = true, AntiAlias = true };
        canvas.DrawBitmap(bmp, null, _dest, paint);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || _window is null) return false;
        float u = (e.GetX() - _dest.Left) / Math.Max(1, _dest.Width());
        float v = (e.GetY() - _dest.Top) / Math.Max(1, _dest.Height());
        var hit = _window.HitTest(u, v);
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _pressed = hit.Index;
                _pressedFraction = hit.Fraction;
                Repaint();
                return true;
            case MotionEventActions.Move:
                if (_pressed >= 0 && _pressed < _window.Elements.Count && _window.Elements[_pressed] is { Kind: UiKind.Slider } slider)
                {
                    // Dragging a slider changes the value live.
                    float f = Math.Clamp((u - slider.Rect.X0) / slider.Rect.Width, 0f, 1f);
                    if (MathF.Abs(f - _pressedFraction) > 0.005f)
                    {
                        _pressedFraction = f;
                        Clicked?.Invoke(slider.Action, f, slider.Arg);
                    }
                }
                return true;
            case MotionEventActions.Up:
                int pressed = _pressed;
                _pressed = -1;
                Repaint();
                if (pressed >= 0 && pressed == hit.Index) Clicked?.Invoke(hit.Action, hit.Fraction, hit.Arg);
                else if (pressed >= 0 && pressed < _window.Elements.Count && _window.Elements[pressed].Kind == UiKind.Slider)
                    Clicked?.Invoke(_window.Elements[pressed].Action, _pressedFraction, _window.Elements[pressed].Arg);
                return true;
            case MotionEventActions.Cancel:
                _pressed = -1;
                Repaint();
                return true;
        }
        return true;
    }
}
