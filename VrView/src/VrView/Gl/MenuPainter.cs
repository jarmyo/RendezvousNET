using Android.Graphics;
using VrView.Core;

namespace VrView.Gl;

/// <summary>Draws the in-VR menu panel into a bitmap with the Android Canvas API.</summary>
internal sealed class MenuPainter : IDisposable
{
    public const int Width = 1600;
    public const int Height = (int)(Width / VrMenuLayout.Aspect);

    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _text = new() { AntiAlias = true, Color = Color.White, TextAlign = Paint.Align.Center };

    public Bitmap Bitmap { get; } = Bitmap.CreateBitmap(Width, Height, Bitmap.Config.Argb8888!)!;

    public void Draw(MenuPage page, MenuModel m, MenuAction hovered, int hoveredSlot, Func<long, Bitmap?>? thumbnails)
    {
        var c = new Canvas(Bitmap);
        c.DrawColor(Color.Transparent, PorterDuff.Mode.Clear!);

        // Panel background
        _fill.Color = Color.Argb(225, 18, 24, 32);
        c.DrawRoundRect(new RectF(0, 0, Width, Height), 36, 36, _fill);

        // Title and status line
        _text.TextAlign = Paint.Align.Left;
        _text.TextSize = Height * 0.075f;
        _text.FakeBoldText = true;
        _text.Color = Color.White;
        c.DrawText(Ellipsize(m.Title, Width * 0.86f), Width * 0.03f, Height * 0.10f, _text);
        _text.FakeBoldText = false;
        _text.TextSize = Height * 0.058f;
        _text.Color = Color.Argb(255, 176, 190, 197);
        c.DrawText(Ellipsize(Labels.Header(page, m), Width * 0.94f), Width * 0.03f, Height * 0.195f, _text);

        if (page == MenuPage.Main)
            DrawSeekBar(c, m, hovered == MenuAction.Seek);

        _text.TextAlign = Paint.Align.Center;
        foreach (var b in VrMenuLayout.Buttons(page))
        {
            if (b.Action == MenuAction.Seek) continue;
            if (b.Action == MenuAction.SpotSlot)
            {
                DrawSpot(c, b, m, hovered == MenuAction.SpotSlot && hoveredSlot == b.Slot, thumbnails);
                continue;
            }
            var r = new RectF(b.X0 * Width, b.Y0 * Height, b.X1 * Width, b.Y1 * Height);
            bool active = b.Action switch
            {
                MenuAction.Shuffle => m.Shuffle,
                MenuAction.Repeat => m.Repeat != RepeatMode.Off,
                MenuAction.Lens => m.LensLevel > 0,
                MenuAction.Dwell => m.GazeClick,
                MenuAction.SpotDeleteMode => m.SpotDeleteMode,
                MenuAction.TuneNadir => m.Format.NadirDeg > 0,
                _ => false,
            };
            bool enabled = FineTuning.Applies(b.Action, m.Format.Projection);
            _fill.Color = !enabled ? Color.Argb(255, 30, 36, 42)
                : b.Action == hovered ? Color.Argb(255, 79, 195, 247)
                : active ? Color.Argb(255, 38, 84, 110)
                : Color.Argb(255, 44, 55, 66);
            c.DrawRoundRect(r, 18, 18, _fill);
            _text.Color = !enabled ? Color.Argb(255, 90, 100, 110) : b.Action == hovered ? Color.Black : Color.White;
            _text.TextSize = Height * 0.062f;
            string label = Labels.For(b.Action, m);
            DrawFitted(c, label, r);
        }
    }

    /// <summary>
    /// Head-locked guide for "set horizon with the head": a long level line to align with the
    /// video's horizon, and short instructions.
    /// </summary>
    public static Bitmap PaintHorizonGuide()
    {
        const int w = 2048, h = 256;
        var bmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
        var c = new Canvas(bmp);
        using var line = new Paint { AntiAlias = true, Color = Color.Argb(220, 79, 195, 247), StrokeWidth = 5 };
        // Leave a gap in the middle for the progress ring.
        c.DrawLine(0, h / 2f, w / 2f - 60, h / 2f, line);
        c.DrawLine(w / 2f + 60, h / 2f, w, h / 2f, line);
        using var text = new Paint { AntiAlias = true, Color = Color.White, TextAlign = Paint.Align.Center, TextSize = 26 };
        text.SetShadowLayer(6, 0, 0, Color.Black);
        c.DrawText("Inclina la cabeza hasta que esta línea quede sobre el horizonte del video", w / 2f, h / 2f - 40, text);
        c.DrawText("y quédate quieto · toca la pantalla para cancelar", w / 2f, h / 2f + 66, text);
        return bmp;
    }

