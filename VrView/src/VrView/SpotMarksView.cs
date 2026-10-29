using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace VrView;

/// <summary>Draws the spots as small ticks on top of the touch seek bar (does not take touches).</summary>
internal sealed class SpotMarksView : View
{
    private readonly SeekBar _seekBar;
    private readonly Paint _paint = new() { AntiAlias = true, Color = Color.Rgb(255, 202, 40) };
    private long[] _spots = [];
    private long _durationMs;

    public SpotMarksView(Context context, SeekBar seekBar) : base(context)
    {
        _seekBar = seekBar;
        Clickable = false;
        Focusable = false;
    }

    public void SetSpots(long[] spots, long durationMs)
    {
        if (ReferenceEquals(spots, _spots) && durationMs == _durationMs) return;
        _spots = spots;
        _durationMs = durationMs;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_durationMs <= 0 || _spots.Length == 0) return;
        float left = _seekBar.PaddingLeft, right = Width - _seekBar.PaddingRight;
        float cy = Height / 2f, half = Resources!.DisplayMetrics!.Density * 7, w = Resources.DisplayMetrics.Density * 1.5f;
        foreach (long s in _spots)
        {
            float x = left + (right - left) * Math.Clamp((float)s / _durationMs, 0f, 1f);
            canvas.DrawRoundRect(x - w, cy - half, x + w, cy + half, w, w, _paint);
        }
    }
}
