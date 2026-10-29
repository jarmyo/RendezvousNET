using VrView.Core;

namespace VrView;

/// <summary>
/// Immutable rendering parameters. The UI thread replaces the whole record; the GL thread reads it
/// once per frame, so no locking is needed.
/// </summary>
internal sealed record RenderConfig(
    VideoFormat Format,
    int VideoWidth,
    int VideoHeight,
    float Pitch,
    float Roll,
    float Yaw,
    bool StereoView,
    float Fov,
    float IpdShift,
    int LensLevel,
    bool GazeClick,
    float MenuScale,
    float MenuOpacity)
{
    public static RenderConfig Default => new(VideoFormat.Default, 0, 0, 0, 0, 0, true, 95f, 0f, 0, true, ControlSizes.MenuScale(1), 0.9f);

    /// <summary>Lens pre-distortion coefficients (k1, k2) for each "Lente" level.</summary>
    public (float K1, float K2) LensCoefficients => LensLevel switch
    {
        1 => (0.22f, 0.24f),   // mild, for most plastic viewers
        2 => (0.34f, 0.55f),   // strong, close to Cardboard v2 lenses
        _ => (0f, 0f),
    };
}

/// <summary>Snapshot of player state shown on the in-VR menu panel.</summary>
internal sealed record MenuModel(
    string Title,
    long PositionMs,
    long DurationMs,
    bool Playing,
    bool Shuffle,
    RepeatMode Repeat,
    int VolumePercent,
    VideoFormat Format,
    string FormatLabel,
    // Where the format comes from: "auto", "carpeta" or "" (chosen for this video).
    string FormatNote,
    float Pitch,
    float Roll,
    float Yaw,
    float Fov,
    float IpdShift,
    int LensLevel,
    bool GazeClick,
    int Index,
    int Count,
    int ControlsSize,
    float Opacity,
    // Same array instance until the spots change, so record equality stays cheap.
    long[] Spots,
    int SpotPage,
    bool SpotDeleteMode,
    int ThumbsVersion)
{
    public static MenuModel Empty => new("", 0, 0, false, false, RepeatMode.All, 0, VideoFormat.Default, "", "auto", 0, 0, 0, 95, 0, 0, true, 0, 0, 1,
        0.9f, [], 0, false, 0);

    public int SpotPageCount => Math.Max(1, (Spots.Length + VrMenuLayout.SpotsPerPage - 1) / VrMenuLayout.SpotsPerPage);

    public string FormatText => FormatNote.Length > 0 ? $"{FormatLabel} ({FormatNote})" : FormatLabel;
}

/// <summary>The "Controles: chicos / medianos / grandes" setting, shared by the VR panel and the touch overlay.</summary>
internal static class ControlSizes
{
    public const int Count = 3;

    /// <summary>Scale of the in-VR panel (1 = the original size).</summary>
    public static float MenuScale(int size) => size switch { 0 => 0.6f, 1 => 0.8f, _ => 1f };

    public static string Label(int size) => size switch { 0 => "chicos", 1 => "medianos", _ => "grandes" };

    private static readonly float[] Opacities = [1f, 0.8f, 0.6f, 0.4f];

    /// <summary>Next value of the "Opacidad" cycle: 100 → 80 → 60 → 40 → 100 %.</summary>
    public static float NextOpacity(float current)
    {
        int i = Array.FindIndex(Opacities, o => MathF.Abs(o - current) < 0.05f);
        return Opacities[(i + 1) % Opacities.Length];
    }
}

/// <summary>Spanish labels shared by the VR panel and the touch overlay.</summary>
internal static class Labels
{
    private static string TuneHeader(VideoFormat f) => f.Projection switch
    {
        Projection.Fisheye =>
            $"Ajuste fino · ojo de pez {f.FisheyeFov:0}° {FineTuning.LensLabel(f.Lens)} · centro {f.CenterX * 100:+0.0;-0.0;0}/{f.CenterY * 100:+0.0;-0.0;0} % · círculo {f.RadiusScale * 100:0} %",
        Projection.Dome180 or Projection.Sphere360 =>
            $"Ajuste fino · {f.Label} · cubre {f.EquirectHFov:0}° × {f.EquirectVFov:0}°",
        _ => "Ajuste fino · video plano: solo aplica la tapa de abajo",
    };