    /// <summary>Soft black disc used to hide the nadir: opaque center fading out at the rim.</summary>
    public static Bitmap PaintNadirPatch()
    {
        const int size = 256;
        var bmp = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!)!;
        var c = new Canvas(bmp);
        using var paint = new Paint { AntiAlias = true };
        paint.SetShader(new RadialGradient(size / 2f, size / 2f, size / 2f,
            new int[] { Color.Argb(255, 8, 8, 10), Color.Argb(255, 8, 8, 10), Color.Argb(0, 8, 8, 10) },
            new float[] { 0f, 0.75f, 1f }, Shader.TileMode.Clamp!));
        c.DrawCircle(size / 2f, size / 2f, size / 2f, paint);
        return bmp;
    }

    private void DrawSeekBar(Canvas c, MenuModel m, bool hovered)
    {
        var bar = VrMenuLayout.SeekBar;
        float x0 = bar.X0 * Width, x1 = bar.X1 * Width;
        float cy = (bar.Y0 + bar.Y1) / 2 * Height;
        float h = hovered ? 16 : 10;
        _fill.Color = Color.Argb(255, 70, 80, 90);
        c.DrawRoundRect(new RectF(x0, cy - h, x1, cy + h), h, h, _fill);
        float f = m.DurationMs > 0 ? Math.Clamp((float)m.PositionMs / m.DurationMs, 0, 1) : 0;
        _fill.Color = Color.Argb(255, 79, 195, 247);
        c.DrawRoundRect(new RectF(x0, cy - h, x0 + (x1 - x0) * f, cy + h), h, h, _fill);
        // Spots as small yellow ticks on the bar.
        if (m.DurationMs > 0)
        {
            _fill.Color = Color.Argb(255, 255, 202, 40);
            foreach (long s in m.Spots)
            {
                float sx = x0 + (x1 - x0) * Math.Clamp((float)s / m.DurationMs, 0, 1);
                c.DrawRoundRect(new RectF(sx - 4, cy - h - 12, sx + 4, cy + h + 12), 4, 4, _fill);
            }
        }
        _fill.Color = Color.White;
        c.DrawCircle(x0 + (x1 - x0) * f, cy, h + 8, _fill);
    }

    private void DrawSpot(Canvas c, MenuButton b, MenuModel m, bool hovered, Func<long, Bitmap?>? thumbnails)
    {
        int index = m.SpotPage * VrMenuLayout.SpotsPerPage + b.Slot;
        if (index >= m.Spots.Length) return;
        long ms = m.Spots[index];
        var cell = new RectF(b.X0 * Width, b.Y0 * Height, b.X1 * Width, b.Y1 * Height);
        _fill.Color = hovered ? (m.SpotDeleteMode ? Color.Argb(255, 229, 57, 53) : Color.Argb(255, 79, 195, 247)) : Color.Argb(255, 44, 55, 66);
        c.DrawRoundRect(cell, 18, 18, _fill);

        float labelH = cell.Height() * 0.2f;
        var image = new RectF(cell.Left + 8, cell.Top + 8, cell.Right - 8, cell.Bottom - labelH - 4);
        if (thumbnails?.Invoke(ms) is { IsRecycled: false } bmp)
        {
            // Center-crop the thumbnail into the cell.
            float scale = Math.Max(image.Width() / bmp.Width, image.Height() / bmp.Height);
            float sw = image.Width() / scale, sh = image.Height() / scale;
            var src = new Rect((int)((bmp.Width - sw) / 2), (int)((bmp.Height - sh) / 2),
                (int)((bmp.Width + sw) / 2), (int)((bmp.Height + sh) / 2));
            c.DrawBitmap(bmp, src, image, null);
        }
        else
        {
            _fill.Color = Color.Argb(255, 30, 38, 46);
            c.DrawRect(image, _fill);
        }

        _text.Color = hovered ? Color.Black : Color.White;
        _text.TextSize = labelH * 0.75f;
        string label = (m.SpotDeleteMode ? "✕ " : "") + MediaUtil.FormatTime(ms);
        DrawFitted(c, label, new RectF(cell.Left, cell.Bottom - labelH - 4, cell.Right, cell.Bottom - 4));
    }

    private void DrawFitted(Canvas c, string text, RectF r)
    {
        float max = r.Width() * 0.92f;
        float w = _text.MeasureText(text);
        if (w > max) _text.TextSize *= max / w;
        var fm = _text.GetFontMetrics()!;
        float baseline = r.CenterY() - (fm.Ascent + fm.Descent) / 2;
        c.DrawText(text, r.CenterX(), baseline, _text);
    }

    private string Ellipsize(string text, float maxWidth)
    {
        if (_text.MeasureText(text) <= maxWidth) return text;
        int n = text.Length;
        while (n > 1 && _text.MeasureText(text[..n] + "…") > maxWidth) n--;
        return text[..n] + "…";
    }

    public void Dispose()
    {
        Bitmap.Recycle();
        _fill.Dispose();
        _text.Dispose();
    }
}
