using VrView.Core;

namespace VrView.Core.Tests;

public class WindowsTests
{
    private static FormatEditorModel Editor(VideoFormat f, FormatScope scope = FormatScope.Video) =>
        new("clip_180_LR.mp4", FormatResolver.Resolve(new FormatInputs(f, null, null, new Dictionary<string, VideoFormat>(),
            "clip_180_LR.mp4", 3840, 1920, null)), scope, null, true, 2, -3.5f, 0, false);

    private static UiState State(WindowKind w, int tab = 0, VideoFormat? f = null) => new(
        w, tab,
        new PlayerStatus("clip.mp4", 60_000, 600_000, true, 50, 2, 24, "180° SBS (este video)", "Sensor: anti-deriva ✓", 30),
        [0.1f, 0.5f],
        Editor(f ?? new VideoFormat(Projection.Fisheye, StereoMode.SideBySide)),
        new SpotsModel([1000, 5000, 9000, 20_000, 40_000, 80_000], ["", "Muelle", "", "", "", ""], 0, false, 0),
        new ViewSettings(ViewerName: "Cardboard v2"),
        new TrackingStatus(TrackingMode.AntiDrift, true, true, 0.4f, true, 0.2f, false, 0),
        ["Archivo", "clip.mp4", "Video", "3840×1920 · 30 fps"],
        LibraryModel.Empty with
        {
            Folders = ["Viajes", "Conciertos"], Total = 9, PageCount = 2,
            Items = Enumerable.Range(0, 8).Select(i => new LibraryEntry(i, $"v{i}", "180° SBS", "10:00", 0.3f, i == 1)).ToArray(),
        },
        new TracksModel(["Español", "English"], 0, ["clip.srt"], -1),
        ["Cámara 200°", "Canon"],
        "Viajes/vrview.json",
        2);

    public static IEnumerable<object[]> AllWindows()
    {
        foreach (var w in Enum.GetValues<WindowKind>())
        {
            if (w == WindowKind.Settings)
                for (int t = 0; t < Windows.SettingsTabs.Length; t++) yield return [w, t, Projection.Fisheye];
            else if (w == WindowKind.Format)
                foreach (var p in Enum.GetValues<Projection>()) yield return [w, 0, p];
            else yield return [w, 0, Projection.Fisheye];
        }
    }

    [Theory]
    [MemberData(nameof(AllWindows))]
    public void Elements_stay_inside_and_clickables_do_not_overlap(WindowKind kind, int tab, Projection projection)
    {
        var win = Windows.Build(State(kind, tab, new VideoFormat(projection, StereoMode.SideBySide)));
        Assert.Equal(kind, win.Kind);
        foreach (var e in win.Elements)
        {
            Assert.InRange(e.Rect.X0, 0f, 1f); Assert.InRange(e.Rect.X1, 0f, 1f);
            Assert.InRange(e.Rect.Y0, 0f, 1f); Assert.InRange(e.Rect.Y1, 0f, 1f);
            Assert.True(e.Rect.X1 > e.Rect.X0 && e.Rect.Y1 > e.Rect.Y0, $"{e.Action} {e.Text} has no area");
        }
        var clickable = win.Elements.Where(e => e.Action != MenuAction.None).ToList();
        for (int i = 0; i < clickable.Count; i++)
            for (int j = i + 1; j < clickable.Count; j++)
            {
                var a = clickable[i].Rect; var c = clickable[j].Rect;
                bool overlap = a.X0 < c.X1 - 1e-4 && c.X0 < a.X1 - 1e-4 && a.Y0 < c.Y1 - 1e-4 && c.Y0 < a.Y1 - 1e-4;
                Assert.False(overlap, $"{kind}/{tab}: {clickable[i].Action} '{clickable[i].Text}' overlaps {clickable[j].Action} '{clickable[j].Text}'");
            }
        // Every window can be closed.
        Assert.Contains(win.Elements, e => e.Action == MenuAction.Hide);
    }

