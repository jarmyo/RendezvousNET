using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace VrView;

/// <summary>
/// Draws the spots as small ticks on top of the touch seek bar, or in edit mode the parts marked for
/// removal (red) and the start of the one being marked (green). Does not take touches.
/// </summary>
internal sealed class SpotMarksView : View
{
    private readonly SeekBar _seekBar;
    private readonly Paint _paint = new() { AntiAlias = true, Color = Color.Rgb(255, 202, 40) };
    private readonly Paint _cutPaint = new() { AntiAlias = true, Color = Color.Argb(200, 229, 57, 53) };
    private readonly Paint _pendingPaint = new() { AntiAlias = true, Color = Color.Rgb(60, 220, 90) };
    private long[] _spots = [];
    private Core.CutRange[] _cuts = [];
    private long _pendingMs = -1;
    private long _durationMs;

    public SpotMarksView(Context context, SeekBar seekBar) : base(context)
    {
        _seekBar = seekBar;
        Clickable = false;
        Focusable = false;
    }

    public void SetSpots(long[] spots, long durationMs) => Set(spots, [], -1, durationMs);

    public void SetCuts(Core.CutRange[] cuts, long pendingMs, long durationMs) => Set([], cuts, pendingMs, durationMs);

    private void Set(long[] spots, Core.CutRange[] cuts, long pendingMs, long durationMs)
    {
        if (ReferenceEquals(spots, _spots) && ReferenceEquals(cuts, _cuts) && pendingMs == _pendingMs && durationMs == _durationMs) return;
        _spots = spots;
        _cuts = cuts;
        _pendingMs = pendingMs;
        _durationMs = durationMs;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_durationMs <= 0) return;
        float left = _seekBar.PaddingLeft, right = Width - _seekBar.PaddingRight;
        float cy = Height / 2f, half = Resources!.DisplayMetrics!.Density * 7, w = Resources.DisplayMetrics.Density * 1.5f;
        float X(long ms) => left + (right - left) * Math.Clamp((float)ms / _durationMs, 0f, 1f);
        foreach (var c in _cuts)
            canvas.DrawRect(X(c.FromMs), cy - half * 0.6f, Math.Max(X(c.FromMs) + 2, X(c.ToMs)), cy + half * 0.6f, _cutPaint);
        if (_pendingMs >= 0)
            canvas.DrawRect(X(_pendingMs) - w * 1.5f, cy - half, X(_pendingMs) + w * 1.5f, cy + half, _pendingPaint);
        foreach (long s in _spots)
        {
            float x = left + (right - left) * Math.Clamp((float)s / _durationMs, 0f, 1f);
            canvas.DrawRoundRect(x - w, cy - half, x + w, cy + half, w, w, _paint);
        }
    }
}
