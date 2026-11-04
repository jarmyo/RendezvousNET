using VrView.Core;

namespace VrView;

/// <summary>
/// Immutable rendering parameters. The UI thread replaces the whole record; the GL thread reads it
/// once per frame, so no locking is needed.
/// </summary>
/// <param name="CalibrationStep">0 = off; 1–3 = which target the calibration wizard shows.</param>
/// <param name="EmbeddedLeft">Projection mesh read from the file (left eye or both), or null.</param>
internal sealed record RenderConfig(
    VideoFormat Format,
    int VideoWidth,
    int VideoHeight,
    float Pitch,
    float Roll,
    float Yaw,
    ViewSettings View,
    int CalibrationStep = 0,
    Mesh? EmbeddedLeft = null,
    Mesh? EmbeddedRight = null,
    string? Subtitle = null)
{
    public static RenderConfig Default => new(VideoFormat.Default, 0, 0, 0, 0, 0, new ViewSettings());
}
