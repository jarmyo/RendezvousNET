namespace VrView.Core;

/// <summary>Which sensors drive head tracking.</summary>
public enum TrackingMode
{
    /// <summary>Gyroscope for smooth motion plus a slow compass correction so the view does not drift sideways.</summary>
    AntiDrift,
    /// <summary>Gyroscope and accelerometer only (smoothest, drifts sideways over time).</summary>
    GyroOnly,
    /// <summary>The compass-based rotation vector as is (never drifts, may shake near metal).</summary>
    Compass,
}

/// <summary>What the tracking settings show: mode, measured drift and compass health.</summary>
public sealed record TrackingStatus(
    TrackingMode Mode,
    bool HasCompass,
    bool CompassOk,
    float DriftDegPerMin,
    bool DriftMeasured,
    float BiasDegPerMin,
    bool Calibrating,
    float CalibrationProgress)
{
    public static TrackingStatus None => new(TrackingMode.AntiDrift, false, false, 0, false, 0, false, 0);
}

/// <summary>
/// Removes the slow sideways (yaw) drift of the game rotation vector, which fuses gyroscope and
/// accelerometer but no compass: gravity keeps pitch and roll right, but nothing corrects yaw, so a
/// tiny constant gyro error adds up (0.5°/min becomes 15° in half an hour).
/// <para>
/// Two corrections are combined: (1) the gyro bias, measured while the phone is held still and then
/// subtracted continuously; (2) when a compass-based rotation vector is available and trustworthy,
/// the remaining difference between both is corrected very slowly, so it is never noticed. Sudden
/// jumps of the compass (a speaker, a metal table) are ignored.
/// </para>
/// Rotations are Android row-major 3x3 device→world matrices (world Z up). The correction is a
/// rotation about world Z applied on the left: R' = Rz(<see cref="CorrectionRad"/>) · R.
/// </summary>
public sealed class DriftCorrector
{
    /// <summary>Time constant of the compass correction (seconds).</summary>
    public float TimeConstant { get; set; } = 20f;
    /// <summary>Hard limit of the compass correction speed (degrees per second).</summary>
    public float MaxCorrectionDegPerSec { get; set; } = 1f;
    /// <summary>A compass reading this far from the current estimate is treated as a disturbance.</summary>
    public float DisturbanceDeg { get; set; } = 20f;
    /// <summary>Angular speed below which the phone counts as still (degrees per second).</summary>
    public float StillSpeedDeg { get; set; } = 1.2f;
    /// <summary>Stillness needed before the gyro bias is measured (seconds).</summary>
    public float StillSeconds { get; set; } = 3f;
    /// <summary>Length of an explicit calibration (seconds).</summary>
    public float CalibrationSeconds { get; set; } = 5f;

    private const float MaxBiasDegPerMin = 15f;
    private const float RebaseAfterSeconds = 15f;

    // Gyro bias (rad/s of yaw drift) and its accumulated correction.
    private double _bias;
    private double _biasCorrection;
    private bool _biasMeasured;

    // Compass correction: smoothed (compass yaw − corrected gyro yaw), relative to where it started.
    private bool _hasOffset;
    private double _offset, _base;
    private float _disturbedSeconds;
    private bool _compassOk;
    private double _lastAbsTime = double.NaN;
    private readonly List<double> _warmup = [];
    private double _warmupStart;
    private const double WarmupSeconds = 3;

    // Last game rotation, for angular speed and per-sample yaw increments.
    private readonly float[] _lastGame = new float[9];
    private double _lastGameTime = double.NaN;

    // Stillness window: time and accumulated raw yaw of each sample while still.
    private readonly List<(double T, double Yaw)> _still = [];
    private double _rawYaw;
    private float _stillFor;

    // Explicit calibration.
    private bool _calibrating;
    private float _calibrationElapsed;

    // Drift meter: once per second, the raw (compass − gyro) yaw difference.
    private readonly Queue<(double T, double Diff)> _driftSamples = new();
    private double _lastDriftSampleTime = double.NaN, _rawDiffUnwrapped, _lastRawDiff = double.NaN;

    /// <summary>Yaw correction to apply to the game rotation (radians, about world Z).</summary>
    public float CorrectionRad => (float)(_biasCorrection + (_hasOffset ? _offset - _base : 0));

    public bool CompassOk => _compassOk;

    /// <summary>Starts an explicit bias calibration: the phone must stay still for <see cref="CalibrationSeconds"/>.</summary>
    public void StartCalibration()
    {
        _calibrating = true;
        _calibrationElapsed = 0;
        _still.Clear();
        _stillFor = 0;
    }

