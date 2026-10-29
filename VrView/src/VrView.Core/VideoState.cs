using System.Globalization;

namespace VrView.Core;

/// <summary>
/// Per-video remembered state: resume position, manual format override and horizon correction.
/// Serialized to a short "key=value;..." string (no reflection, trimming-safe).
/// </summary>
public sealed class VideoState
{
    public long PositionMs { get; set; }
    public VideoFormat? FormatOverride { get; set; }
    /// <summary>Horizon correction in degrees.</summary>
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public float Yaw { get; set; }

    public bool HasHorizon => Pitch != 0 || Roll != 0 || Yaw != 0;

    public string Serialize()
    {
        var ci = CultureInfo.InvariantCulture;
        var parts = new List<string>
        {
            "pos=" + PositionMs.ToString(ci),
            "pitch=" + Pitch.ToString("0.##", ci),
            "roll=" + Roll.ToString("0.##", ci),
            "yaw=" + Yaw.ToString("0.##", ci),
        };
        if (FormatOverride is { } f) parts.Add("fmt=" + f.Serialize());
        return string.Join(';', parts);
    }

    public static VideoState Parse(string? text)
    {
        var s = new VideoState();
        if (string.IsNullOrEmpty(text)) return s;
        var ci = CultureInfo.InvariantCulture;
        foreach (var part in text.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq], value = part[(eq + 1)..];
            switch (key)
            {
                case "pos" when long.TryParse(value, NumberStyles.Integer, ci, out var p): s.PositionMs = p; break;
                case "pitch" when float.TryParse(value, NumberStyles.Float, ci, out var v): s.Pitch = v; break;
                case "roll" when float.TryParse(value, NumberStyles.Float, ci, out var v): s.Roll = v; break;
                case "yaw" when float.TryParse(value, NumberStyles.Float, ci, out var v): s.Yaw = v; break;
                case "fmt": s.FormatOverride = VideoFormat.TryParse(value); break;
            }
        }
        return s;
    }

    /// <summary>Clamps an angle to (-180, 180].</summary>
    public static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees <= -180f) degrees += 360f;
        else if (degrees > 180f) degrees -= 360f;
        return degrees;
    }
}
