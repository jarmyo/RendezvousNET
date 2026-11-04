namespace VrView.Core;

/// <summary>Everything the VR windows, gamepad and touch UI can ask the player to do.</summary>
public enum MenuAction
{
    None,
    // Transport
    Seek, Prev, Back10, PlayPause, Fwd10, Next, VolDown, VolUp, Shuffle, Repeat, Recenter, Exit,
    // Windows: Open (Arg = WindowKind), Tab (Arg = tab index), Hide (✕), Back (previous window), Done
    Open, Tab, Hide, Back, Done,
    // Horizon
    PitchUp, PitchDown, RollLeft, RollRight, YawLeft, YawRight, ResetHorizon, SetHorizon,
    // Spots
    SpotAdd, SpotPrev, SpotNext, SpotPagePrev, SpotPageNext, SpotDeleteMode, SpotSlot,
    // Format editor (Arg = Projection / StereoMode / Param / FormatScope)
    FormatProjection, FormatEyes, FormatSwap, FormatLens, ParamSet, ParamMinus, ParamPlus, ParamReset,
    NadirToggle, HorizonToggle, FormatScope, FormatAuto,
    // Settings (Arg = choice index)
    LensLevel, GazeClick, ControlsSize, EdgeButtons, EdgeFollowHead, LookDownMenu, StereoView, TrackingMode,
    Calibrate, AutoRecenter, Quality, MeshDetail, ProfileUse, ProfileDelete, ClearVideo, ClearFolder,
    CalibrationStep, ViewerReset, ShowFps,
    // Library (Arg = folder index / filter / sort / slot)
    LibraryFolder, LibraryFilter, LibrarySort, LibraryItem, LibraryPagePrev, LibraryPageNext, LibraryContinue,
    // Tracks (Arg = track index, -1 = off)
    AudioTrack, SubtitleTrack,
}

/// <summary>The windows of the VR interface (and of the touch overlay).</summary>
public enum WindowKind { Player, Format, Spots, Info, Settings, Library, Horizon, Calibration, Tracks }

/// <summary>How an element looks and reacts.</summary>
public enum UiKind
{
    /// <summary>Plain button.</summary>
    Button,
    /// <summary>One choice of a group (projection, eyes, tab-like choices); Active = selected.</summary>
    Tile,
    /// <summary>On/off switch; Active = on.</summary>
    Toggle,
    /// <summary>Track with a knob; Value and Mark (standard) are 0..1 fractions.</summary>
    Slider,
    /// <summary>Settings tab; Active = current.</summary>
    Tab,
    /// <summary>Static text (Text) with an optional dimmer second line (Detail).</summary>
    Text,
    /// <summary>Section heading.</summary>
    Section,
    /// <summary>Key (Text) / value (Detail) row.</summary>
    Info,
    /// <summary>Thumbnail cell: picture of <see cref="UiElement.Ms"/> or of a library item, with a caption.</summary>
    Thumb,
    /// <summary>The seek bar of the player; Value = position fraction.</summary>
    Seek,
    /// <summary>Picture of the raw decoded frame with format guides (format editor).</summary>
    Preview,
}

/// <summary>A rectangle in window coordinates (0..1, origin top-left).</summary>
public readonly record struct UiRect(float X0, float Y0, float X1, float Y1)
{
    public bool Contains(float u, float v) => u >= X0 && u <= X1 && v >= Y0 && v <= Y1;
    public float Width => X1 - X0;
    public float Height => Y1 - Y0;
}

