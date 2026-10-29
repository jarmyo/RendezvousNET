using System.Numerics;

namespace VrView.Core;

/// <summary>
/// Detects that the head has been held still: after a short warm-up (time to move into position),
/// fires once the view direction and the up vector stay within <see cref="ToleranceDegrees"/>
/// of where they settled for <see cref="HoldSeconds"/>.
/// </summary>
public sealed class StillnessDetector
{
    private Vector3 _forward, _up;
    private bool _anchored;
    private float _warmup, _still;

    public float WarmupSeconds { get; set; } = 1.5f;
    public float HoldSeconds { get; set; } = 2f;
    public float ToleranceDegrees { get; set; } = 2.5f;

    /// <summary>0..1 progress towards firing, for drawing a ring; 0 during the warm-up.</summary>
    public float Progress => _warmup < WarmupSeconds ? 0f : Math.Clamp(_still / HoldSeconds, 0f, 1f);

    /// <returns>True when the pose has been held still long enough.</returns>
    public bool Update(Vector3 forward, Vector3 up, float dt)
    {
        if (!_anchored || _warmup < WarmupSeconds)
        {
            _warmup += dt;
            Anchor(forward, up);
            return false;
        }
        if (Math.Max(AngleDeg(_forward, forward), AngleDeg(_up, up)) > ToleranceDegrees)
        {
            Anchor(forward, up);
            return false;
        }
        _still += dt;
        return _still >= HoldSeconds;
    }

    public void Reset()
    {
        _anchored = false;
        _warmup = 0;
        _still = 0;
    }

    private void Anchor(Vector3 forward, Vector3 up)
    {
        _forward = forward;
        _up = up;
        _anchored = true;
        _still = 0;
    }

    private static float AngleDeg(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * VrMath.Rad2Deg;
}
