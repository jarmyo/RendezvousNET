using Android.Content;
using Android.Hardware;
using Android.Runtime;

namespace VrView;

/// <summary>
/// Reads the phone orientation from the game rotation vector (gyro + accelerometer fusion,
/// no magnetometer jumps) and exposes the latest rotation matrix to the GL thread.
/// </summary>
internal sealed class HeadTracker : Java.Lang.Object, ISensorEventListener
{
    private readonly SensorManager? _sensors;
    private readonly Sensor? _sensor;
    private readonly float[] _vector = new float[4];
    private readonly float[] _matrix = new float[9];
    private readonly float[] _latest = new float[9];
    private readonly object _lock = new();
    private bool _hasData;

    /// <summary>Surface.ROTATION_* value of the display (0..3); updated by the activity.</summary>
    public volatile int DisplayRotation = 1;

    public HeadTracker(Context context)
    {
        _sensors = context.GetSystemService(Context.SensorService) as SensorManager;
        _sensor = _sensors?.GetDefaultSensor(SensorType.GameRotationVector)
                  ?? _sensors?.GetDefaultSensor(SensorType.RotationVector);
    }

    public bool IsAvailable => _sensor is not null;

    public void Start()
    {
        if (_sensor is not null)
            _sensors!.RegisterListener(this, _sensor, SensorDelay.Fastest);
    }

    public void Stop()
    {
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
        if (values is null || values.Count < 3) return;
        // Only pass x, y, z (and w when present): some devices report a 5th "accuracy" value
        // that older getRotationMatrixFromVector implementations reject.
        int n = Math.Min(values.Count, 4);
        float[] v = n == 4 ? _vector : new float[3];
        for (int i = 0; i < n; i++) v[i] = values[i];
        SensorManager.GetRotationMatrixFromVector(_matrix, v);
        lock (_lock)
        {
            Array.Copy(_matrix, _latest, 9);
            _hasData = true;
        }
    }

    public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy)
    {
    }
}
