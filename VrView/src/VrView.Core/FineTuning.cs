namespace VrView.Core;

/// <summary>What each button of the "Ajuste fino" page does to a <see cref="VideoFormat"/>.</summary>
public static class FineTuning
{
    public const float FovStep = 5f;
    public const float CenterStep = 0.005f;
    public const float RadiusStep = 0.01f;

    /// <summary>Nadir patch sizes offered by the cycle button (angular radius, 0 = off).</summary>
    public static readonly float[] NadirSizes = [0f, 20f, 30f, 40f, 50f];

    /// <summary>Whether a tuning button does something for this projection (others are shown dimmed).</summary>
    public static bool Applies(MenuAction action, Projection projection) => action switch
    {
        MenuAction.TuneHMinus or MenuAction.TuneHPlus => projection != Projection.Flat,
        MenuAction.TuneVMinus or MenuAction.TuneVPlus => projection is Projection.Dome180 or Projection.Sphere360,
        MenuAction.TuneLeft or MenuAction.TuneRight or MenuAction.TuneUp or MenuAction.TuneDown or
            MenuAction.TuneRadiusMinus or MenuAction.TuneRadiusPlus or MenuAction.TuneLens => projection == Projection.Fisheye,
        _ => true,
    };

    /// <summary>Returns the adjusted format, or null when the button does not apply.</summary>
    public static VideoFormat? Apply(VideoFormat f, MenuAction action)
    {
        if (!Applies(action, f.Projection)) return null;
        bool fisheye = f.Projection == Projection.Fisheye;
        return action switch
        {
            // "Campo H": the fisheye field of view, or the horizontal coverage of an equirectangular video.
            MenuAction.TuneHMinus or MenuAction.TuneHPlus when fisheye =>
                f with { FisheyeFov = Math.Clamp(f.FisheyeFov + Sign(action, MenuAction.TuneHPlus) * FovStep, 120f, 280f) },
            MenuAction.TuneHMinus or MenuAction.TuneHPlus =>
                f with { HFov = Standard(Math.Clamp(f.EquirectHFov + Sign(action, MenuAction.TuneHPlus) * FovStep, 90f, 360f), f.Projection == Projection.Sphere360 ? 360f : 180f) },
            MenuAction.TuneVMinus or MenuAction.TuneVPlus =>
                f with { VFov = Standard(Math.Clamp(f.EquirectVFov + Sign(action, MenuAction.TuneVPlus) * FovStep, 60f, 180f), 180f) },
            // Move the picture: the sampled circle center goes the opposite way.
            MenuAction.TuneLeft => f with { CenterX = Round(Math.Clamp(f.CenterX + CenterStep, -0.25f, 0.25f)) },
            MenuAction.TuneRight => f with { CenterX = Round(Math.Clamp(f.CenterX - CenterStep, -0.25f, 0.25f)) },
            MenuAction.TuneUp => f with { CenterY = Round(Math.Clamp(f.CenterY - CenterStep, -0.25f, 0.25f)) },
            MenuAction.TuneDown => f with { CenterY = Round(Math.Clamp(f.CenterY + CenterStep, -0.25f, 0.25f)) },
            MenuAction.TuneRadiusMinus => f with { RadiusScale = Round(Math.Clamp(f.RadiusScale - RadiusStep, 0.5f, 1.5f)) },
            MenuAction.TuneRadiusPlus => f with { RadiusScale = Round(Math.Clamp(f.RadiusScale + RadiusStep, 0.5f, 1.5f)) },
            MenuAction.TuneLens => f with { Lens = (FisheyeLens)(((int)f.Lens + 1) % 3) },
            MenuAction.TuneNadir => f with { NadirDeg = NadirSizes[(Array.IndexOf(NadirSizes, f.NadirDeg) + 1) % NadirSizes.Length] },
            MenuAction.TuneReset => f.WithoutTuning() with { FisheyeFov = 190f },
            _ => null,
        };
    }

    public static string LensLabel(FisheyeLens lens) => lens switch
    {
        FisheyeLens.Equisolid => "equisólida",
        FisheyeLens.Stereographic => "estereográfica",
        _ => "equidistante",
    };

    private static float Sign(MenuAction action, MenuAction plus) => action == plus ? 1f : -1f;

    // Storing 0 for the standard value keeps the saved format short.
    private static float Standard(float value, float standard) => MathF.Abs(value - standard) < 0.01f ? 0f : value;

    private static float Round(float v) => MathF.Round(v, 4);
}
