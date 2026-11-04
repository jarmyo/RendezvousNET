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
    public static readonly string[] SettingsTabs = ["Visor", "Controles", "Seguimiento", "Calidad", "Datos"];
    public static readonly string[] LensLevels = ["No", "Suave", "Fuerte", "Perfil QR"];

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
            Btn(MenuAction.Prev, "|◀ Anterior"), Btn(MenuAction.Back10, "◀◀ 10 s", repeat: true),
            Btn(MenuAction.PlayPause, p.Playing ? "II Pausa" : "▶ Reproducir"), Btn(MenuAction.Fwd10, "10 s ▶▶", repeat: true),
            Btn(MenuAction.Next, "Siguiente ▶|"), Btn(MenuAction.SpotAdd, "＋ Marca"),
            Btn(MenuAction.VolDown, "Vol −", repeat: true), Btn(MenuAction.VolUp, "Vol +", repeat: true)));
        e.AddRange(Row(0.71f, 0.95f, 0.03f, 0.97f,
            Open(WindowKind.Spots, s.Spots.Spots.Length > 0 ? $"Marcas ({s.Spots.Spots.Length})" : "Marcas"),
            Open(WindowKind.Format, "Formato"), Open(WindowKind.Horizon, "Horizonte"), Open(WindowKind.Tracks, "Audio y subt."),
            Open(WindowKind.Info, "Info"), Open(WindowKind.Library, "Biblioteca"), Open(WindowKind.Settings, "⚙ Ajustes"),
            Btn(MenuAction.Exit, "Salir")));
        e.Add(Close(aspect, width));
        string fps = s.Settings.ShowFps && p.Fps > 0 ? $"   ·   {p.Fps:0.#} fps" : "";
        string subtitle = $"{MediaUtil.FormatTime(p.PositionMs)} / {MediaUtil.FormatTime(p.DurationMs)}   ·   {p.FormatText}   ·   " +
                          $"{p.SensorText}   ·   Vol {p.VolumePercent}%   ·   {p.Index + 1} de {p.Count}{fps}";
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
        e.Add(new UiElement(UiKind.Preview, MenuAction.None, new UiRect(0.02f, 0.11f, 0.37f, 0.43f)) { Text = "Archivo original" });
        e.Add(Section("¿De dónde sale este formato?", 0.02f, 0.445f, 0.37f));
        float y = 0.49f;
        foreach (var c in m.Choice.Candidates)
        {
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.02f, y, 0.37f, y + 0.042f))
            {
                Text = (c.Used ? "◆ " : "◇ ") + c.Text, Active = c.Used, Enabled = c.Available,
            });
            y += 0.044f;
        }
        e.Add(Section("Correcciones opcionales", 0.02f, 0.72f, 0.37f));
        e.Add(new UiElement(UiKind.Toggle, MenuAction.NadirToggle, new UiRect(0.02f, 0.76f, 0.37f, 0.825f))
        {
            Text = "Tapa abajo (nadir)",
            Detail = f.NadirDeg > 0 ? $"disco oscuro de {f.NadirDeg:0}° bajo tus pies" : "apagada",
            Active = f.NadirDeg > 0,
            Enabled = f.Projection != Projection.Flat,
        });
        e.Add(new UiElement(UiKind.Toggle, MenuAction.HorizonToggle, new UiRect(0.02f, 0.835f, 0.25f, 0.9f))
        {
            Text = "Horizonte de este video",
            Detail = m.HasHorizon ? $"nivel {Deg(m.Roll)} · inclinación {Deg(m.Pitch)}" : "sin corrección",
            Active = m.HasHorizon,
        });
        e.Add(Btn(MenuAction.SetHorizon, m.HasHorizon ? "Volver a fijar" : "Fijar con la cabeza")
            .At(new UiRect(0.26f, 0.835f, 0.37f, 0.9f)));

        // Right column: projection, eyes, geometry.
        e.Add(Section("Proyección", 0.40f, 0.11f, 0.98f));
        var projections = new List<Projection> { Projection.Flat, Projection.Dome180, Projection.Sphere360, Projection.Fisheye, Projection.Eac, Projection.Cubemap };
        if (m.HasEmbeddedMesh || f.Projection == Projection.Mesh) projections.Add(Projection.Mesh);
        e.AddRange(Row(0.15f, 0.235f, 0.40f, 0.98f, projections.Select(p => new UiElement(UiKind.Tile, MenuAction.FormatProjection, default)
        {
            Text = p switch
            {
                Projection.Flat => "Plano", Projection.Dome180 => "180°", Projection.Sphere360 => "360°",
                Projection.Fisheye => "Ojo de pez", Projection.Eac => "Cubemap EAC", Projection.Cubemap => "Cubemap",
                _ => "Malla",
            },
            Detail = p switch
            {
                Projection.Flat => "pantalla", Projection.Dome180 or Projection.Sphere360 => "equirect.",
                Projection.Fisheye => "circular", Projection.Eac => "YouTube", Projection.Cubemap => "3×2", _ => "del archivo",
            },
            Arg = (int)p,
            Active = f.Projection == p,
        }).ToArray()));

        e.Add(Section("Ojos", 0.40f, 0.25f, 0.98f));
        e.AddRange(Row(0.29f, 0.36f, 0.40f, 0.76f,
            Tile(MenuAction.FormatEyes, "2D", (int)StereoMode.Mono, f.Stereo == StereoMode.Mono),
            Tile(MenuAction.FormatEyes, "Lado a lado", (int)StereoMode.SideBySide, f.Stereo == StereoMode.SideBySide),
            Tile(MenuAction.FormatEyes, "Arriba-abajo", (int)StereoMode.TopBottom, f.Stereo == StereoMode.TopBottom)));
        e.Add(new UiElement(UiKind.Toggle, MenuAction.FormatSwap, new UiRect(0.775f, 0.29f, 0.98f, 0.36f))
        {
            Text = "Invertir ojos", Active = f.SwapEyes, Enabled = f.IsStereo,
        });

        e.Add(Section("Geometría", 0.40f, 0.38f, 0.98f));
        y = 0.42f;
        foreach (var p in GeometryParams)
        {
            bool applies = Params.Applies(p, f) && (p != Param.Nadir || f.NadirDeg > 0);
            string reason = p == Param.Nadir && Params.Applies(p, f) ? "enciende la tapa" : Params.NotApplicableReason(p);
            e.AddRange(SliderRow(p, Params.Spec(p), Params.Get(f, p), Params.Standard(p, f), applies, reason,
                y, y + 0.06f, 0.40f, 0.98f, Params.IsStandard(f, p), labelWidth: 0.17f));
            y += 0.068f;
        }
        if (f.Projection == Projection.Fisheye)
        {
            // The lens type takes the start of the bottom row.
            e.Add(Btn(MenuAction.FormatLens, "Lente: " + Params.LensLabel(f.Lens))
                .At(new UiRect(0.02f, 0.91f, 0.20f, 0.975f)));
        }

        // Bottom: where changes are saved.
        string profileText = m.FolderProfile is { } fp ? $"Perfil «{fp}»" : "Guardar como perfil";
        string tagNote = m.HasTags ? " (y este video)" : "";
        e.AddRange(Row(0.91f, 0.975f, f.Projection == Projection.Fisheye ? 0.22f : 0.02f, 0.66f,
            Tile(MenuAction.FormatScope, "Solo este video", (int)FormatScope.Video, m.Scope == FormatScope.Video),
            Tile(MenuAction.FormatScope, "Toda la carpeta" + tagNote, (int)FormatScope.Folder, m.Scope == FormatScope.Folder),
            Tile(MenuAction.FormatScope, profileText, (int)FormatScope.Profile, m.Scope == FormatScope.Profile)));
        e.Add(Btn(MenuAction.FormatAuto, "Volver a automático").At(new UiRect(0.68f, 0.91f, 0.84f, 0.975f)));
        e.Add(Btn(MenuAction.Done, "✓ Listo").At(new UiRect(0.855f, 0.91f, 0.98f, 0.975f)));
        e.Add(Close(aspect, width));

        string changed = f.IsTuned ? "cambiado" : "estándar";
        return new UiWindow(WindowKind.Format, "Formato · " + m.FileName,
            $"{f.ShortLabel} · {changed} · se guarda en: {ScopeText(m)}", aspect, e) { Width = width };
    }

    private static string ScopeText(FormatEditorModel m) => m.Scope switch
    {
        FormatScope.Folder => "la carpeta",
        FormatScope.Profile => m.FolderProfile is { } p ? $"el perfil «{p}»" : "un perfil nuevo",
        _ => "este video",
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
            Btn(MenuAction.SpotPrev, "◀ Marca"), Btn(MenuAction.SpotAdd, "＋ Marca"), Btn(MenuAction.SpotNext, "Marca ▶"),
            Btn(MenuAction.SpotPagePrev, "◀ Pág.") with { Enabled = m.Page > 0 },
            Btn(MenuAction.SpotPageNext, "Pág. ▶") with { Enabled = m.Page < m.PageCount - 1 },
            Tile(MenuAction.SpotDeleteMode, m.DeleteMode ? "Borrar: sí" : "Borrar: no", 0, m.DeleteMode) with { Danger = m.DeleteMode },
            Btn(MenuAction.Back, "Volver")));
        e.Add(Close(aspect, width));
        string sub = m.Spots.Length == 0
            ? "Aún no hay: pulsa «＋ Marca» para guardar el momento actual"
            : $"{m.Spots.Length} marcas · página {m.Page + 1} de {m.PageCount}" +
              (m.DeleteMode ? " · mira una marca para BORRARLA" : " · mira una marca para saltar a ella");
        return new UiWindow(WindowKind.Spots, "Marcas · " + s.Player.Title, sub, aspect, e) { Width = width };
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
        e.Add(Btn(MenuAction.Back, "Volver").At(new UiRect(0.04f, 0.89f, 0.30f, 0.97f)));
        e.Add(Btn(MenuAction.Open, "Editar formato", (int)WindowKind.Format).At(new UiRect(0.32f, 0.89f, 0.58f, 0.97f)));
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Info, "Información", s.Player.Title, aspect, e) { Width = width };
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
                rows.Tiles("Vista", MenuAction.StereoView, v.StereoView ? 0 : 1, "Visor VR", "Pantalla (sin visor)");
                rows.Slider(Param.Ipd, v.IpdShift * 100);
                rows.Slider(Param.Zoom, v.Fov);
                rows.Tiles("Distorsión de las lentes", MenuAction.LensLevel, v.LensLevel, LensLevels, enabled: i => i < 3 || v.ViewerName is not null);
                rows.Text("Perfil del visor", v.ViewerName is { } vn ? $"{vn} (escaneado del código QR)" : "ninguno · se escanea fuera del visor: ☰ › Escanear QR",
                    v.ViewerName is not null ? Btn(MenuAction.ViewerReset, "Quitar perfil") : null);
                rows.Button(Btn(MenuAction.Open, "Asistente de calibración…", (int)WindowKind.Calibration),
                    "alinear lentes, menú y distorsión en 3 pasos");
                break;
            case 1:
                rows.Tiles("Tamaño", MenuAction.ControlsSize, v.ControlsSize, "Chicos", "Medianos", "Grandes");
                rows.Slider(Param.Opacity, v.Opacity * 100);
                rows.Slider(Param.MenuShift, v.MenuShift * 100);
                rows.Toggle("Clic con la mirada", MenuAction.GazeClick, v.GazeClick, "mirar un botón un momento lo pulsa");
                rows.Slider(Param.DwellTime, v.DwellSeconds, enabled: v.GazeClick);
                rows.Toggle("Botones ◀ ▶ a los lados", MenuAction.EdgeButtons, v.EdgeButtonsOn, "anterior y siguiente siempre visibles");
                rows.Tiles("Los botones ◀ ▶", MenuAction.EdgeFollowHead, v.EdgeFollowHead ? 1 : 0, ["Fijos en la escena", "Siguen la cabeza"], enabled: _ => v.EdgeButtonsOn);
                rows.Slider(Param.EdgeAngle, v.EdgeButtonsAngle, enabled: v.EdgeButtonsOn && !v.EdgeFollowHead);
                break;
            case 2:
                rows.Tiles("Modo del sensor", MenuAction.TrackingMode, (int)v.Tracking,
                    ["Anti-deriva (recomendado)", "Solo giroscopio", "Brújula"],
                    enabled: i => i == 1 || s.Tracking.HasCompass);
                rows.Text("Qué hace cada modo", v.Tracking switch
                {
                    TrackingMode.AntiDrift => "giroscopio para moverse suave + brújula, muy despacio, para no desviarse",
                    TrackingMode.GyroOnly => "el más suave; se desvía con el tiempo (se resta el error medido en reposo)",
                    _ => "nunca se desvía; puede temblar cerca de metal o imanes",
                });
                rows.Text("Estado", TrackingText(s.Tracking));
                rows.Button(Btn(MenuAction.Calibrate, s.Tracking.Calibrating
                        ? $"Calibrando… {s.Tracking.CalibrationProgress * 100:0} %" : "Calibrar (dejar quieto 5 s)"),
                    "pon el teléfono sobre la mesa; mide el error del giroscopio");
                rows.Toggle("Recentrar al empezar cada video", MenuAction.AutoRecenter, v.AutoRecenter, "el frente del video queda donde miras");
                rows.Button(Btn(MenuAction.Recenter, "Recentrar ahora"), "también: botón Y del control o tecla R");
                break;
            case 3:
                rows.Tiles("Calidad de imagen", MenuAction.Quality, v.HighQuality ? 0 : 1, "Alta (bordes suaves)", "Normal (menos batería)");
                rows.Tiles("Densidad de la malla", MenuAction.MeshDetail, v.DenseMesh ? 1 : 0, "Normal", "Densa (líneas más rectas)");
                rows.Toggle("Mostrar cuadros por segundo", MenuAction.ShowFps, v.ShowFps, "en la barra del reproductor");
                break;
            default:
                rows.Text("Guardado en", s.Storage);
                if (s.Profiles.Length == 0)
                    rows.Text("Perfiles de formato", "aún no hay · en el editor de formato: «Guardar como perfil»");
                for (int i = 0; i < Math.Min(4, s.Profiles.Length); i++)
                    rows.Text(i == 0 ? "Perfiles de formato" : "", s.Profiles[i],
                        Btn(MenuAction.ProfileUse, "Usar en carpeta", i), Btn(MenuAction.ProfileDelete, "Borrar", i) with { Danger = true });
                rows.Text("Limpiar", "", Btn(MenuAction.ClearVideo, "Datos de este video") with { Danger = true },
                    Btn(MenuAction.ClearFolder, "Formato de la carpeta") with { Danger = true });
                break;
        }
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Settings, "Ajustes", SettingsTabs[Math.Clamp(s.Tab, 0, SettingsTabs.Length - 1)] +
            " · los cambios se guardan solos", aspect, e) { Width = width };
    }

    public static string TrackingText(TrackingStatus t)
    {
        string drift = t.DriftMeasured
            ? $"{MathF.Abs(t.DriftDegPerMin).ToString("0.0", CultureInfo.InvariantCulture)}° por minuto de desvío medido"
            : "midiendo el desvío…";
        string compass = !t.HasCompass ? "sin brújula" : t.CompassOk ? "brújula: bien" : "brújula: perturbada (pausada)";
        string bias = t.BiasDegPerMin != 0 ? $" · error en reposo {MathF.Abs(t.BiasDegPerMin).ToString("0.0", CultureInfo.InvariantCulture)}°/min" : "";
        return t.Mode == TrackingMode.Compass ? compass : $"{drift} · {compass}{bias}";
    }

    /// <summary>Short sensor status for the player's status line.</summary>
    public static string SensorChip(TrackingStatus t) => t.Mode switch
    {
        TrackingMode.AntiDrift => t.CompassOk ? "Sensor: anti-deriva ✓" : t.HasCompass ? "Sensor: anti-deriva (brújula en pausa)" : "Sensor: giroscopio",
        TrackingMode.GyroOnly => "Sensor: giroscopio",
        _ => "Sensor: brújula",
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
            Btn(MenuAction.LibraryContinue, m.ContinueName is { } c ? "▶ Continuar: " + c : "▶ Continuar")
                .At(new UiRect(0.02f, 0.12f, 0.24f, 0.2f)) with { Enabled = m.ContinueName is not null },
            Section("Carpetas", 0.02f, 0.22f, 0.24f),
        };
        float y = 0.26f;
        for (int i = 0; i < Math.Min(6, m.Folders.Length); i++)
        {
            e.Add(Tile(MenuAction.LibraryFolder, m.Folders[i], i, i == m.Folder).At(new UiRect(0.02f, y, 0.24f, y + 0.065f)));
            y += 0.075f;
        }
        if (m.Folders.Length == 0)
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.02f, y, 0.24f, y + 0.065f)) { Detail = "Elige carpetas en la lista (fuera del visor)" });
        e.Add(new UiElement(UiKind.Toggle, MenuAction.Shuffle, new UiRect(0.02f, 0.8f, 0.24f, 0.88f)) { Text = "Aleatorio", Active = m.Shuffle });
        e.Add(Btn(MenuAction.Repeat, Playlist.RepeatLabel(m.Repeat)).At(new UiRect(0.02f, 0.895f, 0.24f, 0.975f)));

        e.AddRange(Row(0.12f, 0.19f, 0.26f, 0.78f, LibraryModel.FilterLabels.Select((l, i) => Tile(MenuAction.LibraryFilter, l, i, i == m.Filter)).ToArray()));
        e.Add(Btn(MenuAction.LibrarySort, "Orden: " + LibraryModel.SortLabels[Math.Clamp(m.Sort, 0, 2)]).At(new UiRect(0.8f, 0.12f, 0.98f, 0.19f)));

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
            { Detail = m.Loading ? "Buscando videos…" : "No hay videos con este filtro" });
        e.Add(Btn(MenuAction.LibraryPagePrev, "◀ Página").At(new UiRect(0.26f, 0.905f, 0.44f, 0.975f)) with { Enabled = m.Page > 0 });
        e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.45f, 0.905f, 0.79f, 0.975f)) { Detail = $"página {m.Page + 1} de {m.PageCount} · {m.Total} videos" });
        e.Add(Btn(MenuAction.LibraryPageNext, "Página ▶").At(new UiRect(0.8f, 0.905f, 0.98f, 0.975f)) with { Enabled = m.Page < m.PageCount - 1 });
        e.Add(Close(aspect, width));
        string folder = m.Folders.Length > m.Folder ? m.Folders[m.Folder] : "";
        return new UiWindow(WindowKind.Library, "Biblioteca" + (folder.Length > 0 ? " · " + folder : ""),
            $"{m.Total} videos · mira un video para reproducirlo", aspect, e) { Width = width };
    }

    // ---- Horizon ----

    private static UiWindow Horizon(UiState s)
    {
        const float aspect = 3.2f, width = 2.2f;
        var f = s.Format;
        var e = new List<UiElement>();
        e.AddRange(Row(0.3f, 0.6f, 0.03f, 0.97f,
            Btn(MenuAction.PitchUp, "Inclinar ↑", repeat: true), Btn(MenuAction.PitchDown, "Inclinar ↓", repeat: true),
            Btn(MenuAction.RollLeft, "Nivelar ↺", repeat: true), Btn(MenuAction.RollRight, "Nivelar ↻", repeat: true),
            Btn(MenuAction.YawLeft, "Girar ←", repeat: true), Btn(MenuAction.YawRight, "Girar →", repeat: true)));
        e.AddRange(Row(0.67f, 0.95f, 0.03f, 0.97f,
            Btn(MenuAction.SetHorizon, "Fijar con la cabeza"), Btn(MenuAction.ResetHorizon, "Restablecer"),
            Btn(MenuAction.Recenter, "Recentrar"), Btn(MenuAction.Back, "Volver")));
        e.Add(Close(aspect, width));
        string sub = f is null ? "" : $"inclinación {Deg(f.Pitch)} · nivel {Deg(f.Roll)} · giro {Deg(f.Yaw)} · solo este video";
        return new UiWindow(WindowKind.Horizon, "Horizonte", sub, aspect, e) { Width = width };
    }

    // ---- Audio and subtitles ----

    private static UiWindow Tracks(UiState s)
    {
        const float aspect = 2.6f, width = 2.2f;
        var t = s.Tracks;
        var e = new List<UiElement> { Section("Audio", 0.03f, 0.2f, 0.97f) };
        if (t.Audio.Length == 0)
            e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.03f, 0.27f, 0.97f, 0.39f)) { Detail = "Este video tiene una sola pista de audio (o ninguna)" });
        else
            e.AddRange(Row(0.27f, 0.39f, 0.03f, 0.97f, t.Audio.Take(5).Select((a, i) => Tile(MenuAction.AudioTrack, a, i, i == t.SelectedAudio)).ToArray()));
        e.Add(Section("Subtítulos", 0.03f, 0.43f, 0.97f));
        var subs = new List<UiElement> { Tile(MenuAction.SubtitleTrack, "No", -1, t.SelectedSubtitle < 0) };
        subs.AddRange(t.Subtitles.Take(5).Select((x, i) => Tile(MenuAction.SubtitleTrack, x, i, i == t.SelectedSubtitle)));
        e.AddRange(Row(0.5f, 0.62f, 0.03f, 0.97f, subs.ToArray()));
        var spec = Params.Spec(Param.SubtitleSize);
        e.AddRange(SliderRow(Param.SubtitleSize, spec, s.Settings.SubtitleSize, spec.Standard, true, "", 0.68f, 0.79f, 0.03f, 0.97f,
            MathF.Abs(s.Settings.SubtitleSize - spec.Standard) < 0.01f, labelWidth: 0.25f));
        e.Add(Btn(MenuAction.Back, "Volver").At(new UiRect(0.03f, 0.85f, 0.3f, 0.96f)));
        e.Add(new UiElement(UiKind.Text, MenuAction.None, new UiRect(0.32f, 0.85f, 0.97f, 0.96f))
        { Detail = "Los subtítulos .srt con el mismo nombre del video se encuentran solos" });
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Tracks, "Audio y subtítulos", s.Player.Title, aspect, e) { Width = width };
    }

    // ---- Calibration wizard ----

    public static readonly string[] CalibrationTitles =
    [
        "1 de 3 · Lentes: mueve el deslizador hasta que las dos cruces del fondo se vuelvan una sola",
        "2 de 3 · Menú: ahora junta las cruces cercanas (las del panel) en una sola",
        "3 de 3 · Distorsión: elige la opción con la que las líneas de la cuadrícula se ven rectas",
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
        e.Add(Btn(MenuAction.CalibrationStep, "◀ Anterior", step - 1).At(new UiRect(0.03f, 0.62f, 0.3f, 0.92f)) with { Enabled = step > 1 });
        e.Add(Btn(MenuAction.CalibrationStep, step < 3 ? "Siguiente ▶" : "✓ Terminar", step + 1).At(new UiRect(0.7f, 0.62f, 0.97f, 0.92f)));
        e.Add(Close(aspect, width));
        return new UiWindow(WindowKind.Calibration, "Asistente de calibración", CalibrationTitles[step - 1], aspect, e) { Width = width };
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
            Arg = (int)p, Text = spec.Format(value), Detail = isStandard ? "estándar" : "cambiado · tocar = estándar",
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