    [Fact]
    public void Hit_test_finds_buttons_and_slider_fraction()
    {
        var player = Windows.Build(State(WindowKind.Player));
        var play = player.Find(MenuAction.PlayPause)!;
        var hit = player.HitTest((play.Rect.X0 + play.Rect.X1) / 2, (play.Rect.Y0 + play.Rect.Y1) / 2);
        Assert.Equal(MenuAction.PlayPause, hit.Action);

        var seek = player.Find(MenuAction.Seek)!;
        var s = player.HitTest(seek.Rect.X0 + seek.Rect.Width * 0.25f, (seek.Rect.Y0 + seek.Rect.Y1) / 2);
        Assert.Equal(MenuAction.Seek, s.Action);
        Assert.Equal(0.25f, s.Fraction, 3);
        Assert.Equal(MenuAction.None, player.HitTest(0.5f, 0.05f).Action);

        var format = Windows.Build(State(WindowKind.Format));
        var slider = format.Find(MenuAction.ParamSet, (int)Param.LensFov)!;
        var h = format.HitTest(slider.Rect.X0 + slider.Rect.Width * 0.5f, (slider.Rect.Y0 + slider.Rect.Y1) / 2);
        Assert.Equal(MenuAction.ParamSet, h.Action);
        Assert.Equal((int)Param.LensFov, h.Arg);
        Assert.Equal(0.5f, h.Fraction, 3);
    }

    [Fact]
    public void Format_editor_dims_parameters_that_do_not_apply()
    {
        var w = Windows.Build(State(WindowKind.Format, f: new VideoFormat(Projection.Dome180, StereoMode.SideBySide)));
        Assert.True(w.Find(MenuAction.ParamSet, (int)Param.VFov)!.Enabled);
        Assert.False(w.Find(MenuAction.ParamSet, (int)Param.LensFov)!.Enabled);
        // The nadir size only applies once the patch is on.
        Assert.False(w.Find(MenuAction.ParamSet, (int)Param.Nadir)!.Enabled);
        Assert.False(w.Find(MenuAction.NadirToggle)!.Active);
        Assert.True(w.Find(MenuAction.FormatProjection, (int)Projection.Dome180)!.Active);
        Assert.Contains("standard", w.Find(MenuAction.ParamReset, (int)Param.VFov)!.Detail);

        var tuned = Windows.Build(State(WindowKind.Format, f: new VideoFormat(Projection.Dome180, StereoMode.SideBySide, VFov: 150, NadirDeg: 30)));
        Assert.True(tuned.Find(MenuAction.ParamSet, (int)Param.Nadir)!.Enabled);
        Assert.True(tuned.Find(MenuAction.NadirToggle)!.Active);
        Assert.Contains("changed", tuned.Find(MenuAction.ParamReset, (int)Param.VFov)!.Detail);
    }

    [Fact]
    public void Same_content_detects_changes()
    {
        var a = Windows.Build(State(WindowKind.Player));
        var b = Windows.Build(State(WindowKind.Player));
        Assert.True(Windows.SameContent(a, b));
        var c = Windows.Build(State(WindowKind.Player) with { SpotTicks = [0.1f, 0.6f] });
        Assert.False(Windows.SameContent(a, c));
    }

    [Fact]
    public void Spot_cells_show_names_and_report_their_slot()
    {
        var w = Windows.Build(State(WindowKind.Spots));
        var cells = w.Elements.Where(e => e.Action == MenuAction.SpotSlot).ToList();
        Assert.Equal(SpotsModel.PerPage, cells.Count);
        Assert.Contains("Muelle", cells[1].Text);
        var hit = w.HitTest((cells[2].Rect.X0 + cells[2].Rect.X1) / 2, (cells[2].Rect.Y0 + cells[2].Rect.Y1) / 2);
        Assert.Equal(2, hit.Arg);
    }
}

public class GazeDwellTests
{
    [Fact]
    public void Fires_once_for_normal_buttons()
    {
        var d = new GazeDwell { DwellSeconds = 1f, RepeatSeconds = 0.25f };
        int fired = 0;
        d.Update(MenuAction.Next, 0, false, 0.1f);
        for (int i = 0; i < 40; i++) if (d.Update(MenuAction.Next, 0, false, 0.1f)) fired++;
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Repeats_and_resets_on_change()
    {
        var d = new GazeDwell { DwellSeconds = 1f, RepeatSeconds = 0.25f };
        int fired = 0;
        d.Update(MenuAction.Fwd10, 0, true, 0f);
        for (int i = 0; i < 20; i++) if (d.Update(MenuAction.Fwd10, 0, true, 0.1f)) fired++;
        Assert.InRange(fired, 4, 5);
        d.Update(MenuAction.Back10, 0, true, 0.1f);
        Assert.Equal(0f, d.Progress);
    }

    [Fact]
    public void Accelerating_repeat_gets_faster()
    {
        var plain = new GazeDwell { DwellSeconds = 1f, RepeatSeconds = 0.5f };
        var fast = new GazeDwell { DwellSeconds = 1f, RepeatSeconds = 0.5f };
        int a = 0, b = 0;
        plain.Update(MenuAction.ParamPlus, 1, true, 0f);
        fast.Update(MenuAction.ParamPlus, 1, true, 0f, accelerate: true);
        for (int i = 0; i < 100; i++)
        {
            if (plain.Update(MenuAction.ParamPlus, 1, true, 0.05f)) a++;
            if (fast.Update(MenuAction.ParamPlus, 1, true, 0.05f, accelerate: true)) b++;
        }
        Assert.True(b > a * 3 / 2, $"{b} vs {a}");
    }
}

public class FormatResolverTests
{
    private static readonly Dictionary<string, VideoFormat> Profiles = new()
    {
        ["Cámara 200°"] = new VideoFormat(Projection.Dome180, StereoMode.SideBySide, HFov: 200, NadirDeg: 30),
    };

