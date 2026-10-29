using VrView.Core;

namespace VrView.Core.Tests;

public class SpotListTests
{
    [Fact]
    public void Add_keeps_order_and_ignores_near_duplicates()
    {
        var spots = new List<long>();
        Assert.True(SpotList.Add(spots, 50_000));
        Assert.True(SpotList.Add(spots, 10_000));
        Assert.True(SpotList.Add(spots, 30_000));
        Assert.False(SpotList.Add(spots, 30_400));
        Assert.Equal([10_000L, 30_000L, 50_000L], spots);
    }

    [Fact]
    public void Previous_and_next_navigate_like_track_buttons()
    {
        long[] spots = [10_000, 30_000, 50_000];
        Assert.Equal(30_000, SpotList.Next(spots, 10_000));      // on a spot → the following one
        Assert.Equal(10_000, SpotList.Next(spots, 0));
        Assert.Null(SpotList.Next(spots, 50_000));
        Assert.Equal(30_000, SpotList.Previous(spots, 40_000));
        Assert.Equal(10_000, SpotList.Previous(spots, 31_000));  // just passed 30 s → go further back
        Assert.Null(SpotList.Previous(spots, 11_000));
    }
}

public class VideoStateSpotsTests
{
    [Fact]
    public void Spots_survive_the_preferences_format()
    {
        var s = new VideoState { PositionMs = 1234 };
        SpotList.Add(s.Spots, 90_000);
        SpotList.Add(s.Spots, 5_000);
        var back = VideoState.Parse(s.Serialize());
        Assert.Equal([5_000L, 90_000L], back.Spots);
        Assert.Equal(1234, back.PositionMs);
    }
}

public class FolderSettingsTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        var fs = new FolderSettings();
        var state = new VideoState
        {
            PositionMs = 61_000,
            Roll = -3.25f,
            FormatOverride = new VideoFormat(Projection.Dome180, StereoMode.SideBySide),
        };
        SpotList.Add(state.Spots, 12_000);
        fs.SetVideo("VR/clip.mp4", state);
        fs.SetFolderFormat("VR/clip.mp4", new VideoFormat(Projection.Sphere360, StereoMode.TopBottom));

        string json = fs.ToJson();
        Assert.Contains("\"videos\"", json);
        Assert.Contains("\"VR/clip.mp4\"", json);

        var back = FolderSettings.Parse(json);
        var v = back.GetVideo("VR/clip.mp4")!;
        Assert.Equal(61_000, v.PositionMs);
        Assert.Equal(-3.25f, v.Roll);
        Assert.Equal(Projection.Dome180, v.FormatOverride!.Value.Projection);
        Assert.Equal([12_000L], v.Spots);
        Assert.Equal(Projection.Sphere360, back.GetFolderFormat("VR/other.mp4")!.Value.Projection);
        Assert.Null(back.GetFolderFormat("other.mp4"));
    }

    [Fact]
    public void Empty_states_are_not_stored()
    {
        var fs = new FolderSettings();
        fs.SetVideo("a.mp4", new VideoState { PositionMs = 5 });
        fs.SetVideo("a.mp4", new VideoState());
        Assert.Empty(fs.Videos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    public void Damaged_files_give_empty_settings(string? json)
    {
        var fs = FolderSettings.Parse(json);
        Assert.Empty(fs.Videos);
        Assert.Null(fs.GetVideo("x.mp4"));
    }

    [Fact]
    public void Hand_edited_file_is_read()
    {
        const string json = """
            {
              "videos": {
                "a_180_LR.mp4": { "position": 3000, "roll": 2.5, "spots": [ 9000, 1000 ] }
              },
              "folders": { "": "Dome180,SideBySide,0,190" }
            }
            """;
        var fs = FolderSettings.Parse(json);
        Assert.Equal([1000L, 9000L], fs.GetVideo("a_180_LR.mp4")!.Spots);
        Assert.Equal(StereoMode.SideBySide, fs.GetFolderFormat("b.mp4")!.Value.Stereo);
    }
}

public class ThumbnailCropTests
{
    [Fact]
    public void Side_by_side_180_uses_the_center_of_the_left_eye()
    {
        var (x0, y0, x1, y1) = new VideoFormat(Projection.Dome180, StereoMode.SideBySide).ThumbnailCrop();
        Assert.True(x0 > 0f && x1 < 0.5f);
        Assert.Equal(0.25f, (x0 + x1) / 2, 3);
        Assert.Equal(0.5f, (y0 + y1) / 2, 3);
    }

    [Fact]
    public void Flat_mono_uses_the_whole_frame()
    {
        Assert.Equal((0f, 0f, 1f, 1f), VideoFormat.Default.ThumbnailCrop());
    }
}

public class SpotsMenuTests
{
    [Fact]
    public void Spot_thumbnails_report_their_slot()
    {
        var slots = VrMenuLayout.Buttons(MenuPage.Spots).Where(b => b.Action == MenuAction.SpotSlot).ToList();
        Assert.Equal(VrMenuLayout.SpotsPerPage, slots.Count);
        var third = slots[2];
        var hit = VrMenuLayout.HitTest(MenuPage.Spots, (third.X0 + third.X1) / 2, (third.Y0 + third.Y1) / 2);
        Assert.Equal(MenuAction.SpotSlot, hit.Action);
        Assert.Equal(2, hit.Slot);
    }
}

public class TreeOfTests
{
    [Fact]
    public void Finds_the_picked_folder_of_a_document()
    {
        const string tree = "content://com.android.externalstorage.documents/tree/primary%3AMovies";
        Assert.Equal(tree, MediaUtil.TreeOf(tree + "/document/primary%3AMovies%2Fa.mp4"));
        Assert.Null(MediaUtil.TreeOf("content://media/external/video/media/42"));
        Assert.Null(MediaUtil.TreeOf("content://com.android.providers.downloads.documents/document/msf%3A12"));
    }
}
