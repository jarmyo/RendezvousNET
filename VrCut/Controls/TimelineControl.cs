using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VrCut.Services;

namespace VrCut.Controls;

/// <summary>
/// Thumbnail strip with time ruler, keyframe ticks, cut ranges and a draggable playhead.
/// </summary>
public sealed class TimelineControl : Control
{
    private const double RulerHeight = 18;
    private const double TickAreaHeight = 8;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(24, 24, 28));
    private static readonly IBrush CutFill = new SolidColorBrush(Color.FromArgb(150, 220, 40, 40));
    private static readonly IPen CutBorder = new Pen(new SolidColorBrush(Color.FromRgb(255, 80, 80)), 1.5);
    private static readonly IPen MarkInPen = new Pen(new SolidColorBrush(Color.FromRgb(60, 220, 90)), 3);
    private static readonly IBrush PendingFill = new SolidColorBrush(Color.FromArgb(90, 60, 220, 90));
    private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.FromRgb(255, 210, 40));
    private static readonly IPen KeyframePen = new Pen(new SolidColorBrush(Color.FromArgb(160, 120, 180, 255)), 1);
    private static readonly IPen RulerPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1);
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

    private Bitmap?[] _thumbs = [];
    private IReadOnlyList<CutRange> _cuts = [];
    private double[] _keyframes = [];
    private double _duration;
    private double _position;
    private double? _markIn;

    public event EventHandler<double>? Seek;

    public TimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Duration => _duration;

    public double Position
    {
        get => _position;
        set { _position = Math.Clamp(value, 0, _duration); InvalidateVisual(); }
    }

    public double? MarkIn
    {
        get => _markIn;
        set { _markIn = value; InvalidateVisual(); }
    }

    public IReadOnlyList<CutRange> Cuts
    {
        set { _cuts = value; InvalidateVisual(); }
    }

    public double[] Keyframes
    {
        set { _keyframes = value; InvalidateVisual(); }
    }

    public void Reset(double duration, int thumbnailCount)
    {
        foreach (var t in _thumbs)
            t?.Dispose();
        _thumbs = new Bitmap?[thumbnailCount];
        _keyframes = [];
        _cuts = [];
        _markIn = null;
        _duration = duration;
        _position = 0;
        InvalidateVisual();
    }

    public void SetThumbnail(int index, Bitmap bitmap)
    {
        if (index < 0 || index >= _thumbs.Length)
        {
            bitmap.Dispose();
            return;
        }
        _thumbs[index]?.Dispose();
        _thumbs[index] = bitmap;
        InvalidateVisual();
    }

    private double X(double time) => _duration <= 0 ? 0 : time / _duration * Bounds.Width;

    private double Time(double x) => _duration <= 0 ? 0 : Math.Clamp(x / Bounds.Width, 0, 1) * _duration;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var strip = new Rect(0, RulerHeight, w, Math.Max(0, h - RulerHeight - TickAreaHeight));
        ctx.FillRectangle(Background, new Rect(Bounds.Size));

        DrawThumbnails(ctx, strip);
        if (_duration <= 0)
            return;

        DrawRuler(ctx, w);

        // Keyframe ticks: where a cut can end without leaving part of the removed scene behind.
        double tickTop = h - TickAreaHeight;
        double lastX = -1;
        foreach (var k in _keyframes)
        {
            double x = Math.Round(X(k)) + 0.5;
            if (x - lastX < 2)
                continue;
            ctx.DrawLine(KeyframePen, new Point(x, tickTop + 1), new Point(x, h));
            lastX = x;
        }

        foreach (var c in _cuts)
        {
            var r = new Rect(X(c.From), strip.Top, Math.Max(2, X(c.To) - X(c.From)), strip.Height);
            ctx.FillRectangle(CutFill, r);
            ctx.DrawRectangle(CutBorder, r);
        }

        if (_markIn is double mi)
        {
            // Preview of the range that "Mark out" would remove.
            double a = X(Math.Min(mi, _position)), b = X(Math.Max(mi, _position));
            ctx.FillRectangle(PendingFill, new Rect(a, strip.Top, b - a, strip.Height));
            ctx.DrawLine(MarkInPen, new Point(X(mi), strip.Top), new Point(X(mi), strip.Bottom));
        }

        double px = X(_position);
        ctx.FillRectangle(PlayheadBrush, new Rect(px - 1, RulerHeight, 2, h - RulerHeight));
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(px - 6, 0), true);
            g.LineTo(new Point(px + 6, 0));
            g.LineTo(new Point(px, RulerHeight));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(PlayheadBrush, null, head);
    }

    private void DrawThumbnails(DrawingContext ctx, Rect strip)
    {
        if (_thumbs.Length == 0 || strip.Height <= 0)
            return;
        double tw = strip.Width / _thumbs.Length;
        for (int i = 0; i < _thumbs.Length; i++)
        {
            var bmp = _thumbs[i];
            if (bmp is null)
                continue;
            var dest = new Rect(i * tw, strip.Top, tw + 0.5, strip.Height);
            // Crop the source to fill the cell (uniform-to-fill).
            var size = bmp.Size;
            double scale = Math.Max(dest.Width / size.Width, dest.Height / size.Height);
            double sw = dest.Width / scale, sh = dest.Height / scale;
            var src = new Rect((size.Width - sw) / 2, (size.Height - sh) / 2, sw, sh);
            ctx.DrawImage(bmp, src, dest);
        }
    }

    private void DrawRuler(DrawingContext ctx, double width)
    {
        // Pick a label step that leaves roughly 80px between labels.
        double[] steps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
        double pxPerSec = width / _duration;
        double step = steps.FirstOrDefault(s => s * pxPerSec >= 80, 3600);

        for (double t = 0; t <= _duration; t += step)
        {
            double x = X(t);
            ctx.DrawLine(RulerPen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));
            var ts = TimeSpan.FromSeconds(t);
            string label = ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 10, TextBrush);
            ctx.DrawText(ft, new Point(x + 3, 1));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Pointer.Capture(this);
        Focus();
        SeekTo(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ReferenceEquals(e.Pointer.Captured, this) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            SeekTo(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void SeekTo(double x)
    {
        Position = Time(x);
        Seek?.Invoke(this, _position);
    }
}
