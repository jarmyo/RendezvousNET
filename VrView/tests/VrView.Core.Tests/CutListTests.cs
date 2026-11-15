using VrView.Core;

namespace VrView.Core.Tests;

public class CutListTests
{
    [Fact]
    public void Add_merges_overlapping_parts_and_keeps_order()
    {
        var cuts = new List<CutRange>();
        Assert.True(CutList.Add(cuts, 300_000, 200_000));   // any order
        Assert.True(CutList.Add(cuts, 0, 60_000));
        Assert.True(CutList.Add(cuts, 250_000, 400_000));   // overlaps the first
        Assert.Equal([new CutRange(0, 60_000), new CutRange(200_000, 400_000)], cuts);
    }

    [Fact]
    public void Add_ignores_tiny_parts_and_clamps_to_the_duration()
    {
        var cuts = new List<CutRange>();
        Assert.False(CutList.Add(cuts, 10_000, 10_200));
        Assert.True(CutList.Add(cuts, 590_000, 700_000, durationMs: 600_000));
        Assert.Equal([new CutRange(590_000, 600_000)], cuts);
    }

    [Fact]
    public void Remap_moves_times_back_by_what_was_removed_before_them()
    {
        // The example: a 10 min video, a spot at 8:00, 0:00–3:00 removed → the spot is at 5:00.
        CutRange[] removed = [new(0, 180_000)];
        Assert.Equal(300_000, CutList.Remap(removed, 480_000));
    }

    [Fact]
    public void Remap_with_several_parts_and_times_inside_them()
    {
        CutRange[] removed = [new(60_000, 120_000), new(300_000, 360_000)];
        Assert.Equal(30_000, CutList.Remap(removed, 30_000));     // before everything
        Assert.Equal(60_000, CutList.Remap(removed, 90_000));     // inside the first → its cut point
        Assert.Equal(140_000, CutList.Remap(removed, 200_000));   // after the first
        Assert.Equal(240_000, CutList.Remap(removed, 330_000));   // inside the second → 300 − 60
        Assert.Equal(340_000, CutList.Remap(removed, 460_000));   // after both: − 120
    }

    [Fact]
    public void SkipTarget_only_skips_parts_entered_while_playing()
    {
        CutRange[] cuts = [new(60_000, 120_000)];
        Assert.Equal(120_000, CutList.SkipTarget(cuts, 59_800, 60_050));
        Assert.Null(CutList.SkipTarget(cuts, 60_000, 60_250));    // jumped to the start on purpose
        Assert.Null(CutList.SkipTarget(cuts, 90_000, 90_250));    // already inside (jumped there)
        Assert.Null(CutList.SkipTarget(cuts, 80_000, 50_000));    // went back
        Assert.Null(CutList.SkipTarget(cuts, 50_000, 130_000));   // jumped over it
    }

    [Fact]
    public void Complement_turns_kept_parts_into_removed_parts()
    {
        CutRange[] keep = [new(180_000, 400_000), new(450_000, 600_000)];
        Assert.Equal([new CutRange(0, 180_000), new CutRange(400_000, 450_000)], CutList.Complement(keep, 600_000));
        Assert.Equal([new CutRange(0, 1000)], CutList.Complement([], 1000));
    }

    [Fact]
    public void Previous_and_next_go_by_cut_starts()
    {
        CutRange[] cuts = [new(10_000, 20_000), new(50_000, 60_000)];
        Assert.Equal(new CutRange(50_000, 60_000), CutList.Next(cuts, 10_000));
        Assert.Null(CutList.Next(cuts, 50_000));
        Assert.Equal(new CutRange(10_000, 20_000), CutList.Previous(cuts, 51_000));
        Assert.Equal(new CutRange(50_000, 60_000), CutList.Previous(cuts, 70_000));
    }

    [Fact]
    public void BelongsTo_tolerates_rounding_but_not_an_edited_file()
    {
        Assert.True(CutList.BelongsTo(600_000, 600_900));
        Assert.True(CutList.BelongsTo(0, 420_000));
        Assert.False(CutList.BelongsTo(600_000, 420_000));
    }

    [Fact]
    public void Cuts_survive_the_preferences_format()
    {
        var s = new VideoState { CutsDurationMs = 600_000 };
        CutList.Add(s.Cuts, 0, 180_000);
        CutList.Add(s.Cuts, 400_000, 420_000);
        var back = VideoState.Parse(s.Serialize());
        Assert.Equal(s.Cuts, back.Cuts);
        Assert.Equal(600_000, back.CutsDurationMs);
        Assert.False(new VideoState { Cuts = [new(0, 1000)] }.IsEmpty);
    }
}

public class FolderSettingsCutsTests
{
    private const string Json = """
        {
          "version": 2,
          "videos": {
            "VR/paseo_180_LR.mp4": {
              "position": 480000,
              "format": { "projection": "180", "eyes": "sbs" },
              "horizon": { "pitch": 2, "roll": -3.5, "yaw": 0 },
              "spots": [ 60000, { "at": 480000, "name": "Muelle" }, 540000 ],
              "cuts": { "duration": 600000, "ranges": [ [0, 180000] ] }
            }
          }
        }
        """;

