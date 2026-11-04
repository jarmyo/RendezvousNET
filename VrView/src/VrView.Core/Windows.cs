using System.Globalization;

namespace VrView.Core;

/// <summary>
/// Builds every window of the interface from a <see cref="UiState"/>. The same windows are drawn on
/// a panel inside the headset and on the touch screen, so both always offer the same functions.
/// Layout is in window fractions; the header band (title + subtitle) is 0.14 m tall.
/// </summary>
public static class Windows
{
    public const float HeaderMeters = 0.14f;
    public static string[] SettingsTabs => [T("Viewer"), T("Controls"), T("Tracking"), T("Quality"), T("Data")];
    public static string[] LensLevels => [T("None"), T("Mild"), T("Strong"), T("QR profile")];

    public static UiWindow Build(UiState s) => s.Window switch
    {
        WindowKind.Format when s.Format is not null => Format(s, s.Format),
        WindowKind.Spots => Spots(s),
        WindowKind.Info => Info(s),
        WindowKind.Settings => Settings(s),
        WindowKind.Library => Library(s),
        WindowKind.Horizon => Horizon(s),
        WindowKind.Tracks => Tracks(s),
        WindowKind.Calibration => Calibration(s),
        _ => Player(s),
    };

    /// <summary>True when two windows draw exactly the same (so the panel need not be repainted).</summary>
    public static bool SameContent(UiWindow? a, UiWindow? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Kind != b.Kind || a.Title != b.Title || a.Subtitle != b.Subtitle ||
            a.Aspect != b.Aspect || a.Width != b.Width || a.Elements.Count != b.Elements.Count) return false;
        for (int i = 0; i < a.Elements.Count; i++)
        {
            var x = a.Elements[i];
            var y = b.Elements[i];
            if (x == y) continue;
            if (x with { Ticks = null } != y with { Ticks = null }) return false;
            if (!(x.Ticks ?? []).AsSpan().SequenceEqual(y.Ticks ?? [])) return false;
        }
        return true;
    }

    // ---- Player ----

    private static UiWindow Player(UiState s)
    {
        var p = s.Player;
        const float aspect = 3.2f, width = 2.2f;
        var e = new List<UiElement>
        {
            new(UiKind.Seek, MenuAction.Seek, new UiRect(0.04f, 0.25f, 0.96f, 0.35f))
            {
                Value = p.DurationMs > 0 ? Math.Clamp((float)p.PositionMs / p.DurationMs, 0f, 1f) : 0f,
                Ticks = s.SpotTicks,
            },
        };
        e.AddRange(Row(0.41f, 0.65f, 0.03f, 0.97f,
            Btn(MenuAction.Prev, T("|◀ Previous")), Btn(MenuAction.Back10, "◀◀ 10 s", repeat: true),
            Btn(MenuAction.PlayPause, p.Playing ? T("II Pause") : T("▶ Play")), Btn(MenuAction.Fwd10, "10 s ▶▶", repeat: true),
            Btn(MenuAction.Next, T("Next ▶|")), Btn(MenuAction.SpotAdd, T("＋ Bookmark")),
            Btn(MenuAction.VolDown, "Vol −", repeat: true), Btn(MenuAction.VolUp, "Vol +", repeat: true)));
        e.AddRange(Row(0.71f, 0.95f, 0.03f, 0.97f,
            Open(WindowKind.Spots, s.Spots.Spots.Length > 0 ? F("Bookmarks ({0})", s.Spots.Spots.Length) : T("Bookmarks")),
            Open(WindowKind.Format, T("Format")), Open(WindowKind.Horizon, T("Horizon")), Open(WindowKind.Tracks, T("Audio & subs")),
            Open(WindowKind.Info, T("Info")), Open(WindowKind.Library, T("Library")), Open(WindowKind.Settings, T("⚙ Settings")),
            Btn(MenuAction.Exit, T("Exit"))));
        e.Add(Close(aspect, width));
        string fps = s.Settings.ShowFps && p.Fps > 0 ? $"   ·   {p.Fps:0.#} fps" : "";
        string subtitle = $"{MediaUtil.FormatTime(p.PositionMs)} / {MediaUtil.FormatTime(p.DurationMs)}   ·   {p.FormatText}   ·   " +
                          $"{p.SensorText}   ·   Vol {p.VolumePercent}%   ·   {F("{0} of {1}", p.Index + 1, p.Count)}{fps}";
        return new UiWindow(WindowKind.Player, p.Title, subtitle, aspect, e) { Width = width };
    }

    // ---- Format editor ----

    private static readonly Param[] GeometryParams =
        [Param.HFov, Param.VFov, Param.LensFov, Param.CenterX, Param.CenterY, Param.Radius, Param.Nadir];

    private static UiWindow Format(UiState s, FormatEditorModel m)
    {
        const float aspect = 1.7f, width = 2.5f;
        var f = m.Format;
        var e = new List<UiElement>();

        // Left column: raw frame, origin, optional corrections.
        e.Add(new UiElement(UiKind.Preview, MenuAction.None, new UiRect(0.02f, 0.11f, 0.37f, 0.43f)) { Text = T("Original file") });
        e.Add(Section(T("Where does this format come from?"), 0.02f, 0.445f, 0.37f));
        float y = 0.49f;
        foreach (var c in m.Choice.Candidates)
        {
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.02f, y, 0.37f, y + 0.042f))
            {
                Text = (c.Used ? "◆ " : "◇ ") + c.Text, Active = c.Used, Enabled = c.Available,
            });
            y += 0.044f;
        }
        e.Add(Section(T("Optional corrections"), 0.02f, 0.72f, 0.37f));
        e.Add(new UiElement(UiKind.Toggle, MenuAction.NadirToggle, new UiRect(0.02f, 0.76f, 0.37f, 0.825f))
        {
            Text = T("Bottom cover (nadir)"),
            Detail = f.NadirDeg > 0 ? F("dark {0:0}° disc under your feet", f.NadirDeg) : T("off"),
            Active = f.NadirDeg > 0,
            Enabled = f.Projection != Projection.Flat,
        });
        e.Add(new UiElement(UiKind.Toggle, MenuAction.HorizonToggle, new UiRect(0.02f, 0.835f, 0.25f, 0.9f))
        {
            Text = T("Horizon of this video"),
            Detail = m.HasHorizon ? F("level {0} · tilt {1}", Deg(m.Roll), Deg(m.Pitch)) : T("no correction"),
            Active = m.HasHorizon,
        });
        e.Add(Btn(MenuAction.SetHorizon, m.HasHorizon ? T("Set again") : T("Set with your head"))
            .At(new UiRect(0.26f, 0.835f, 0.37f, 0.9f)));

        // Right column: projection, eyes, geometry.
        e.Add(Section(T("Projection"), 0.40f, 0.11f, 0.98f));
        var projections = new List<Projection> { Projection.Flat, Projection.Dome180, Projection.Sphere360, Projection.Fisheye, Projection.Eac, Projection.Cubemap };
        if (m.HasEmbeddedMesh || f.Projection == Projection.Mesh) projections.Add(Projection.Mesh);
        e.AddRange(Row(0.15f, 0.235f, 0.40f, 0.98f, projections.Select(p => new UiElement(UiKind.Tile, MenuAction.FormatProjection, default)
        {
            Text = p switch
            {
                Projection.Flat => T("Flat"), Projection.Dome180 => "180°", Projection.Sphere360 => "360°",
                Projection.Fisheye => T("Fisheye"), Projection.Eac => "Cubemap EAC", Projection.Cubemap => "Cubemap",
                _ => T("Mesh"),
            },
            Detail = p switch
            {
                Projection.Flat => T("screen"), Projection.Dome180 or Projection.Sphere360 => "equirect.",
                Projection.Fisheye => T("circular"), Projection.Eac => "YouTube", Projection.Cubemap => "3×2", _ => T("from file"),
            },
            Arg = (int)p,
            Active = f.Projection == p,
        }).ToArray()));

        e.Add(Section(T("Eyes"), 0.40f, 0.25f, 0.98f));
        e.AddRange(Row(0.29f, 0.36f, 0.40f, 0.76f,
            Tile(MenuAction.FormatEyes, "2D", (int)StereoMode.Mono, f.Stereo == StereoMode.Mono),
            Tile(MenuAction.FormatEyes, T("Side by side"), (int)StereoMode.SideBySide, f.Stereo == StereoMode.SideBySide),
            Tile(MenuAction.FormatEyes, T("Top-bottom"), (int)StereoMode.TopBottom, f.Stereo == StereoMode.TopBottom)));
        e.Add(new UiElement(UiKind.Toggle, MenuAction.FormatSwap, new UiRect(0.775f, 0.29f, 0.98f, 0.36f))
        {
            Text = T("Swap eyes"), Active = f.SwapEyes, Enabled = f.IsStereo,
        });

        e.Add(Section(T("Geometry"), 0.40f, 0.38f, 0.98f));
        y = 0.42f;
        foreach (var p in GeometryParams)
        {
            bool applies = Params.Applies(p, f) && (p != Param.Nadir || f.NadirDeg > 0);
            string reason = p == Param.Nadir && Params.Applies(p, f) ? T("turn the cover on") : Params.NotApplicableReason(p);
            e.AddRange(SliderRow(p, Params.Spec(p), Params.Get(f, p), Params.Standard(p, f), applies, reason,
                y, y + 0.06f, 0.40f, 0.98f, Params.IsStandard(f, p), labelWidth: 0.17f));
            y += 0.068f;
        }
        if (f.Projection == Projection.Fisheye)
        {
            // The lens type takes the start of the bottom row.
            e.Add(Btn(MenuAction.FormatLens, F("Lens: {0}", Params.LensLabel(f.Lens)))
                .At(new UiRect(0.02f, 0.91f, 0.20f, 0.975f)));
        }

        // Bottom: where changes are saved.
        string profileText = m.FolderProfile is { } fp ? F("Profile “{0}”", fp) : T("Save as profile");
        e.AddRange(Row(0.91f, 0.975f, f.Projection == Projection.Fisheye ? 0.22f : 0.02f, 0.66f,
            Tile(MenuAction.FormatScope, T("This video only"), (int)FormatScope.Video, m.Scope == FormatScope.Video),
            Tile(MenuAction.FormatScope, m.HasTags ? T("Whole folder (and this video)") : T("Whole folder"), (int)FormatScope.Folder, m.Scope == FormatScope.Folder),
            Tile(MenuAction.FormatScope, profileText, (int)FormatScope.Profile, m.Scope == FormatScope.Profile)));
        e.Add(Btn(MenuAction.FormatAuto, T("Back to automatic")).At(new UiRect(0.68f, 0.91f, 0.84f, 0.975f)));
        e.Add(Btn(MenuAction.Done, T("✓ Done")).At(new UiRect(0.855f, 0.91f, 0.98f, 0.975f)));
        e.Add(Close(aspect, width));

        string changed = f.IsTuned ? T("changed") : T("standard");
        return new UiWindow(WindowKind.Format, F("Format · {0}", m.FileName),
            F("{0} · {1} · saved to: {2}", f.ShortLabel, changed, ScopeText(m)), aspect, e) { Width = width };
    }

    private static string ScopeText(FormatEditorModel m) => m.Scope switch
    {
        FormatScope.Folder => T("the folder"),
        FormatScope.Profile => m.FolderProfile is { } p ? F("the profile “{0}”", p) : T("a new profile"),
        _ => T("this video"),
    };

    // ---- Spots ----

    private static UiWindow Spots(UiState s)
    {
        const float aspect = 3.2f, width = 2.2f;
        var m = s.Spots;
        var e = new List<UiElement>();
        const float left = 0.03f, right = 0.97f, gap = 0.012f;
        float w = (right - left - gap * (SpotsModel.PerPage - 1)) / SpotsModel.PerPage;
        for (int i = 0; i < SpotsModel.PerPage; i++)
        {
            int index = m.Page * SpotsModel.PerPage + i;
            if (index >= m.Spots.Length) break;
            float x0 = left + i * (w + gap);
            string name = m.Names.Length > index && m.Names[index] is { Length: > 0 } n ? " · " + n : "";
            e.Add(new UiElement(UiKind.Thumb, MenuAction.SpotSlot, new UiRect(x0, 0.24f, x0 + w, 0.66f))
            {
                Arg = i, Ms = m.Spots[index], Danger = m.DeleteMode,
                Text = (m.DeleteMode ? "✕ " : "") + MediaUtil.FormatTime(m.Spots[index]) + name,
            });
        }
        e.AddRange(Row(0.71f, 0.95f, 0.03f, 0.97f,
            Btn(MenuAction.SpotPrev, T("◀ Bookmark")), Btn(MenuAction.SpotAdd, T("＋ Bookmark")), Btn(MenuAction.SpotNext, T("Bookmark ▶")),
            Btn(MenuAction.SpotPagePrev, T("◀ Page")) with { Enabled = m.Page > 0 },
            Btn(MenuAction.SpotPageNext, T("Page ▶")) with { Enabled = m.Page < m.PageCount - 1 },
            Tile(MenuAction.SpotDeleteMode, m.DeleteMode ? T("Delete: on") : T("Delete: off"), 0, m.DeleteMode) with { Danger = m.DeleteMode },
            Btn(MenuAction.Back, T("Back"))));
        e.Add(Close(aspect, width));
        string sub = m.Spots.Length == 0
            ? T("None yet: press “＋ Bookmark” to save the current moment")
            : F("{0} · page {1} of {2}", BookmarkCount(m.Spots.Length), m.Page + 1, m.PageCount) + " · " +
              (m.DeleteMode ? T("look at a bookmark to DELETE it") : T("look at a bookmark to jump to it"));
        return new UiWindow(WindowKind.Spots, F("Bookmarks · {0}", s.Player.Title), sub, aspect, e) { Width = width };
    }

    // ---- Info ----

    private static UiWindow Info(UiState s)
    {
        const float aspect = 1.6f, width = 2.0f;
        var e = new List<UiElement>();
        int rows = Math.Max(1, s.Info.Length / 2);
        float top = 0.12f, h = Math.Min(0.053f, (0.86f - top) / rows);
        for (int i = 0; i + 1 < s.Info.Length; i += 2)
        {
            float y = top + i / 2 * h;
            e.Add(new UiElement(UiKind.Info, MenuAction.None, new UiRect(0.04f, y, 0.96f, y + h)) { Text = s.Info[i], Detail = s.Info[i + 1] });
        }
        e.Add(Btn(MenuAction.Back, T("Back")).At(new UiRect(0.04f, 0.89f, 0.30f, 0.97f)));
        e.Add(Btn(MenuAction.Open, T("Edit format"), (int)WindowKind.Format).At(new UiRect(0.32f, 0.89f, 0.58f, 0.97f)));
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Info, T("Information"), s.Player.Title, aspect, e) { Width = width };
    }

    // ---- Settings ----

    private static UiWindow Settings(UiState s)
    {
        const float aspect = 1.9f, width = 2.4f;
        var v = s.Settings;
        var e = new List<UiElement>();
        e.AddRange(Row(0.12f, 0.2f, 0.03f, 0.88f, SettingsTabs.Select((t, i) =>
            new UiElement(UiKind.Tab, MenuAction.Tab, default) { Text = t, Arg = i, Active = s.Tab == i }).ToArray()));
        var rows = new SettingsRows(e);
        switch (s.Tab)
        {
            case 0:
                rows.Tiles(T("View"), MenuAction.StereoView, v.StereoView ? 0 : 1, T("VR viewer"), T("Screen (no viewer)"));
                rows.Slider(Param.Ipd, v.IpdShift * 100);
                rows.Slider(Param.Zoom, v.Fov);
                rows.Tiles(T("Lens distortion"), MenuAction.LensLevel, v.LensLevel, LensLevels, enabled: i => i < 3 || v.ViewerName is not null);
                rows.Text(T("Viewer profile"), v.ViewerName is { } vn ? F("{0} (scanned from the QR code)", vn) : T("none · scan it from the video list: “Viewer: scan its QR code”"),
                    v.ViewerName is not null ? Btn(MenuAction.ViewerReset, T("Remove profile")) : null);
                rows.Button(Btn(MenuAction.Open, T("Calibration wizard…"), (int)WindowKind.Calibration),
                    T("align lenses, menu and distortion in 3 steps"));
                break;
            case 1:
                rows.Tiles(T("Size"), MenuAction.ControlsSize, v.ControlsSize, T("Small"), T("Medium"), T("Large"));
                rows.Slider(Param.Opacity, v.Opacity * 100);
                rows.Slider(Param.MenuShift, v.MenuShift * 100);
                rows.Toggle(T("Gaze click"), MenuAction.GazeClick, v.GazeClick, T("look at a button for a moment to press it"));
                rows.Slider(Param.DwellTime, v.DwellSeconds, enabled: v.GazeClick);
                rows.Toggle(T("◀ ▶ buttons at the sides"), MenuAction.EdgeButtons, v.EdgeButtonsOn, T("previous and next always visible"));
                rows.Tiles(T("The ◀ ▶ buttons"), MenuAction.EdgeFollowHead, v.EdgeFollowHead ? 1 : 0, [T("Fixed in the scene"), T("Follow your head")], enabled: _ => v.EdgeButtonsOn);
                rows.Slider(Param.EdgeAngle, v.EdgeButtonsAngle, enabled: v.EdgeButtonsOn && !v.EdgeFollowHead);
                break;
            case 2:
                rows.Tiles(T("Sensor mode"), MenuAction.TrackingMode, (int)v.Tracking,
                    [T("Anti-drift (recommended)"), T("Gyroscope only"), T("Compass")],
                    enabled: i => i == 1 || s.Tracking.HasCompass);
                rows.Text(T("What each mode does"), v.Tracking switch
                {
                    TrackingMode.AntiDrift => T("gyroscope for smooth motion + compass, very slowly, to stop drifting"),
                    TrackingMode.GyroOnly => T("the smoothest; drifts over time (the error measured at rest is subtracted)"),
                    _ => T("never drifts; may shake near metal or magnets"),
                });
                rows.Text(T("Status"), TrackingText(s.Tracking));
                rows.Button(Btn(MenuAction.Calibrate, s.Tracking.Calibrating
                        ? F("Calibrating… {0:0} %", s.Tracking.CalibrationProgress * 100) : T("Calibrate (keep still for 5 s)")),
                    T("put the phone on a table; measures the gyroscope error"));
                rows.Toggle(T("Recenter when each video starts"), MenuAction.AutoRecenter, v.AutoRecenter, T("the front of the video lands where you are looking"));
                rows.Button(Btn(MenuAction.Recenter, T("Recenter now")), T("also: Y on the gamepad or the R key"));
                break;
            case 3:
                rows.Tiles(T("Image quality"), MenuAction.Quality, v.HighQuality ? 0 : 1, T("High (smooth edges)"), T("Normal (less battery)"));
                rows.Tiles(T("Mesh density"), MenuAction.MeshDetail, v.DenseMesh ? 1 : 0, T("Normal"), T("Dense (straighter lines)"));
                rows.Toggle(T("Show frames per second"), MenuAction.ShowFps, v.ShowFps, T("in the player bar"));
                break;
            default:
                rows.Text(T("Saved in"), s.Storage);
                if (s.Profiles.Length == 0)
                    rows.Text(T("Format profiles"), T("none yet · in the format editor: “Save as profile”"));
                for (int i = 0; i < Math.Min(4, s.Profiles.Length); i++)
                    rows.Text(i == 0 ? T("Format profiles") : "", s.Profiles[i],
                        Btn(MenuAction.ProfileUse, T("Use in folder"), i), Btn(MenuAction.ProfileDelete, T("Delete"), i) with { Danger = true });
                rows.Text(T("Clean up"), "", Btn(MenuAction.ClearVideo, T("This video's data")) with { Danger = true },
                    Btn(MenuAction.ClearFolder, T("Folder format")) with { Danger = true });
                break;
        }
        e.Add(Close(aspect, width));
        var tabs = SettingsTabs;
        return new UiWindow(WindowKind.Settings, T("Settings"), F("{0} · changes are saved automatically", tabs[Math.Clamp(s.Tab, 0, tabs.Length - 1)]), aspect, e) { Width = width };
    }

    public static string TrackingText(TrackingStatus t)
    {
        string drift = t.DriftMeasured
            ? F("{0}° per minute of measured drift", MathF.Abs(t.DriftDegPerMin).ToString("0.0", CultureInfo.InvariantCulture))
            : T("measuring drift…");
        string compass = !t.HasCompass ? T("no compass") : t.CompassOk ? T("compass: good") : T("compass: disturbed (paused)");
        string bias = t.BiasDegPerMin != 0 ? " · " + F("error at rest {0}°/min", MathF.Abs(t.BiasDegPerMin).ToString("0.0", CultureInfo.InvariantCulture)) : "";
        return t.Mode == TrackingMode.Compass ? compass : $"{drift} · {compass}{bias}";
    }

    /// <summary>Short sensor status for the player's status line.</summary>
    public static string SensorChip(TrackingStatus t) => t.Mode switch
    {
        TrackingMode.AntiDrift => t.CompassOk ? T("Sensor: anti-drift ✓") : t.HasCompass ? T("Sensor: anti-drift (compass paused)") : T("Sensor: gyroscope"),
        TrackingMode.GyroOnly => T("Sensor: gyroscope"),
        _ => T("Sensor: compass"),
    };

    private sealed class SettingsRows(List<UiElement> e)
    {
        private float _y = 0.235f;
        private const float H = 0.085f, Gap = 0.012f, LabelRight = 0.3f, Left = 0.03f, Right = 0.97f;

        private UiRect Next(out UiRect label)
        {
            label = new UiRect(Left, _y, LabelRight, _y + H);
            var r = new UiRect(LabelRight + 0.01f, _y, Right, _y + H);
            _y += H + Gap;
            return r;
        }

        public void Slider(Param p, float value, bool enabled = true)
        {
            var spec = Params.Spec(p);
            e.AddRange(SliderRow(p, spec, value, spec.Standard, enabled, "", _y, _y + H, Left, Right,
                MathF.Abs(value - spec.Standard) < 0.001f, labelWidth: LabelRight - Left));
            _y += H + Gap;
        }

        public void Tiles(string label, MenuAction action, int selected, params string[] options) =>
            Tiles(label, action, selected, options, null);

        public void Tiles(string label, MenuAction action, int selected, string[] options, Func<int, bool>? enabled)
        {
            var r = Next(out var l);
            e.Add(new UiElement(UiKind.Text, MenuAction.None, l) { Text = label });
            e.AddRange(Row(r.Y0, r.Y1, r.X0, r.X1, options.Select((o, i) =>
                Tile(action, o, i, i == selected) with { Enabled = enabled?.Invoke(i) ?? true }).ToArray()));
        }

        public void Toggle(string label, MenuAction action, bool on, string detail)
        {
            var r = Next(out _);
            e.Add(new UiElement(UiKind.Toggle, action, new UiRect(Left, r.Y0, Right, r.Y1)) { Text = label, Detail = detail, Active = on });
        }

        public void Text(string label, string text, params UiElement?[] buttons)
        {
            var r = Next(out var l);
            var bs = buttons.OfType<UiElement>().ToArray();
            float textRight = bs.Length > 0 ? Right - 0.2f * bs.Length : Right;
            e.Add(new UiElement(UiKind.Text, MenuAction.None, l) { Text = label });
            e.Add(new UiElement(UiKind.Text, MenuAction.None, r with { X1 = textRight }) { Detail = text });
            if (bs.Length > 0) e.AddRange(Row(r.Y0, r.Y1, textRight + 0.01f, Right, bs));
        }

        public void Button(UiElement button, string detail)
        {
            var r = Next(out var l);
            e.Add(button.At(l with { X1 = 0.45f }));
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.47f, r.Y0, Right, r.Y1)) { Detail = detail });
        }
    }

    // ---- Library ----

    private static UiWindow Library(UiState s)
    {
        const float aspect = 2.0f, width = 2.6f;
        var m = s.Library;
        var e = new List<UiElement>
        {
            Btn(MenuAction.LibraryContinue, m.ContinueName is { } c ? F("▶ Continue: {0}", c) : T("▶ Continue"))
                .At(new UiRect(0.02f, 0.12f, 0.24f, 0.2f)) with { Enabled = m.ContinueName is not null },
            Section(T("Folders"), 0.02f, 0.22f, 0.24f),
        };
        float y = 0.26f;
        for (int i = 0; i < Math.Min(6, m.Folders.Length); i++)
        {
            e.Add(Tile(MenuAction.LibraryFolder, m.Folders[i], i, i == m.Folder).At(new UiRect(0.02f, y, 0.24f, y + 0.065f)));
            y += 0.075f;
        }
        if (m.Folders.Length == 0)
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.02f, y, 0.24f, y + 0.065f)) { Detail = T("Choose folders in the list (outside the viewer)") });
        e.Add(new UiElement(UiKind.Toggle, MenuAction.Shuffle, new UiRect(0.02f, 0.8f, 0.24f, 0.88f)) { Text = T("Shuffle"), Active = m.Shuffle });
        e.Add(Btn(MenuAction.Repeat, Playlist.RepeatLabel(m.Repeat)).At(new UiRect(0.02f, 0.895f, 0.24f, 0.975f)));

        e.AddRange(Row(0.12f, 0.19f, 0.26f, 0.78f, LibraryModel.FilterLabels.Select((l, i) => Tile(MenuAction.LibraryFilter, l, i, i == m.Filter)).ToArray()));
        e.Add(Btn(MenuAction.LibrarySort, F("Sort: {0}", LibraryModel.SortLabels[Math.Clamp(m.Sort, 0, 2)])).At(new UiRect(0.8f, 0.12f, 0.98f, 0.19f)));

        const float gx = 0.26f, gr = 0.98f, gap = 0.012f, gy0 = 0.215f, gy1 = 0.885f;
        float cw = (gr - gx - gap * 3) / 4, ch = (gy1 - gy0 - gap) / 2;
        for (int i = 0; i < m.Items.Length && i < LibraryModel.PerPage; i++)
        {
            var it = m.Items[i];
            float x0 = gx + i % 4 * (cw + gap), y0 = gy0 + i / 4 * (ch + gap);
            e.Add(new UiElement(UiKind.Thumb, MenuAction.LibraryItem, new UiRect(x0, y0, x0 + cw, y0 + ch))
            {
                Arg = i, Ms = it.Index, Text = it.Name, Detail = it.FormatLabel + " · " + it.Detail, Value = it.Progress, Active = it.Current,
            });
        }
        if (m.Items.Length == 0)
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(gx, 0.45f, gr, 0.55f))
            { Detail = m.Loading ? T("Searching for videos…") : T("No videos match this filter") });
        e.Add(Btn(MenuAction.LibraryPagePrev, T("◀ Page")).At(new UiRect(0.26f, 0.905f, 0.44f, 0.975f)) with { Enabled = m.Page > 0 });
        e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.45f, 0.905f, 0.79f, 0.975f)) { Detail = F("page {0} of {1} · {2}", m.Page + 1, m.PageCount, VideoCount(m.Total)) });
        e.Add(Btn(MenuAction.LibraryPageNext, T("Page ▶")).At(new UiRect(0.8f, 0.905f, 0.98f, 0.975f)) with { Enabled = m.Page < m.PageCount - 1 });
        e.Add(Close(aspect, width));
        string folder = m.Folders.Length > m.Folder ? m.Folders[m.Folder] : "";
        return new UiWindow(WindowKind.Library, T("Library") + (folder.Length > 0 ? " · " + folder : ""),
            VideoCount(m.Total) + " · " + T("look at a video to play it"), aspect, e) { Width = width };
    }

    // ---- Horizon ----

    private static UiWindow Horizon(UiState s)
    {
        const float aspect = 3.2f, width = 2.2f;
        var f = s.Format;
        var e = new List<UiElement>();
        e.AddRange(Row(0.3f, 0.6f, 0.03f, 0.97f,
            Btn(MenuAction.PitchUp, T("Tilt ↑"), repeat: true), Btn(MenuAction.PitchDown, T("Tilt ↓"), repeat: true),
            Btn(MenuAction.RollLeft, T("Level ↺"), repeat: true), Btn(MenuAction.RollRight, T("Level ↻"), repeat: true),
            Btn(MenuAction.YawLeft, T("Turn ←"), repeat: true), Btn(MenuAction.YawRight, T("Turn →"), repeat: true)));
        e.AddRange(Row(0.67f, 0.95f, 0.03f, 0.97f,
            Btn(MenuAction.SetHorizon, T("Set with your head")), Btn(MenuAction.ResetHorizon, T("Reset")),
            Btn(MenuAction.Recenter, T("Recenter")), Btn(MenuAction.Back, T("Back"))));
        e.Add(Close(aspect, width));
        string sub = f is null ? "" : F("tilt {0} · level {1} · turn {2} · this video only", Deg(f.Pitch), Deg(f.Roll), Deg(f.Yaw));
        return new UiWindow(WindowKind.Horizon, T("Horizon"), sub, aspect, e) { Width = width };
    }

    // ---- Audio and subtitles ----

    private static UiWindow Tracks(UiState s)
    {
        const float aspect = 2.6f, width = 2.2f;
        var t = s.Tracks;
        var e = new List<UiElement> { Section(T("Audio"), 0.03f, 0.2f, 0.97f) };
        if (t.Audio.Length == 0)
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.03f, 0.27f, 0.97f, 0.39f)) { Detail = T("This video has only one audio track (or none)") });
        else
            e.AddRange(Row(0.27f, 0.39f, 0.03f, 0.97f, t.Audio.Take(5).Select((a, i) => Tile(MenuAction.AudioTrack, a, i, i == t.SelectedAudio)).ToArray()));
        e.Add(Section(T("Subtitles"), 0.03f, 0.43f, 0.97f));
        var subs = new List<UiElement> { Tile(MenuAction.SubtitleTrack, T("Off"), -1, t.SelectedSubtitle < 0) };
        subs.AddRange(t.Subtitles.Take(5).Select((x, i) => Tile(MenuAction.SubtitleTrack, x, i, i == t.SelectedSubtitle)));
        e.AddRange(Row(0.5f, 0.62f, 0.03f, 0.97f, subs.ToArray()));
        var spec = Params.Spec(Param.SubtitleSize);
        e.AddRange(SliderRow(Param.SubtitleSize, spec, s.Settings.SubtitleSize, spec.Standard, true, "", 0.68f, 0.79f, 0.03f, 0.97f,
            MathF.Abs(s.Settings.SubtitleSize - spec.Standard) < 0.01f, labelWidth: 0.25f));
        e.Add(Btn(MenuAction.Back, T("Back")).At(new UiRect(0.03f, 0.85f, 0.3f, 0.96f)));
        e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.32f, 0.85f, 0.97f, 0.96f))
        { Detail = T("Subtitle .srt files named like the video are found automatically") });
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Tracks, T("Audio and subtitles"), s.Player.Title, aspect, e) { Width = width };
    }

    // ---- Calibration wizard ----

    public static string[] CalibrationTitles =>
    [
        T("1 of 3 · Lenses: move the slider until the two background crosses become one"),
        T("2 of 3 · Menu: now merge the near crosses (the ones on the panel) into one"),
        T("3 of 3 · Distortion: choose the option that makes the grid lines look straight"),
    ];

    private static UiWindow Calibration(UiState s)
    {
        const float aspect = 2.8f, width = 2.0f;
        int step = Math.Clamp(s.CalibrationStep, 1, 3);
        var v = s.Settings;
        var e = new List<UiElement>();
        switch (step)
        {
            case 1:
                {
                    var spec = Params.Spec(Param.Ipd);
                    e.AddRange(SliderRow(Param.Ipd, spec, v.IpdShift * 100, spec.Standard, true, "", 0.32f, 0.52f, 0.03f, 0.97f,
                        v.IpdShift == 0, labelWidth: 0.25f));
                    break;
                }
            case 2:
                {
                    var spec = Params.Spec(Param.MenuShift);
                    e.AddRange(SliderRow(Param.MenuShift, spec, v.MenuShift * 100, spec.Standard, true, "", 0.32f, 0.52f, 0.03f, 0.97f,
                        v.MenuShift == 0, labelWidth: 0.25f));
                    break;
                }
            default:
                e.AddRange(Row(0.32f, 0.52f, 0.03f, 0.97f, LensLevels.Select((l, i) =>
                    Tile(MenuAction.LensLevel, l, i, i == v.LensLevel) with { Enabled = i < 3 || v.ViewerName is not null }).ToArray()));
                break;
        }
        e.Add(Btn(MenuAction.CalibrationStep, T("◀ Previous"), step - 1).At(new UiRect(0.03f, 0.62f, 0.3f, 0.92f)) with { Enabled = step > 1 });
        e.Add(Btn(MenuAction.CalibrationStep, step < 3 ? T("Next ▶") : T("✓ Finish"), step + 1).At(new UiRect(0.7f, 0.62f, 0.97f, 0.92f)));
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Calibration, T("Calibration wizard"), CalibrationTitles[step - 1], aspect, e) { Width = width };
    }

    // ---- Building blocks ----

    /// <summary>A labelled slider: name, −, track, +, and the value that resets to the standard when clicked.</summary>
    private static IEnumerable<UiElement> SliderRow(Param p, ParamSpec spec, float value, float standard, bool enabled, string reason,
        float y0, float y1, float x0, float x1, bool isStandard, float labelWidth)
    {
        float lx = x0 + labelWidth, w = x1 - lx;
        yield return new UiElement(UiKind.Text, MenuAction.None, new UiRect(x0, y0, lx, y1))
        { Text = spec.Label, Detail = enabled ? "" : reason, Enabled = enabled };
        float bw = w * 0.09f, vw = w * 0.24f;
        yield return Btn(MenuAction.ParamMinus, "−", (int)p, repeat: true).At(new UiRect(lx + 0.005f, y0, lx + bw, y1)) with { Enabled = enabled, Accelerate = true };
        yield return new UiElement(UiKind.Slider, MenuAction.ParamSet, new UiRect(lx + bw + 0.01f, y0, x1 - vw - bw - 0.01f, y1))
        {
            Arg = (int)p, Value = spec.Fraction(value), Mark = spec.Fraction(standard), Enabled = enabled,
        };
        yield return Btn(MenuAction.ParamPlus, "+", (int)p, repeat: true).At(new UiRect(x1 - vw - bw, y0, x1 - vw - 0.005f, y1)) with { Enabled = enabled, Accelerate = true };
        yield return new UiElement(UiKind.Button, MenuAction.ParamReset, new UiRect(x1 - vw + 0.005f, y0, x1, y1))
        {
            Arg = (int)p, Text = spec.Format(value), Detail = isStandard ? T("standard") : T("changed · tap = standard"),
            Active = !isStandard, Enabled = enabled,
        };
    }

    private static UiElement Btn(MenuAction a, string text, int arg = 0, bool repeat = false) =>
        new(UiKind.Button, a, default) { Text = text, Arg = arg, Repeatable = repeat };

    private static UiElement Open(WindowKind w, string text) => Btn(MenuAction.Open, text, (int)w);

    private static UiElement Tile(MenuAction a, string text, int arg, bool active) =>
        new(UiKind.Tile, a, default) { Text = text, Arg = arg, Active = active };

    private static UiElement Section(string text, float x0, float y0, float x1) =>
        new(UiKind.Section, MenuAction.None, new UiRect(x0, y0, x1, y0 + 0.035f)) { Text = text };

    private static UiElement Close(float aspect, float width) =>
        new(UiKind.Button, MenuAction.Hide, UiWindow.CloseRect(aspect, width)) { Text = "✕" };

    private static UiElement At(this UiElement e, UiRect r) => e with { Rect = r };

    /// <summary>Spreads elements evenly in a row.</summary>
    private static IEnumerable<UiElement> Row(float top, float bottom, float left, float right, params UiElement[] items)
    {
        if (items.Length == 0) yield break;
        const float gap = 0.012f;
        float w = (right - left - gap * (items.Length - 1)) / items.Length;
        for (int i = 0; i < items.Length; i++)
        {
            float x0 = left + i * (w + gap);
            yield return items[i] with { Rect = new UiRect(x0, top, x0 + w, bottom) };
        }
    }

    private static string Deg(float v) => (v > 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + "°";
}
