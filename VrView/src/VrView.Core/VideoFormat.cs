namespace VrView.Core;

/// <summary>How the video frame maps onto the viewer's surroundings.</summary>
public enum Projection
{
    /// <summary>Regular video shown on a virtual flat screen.</summary>
    Flat,
    /// <summary>Equirectangular 180° (VR180 / half sphere).</summary>
    Dome180,
    /// <summary>Equirectangular 360° (full sphere).</summary>
    Sphere360,
    /// <summary>Equidistant fisheye lens (typically 180°–200° field of view).</summary>
    Fisheye,
}

/// <summary>How the two eyes are packed into a single frame.</summary>
public enum StereoMode
{
    Mono,
    /// <summary>Left eye on the left half, right eye on the right half (LR / SBS / 3DH).</summary>
    SideBySide,
    /// <summary>Left eye on the top half, right eye on the bottom half (TB / OU / 3DV).</summary>
    TopBottom,
}

/// <summary>How a fisheye lens maps the angle from its axis to the distance from the image center.</summary>
public enum FisheyeLens
{
    /// <summary>r ∝ θ (most VR180 / dual fisheye lenses).</summary>
    Equidistant,
    /// <summary>r ∝ sin(θ/2) (equal-area; some action cameras).</summary>
    Equisolid,
    /// <summary>r ∝ tan(θ/2) (keeps shapes, stretches the edges).</summary>
    Stereographic,
}

/// <summary>Texture-space rectangle of one eye, in GL convention (origin at bottom-left).</summary>
public readonly record struct UvRect(float ScaleU, float ScaleV, float OffsetU, float OffsetV);

