using VrView.Core;

namespace VrView.Core.Tests;

public class FormatDetectorTests
{
    [Theory]
    [InlineData("beach_180_LR.mp4", Projection.Dome180, StereoMode.SideBySide, false)]
    [InlineData("city_360_TB.mp4", Projection.Sphere360, StereoMode.TopBottom, false)]
    [InlineData("concert_360_mono.mp4", Projection.Sphere360, StereoMode.Mono, false)]
    [InlineData("hike_180x180_3dh.mp4", Projection.Dome180, StereoMode.SideBySide, false)]
    [InlineData("Movie.2019.3D.HSBS.1080p.mkv", Projection.Flat, StereoMode.SideBySide, false)]
    [InlineData("trip-VR180.mp4", Projection.Dome180, StereoMode.SideBySide, false)]
    [InlineData("scene_360LR.mp4", Projection.Sphere360, StereoMode.SideBySide, false)]
    [InlineData("scene_180_RL.mp4", Projection.Dome180, StereoMode.SideBySide, true)]
    [InlineData("scene_360_BT.mp4", Projection.Sphere360, StereoMode.TopBottom, true)]
    [InlineData("demo_OU_360.mp4", Projection.Sphere360, StereoMode.TopBottom, false)]
    [InlineData("demo 180 over under.mp4", Projection.Dome180, StereoMode.TopBottom, false)]
    [InlineData("demo side by side 180.mp4", Projection.Dome180, StereoMode.SideBySide, false)]
    [InlineData("cam_fisheye190_LR.mp4", Projection.Fisheye, StereoMode.SideBySide, false)]
    [InlineData("holiday.mp4", Projection.Flat, StereoMode.Mono, false)]
    [InlineData("IMG_3600.mp4", Projection.Flat, StereoMode.Mono, false)]
    public void Detects_from_file_name(string name, Projection p, StereoMode s, bool swap)
    {
        var f = FormatDetector.Detect(name);
        Assert.Equal(p, f.Projection);
        Assert.Equal(s, f.Stereo);
        Assert.Equal(swap, f.SwapEyes);
    }

    [Fact]
    public void Fisheye_fov_is_read_from_tag()
    {
        Assert.Equal(200f, FormatDetector.Detect("x_MKX200.mp4").FisheyeFov);
        Assert.Equal(190f, FormatDetector.Detect("x_fisheye.mp4").FisheyeFov);
    }

    [Theory]
    // Projection tag only: stereo layout inferred from the frame shape.
    [InlineData("a_180.mp4", 5760, 2880, Projection.Dome180, StereoMode.SideBySide)]
    [InlineData("a_180.mp4", 2880, 2880, Projection.Dome180, StereoMode.Mono)]
    [InlineData("a_360.mp4", 5760, 2880, Projection.Sphere360, StereoMode.Mono)]
    [InlineData("a_360.mp4", 4096, 4096, Projection.Sphere360, StereoMode.TopBottom)]
    // Stereo tag only: projection inferred from the frame shape.
    [InlineData("a_LR.mp4", 3840, 1080, Projection.Flat, StereoMode.SideBySide)]
    [InlineData("a_SBS.mp4", 1920, 1080, Projection.Flat, StereoMode.SideBySide)]
    [InlineData("a_SBS.mp4", 4096, 2048, Projection.Dome180, StereoMode.SideBySide)]
    [InlineData("a_TB.mp4", 4096, 4096, Projection.Sphere360, StereoMode.TopBottom)]
    [InlineData("a_TB.mp4", 1920, 1080, Projection.Flat, StereoMode.TopBottom)]
    // No tags at all.
    [InlineData("a.mp4", 3840, 1920, Projection.Sphere360, StereoMode.Mono)]
    [InlineData("a.mp4", 1920, 1080, Projection.Flat, StereoMode.Mono)]
    [InlineData("a.mp4", 1080, 1080, Projection.Flat, StereoMode.Mono)]
    public void Uses_frame_size_when_tags_are_incomplete(string name, int w, int h, Projection p, StereoMode s)
    {
        var f = FormatDetector.Detect(name, w, h);
        Assert.Equal(p, f.Projection);
        Assert.Equal(s, f.Stereo);
    }

    [Fact]
    public void Explicit_tags_win_over_frame_size()
    {
        var f = FormatDetector.Detect("x_360_LR.mp4", 1920, 1080);
        Assert.Equal(Projection.Sphere360, f.Projection);
        Assert.Equal(StereoMode.SideBySide, f.Stereo);
    }

    [Fact]
    public void HasTags_reports_presence_of_tags()
    {
        Assert.True(FormatDetector.HasTags("x_SBS.mp4"));
        Assert.False(FormatDetector.HasTags("vacaciones 2024.mp4"));
    }
}
