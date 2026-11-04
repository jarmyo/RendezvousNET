using VrView.Core;

namespace VrView.Core.Tests;

/// <summary>
/// Simulated sensors: a head that keeps moving, a game rotation vector whose yaw drifts at a known
/// rate and a noisy compass-based rotation vector. The corrected orientation must not drift.
/// </summary>
public class DriftCorrectorTests
{
    private const float Deg = MathF.PI / 180f;

    private static float[] Rot(float yaw, float pitch, float roll)
    {
        // Rz(yaw) · Rx(pitch) · Ry(roll), Android row-major convention.
        Span<float> z = stackalloc float[9], x = stackalloc float[9], y = stackalloc float[9], t = stackalloc float[9];
        Rotation3.RotZ(yaw, z);
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch), cr = MathF.Cos(roll), sr = MathF.Sin(roll);
        x[0] = 1; x[4] = cp; x[5] = -sp; x[7] = sp; x[8] = cp;
        y[0] = cr; y[2] = sr; y[4] = 1; y[6] = -sr; y[8] = cr;
        var result = new float[9];
        Rotation3.Multiply(z, x, t);
        Rotation3.Multiply(t, y, result);
        return result;
    }

    private static float[] Yawed(float yaw, float[] r)
    {
        var copy = (float[])r.Clone();
        Rotation3.ApplyYaw(yaw, copy);
        return copy;
    }

    /// <summary>Yaw of a relative to b (degrees).</summary>
    private static double YawDiffDeg(float[] a, float[] b)
    {
        Span<double> d = stackalloc double[9];
        Rotation3.MultiplyTransposed(a, b, d);
        return Math.Atan2(d[3], d[0]) / Deg;
    }

    private static float[] Head(double t, bool still) => still
        ? Rot(0.3f, 0.1f, 0.02f)
        : Rot((float)(0.6 * Math.Sin(0.21 * t) + 0.3 * Math.Sin(0.047 * t)), (float)(0.2 * Math.Sin(0.33 * t)), (float)(0.05 * Math.Sin(0.5 * t)));

    [Fact]
    public void Compass_correction_removes_gyro_drift()
    {
        var c = new DriftCorrector();
        var rng = new Random(1);
        double driftPerSec = 0.8 * Deg / 60; // 0.8°/min
        float g0 = 1.1f, a0 = -0.4f;
        double maxErrLate = 0;
        double initialErr = double.NaN;
        for (int i = 0; i <= 100 * 60 * 20; i++) // 20 minutes at 100 Hz
        {
            double t = i / 100.0;
            var head = Head(t, false);
            var game = Yawed((float)(g0 + driftPerSec * t), head);
            c.UpdateGame(game, t);
            if (i % 2 == 0)
            {
                float noise = (float)((rng.NextDouble() * 2 - 1) * 2 * Deg);
                c.UpdateAbsolute(Yawed(a0 + noise, head), game, t, reliable: true);
            }
            var corrected = Yawed(c.CorrectionRad, game);
            double err = YawDiffDeg(corrected, Yawed(g0, head));
            if (double.IsNaN(initialErr) && t > 1) initialErr = err;
            if (t > 120) maxErrLate = Math.Max(maxErrLate, Math.Abs(err - initialErr));
        }
        // Without correction the view would have moved 16°.
        Assert.True(maxErrLate < 1.0, $"drift left: {maxErrLate:0.00}°");
        var status = c.Status(TrackingMode.AntiDrift, true);
        Assert.True(status.DriftMeasured);
        Assert.InRange(status.DriftDegPerMin, -1.2f, -0.4f); // compass − gyro moves the opposite way
    }

    [Fact]
    public void Magnetic_disturbance_is_ignored()
    {
        var c = new DriftCorrector();
        float g0 = 0.2f, a0 = 0.9f;
        double before = 0;
        for (int i = 0; i <= 100 * 60; i++)
        {
            double t = i / 100.0;
            var head = Head(t, false);
            var game = Yawed(g0, head);
            c.UpdateGame(game, t);
            // 10 s next to a speaker: the compass jumps by 40°.
            float disturbance = t is > 30 and < 40 ? 40 * Deg : 0;
            c.UpdateAbsolute(Yawed(a0 + disturbance, head), game, t, reliable: true);
            if (Math.Abs(t - 29.5) < 0.005) before = c.CorrectionRad / Deg;
            if (t is > 30 and < 40) Assert.False(c.CompassOk);
        }
        Assert.InRange(c.CorrectionRad / Deg - before, -0.2, 0.2);
        Assert.True(c.CompassOk);
    }

    [Fact]
    public void Unreliable_compass_does_not_correct()
    {
        var c = new DriftCorrector();
        for (int i = 0; i <= 3000; i++)
        {
            double t = i / 100.0;
            var game = Yawed((float)(t * 0.01), Head(t, false));
            c.UpdateGame(game, t);
            c.UpdateAbsolute(Head(t, false), game, t, reliable: false);
        }
        Assert.Equal(0f, c.CorrectionRad, 3);
        Assert.False(c.CompassOk);
    }

    [Fact]
    public void Gyro_bias_is_measured_while_still_and_then_subtracted()
    {
        var c = new DriftCorrector();
        double driftPerSec = 1.5 * Deg / 60;
        float g0 = 0.5f;
        // Phone on a table for 20 s, then worn for 10 minutes without a compass.
        double lastErr = 0, errAtStart = double.NaN;
        for (int i = 0; i <= 100 * 620; i++)
        {
            double t = i / 100.0;
            var head = Head(t, still: t < 20);
            var game = Yawed((float)(g0 + driftPerSec * t), head);
            c.UpdateGame(game, t);
            double err = YawDiffDeg(Yawed(c.CorrectionRad, game), Yawed(g0, head));
            if (t >= 20 && double.IsNaN(errAtStart)) errAtStart = err;
            lastErr = err;
        }
        var status = c.Status(TrackingMode.GyroOnly, false);
        Assert.InRange(status.BiasDegPerMin, 1.3f, 1.7f);
        // Uncorrected it would have moved 15° in those 10 minutes.
        Assert.InRange(lastErr - errAtStart, -1.0, 1.0);
    }

    [Fact]
    public void Explicit_calibration_reports_progress_and_finishes()
    {
        var c = new DriftCorrector();
        c.StartCalibration();
        double t = 0;
        for (; t < 2; t += 0.01) c.UpdateGame(Yawed((float)(t * 0.001), Head(t, true)), t);
        var mid = c.Status(TrackingMode.GyroOnly, false);
        Assert.True(mid.Calibrating);
        Assert.InRange(mid.CalibrationProgress, 0.2f, 0.6f);
        for (; t < 6; t += 0.01) c.UpdateGame(Yawed((float)(t * 0.001), Head(t, true)), t);
        var done = c.Status(TrackingMode.GyroOnly, false);
        Assert.False(done.Calibrating);
        Assert.InRange(done.BiasDegPerMin, 0.001f / Deg * 60 * 0.8f, 0.001f / Deg * 60 * 1.2f);
    }

    [Fact]
    public void Moving_resets_the_calibration_timer()
    {
        var c = new DriftCorrector();
        c.StartCalibration();
        double t = 0;
        for (; t < 3; t += 0.01) c.UpdateGame(Head(t, true), t);
        for (; t < 4; t += 0.01) c.UpdateGame(Head(t, false), t);
        var s = c.Status(TrackingMode.GyroOnly, false);
        Assert.True(s.Calibrating);
        Assert.True(s.CalibrationProgress < 0.1f);
    }
}
