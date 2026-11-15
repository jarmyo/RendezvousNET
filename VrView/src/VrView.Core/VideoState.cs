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
    /// <summary>Named format profile chosen for this video (null = none).</summary>
    public string? Profile { get; set; }
    /// <summary>Horizon correction in degrees.</summary>
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public float Yaw { get; set; }

    /// <summary>Bookmarked positions ("marcas") in milliseconds, sorted.</summary>
    public List<long> Spots { get; set; } = [];

    /// <summary>Optional names of spots, by position.</summary>
    public Dictionary<long, string> SpotNames { get; set; } = [];

    /// <summary>Parts marked for removal in edit mode, sorted and not overlapping.</summary>
    public List<CutRange> Cuts { get; set; } = [];

    /// <summary>Duration of the file when the cuts were marked (0 = unknown), to tell whether they still apply.</summary>
    public long CutsDurationMs { get; set; }

    public bool HasHorizon => Pitch != 0 || Roll != 0 || Yaw != 0;

    /// <summary>True when nothing differs from a never-played video (it need not be stored).</summary>
    public bool IsEmpty => PositionMs == 0 && FormatOverride is null && string.IsNullOrEmpty(Profile) && !HasHorizon &&
                           Spots.Count == 0 && Cuts.Count == 0;

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
        if (!string.IsNullOrEmpty(Profile)) parts.Add("profile=" + Uri.EscapeDataString(Profile));
        if (Spots.Count > 0) parts.Add("spots=" + string.Join(',', Spots.Select(x => x.ToString(ci))));
        if (Cuts.Count > 0)
        {
            parts.Add("cuts=" + string.Join(',', Cuts.Select(c => c.FromMs.ToString(ci) + "-" + c.ToMs.ToString(ci))));
            if (CutsDurationMs > 0) parts.Add("cutsdur=" + CutsDurationMs.ToString(ci));
        }
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
                case "profile": s.Profile = Uri.UnescapeDataString(value); break;
                case "spots": s.Spots = SpotList.Parse(value.Split(',')); break;
                case "cuts": s.Cuts = ParseCuts(value); break;
                case "cutsdur" when long.TryParse(value, NumberStyles.Integer, ci, out var d): s.CutsDurationMs = d; break;
            }
        }
        return s;
    }

    private static List<CutRange> ParseCuts(string value)
    {
        var ci = CultureInfo.InvariantCulture;
        var list = new List<CutRange>();
        foreach (var pair in value.Split(','))
        {
            var ends = pair.Split('-');
            if (ends.Length == 2 && long.TryParse(ends[0], NumberStyles.Integer, ci, out var a) &&
                long.TryParse(ends[1], NumberStyles.Integer, ci, out var b))
                list.Add(new CutRange(a, b));
        }
        return CutList.Normalize(list);
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