    public static string For(MenuAction a, MenuModel m) => a switch
    {
        MenuAction.Prev => "|◀ Anterior",
        MenuAction.Back10 => "◀◀ 10 s",
        MenuAction.PlayPause => m.Playing ? "II Pausa" : "▶ Reproducir",
        MenuAction.Fwd10 => "10 s ▶▶",
        MenuAction.Next => "Siguiente ▶|",
        MenuAction.VolDown => "Vol −",
        MenuAction.VolUp => "Vol +",
        MenuAction.Shuffle => m.Shuffle ? "Aleatorio: sí" : "Aleatorio: no",
        MenuAction.Repeat => Playlist.RepeatLabel(m.Repeat),
        MenuAction.Format => "Formato",
        MenuAction.Horizon => "Horizonte",
        MenuAction.Recenter => "Recentrar",
        MenuAction.Settings => "Ajustes",
        MenuAction.Exit => "Salir",
        MenuAction.PitchUp => "Inclinar ↑",
        MenuAction.PitchDown => "Inclinar ↓",
        MenuAction.RollLeft => "Nivelar ↺",
        MenuAction.RollRight => "Nivelar ↻",
        MenuAction.YawLeft => "Girar ←",
        MenuAction.YawRight => "Girar →",
        MenuAction.ResetHorizon => "Restablecer",
        MenuAction.Back => "Volver",
        MenuAction.ZoomOut => "Zoom −",
        MenuAction.ZoomIn => "Zoom +",
        MenuAction.IpdMinus => "Lentes −",
        MenuAction.IpdPlus => "Lentes +",
        MenuAction.Lens => m.LensLevel switch { 1 => "Distorsión: suave", 2 => "Distorsión: fuerte", _ => "Distorsión: no" },
        MenuAction.Dwell => m.GazeClick ? "Clic mirando: sí" : "Clic mirando: no",
        MenuAction.SetHorizon => "Fijar con la cabeza",
        MenuAction.MenuSize => "Controles: " + ControlSizes.Label(m.ControlsSize),
        MenuAction.Hide => "✕",
        MenuAction.Spots => m.Spots.Length > 0 ? $"Marcas ({m.Spots.Length})" : "Marcas",
        MenuAction.SpotAdd => "＋ Marca",
        MenuAction.SpotPrev => "◀ Marca",
        MenuAction.SpotNext => "Marca ▶",
        MenuAction.SpotPagePrev => "◀ Pág.",
        MenuAction.SpotPageNext => "Pág. ▶",
        MenuAction.SpotDeleteMode => m.SpotDeleteMode ? "Borrar: sí" : "Borrar: no",
        MenuAction.Opacity => $"Opacidad: {m.Opacity * 100:0}%",
        MenuAction.Tune => "Ajuste fino",
        MenuAction.TuneHMinus => "Campo H −",
        MenuAction.TuneHPlus => "Campo H +",
        MenuAction.TuneVMinus => "Campo V −",
        MenuAction.TuneVPlus => "Campo V +",
        MenuAction.TuneLeft => "Imagen ←",
        MenuAction.TuneRight => "Imagen →",
        MenuAction.TuneUp => "Imagen ↑",
        MenuAction.TuneDown => "Imagen ↓",
        MenuAction.TuneRadiusMinus => "Círculo −",
        MenuAction.TuneRadiusPlus => "Círculo +",
        MenuAction.TuneLens => "Lente: " + FineTuning.LensLabel(m.Format.Lens),
        MenuAction.TuneNadir => m.Format.NadirDeg > 0 ? $"Tapa abajo: {m.Format.NadirDeg:0}°" : "Tapa abajo: no",
        MenuAction.TuneReset => "Restablecer",
        _ => "",
    };

    public static string Header(MenuPage page, MenuModel m) => page switch
    {
        MenuPage.Horizon => $"Horizonte · inclinación {m.Pitch:+0.0;-0.0;0}° · nivel {m.Roll:+0.0;-0.0;0}° · giro {m.Yaw:+0;-0;0}°",
        MenuPage.Tune => TuneHeader(m.Format),
        MenuPage.Spots => m.Spots.Length == 0
            ? "Marcas · aún no hay: pulsa «＋ Marca» para guardar el momento actual"
            : $"Marcas · {m.Spots.Length} · página {m.SpotPage + 1}/{m.SpotPageCount}" +
              (m.SpotDeleteMode ? " · mira una marca para BORRARLA" : " · mira una marca para saltar a ella"),
        MenuPage.Settings => $"Ajustes · campo de visión {m.Fov:0}° · separación lentes {m.IpdShift * 100:+0;-0;0}",
        _ => $"{MediaUtil.FormatTime(m.PositionMs)} / {MediaUtil.FormatTime(m.DurationMs)}   ·   {m.FormatText}   ·   Vol {m.VolumePercent}%   ·   {m.Index + 1}/{m.Count}",
    };
}
