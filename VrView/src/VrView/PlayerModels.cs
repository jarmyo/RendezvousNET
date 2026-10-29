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
    bool GazeClick)
{
    public static RenderConfig Default => new(VideoFormat.Default, 0, 0, 0, 0, 0, true, 95f, 0f, 0, true);

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
    string FormatLabel,
    bool FormatAuto,
    float Pitch,
    float Roll,
    float Yaw,
    float Fov,
    float IpdShift,
    int LensLevel,
    bool GazeClick,
    int Index,
    int Count)
{
    public static MenuModel Empty => new("", 0, 0, false, false, RepeatMode.All, 0, "", true, 0, 0, 0, 95, 0, 0, true, 0, 0);
}

/// <summary>Spanish labels shared by the VR panel and the touch overlay.</summary>
internal static class Labels
{
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
        _ => "",
    };

    public static string Header(MenuPage page, MenuModel m) => page switch
    {
        MenuPage.Horizon => $"Horizonte · inclinación {m.Pitch:+0.0;-0.0;0}° · nivel {m.Roll:+0.0;-0.0;0}° · giro {m.Yaw:+0;-0;0}°",
        MenuPage.Settings => $"Ajustes · campo de visión {m.Fov:0}° · separación lentes {m.IpdShift * 100:+0;-0;0}",
        _ => $"{MediaUtil.FormatTime(m.PositionMs)} / {MediaUtil.FormatTime(m.DurationMs)}   ·   {m.FormatLabel}{(m.FormatAuto ? " (auto)" : "")}   ·   Vol {m.VolumePercent}%   ·   {m.Index + 1}/{m.Count}",
    };
}