    private static FormatChoice R(string name, VideoFormat? video = null, string? videoProfile = null, FolderEntry? folder = null,
        VideoFormat? meta = null, int w = 3840, int h = 1920) =>
        FormatResolver.Resolve(new FormatInputs(video, videoProfile, folder, Profiles, name, w, h, meta));

    [Fact]
    public void Precedence_is_video_then_folder_then_metadata_then_tags_then_auto()
    {
        var folder = new FolderEntry { Profile = "Cámara 200°" };
        var mono360 = new VideoFormat(Projection.Sphere360, StereoMode.Mono);
        Assert.Equal(FormatSource.Video, R("a.mp4", video: mono360, folder: folder).Source);
        Assert.Equal(FormatSource.VideoProfile, R("a.mp4", videoProfile: "Cámara 200°").Source);
        var fromFolder = R("a.mp4", folder: folder, meta: mono360);
        Assert.Equal(FormatSource.FolderProfile, fromFolder.Source);
        Assert.Equal(200f, fromFolder.Format.HFov);
        Assert.Equal("profile Cámara 200°", fromFolder.Note);
        Assert.Equal(FormatScope.Profile, fromFolder.DefaultScope);
        // Tags in the name win over the folder.
        Assert.Equal(FormatSource.NameTags, R("a_360_TB.mp4", folder: folder).Source);
        Assert.Equal(FormatSource.Metadata, R("a.mp4", meta: mono360, w: 1920, h: 1080).Source);
        Assert.Equal(FormatSource.Auto, R("a.mp4").Source);
    }

    [Fact]
    public void Candidates_explain_what_is_used_and_why_not()
    {
        var c = R("a_180_LR.mp4", folder: new FolderEntry { Format = new VideoFormat(Projection.Sphere360, StereoMode.Mono) });
        Assert.Equal(5, c.Candidates.Count);
        Assert.Single(c.Candidates, x => x.Used);
        Assert.Contains(c.Candidates, x => x.Text.Contains("does not apply"));
        Assert.Equal("2:1", FormatResolver.AspectText(3840, 1920));
        Assert.Equal("16:9", FormatResolver.AspectText(1920, 1080));
    }

    [Fact]
    public void Missing_profile_falls_back()
    {
        Assert.Equal(FormatSource.Auto, R("a.mp4", videoProfile: "borrado", folder: new FolderEntry { Profile = "otro" }).Source);
    }
}

public class ParamsTests
{
    [Fact]
    public void Standard_coverage_is_stored_as_zero()
    {
        var f = new VideoFormat(Projection.Dome180, StereoMode.SideBySide);
        var wide = Params.Step(f, Param.HFov, +1);
        Assert.Equal(185f, wide.HFov);
        Assert.True(wide.IsTuned);
        Assert.Equal(f, Params.Step(wide, Param.HFov, -1));
        Assert.Equal(f, Params.Reset(wide, Param.HFov));
        Assert.Equal(360f, Params.Standard(Param.HFov, f with { Projection = Projection.Sphere360 }));
    }

    [Fact]
    public void Values_are_clamped_and_converted()
    {
        var f = new VideoFormat(Projection.Fisheye, StereoMode.Mono);
        Assert.Equal(0.1f, Params.Set(f, Param.CenterX, 10).CenterX, 4);
        Assert.Equal(0.25f, Params.Set(f, Param.CenterX, 99).CenterX, 4);
        Assert.Equal(1.2f, Params.Set(f, Param.Radius, 120).RadiusScale, 3);
        Assert.Equal(280f, Params.Set(f, Param.LensFov, 1000).FisheyeFov);
        var spec = Params.Spec(Param.LensFov);
        Assert.Equal(200f, spec.FromFraction(spec.Fraction(200)));
        Assert.Equal("+10 %", Params.Spec(Param.CenterX).Format(10));
    }

