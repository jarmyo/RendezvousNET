using Android.Content;
using Android.Hardware;
using Android.Runtime;
using VrView.Core;

namespace VrView;

/// <summary>
/// Phone orientation for head tracking. In "anti-deriva" mode the smooth game rotation vector
/// (gyroscope + accelerometer) is corrected by <see cref="DriftCorrector"/> with the compass-based
/// rotation vector, so the picture does not slowly slide sideways. "Solo giroscopio" uses the game
/// vector with only the at-rest bias correction, and "brújula" the rotation vector as is.
/// </summary>
internal sealed class HeadTracker : Java.Lang.Object, ISensorEventListener
{
    private readonly SensorManager? _sensors;
    private readonly Sensor? _game, _absolute;
    private readonly DriftCorrector _corrector = new();
    private readonly float[] _vector = new float[4];
    private readonly float[] _gameMatrix = new float[9];
    private readonly float[] _absMatrix = new float[9];
    private readonly float[] _latest = new float[9];
    private readonly object _lock = new();
    private bool _hasData, _hasGame, _absReliable = true;
    private TrackingMode _mode = TrackingMode.AntiDrift;
    private bool _running;

    /// <summary>Surface.ROTATION_* value of the display (0..3); updated by the activity.</summary>
    public volatile int DisplayRotation = 1;

    /// <summary>Raised (on the sensor thread) when the reference changes abruptly, so the view should be recentered.</summary>
    public event Action? ReferenceChanged;

    public HeadTracker(Context context)
    {
        _sensors = context.GetSystemService(Context.SensorService) as SensorManager;
        _game = _sensors?.GetDefaultSensor(SensorType.GameRotationVector);
        _absolute = _sensors?.GetDefaultSensor(SensorType.RotationVector);
    }

    public bool IsAvailable => _game is not null || _absolute is not null;
    public bool HasCompass => _absolute is not null && _game is not null;

    public TrackingMode Mode
    {
        get => _mode;
        set
        {
            if (value == _mode) return;
            _mode = value;
            lock (_lock) _corrector.ResetCompass();
            if (_running) { Stop(); Start(); }
            ReferenceChanged?.Invoke();
        }
    }

    public TrackingStatus Status
    {
        get { lock (_lock) return _corrector.Status(EffectiveMode, HasCompass); }
    }

    /// <summary>Measures the gyroscope error: the phone must stay still for a few seconds.</summary>
    public void Calibrate()
    {
        lock (_lock) _corrector.StartCalibration();
    }

    private TrackingMode EffectiveMode => _game is null ? TrackingMode.Compass
        : _absolute is null && _mode != TrackingMode.GyroOnly ? TrackingMode.GyroOnly : _mode;

    public void Start()
    {
        if (_sensors is null) return;
        _running = true;
        var mode = EffectiveMode;
        if (mode != TrackingMode.Compass && _game is not null)
            _sensors.RegisterListener(this, _game, SensorDelay.Fastest);
        if (mode != TrackingMode.GyroOnly && _absolute is not null)
            _sensors.RegisterListener(this, _absolute, mode == TrackingMode.Compass ? SensorDelay.Fastest : SensorDelay.Game);
    }

    public void Stop()
    {
        _running = false;
        _sensors?.UnregisterListener(this);
    }

    /// <summary>Copies the latest device→world rotation (row-major 3x3). False until the first sample.</summary>
    public bool TryGetRotation(float[] destination9)
    {
        lock (_lock)
        {
            if (!_hasData) return false;
            Array.Copy(_latest, destination9, 9);
            return true;
        }
    }

    public void OnSensorChanged(SensorEvent? e)
    {
        var values = e?.Values;
        if (e is null || values is null || values.Count < 3) return;
        // Only pass x, y, z (and w when present): some devices report a 5th "accuracy" value
        // that older getRotationMatrixFromVector implementations reject.
        int n = Math.Min(values.Count, 4);
        float[] v = n == 4 ? _vector : new float[3];
        for (int i = 0; i < n; i++) v[i] = values[i];
        double time = e.Timestamp / 1e9;
        bool isGame = e.Sensor?.Type == SensorType.GameRotationVector;
        var mode = EffectiveMode;

        lock (_lock)
        {
            if (isGame)
            {
                SensorManager.GetRotationMatrixFromVector(_gameMatrix, v);
                _hasGame = true;
                _corrector.UpdateGame(_gameMatrix, time);
                Array.Copy(_gameMatrix, _latest, 9);
                Rotation3.ApplyYaw(_corrector.CorrectionRad, _latest);
                _hasData = true;
            }
            else
            {
                SensorManager.GetRotationMatrixFromVector(_absMatrix, v);
                if (mode == TrackingMode.Compass)
                {
                    Array.Copy(_absMatrix, _latest, 9);
                    _hasData = true;
                }
                else if (_hasGame)
                {
                    // values[4]: estimated heading accuracy in radians (-1 when unknown).
                    float headingError = values.Count > 4 ? values[4] : -1f;
                    bool reliable = _absReliable && (headingError < 0 || headingError < 0.5f);
                    _corrector.UpdateAbsolute(_absMatrix, _gameMatrix, time, reliable);
                }
            }
        }
    }

    public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy)
    {
        if (sensor?.Type == SensorType.RotationVector)
            _absReliable = accuracy is SensorStatus.AccuracyLow or SensorStatus.AccuracyMedium or SensorStatus.AccuracyHigh;
    }
}
