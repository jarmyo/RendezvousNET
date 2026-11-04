namespace VrView.Core;

/// <summary>Global viewer and control options (shown in Ajustes, used by the renderer).</summary>
public sealed record ViewSettings(
    bool StereoView = true,
    float Fov = 95f,
    float IpdShift = 0f,
    /// <summary>0 none, 1 mild, 2 strong, 3 from the scanned viewer profile.</summary>
    int LensLevel = 0,
    bool GazeClick = true,
    float DwellSeconds = 1.4f,
    int ControlsSize = 1,
    float Opacity = 0.9f,
    float MenuShift = 0f,
    bool HighQuality = true,
    bool DenseMesh = false,
    float EdgeButtonsAngle = 40f,
    bool EdgeButtonsOn = true,
    bool EdgeFollowHead = false,
    bool LookDownMenu = true,
    TrackingMode Tracking = TrackingMode.AntiDrift,
    bool AutoRecenter = true,
    float SubtitleSize = 100f,
    bool ShowFps = false,
    string? ViewerName = null,
    float ViewerK1 = 0f,
    float ViewerK2 = 0f)
{
    /// <summary>Scale of the VR windows (1 = the original size).</summary>
    public float MenuScale => ControlsSize switch { 0 => 0.6f, 1 => 0.8f, _ => 1f };

    public (float K1, float K2) LensCoefficients => LensLevel switch
    {
        1 => (0.22f, 0.24f),   // mild, for most plastic viewers
        2 => (0.34f, 0.55f),   // strong, close to Cardboard v2 lenses
        3 => (ViewerK1, ViewerK2),
        _ => (0f, 0f),
    };
}

/// <summary>The status line of the player.</summary>
public sealed record PlayerStatus(
    string Title,
    long PositionMs,
    long DurationMs,
    bool Playing,
    int VolumePercent,
    int Index,
    int Count,
    string FormatText,
    string SensorText,
    float Fps);

/// <summary>Everything the format editor shows.</summary>
public sealed record FormatEditorModel(
    string FileName,
    FormatChoice Choice,
    FormatScope Scope,
    string? FolderProfile,
    bool HasTags,
    float Pitch,
    float Roll,
    float Yaw,
    bool HasEmbeddedMesh)
{
    public VideoFormat Format => Choice.Format;
    public bool HasHorizon => Pitch != 0 || Roll != 0 || Yaw != 0;
}

/// <summary>Spots of the current video. Arrays keep their instance until the spots change.</summary>
public sealed record SpotsModel(long[] Spots, string[] Names, int Page, bool DeleteMode, int ThumbsVersion)
{
    public const int PerPage = 5;
    public static SpotsModel Empty => new([], [], 0, false, 0);
    public int PageCount => Math.Max(1, (Spots.Length + PerPage - 1) / PerPage);
}

/// <summary>One video in the library grid.</summary>
public sealed record LibraryEntry(int Index, string Name, string FormatLabel, string Detail, float Progress, bool Current);

public sealed record LibraryModel(
    string[] Folders,
    int Folder,
    int Filter,
    int Sort,
    int Page,
    int PageCount,
    int Total,
    LibraryEntry[] Items,
    string? ContinueName,
    bool Shuffle,
    RepeatMode Repeat,
    bool Loading,
    int ThumbsVersion)
{
    public const int PerPage = 8;
    public static readonly string[] FilterLabels = ["Todos", "180°", "360°", "Plano", "Sin ver", "Con marcas"];
    public static readonly string[] SortLabels = ["nombre", "avance", "tamaño"];
    public static LibraryModel Empty => new([], 0, 0, 0, 0, 1, 0, [], null, false, RepeatMode.All, false, 0);
}

public sealed record TracksModel(string[] Audio, int SelectedAudio, string[] Subtitles, int SelectedSubtitle)
{
    public static TracksModel Empty => new([], -1, [], -1);
}

/// <summary>Everything the windows are built from.</summary>
public sealed record UiState(
    WindowKind Window,
    int Tab,
    PlayerStatus Player,
    float[] SpotTicks,
    FormatEditorModel? Format,
    SpotsModel Spots,
    ViewSettings Settings,
    TrackingStatus Tracking,
    string[] Info,
    LibraryModel Library,
    TracksModel Tracks,
    string[] Profiles,
    string Storage,
    int CalibrationStep);
