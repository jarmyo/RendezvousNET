using System.Text.RegularExpressions;

namespace VrView.Core;

/// <summary>What could be inferred from a file name alone. Null members mean "no tag found".</summary>
public readonly record struct FormatHints(Projection? Projection, StereoMode? Stereo, bool SwapEyes, float? FisheyeFov);

/// <summary>
/// Guesses the layout of a video from tags in its file name (e.g. "_180_LR", "_360_TB", "_SBS",
/// "180x180_3dh", "fisheye190") and, when tags are missing, from the frame's aspect ratio.
/// </summary>
public static partial class FormatDetector
{
    // e.g. "180", "vr180", "180x180", "180lr", "360tb", "360x180", "180sbs", "mono360"
    [GeneratedRegex(@"^(?:vr|mono)?(?<deg>180|360)(?:x180|x360)?(?<st>lr|rl|tb|bt|sbs|ou|mono)?$")]
    private static partial Regex DegreeToken();

    // e.g. "fisheye", "fisheye190", "fe200", "mkx200", "mkx220", "rf52" (Canon dual fisheye), "vrca220"
    [GeneratedRegex(@"^(?:fisheye|fe|mkx|vrca)(?<fov>\d{3})?$")]
    private static partial Regex FisheyeToken();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex Separators();

    public static FormatHints DetectFromName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName ?? "").ToLowerInvariant();
        string[] tokens = Separators().Split(name).Where(t => t.Length > 0).ToArray();

        Projection? projection = null;
        StereoMode? stereo = null;
        bool swap = false;
        float? fov = null;
        bool flatTag = false;

        void SetStereo(string tag)
        {
            // These tags come from the 3D-movie world (half/full SBS, half OU): flat screen content.
            if (tag is "hsbs" or "fsbs" or "halfsbs" or "fullsbs" or "htab" or "hou" or "halfou" or "fullou")
                flatTag = true;
            switch (tag)
            {
                case "lr": case "sbs": case "3dh": case "hsbs": case "fsbs": case "sidebyside":
                case "halfsbs": case "fullsbs":
                    stereo = StereoMode.SideBySide; swap = false; break;
                case "rl":
                    stereo = StereoMode.SideBySide; swap = true; break;
                case "tb": case "ou": case "3dv": case "tab": case "htab": case "hou": case "overunder":
                case "topbottom": case "halfou": case "fullou":
                    stereo = StereoMode.TopBottom; swap = false; break;
                case "bt":
                    stereo = StereoMode.TopBottom; swap = true; break;
                case "mono":
                    stereo = StereoMode.Mono; swap = false; break;
            }
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i];
            var m = DegreeToken().Match(t);
            if (m.Success)
            {
                projection = m.Groups["deg"].Value == "360" ? Projection.Sphere360 : Projection.Dome180;
                if (t.StartsWith("mono", StringComparison.Ordinal)) stereo = StereoMode.Mono;
                if (m.Groups["st"].Success) SetStereo(m.Groups["st"].Value);
                continue;
            }
            var f = FisheyeToken().Match(t);
            if (f.Success)
            {
                projection = Projection.Fisheye;
                if (f.Groups["fov"].Success && float.TryParse(f.Groups["fov"].Value, out var v) && v >= 120 && v <= 360)
                    fov = v;
                continue;
            }
            if (t == "rf52" || t == "dualfisheye")
            {
                projection = Projection.Fisheye;
                stereo ??= StereoMode.SideBySide;
                continue;
            }
            if (t is "2d" or "flat")
            {
                flatTag = true;
                continue;
            }
            // Multi-token phrases: "side by side", "over under", "top bottom".
            if (i + 2 < tokens.Length && tokens[i + 1] == "by" && t == "side" && tokens[i + 2] == "side")
            {
                stereo = StereoMode.SideBySide; i += 2; continue;
            }
            if (i + 1 < tokens.Length && ((t == "over" && tokens[i + 1] == "under") || (t == "top" && tokens[i + 1] == "bottom")))
            {
                stereo = StereoMode.TopBottom; i += 1; continue;
            }
            if (t == "3d") { stereo ??= StereoMode.SideBySide; flatTag = true; continue; }
            SetStereo(t);
        }

        if (flatTag && projection is null) projection = Projection.Flat;
        return new FormatHints(projection, stereo, swap, fov);
    }

    /// <summary>
    /// Resolves the final format from the file name and (optionally) the decoded frame size.
    /// Pass 0 for unknown dimensions.
    /// </summary>
    public static VideoFormat Detect(string fileName, int width = 0, int height = 0)
    {
        var h = DetectFromName(fileName);
        float aspect = width > 0 && height > 0 ? (float)width / height : 0f;
        static bool Near(float a, float target, float tol = 0.12f) => a > 0 && Math.Abs(a - target) <= target * tol;

        Projection projection;
        StereoMode stereo;

        if (h.Projection is { } p && h.Stereo is { } s)
        {
            projection = p; stereo = s;
        }
        else if (h.Projection is { } p2)
        {
            projection = p2;
            stereo = p2 switch
            {
                // 180 stereo is overwhelmingly side-by-side (2:1 frame of two square eyes).
                Projection.Dome180 => Near(aspect, 1f) ? StereoMode.Mono
                                    : Near(aspect, 0.5f) ? StereoMode.TopBottom
                                    : aspect == 0 || Near(aspect, 2f) ? StereoMode.SideBySide
                                    : StereoMode.Mono,
                // 360: 2:1 mono, 1:1 top-bottom, 4:1 side-by-side.
                Projection.Sphere360 => Near(aspect, 1f) ? StereoMode.TopBottom
                                      : Near(aspect, 4f) ? StereoMode.SideBySide
                                      : StereoMode.Mono,
                Projection.Fisheye => Near(aspect, 2f) ? StereoMode.SideBySide : StereoMode.Mono,
                _ => Near(aspect, 32f / 9f) ? StereoMode.SideBySide : StereoMode.Mono,
            };
        }
        else if (h.Stereo is { } s2)
        {
            stereo = s2;
            projection = s2 switch
            {
                StereoMode.SideBySide => aspect == 0 || Near(aspect, 2f, 0.05f) ? Projection.Dome180 : Projection.Flat,
                StereoMode.TopBottom => aspect == 0 || Near(aspect, 1f, 0.05f) ? Projection.Sphere360
                                      : Near(aspect, 0.5f, 0.05f) ? Projection.Dome180
                                      : Projection.Flat,
                _ => Near(aspect, 2f, 0.03f) ? Projection.Sphere360 : Projection.Flat,
            };
        }
        else
        {
            // No tags at all: only an exact 2:1 frame is a strong hint (mono 360). Everything
            // else stays flat; the user can override manually.
            if (Near(aspect, 2f, 0.01f)) { projection = Projection.Sphere360; stereo = StereoMode.Mono; }
            else if (Near(aspect, 1f, 0.01f) && width >= 2048) { projection = Projection.Sphere360; stereo = StereoMode.TopBottom; }
            else { projection = Projection.Flat; stereo = StereoMode.Mono; }
        }

        return new VideoFormat(projection, stereo, stereo != StereoMode.Mono && h.SwapEyes, h.FisheyeFov ?? 190f);
    }

    /// <summary>True when the file name carries any explicit layout tag.</summary>
    public static bool HasTags(string fileName)
    {
        var h = DetectFromName(fileName);
        return h.Projection is not null || h.Stereo is not null;
    }
}