    /// <summary>Forgets the compass reference (e.g. after switching modes) but keeps the measured bias.</summary>
    public void ResetCompass()
    {
        _hasOffset = false;
        _warmup.Clear();
        _offset = _base = 0;
        _disturbedSeconds = 0;
        _driftSamples.Clear();
        _lastRawDiff = double.NaN;
        _lastDriftSampleTime = double.NaN;
    }

    public TrackingStatus Status(TrackingMode mode, bool hasCompass)
    {
        var (drift, measured) = DriftRate();
        return new TrackingStatus(mode, hasCompass, _compassOk, drift, measured,
            (float)(_bias * 60 / VrMath.Deg2Rad), _calibrating,
            _calibrating ? Math.Clamp(_calibrationElapsed / CalibrationSeconds, 0f, 1f) : 0f);
    }

    /// <summary>Feeds a game rotation vector sample (gyro + accelerometer). Time in seconds.</summary>
    public void UpdateGame(ReadOnlySpan<float> rotation, double time)
    {
        if (double.IsNaN(_lastGameTime))
        {
            rotation.CopyTo(_lastGame);
            _lastGameTime = time;
            return;
        }
        double dt = time - _lastGameTime;
        if (dt <= 0 || dt > 1)
        {
            // Sensor pause (app in background): start over without a bogus increment.
            rotation.CopyTo(_lastGame);
            _lastGameTime = time;
            _still.Clear();
            _stillFor = 0;
            return;
        }

        // World-frame increment D = R · Rprevᵀ: its angle is the angular speed, its Z part the yaw step.
        Span<double> d = stackalloc double[9];
        Rotation3.MultiplyTransposed(rotation, _lastGame, d);
        double angle = Rotation3.Angle(d);
        double yawStep = Math.Atan2(d[3], d[0]);
        rotation.CopyTo(_lastGame);
        _lastGameTime = time;
        _rawYaw += yawStep;

        _biasCorrection -= _bias * dt;
        UpdateStillness(angle / dt, time, (float)dt);
    }

    /// <summary>
    /// Feeds a compass-based rotation vector sample. <paramref name="reliable"/> comes from the
    /// sensor's accuracy report (calibrated compass, small heading error).
    /// </summary>
    /// <param name="game">The latest game rotation (uncorrected), to compare with.</param>
    public void UpdateAbsolute(ReadOnlySpan<float> absolute, ReadOnlySpan<float> game, double time, bool reliable)
    {
        double dt = double.IsNaN(_lastAbsTime) ? 0 : Math.Clamp(time - _lastAbsTime, 0, 1);
        _lastAbsTime = time;

        Span<double> d = stackalloc double[9];
        Rotation3.MultiplyTransposed(absolute, game, d);
        double rawDiff = Math.Atan2(d[3], d[0]);
        SampleDrift(rawDiff, time);

        if (!reliable)
        {
            _compassOk = false;
            return;
        }
        double target = rawDiff - _biasCorrection;
        if (!_hasOffset)
        {
            // The compass is noisy: the reference is the average of the first seconds.
            if (_warmup.Count == 0) _warmupStart = time;
            _warmup.Add(_warmup.Count == 0 ? target : _warmup[0] + Wrap(target - _warmup[0]));
            _compassOk = true;
            if (time - _warmupStart < WarmupSeconds) return;
            _offset = _base = _warmup.Average();
            _warmup.Clear();
            _hasOffset = true;
            return;
        }

        double delta = Wrap(target - _offset);
        if (Math.Abs(delta) > DisturbanceDeg * VrMath.Deg2Rad)
        {
            // A sudden jump is a magnetic disturbance: ignore it. If it lasts, the field really
            // changed (or drift built up while it was ignored): adopt the new reference without a jump.
            _compassOk = false;
            _disturbedSeconds += (float)dt;
            if (_disturbedSeconds > RebaseAfterSeconds)
            {
                _base += delta;
                _offset += delta;
                _disturbedSeconds = 0;
            }
            return;
        }
        _disturbedSeconds = 0;
        _compassOk = true;
        double step = delta * (1 - Math.Exp(-dt / Math.Max(0.1, TimeConstant)));
        double maxStep = MaxCorrectionDegPerSec * VrMath.Deg2Rad * dt;
        _offset += Math.Clamp(step, -maxStep, maxStep);
    }