/// <summary>
/// Complete description of a video layout, including the "fine tuning" for cameras that do not
/// follow the standard exactly (wider/narrower coverage, other fisheye lenses, off-center circles)
/// and the size of the patch that hides the nadir (the pinched area under the camera).
/// </summary>
/// <param name="HFov">Horizontal coverage of an equirectangular video in degrees; 0 = standard (180 or 360).</param>
/// <param name="VFov">Vertical coverage of an equirectangular video in degrees; 0 = standard (180).</param>
/// <param name="CenterX">Fisheye circle center offset, as a fraction of the eye width (right is positive).</param>
/// <param name="CenterY">Fisheye circle center offset, as a fraction of the eye height (up is positive).</param>
/// <param name="RadiusScale">Fisheye circle radius relative to half the eye height.</param>
/// <param name="NadirDeg">Angular radius of the patch drawn under the viewer; 0 = none.</param>
public readonly record struct VideoFormat(Projection Projection, StereoMode Stereo, bool SwapEyes = false, float FisheyeFov = 190f,
    float HFov = 0f, float VFov = 0f, FisheyeLens Lens = FisheyeLens.Equidistant,
    float CenterX = 0f, float CenterY = 0f, float RadiusScale = 1f, float NadirDeg = 0f)
{
    public static VideoFormat Default => new(Projection.Flat, StereoMode.Mono);

    public float EquirectHFov => HFov > 0 ? HFov : Projection == Projection.Sphere360 ? 360f : 180f;
    public float EquirectVFov => VFov > 0 ? VFov : 180f;

    /// <summary>True when any fine-tuning value differs from the standard layout.</summary>
    public bool IsTuned => HFov > 0 || VFov > 0 || Lens != FisheyeLens.Equidistant || CenterX != 0 || CenterY != 0 ||
                           RadiusScale != 1f || NadirDeg > 0;

    /// <summary>The same layout without fine tuning.</summary>
    public VideoFormat WithoutTuning() => new(Projection, Stereo, SwapEyes, FisheyeFov);

    public bool IsStereo => Stereo != StereoMode.Mono;

    /// <summary>
    /// Returns the texture rectangle for an eye (0 = left, 1 = right). For mono content both eyes
    /// get the full frame. UV origin is bottom-left (GL), so the "top" half is v in [0.5, 1].
    /// </summary>
    public UvRect EyeRect(int eye)
    {
        bool first = (eye == 0) != SwapEyes;
        return Stereo switch
        {
            StereoMode.SideBySide => first ? new UvRect(0.5f, 1f, 0f, 0f) : new UvRect(0.5f, 1f, 0.5f, 0f),
            StereoMode.TopBottom => first ? new UvRect(1f, 0.5f, 0f, 0.5f) : new UvRect(1f, 0.5f, 0f, 0f),
            _ => new UvRect(1f, 1f, 0f, 0f),
        };
    }

    /// <summary>
    /// Display aspect ratio (width / height) of one eye's picture for flat videos.
    /// Detects "half" SBS/TB encodings (squeezed to fit a normal 16:9 frame) and un-squeezes them.
    /// </summary>
    public float EyeAspect(int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0) return 16f / 9f;
        float full = (float)videoWidth / videoHeight;
        switch (Stereo)
        {
            case StereoMode.SideBySide:
                {
                    float half = full / 2f;
                    // Half-SBS: each eye is squeezed horizontally; a 16:9 frame gives 0.89 per eye.
                    return half < 1.2f ? full : half;
                }
            case StereoMode.TopBottom:
                {
                    float half = full * 2f;
                    // Half-TB: each eye squeezed vertically; a 16:9 frame gives 3.56 per eye.
                    return half > 2.5f ? full : half;
                }
            default:
                return full;
        }
    }

    /// <summary>
    /// Part of a decoded frame (fractions, origin top-left) that makes a good flat thumbnail:
    /// one eye only, and for 180/360/fisheye the region straight ahead instead of the distorted edges.
    /// </summary>
    public (float X0, float Y0, float X1, float Y1) ThumbnailCrop()
    {
        // One eye, in image coordinates (top-left origin).
        bool first = !SwapEyes;
        var (x0, y0, x1, y1) = Stereo switch
        {
            StereoMode.SideBySide => first ? (0f, 0f, 0.5f, 1f) : (0.5f, 0f, 1f, 1f),
            StereoMode.TopBottom => first ? (0f, 0f, 1f, 0.5f) : (0f, 0.5f, 1f, 1f),
            _ => (0f, 0f, 1f, 1f),
        };
        // Centered window of that eye.
        var (fw, fh) = Projection switch
        {
            Projection.Sphere360 => (0.3f, 0.6f),
            Projection.Dome180 or Projection.Fisheye => (0.6f, 0.6f),
            _ => (1f, 1f),
        };
        float w = x1 - x0, h = y1 - y0, cx = x0 + w / 2, cy = y0 + h / 2;
        return (cx - w * fw / 2, cy - h * fh / 2, cx + w * fw / 2, cy + h * fh / 2);
    }

    /// <summary>Short user-facing (Spanish) label, e.g. "180° SBS".</summary>
    public string Label
    {
        get
        {
            string p = Projection switch
            {
                Projection.Flat => "Plano",
                Projection.Dome180 => "180°",
                Projection.Sphere360 => "360°",
                Projection.Fisheye => $"Ojo de pez {FisheyeFov:0}°",
                _ => "?",
            };
            string s = Stereo switch
            {
                StereoMode.SideBySide => SwapEyes ? "RL" : "SBS",
                StereoMode.TopBottom => SwapEyes ? "BT" : "TB",
                _ => "2D",
            };
            return IsTuned ? $"{p} {s} · ajustado" : $"{p} {s}";
        }
    }

    /// <summary>Formats offered in the manual override cycle, in order.</summary>
    public static IReadOnlyList<VideoFormat> ManualChoices { get; } = new[]
    {
        new VideoFormat(Projection.Flat, StereoMode.Mono),
        new VideoFormat(Projection.Flat, StereoMode.SideBySide),
        new VideoFormat(Projection.Flat, StereoMode.TopBottom),
        new VideoFormat(Projection.Dome180, StereoMode.Mono),
        new VideoFormat(Projection.Dome180, StereoMode.SideBySide),
        new VideoFormat(Projection.Dome180, StereoMode.TopBottom),
        new VideoFormat(Projection.Sphere360, StereoMode.Mono),
        new VideoFormat(Projection.Sphere360, StereoMode.SideBySide),
        new VideoFormat(Projection.Sphere360, StereoMode.TopBottom),
        new VideoFormat(Projection.Fisheye, StereoMode.Mono),
        new VideoFormat(Projection.Fisheye, StereoMode.SideBySide),
    };

    /// <summary>
    /// Compact, culture-invariant serialization: "Dome180,SideBySide,0,190", followed by
    /// "key=value" items only for the fine-tuning values that are set (e.g. ",h=200,nadir=30").
    /// </summary>
    public string Serialize()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new List<string> { Projection.ToString(), Stereo.ToString(), SwapEyes ? "1" : "0", FisheyeFov.ToString("0.##", ci) };
        if (HFov > 0) parts.Add("h=" + HFov.ToString("0.##", ci));
        if (VFov > 0) parts.Add("v=" + VFov.ToString("0.##", ci));
        if (Lens != FisheyeLens.Equidistant) parts.Add("lens=" + Lens);
        if (CenterX != 0) parts.Add("cx=" + CenterX.ToString("0.####", ci));
        if (CenterY != 0) parts.Add("cy=" + CenterY.ToString("0.####", ci));
        if (RadiusScale != 1f) parts.Add("r=" + RadiusScale.ToString("0.###", ci));
        if (NadirDeg > 0) parts.Add("nadir=" + NadirDeg.ToString("0.##", ci));
        return string.Join(',', parts);
    }

    public static VideoFormat? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(',');
        if (parts.Length < 2) return null;
        if (!Enum.TryParse<Projection>(parts[0], out var p)) return null;
        if (!Enum.TryParse<StereoMode>(parts[1], out var s)) return null;
        bool swap = parts.Length > 2 && parts[2] == "1";
        float fov = 190f;
        if (parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 90 && f <= 360)
            fov = f;
        var result = new VideoFormat(p, s, swap, fov);
        foreach (var item in parts.Skip(4))
        {
            int eq = item.IndexOf('=');
            if (eq <= 0) continue;
            string key = item[..eq].Trim(), value = item[(eq + 1)..].Trim();
            if (key == "lens")
            {
                if (Enum.TryParse<FisheyeLens>(value, true, out var lens)) result = result with { Lens = lens };
                continue;
            }
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var n)) continue;
            result = key switch
            {
                "h" => result with { HFov = Math.Clamp(n, 0, 360) },
                "v" => result with { VFov = Math.Clamp(n, 0, 180) },
                "cx" => result with { CenterX = Math.Clamp(n, -0.25f, 0.25f) },
                "cy" => result with { CenterY = Math.Clamp(n, -0.25f, 0.25f) },
                "r" => result with { RadiusScale = Math.Clamp(n, 0.5f, 1.5f) },
                "nadir" => result with { NadirDeg = Math.Clamp(n, 0, 90) },
                _ => result,
            };
        }
        return result;
    }
}