/// <summary>One thing drawn on a window, possibly clickable.</summary>
public sealed record UiElement(UiKind Kind, MenuAction Action, UiRect Rect)
{
    public string Text { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Active { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Repeatable { get; init; }
    /// <summary>Accelerating auto-repeat (− / + of sliders).</summary>
    public bool Accelerate { get; init; }
    public int Arg { get; init; }
    /// <summary>Slider knob / seek position, 0..1.</summary>
    public float Value { get; init; }
    /// <summary>Slider standard value, 0..1 (negative = none).</summary>
    public float Mark { get; init; } = -1;
    /// <summary>Thumbnail time (ms) for spots; for library items the item index.</summary>
    public long Ms { get; init; } = -1;
    /// <summary>Red highlight (delete actions).</summary>
    public bool Danger { get; init; }
    /// <summary>Spot positions as fractions, drawn on the seek bar.</summary>
    public float[]? Ticks { get; init; }

    public bool Clickable => Action != MenuAction.None && Enabled;
}

/// <summary>What the reticle or a finger points at.</summary>
public readonly record struct UiHit(MenuAction Action, float Fraction, bool Repeatable, int Arg, int Index, bool Accelerate = false)
{
    public static UiHit None => new(MenuAction.None, 0f, false, 0, -1);

    /// <summary>Distinguishes sub-targets for the dwell timer (seek / slider position buckets, slots).</summary>
    public int DwellKey => Action is MenuAction.Seek or MenuAction.ParamSet ? Arg * 1000 + (int)(Fraction * 40) : Arg;
}

/// <summary>A window: title, subtitle and elements laid out in a <see cref="Aspect"/> (width / height) rectangle.</summary>
public sealed record UiWindow(WindowKind Kind, string Title, string Subtitle, float Aspect, IReadOnlyList<UiElement> Elements)
{
    /// <summary>Width in meters at the panel distance (2 m).</summary>
    public float Width { get; init; } = 2.2f;

    /// <summary>Square "✕" (11 cm at 2 m) in the top-right corner of a window.</summary>
    public static UiRect CloseRect(float aspect, float widthMeters)
    {
        float w = 0.11f / widthMeters, h = 0.11f * aspect / widthMeters;
        return new UiRect(0.985f - w, 0.02f, 0.985f, 0.02f + h);
    }

    public UiHit HitTest(float u, float v)
    {
        for (int i = 0; i < Elements.Count; i++)
        {
            var e = Elements[i];
            if (!e.Clickable || !e.Rect.Contains(u, v)) continue;
            float fraction = e.Kind is UiKind.Slider or UiKind.Seek ? Math.Clamp((u - e.Rect.X0) / e.Rect.Width, 0f, 1f) : 0f;
            return new UiHit(e.Action, fraction, e.Repeatable, e.Arg, i, e.Accelerate);
        }
        return UiHit.None;
    }

    public UiElement? Find(MenuAction action, int arg = 0) =>
        Elements.FirstOrDefault(e => e.Action == action && e.Arg == arg);
}

/// <summary>
/// "Look at it to click it": fires once the gaze rests on the same target for the dwell time,
/// and keeps firing for repeatable targets — faster and faster for accelerating ones (slider − / +).
/// </summary>
public sealed class GazeDwell
{
    private (MenuAction Action, int Key) _target;
    private float _elapsed;
    private bool _fired;
    private bool _repeatable, _accelerate;
    private int _repeats;

    public float DwellSeconds { get; set; } = 1.4f;
    public float RepeatSeconds { get; set; } = 0.5f;

    private float Interval => !_fired ? DwellSeconds
        : _accelerate ? RepeatSeconds / (_repeats >= 12 ? 5f : _repeats >= 4 ? 2.5f : 1f)
        : RepeatSeconds;

    /// <summary>0..1 progress towards the next click, for drawing the reticle ring.</summary>
    public float Progress => _target.Action == MenuAction.None || (_fired && !_repeatable) ? 0f
        : Math.Clamp(_elapsed / Interval, 0f, 1f);

    /// <param name="action">What the reticle is over.</param>
    /// <param name="key">Sub-target discriminator (e.g. seek bar bucket) – a change restarts the timer.</param>
    /// <param name="repeatable">Whether holding the gaze keeps clicking.</param>
    /// <param name="dt">Seconds since the last update.</param>
    /// <param name="accelerate">Repeat faster the longer the gaze stays.</param>
    /// <returns>True when a click should be performed now.</returns>
    public bool Update(MenuAction action, int key, bool repeatable, float dt, bool accelerate = false)
    {
        _repeatable = repeatable;
        _accelerate = accelerate;
        if ((action, key) != _target)
        {
            _target = (action, key);
            _elapsed = 0f;
            _fired = false;
            _repeats = 0;
            return false;
        }
        if (action == MenuAction.None) return false;
        _elapsed += dt;
        if (!_fired)
        {
            if (_elapsed < DwellSeconds) return false;
            _fired = true;
            _elapsed = 0f;
            return true;
        }
        if (repeatable && _elapsed >= Interval)
        {
            _elapsed = 0f;
            _repeats++;
            return true;
        }
        return false;
    }

    public void Reset()
    {
        _target = (MenuAction.None, 0);
        _elapsed = 0f;
        _fired = false;
        _repeats = 0;
    }
}