    [Fact]
    public void Applies_by_projection()
    {
        Assert.True(Params.Applies(Param.VFov, new VideoFormat(Projection.Sphere360, StereoMode.Mono)));
        Assert.False(Params.Applies(Param.VFov, new VideoFormat(Projection.Fisheye, StereoMode.Mono)));
        Assert.False(Params.Applies(Param.Nadir, VideoFormat.Default));
    }
}

public class FolderSettingsV2Tests
{
    [Fact]
    public void Profiles_folders_and_named_spots_round_trip_readably()
    {
        var fs = new FolderSettings();
        fs.Profiles["Cámara 200°"] = new VideoFormat(Projection.Dome180, StereoMode.SideBySide, HFov: 200, VFov: 150, NadirDeg: 30);
        fs.SetFolder("x.mp4", new FolderEntry { Profile = "Cámara 200°" });
        var st = new VideoState { PositionMs = 768_000, Pitch = 2, Roll = -3.5f, Profile = "Cámara 200°" };
        SpotList.Add(st.Spots, 135_000);
        SpotList.Add(st.Spots, 1_214_000);
        st.SpotNames[1_214_000] = "Muelle";
        fs.SetVideo("paseo.mp4", st);

        string json = fs.ToJson();
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"coverage\": [", json);
        Assert.Contains("\"name\": \"Muelle\"", json);
        Assert.Contains("\"horizon\"", json);

        var back = FolderSettings.Parse(json);
        Assert.Equal(200f, back.GetFolderFormat("y.mp4")!.Value.HFov);
        var v = back.GetVideo("paseo.mp4")!;
        Assert.Equal("Cámara 200°", v.Profile);
        Assert.Equal(-3.5f, v.Roll);
        Assert.Equal("Muelle", v.SpotNames[1_214_000]);
        Assert.Equal([135_000L, 1_214_000L], v.Spots);
    }

    [Fact]
    public void Version_1_files_are_migrated()
    {
        const string json = """
            {
              "version": 1,
              "videos": { "a.mp4": { "position": 3000, "roll": 2.5, "format": "Fisheye,SideBySide,0,200,lens=Equisolid,nadir=30", "spots": [9000, 1000] } },
              "folders": { "": "Dome180,SideBySide,0,190,v=150" }
            }
            """;
        var fs = FolderSettings.Parse(json);
        var v = fs.GetVideo("a.mp4")!;
        Assert.Equal(2.5f, v.Roll);
        Assert.Equal(FisheyeLens.Equisolid, v.FormatOverride!.Value.Lens);
        Assert.Equal(200f, v.FormatOverride.Value.FisheyeFov);
        Assert.Equal([1000L, 9000L], v.Spots);
        Assert.Equal(150f, fs.GetFolderFormat("b.mp4")!.Value.VFov);

        string rewritten = fs.ToJson();
        Assert.DoesNotContain("\"roll\": 2.5,\n      \"spots", rewritten);
        var again = FolderSettings.Parse(rewritten);
        Assert.Equal(2.5f, again.GetVideo("a.mp4")!.Roll);
        Assert.Equal(30f, again.GetVideo("a.mp4")!.FormatOverride!.Value.NadirDeg);
    }

    [Fact]
    public void Deleting_and_renaming_profiles_keeps_references_working()
    {
        var fs = new FolderSettings();
        var f = new VideoFormat(Projection.Sphere360, StereoMode.TopBottom, VFov: 160);
        fs.Profiles["A"] = f;
        fs.SetFolder("x.mp4", new FolderEntry { Profile = "A" });
        fs.SetVideo("v.mp4", new VideoState { Profile = "A" });
        Assert.True(fs.RenameProfile("A", "B"));
        Assert.Equal("B", fs.GetFolder("x.mp4")!.Profile);
        Assert.Equal("B", fs.GetVideo("v.mp4")!.Profile);
        fs.DeleteProfile("B");
        Assert.Equal(f, fs.GetFolderFormat("x.mp4"));
        Assert.Equal(f, fs.GetVideo("v.mp4")!.FormatOverride);
        Assert.Equal("A", fs.NewProfileName("A"));
        fs.Profiles["A"] = f;
        Assert.Equal("A 2", fs.NewProfileName("A"));
    }
}