    private void UpdateStillness(double speedRadPerSec, double time, float dt)
    {
        float threshold = StillSpeedDeg * (_calibrating ? 2f : 1f) * VrMath.Deg2Rad;
        if (speedRadPerSec > threshold)
        {
            _still.Clear();
            _stillFor = 0;
            if (_calibrating) _calibrationElapsed = 0;
            return;
        }
        _stillFor += dt;
        _still.Add((time, _rawYaw));
        if (_calibrating) _calibrationElapsed += dt;
        // Keep at most a minute of samples.
        if (_still.Count > 0 && time - _still[0].T > 60) _still.RemoveAt(0);

        bool calibrationDone = _calibrating && _calibrationElapsed >= CalibrationSeconds;
        if (_stillFor < StillSeconds && !calibrationDone) return;
        // Re-estimate once per second of stillness (and when a calibration completes).
        if (!calibrationDone && _still.Count > 1 && (int)(_stillFor) == (int)(_stillFor - dt)) return;

        double slope = Slope(_still);
        double max = MaxBiasDegPerMin * VrMath.Deg2Rad / 60;
        if (Math.Abs(slope) <= max)
        {
            // A longer window is a better measurement: trust it more.
            double weight = Math.Clamp(_stillFor / 20.0, 0.25, 1.0);
            if (calibrationDone || !_biasMeasured) weight = 1;
            _bias += (slope - _bias) * weight;
            _biasMeasured = true;
        }
        if (calibrationDone) _calibrating = false;
    }

    private void SampleDrift(double rawDiff, double time)
    {
        if (!double.IsNaN(_lastDriftSampleTime) && time - _lastDriftSampleTime < 1) return;
        _lastDriftSampleTime = time;
        _rawDiffUnwrapped = double.IsNaN(_lastRawDiff) ? rawDiff : _rawDiffUnwrapped + Wrap(rawDiff - _lastRawDiff);
        _lastRawDiff = rawDiff;
        _driftSamples.Enqueue((time, _rawDiffUnwrapped));
        while (_driftSamples.Count > 0 && time - _driftSamples.Peek().T > 180) _driftSamples.Dequeue();
    }

    /// <summary>How fast the gyroscope drifts against the compass (degrees per minute), over the last minutes.</summary>
    private (float Rate, bool Measured) DriftRate()
    {
        if (_driftSamples.Count >= 30)
        {
            var list = _driftSamples.ToList();
            // The compass is noisy: only trust the slope over a long enough window.
            if (list[^1].T - list[0].T >= 30)
                return ((float)(Slope(list) * 60 / VrMath.Deg2Rad), true);
        }
        return ((float)(_bias * 60 / VrMath.Deg2Rad), _biasMeasured);
    }

    private static double Slope(IReadOnlyList<(double T, double Y)> samples)
    {
        int n = samples.Count;
        if (n < 2) return 0;
        double t0 = samples[0].T, mt = 0, my = 0;
        foreach (var (t, y) in samples) { mt += t - t0; my += y; }
        mt /= n;
        my /= n;
        double num = 0, den = 0;
        foreach (var (t, y) in samples)
        {
            double dt = t - t0 - mt;
            num += dt * (y - my);
            den += dt * dt;
        }
        return den > 1e-9 ? num / den : 0;
    }

    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}

/// <summary>Row-major 3x3 rotation helpers in the Android sensor convention.</summary>
public static class Rotation3
{
    /// <summary>result = a · bᵀ, in double precision (the increments between samples are tiny).</summary>
    public static void MultiplyTransposed(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<double> result)
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                result[r * 3 + c] = (double)a[r * 3] * b[c * 3] + (double)a[r * 3 + 1] * b[c * 3 + 1] + (double)a[r * 3 + 2] * b[c * 3 + 2];
    }

    /// <summary>result = a · b</summary>
    public static void Multiply(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> result)
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                result[r * 3 + c] = a[r * 3] * b[c] + a[r * 3 + 1] * b[3 + c] + a[r * 3 + 2] * b[6 + c];
    }

    /// <summary>Rotation by <paramref name="angle"/> radians about the world Z (up) axis.</summary>
    public static void RotZ(float angle, Span<float> result)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        result[0] = c; result[1] = -s; result[2] = 0;
        result[3] = s; result[4] = c; result[5] = 0;
        result[6] = 0; result[7] = 0; result[8] = 1;
    }

    /// <summary>R' = Rz(yaw) · R, in place.</summary>
    public static void ApplyYaw(float yaw, Span<float> rotation)
    {
        if (yaw == 0) return;
        Span<float> z = stackalloc float[9];
        Span<float> tmp = stackalloc float[9];
        RotZ(yaw, z);
        Multiply(z, rotation, tmp);
        tmp.CopyTo(rotation);
    }

    /// <summary>Rotation angle of a rotation matrix (radians), accurate for tiny angles too.</summary>
    public static double Angle(ReadOnlySpan<double> r)
    {
        double sx = r[7] - r[5], sy = r[2] - r[6], sz = r[3] - r[1];
        return Math.Atan2(0.5 * Math.Sqrt(sx * sx + sy * sy + sz * sz), (r[0] + r[4] + r[8] - 1) / 2.0);
    }
}
