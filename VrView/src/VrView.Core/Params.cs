using System.Globalization;

namespace VrView.Core;

/// <summary>Every value that has a slider in the editor or the settings.</summary>
public enum Param
{
    // Format
    HFov, VFov, LensFov, CenterX, CenterY, Radius, Nadir,
    // Viewer and controls
    Ipd, Zoom, MenuShift, Opacity, DwellTime, EdgeAngle, SubtitleSize,
}

/// <summary>Range, step and standard value of a <see cref="Param"/>, in the units shown to the user.</summary>
public readonly record struct ParamSpec(string Label, float Min, float Max, float Step, float Standard, string Unit)
{
    public float Clamp(float v) => Math.Clamp(MathF.Round(v / Step) * Step, Min, Max);
    public float Fraction(float v) => Max > Min ? Math.Clamp((v - Min) / (Max - Min), 0f, 1f) : 0f;
    public float FromFraction(float f) => Clamp(Min + f * (Max - Min));

    public string Format(float v)
    {
        string number = Step < 1 ? v.ToString("0.##", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture);
        if (Min < 0 && v > 0) number = "+" + number;
        return number + Unit;
    }
}

public static class Params
{
    private static readonly Dictionary<Param, ParamSpec> Specs = new()
    {
        [Param.HFov] = new("Cobertura horizontal", 90, 360, 5, 180, "°"),
        [Param.VFov] = new("Cobertura vertical", 60, 180, 5, 180, "°"),
        [Param.LensFov] = new("Ángulo del lente", 120, 280, 5, 190, "°"),
        [Param.CenterX] = new("Centro ← →", -25, 25, 0.5f, 0, " %"),
        [Param.CenterY] = new("Centro ↓ ↑", -25, 25, 0.5f, 0, " %"),
        [Param.Radius] = new("Tamaño del círculo", 50, 150, 1, 100, " %"),
        [Param.Nadir] = new("Tamaño de la tapa", 10, 90, 5, 30, "°"),
        [Param.Ipd] = new("Separación de lentes", -30, 30, 1, 0, ""),
        [Param.Zoom] = new("Campo de visión", 40, 130, 5, 95, "°"),
        [Param.MenuShift] = new("Separación del menú", -20, 20, 0.5f, 0, ""),
        [Param.Opacity] = new("Opacidad", 30, 100, 10, 90, " %"),
        [Param.DwellTime] = new("Tiempo de clic con la mirada", 0.6f, 3f, 0.2f, 1.4f, " s"),
        [Param.EdgeAngle] = new("Ángulo de los botones ◀ ▶", 20, 70, 5, 40, "°"),
        [Param.SubtitleSize] = new("Tamaño de subtítulos", 50, 200, 10, 100, " %"),
    };

    public static ParamSpec Spec(Param p) => Specs[p];

    /// <summary>The standard value for this format (360 videos cover 360°).</summary>
    public static float Standard(Param p, VideoFormat f) =>
        p == Param.HFov ? f.StandardHFov : Specs[p].Standard;

    public static bool IsFormatParam(Param p) => p <= Param.Nadir;

    /// <summary>Whether a format parameter does something for this projection.</summary>
    public static bool Applies(Param p, VideoFormat f) => p switch
    {
        Param.HFov or Param.VFov => f.IsEquirect,
        Param.LensFov or Param.CenterX or Param.CenterY or Param.Radius => f.Projection == Projection.Fisheye,
        Param.Nadir => f.Projection != Projection.Flat,
        _ => true,
    };

    /// <summary>Why a parameter is dimmed, e.g. "solo ojo de pez".</summary>
    public static string NotApplicableReason(Param p) => p switch
    {
        Param.HFov or Param.VFov => "solo 180° y 360°",
        Param.LensFov or Param.CenterX or Param.CenterY or Param.Radius => "solo ojo de pez",
        Param.Nadir => "no en videos planos",
        _ => "",
    };

    /// <summary>Value of a format parameter in display units.</summary>
    public static float Get(VideoFormat f, Param p) => p switch
    {
        Param.HFov => f.EquirectHFov,
        Param.VFov => f.EquirectVFov,
        Param.LensFov => f.FisheyeFov,
        Param.CenterX => f.CenterX * 100,
        Param.CenterY => f.CenterY * 100,
        Param.Radius => f.RadiusScale * 100,
        Param.Nadir => f.NadirDeg > 0 ? f.NadirDeg : Specs[Param.Nadir].Standard,
        _ => 0,
    };

    /// <summary>Returns the format with a parameter set (display units, clamped to the range).</summary>
    public static VideoFormat Set(VideoFormat f, Param p, float value)
    {
        value = Specs[p].Clamp(value);
        return p switch
        {
            // Coverage equal to the standard is stored as 0 so the format stays "standard".
            Param.HFov => f with { HFov = MathF.Abs(value - f.StandardHFov) < 0.01f ? 0 : value },
            Param.VFov => f with { VFov = MathF.Abs(value - 180f) < 0.01f ? 0 : value },
            Param.LensFov => f with { FisheyeFov = value },
            Param.CenterX => f with { CenterX = MathF.Round(value / 100, 4) },
            Param.CenterY => f with { CenterY = MathF.Round(value / 100, 4) },
            Param.Radius => f with { RadiusScale = MathF.Round(value / 100, 3) },
            Param.Nadir => f with { NadirDeg = value },
            _ => f,
        };
    }

    public static VideoFormat Step(VideoFormat f, Param p, int direction) =>
        Set(f, p, Get(f, p) + direction * Specs[p].Step);

    public static VideoFormat Reset(VideoFormat f, Param p) => Set(f, p, Standard(p, f));

    public static bool IsStandard(VideoFormat f, Param p) =>
        p == Param.Nadir ? f.NadirDeg == 0 : MathF.Abs(Get(f, p) - Standard(p, f)) < 0.001f;

    /// <summary>The fisheye lens types in the order the editor cycles them.</summary>
    public static string LensLabel(FisheyeLens lens) => lens switch
    {
        FisheyeLens.Equisolid => "equisólida",
        FisheyeLens.Stereographic => "estereográfica",
        _ => "equidistante",
    };
}
