namespace VrView.Core;

/// <summary>Everything the in-VR menu, gamepad and touch UI can ask the player to do.</summary>
public enum MenuAction
{
    None,
    Seek,
    Prev, Back10, PlayPause, Fwd10, Next, VolDown, VolUp,
    Shuffle, Repeat, Format, Horizon, Recenter, Settings, Exit,
    PitchUp, PitchDown, RollLeft, RollRight, YawLeft, YawRight, ResetHorizon, Back,
    ZoomOut, ZoomIn, IpdMinus, IpdPlus, Lens, Dwell,
}

public enum MenuPage
{
    Main,
    Horizon,
    Settings,
}

/// <summary>A button rectangle in panel coordinates (0..1, origin top-left).</summary>
public readonly record struct MenuButton(MenuAction Action, float X0, float Y0, float X1, float Y1, bool Repeatable = false)
{
    public bool Contains(float u, float v) => u >= X0 && u <= X1 && v >= Y0 && v <= Y1;
}

/// <summary>
/// Layout of the gaze-controlled panel. Shared by the renderer (to draw it) and the hit test
/// (to know what the reticle is pointing at).
/// </summary>
public static class VrMenuLayout
{
    /// <summary>Panel width / height.</summary>
    public const float Aspect = 3.2f;

    public static readonly MenuButton SeekBar = new(MenuAction.Seek, 0.04f, 0.25f, 0.96f, 0.35f);

    private const float Row1Top = 0.41f, Row1Bottom = 0.65f, Row2Top = 0.71f, Row2Bottom = 0.95f;

    private static readonly Dictionary<MenuPage, MenuButton[]> Pages = new()
    {
        [MenuPage.Main] = Row(Row1Top, Row1Bottom,
                (MenuAction.Prev, false), (MenuAction.Back10, true), (MenuAction.PlayPause, false),
                (MenuAction.Fwd10, true), (MenuAction.Next, false), (MenuAction.VolDown, true), (MenuAction.VolUp, true))
            .Concat(Row(Row2Top, Row2Bottom,
                (MenuAction.Shuffle, false), (MenuAction.Repeat, false), (MenuAction.Format, false),
                (MenuAction.Horizon, false), (MenuAction.Recenter, false), (MenuAction.Settings, false), (MenuAction.Exit, false)))
            .Append(SeekBar).ToArray(),
        [MenuPage.Horizon] = Row(Row1Top, Row1Bottom,
                (MenuAction.PitchUp, true), (MenuAction.PitchDown, true), (MenuAction.RollLeft, true),
                (MenuAction.RollRight, true), (MenuAction.YawLeft, true), (MenuAction.YawRight, true))
            .Concat(Row(Row2Top, Row2Bottom,
                (MenuAction.ResetHorizon, false), (MenuAction.Recenter, false), (MenuAction.Back, false)))
            .ToArray(),
        [MenuPage.Settings] = Row(Row1Top, Row1Bottom,
                (MenuAction.ZoomOut, true), (MenuAction.ZoomIn, true), (MenuAction.IpdMinus, true),
                (MenuAction.IpdPlus, true), (MenuAction.Lens, false), (MenuAction.Dwell, false))
            .Concat(Row(Row2Top, Row2Bottom, (MenuAction.Format, false), (MenuAction.Back, false)))
            .ToArray(),
    };

    public static IReadOnlyList<MenuButton> Buttons(MenuPage page) => Pages[page];

    /// <summary>
    /// Returns the action under (u, v) and, for the seek bar, the position fraction 0..1.
    /// </summary>
    public static (MenuAction Action, float Fraction, bool Repeatable) HitTest(MenuPage page, float u, float v)
    {
        foreach (var b in Pages[page])
        {
            if (!b.Contains(u, v)) continue;
            float fraction = b.Action == MenuAction.Seek ? Math.Clamp((u - b.X0) / (b.X1 - b.X0), 0f, 1f) : 0f;
            return (b.Action, fraction, b.Repeatable);
        }
        return (MenuAction.None, 0f, false);
    }

    private static IEnumerable<MenuButton> Row(float top, float bottom, params (MenuAction Action, bool Repeat)[] items)
    {
        const float left = 0.03f, right = 0.97f, gap = 0.012f;
        float w = (right - left - gap * (items.Length - 1)) / items.Length;
        for (int i = 0; i < items.Length; i++)
        {
            float x0 = left + i * (w + gap);
            yield return new MenuButton(items[i].Action, x0, top, x0 + w, bottom, items[i].Repeat);
        }
    }
}

/// <summary>
/// "Look at it to click it": fires once the gaze rests on the same target for the dwell time,
/// and keeps firing at a faster rate for repeatable targets (seek, volume, horizon nudges).
/// </summary>
public sealed class GazeDwell
{
    private (MenuAction Action, int Key) _target;
    private float _elapsed;
    private bool _fired;
    private bool _repeatable;

    public float DwellSeconds { get; set; } = 1.4f;
    public float RepeatSeconds { get; set; } = 0.5f;

    /// <summary>0..1 progress towards the next click, for drawing the reticle ring.</summary>
    public float Progress => _target.Action == MenuAction.None || (_fired && !_repeatable) ? 0f
        : Math.Clamp(_elapsed / (_fired ? RepeatSeconds : DwellSeconds), 0f, 1f);

    /// <param name="action">What the reticle is over.</param>
    /// <param name="key">Sub-target discriminator (e.g. seek bar bucket) – a change restarts the timer.</param>
    /// <param name="repeatable">Whether holding the gaze keeps clicking.</param>
    /// <param name="dt">Seconds since the last update.</param>
    /// <returns>True when a click should be performed now.</returns>
    public bool Update(MenuAction action, int key, bool repeatable, float dt)
    {
        _repeatable = repeatable;
        if ((action, key) != _target)
        {
            _target = (action, key);
            _elapsed = 0f;
            _fired = false;
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
        if (repeatable && _elapsed >= RepeatSeconds)
        {
            _elapsed = 0f;
            return true;
        }
        return false;
    }

    public void Reset()
    {
        _target = (MenuAction.None, 0);
        _elapsed = 0f;
        _fired = false;
    }
}