    [Fact]
    public void Cuts_are_read_and_written_with_the_video()
    {
        var s = FolderSettings.Parse(Json);
        var v = s.GetVideo("VR/paseo_180_LR.mp4")!;
        Assert.Equal([new CutRange(0, 180_000)], v.Cuts);
        Assert.Equal(600_000, v.CutsDurationMs);

        string json = s.ToJson();
        Assert.Contains("\"cuts\"", json);
        Assert.Equal(v.Cuts, FolderSettings.Parse(json).GetVideo("VR/paseo_180_LR.mp4")!.Cuts);
    }

    [Fact]
    public void ApplyEdit_in_place_moves_spots_and_position_and_clears_the_cuts()
    {
        var s = FolderSettings.Parse(Json);
        var result = s.ApplyEdit("VR/paseo_180_LR.mp4", "VR/paseo_180_LR.mp4", [new CutRange(0, 180_000)]);

        var v = s.GetVideo("VR/paseo_180_LR.mp4")!;
        // 60 s was inside the removed part → cut point (0); 8:00 → 5:00 keeps its name; 9:00 → 6:00.
        Assert.Equal([0L, 300_000L, 360_000L], v.Spots);
        Assert.Equal("Muelle", v.SpotNames[300_000]);
        Assert.Equal(300_000, v.PositionMs);
        Assert.Empty(v.Cuts);
        Assert.Equal(Projection.Dome180, v.FormatOverride!.Value.Projection);
        Assert.Equal(-3.5f, v.Roll);
        Assert.Equal(new EditResult(3, 1, 0), result);
    }

    [Fact]
    public void ApplyEdit_to_a_copy_keeps_the_original_without_cuts()
    {
        var s = FolderSettings.Parse(Json);
        s.ApplyEdit("VR/paseo_180_LR.mp4", "VR/cut_paseo_180_LR.mp4", [new CutRange(0, 180_000)]);

        var original = s.GetVideo("VR/paseo_180_LR.mp4")!;
        Assert.Empty(original.Cuts);
        Assert.Equal(480_000, original.PositionMs);
        Assert.Equal(3, original.Spots.Count);
        Assert.Equal([0L, 300_000L, 360_000L], s.GetVideo("VR/cut_paseo_180_LR.mp4")!.Spots);
    }

    [Fact]
    public void ApplyEdit_merges_spots_that_end_up_together()
    {
        var s = new FolderSettings();
        var st = new VideoState();
        SpotList.Add(st.Spots, 100_000);
        SpotList.Add(st.Spots, 150_000);   // both inside the removed part → same cut point
        SpotList.Add(st.Spots, 250_000);
        s.SetVideo("a.mp4", st);
        var result = s.ApplyEdit("a.mp4", "a.mp4", [new CutRange(90_000, 200_000)]);
        Assert.Equal([90_000L, 140_000L], s.GetVideo("a.mp4")!.Spots);
        Assert.Equal(new EditResult(2, 2, 1), result);
    }
}

public class FolderSettingsLocatorTests
{
    [Fact]
    public void Candidates_go_up_with_the_key_relative_to_each_folder()
    {
        var c = FolderSettingsLocator.Candidates("/sdcard/Movies/VR/clip.mp4").ToList();
        Assert.Equal(("/sdcard/Movies/VR", "clip.mp4"), c[0]);
        Assert.Equal(("/sdcard/Movies", "VR/clip.mp4"), c[1]);
        Assert.Equal(("/sdcard", "Movies/VR/clip.mp4"), c[2]);
        Assert.Equal(("/", "sdcard/Movies/VR/clip.mp4"), c[3]);
    }

    [Fact]
    public void Candidates_understand_windows_paths()
    {
        var c = FolderSettingsLocator.Candidates(@"C:\Videos\VR\clip.mp4").ToList();
        Assert.Equal((@"C:\Videos\VR", "clip.mp4"), c[0]);
        Assert.Equal((@"C:\Videos", "VR/clip.mp4"), c[1]);
        Assert.Equal((@"C:\", "Videos/VR/clip.mp4"), c[2]);
    }

    [Fact]
    public void Find_picks_the_file_that_knows_the_video()
    {
        var inner = new FolderSettings();   // a file without this video
        var outer = new FolderSettings();
        outer.SetVideo("VR/clip.mp4", new VideoState { PositionMs = 5000 });
        var files = new Dictionary<string, FolderSettings> { ["/m/VR"] = inner, ["/m"] = outer };
        var found = FolderSettingsLocator.Find("/m/VR/clip.mp4", f => files.GetValueOrDefault(f));
        Assert.Equal("/m", found!.Value.Folder);
        Assert.Equal("VR/clip.mp4", found.Value.Key);
    }
}
