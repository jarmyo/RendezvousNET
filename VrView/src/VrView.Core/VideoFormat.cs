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

/// <summary>Texture-space rectangle of one eye, in GL convention (origin at bottom-left).</summary>
public readonly record struct UvRect(float ScaleU, float ScaleV, float OffsetU, float OffsetV);

/// <summary>Complete description of a video layout.</summary>
public readonly record struct VideoFormat(Projection Projection, StereoMode Stereo, bool SwapEyes = false, float FisheyeFov = 190f)
{
    public static VideoFormat Default => new(Projection.Flat, StereoMode.Mono);

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
            return $"{p} {s}";
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

    /// <summary>Compact, culture-invariant serialization ("Dome180,SideBySide,0,190").</summary>
    public string Serialize() =>
        string.Join(',', Projection, Stereo, SwapEyes ? "1" : "0",
            FisheyeFov.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
        return new VideoFormat(p, s, swap, fov);
    }
}
