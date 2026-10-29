using VrView.Core;

namespace VrView.Core.Tests;

public class MiscTests
{
    [Fact]
    public void Natural_sort_orders_numbers_by_value()
    {
        var names = new[] { "clip10.mp4", "Clip2.mp4", "clip1.mp4", "a.mp4", "clip02b.mp4" };
        var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(new[] { "a.mp4", "clip1.mp4", "Clip2.mp4", "clip02b.mp4", "clip10.mp4" }, sorted);
    }

    [Theory]
    [InlineData("a.MP4", null, true)]
    [InlineData("a.mkv", null, true)]
    [InlineData("a.txt", null, false)]
    [InlineData("noext", "video/mp4", true)]
    [InlineData("a.jpg", "image/jpeg", false)]
    public void Recognizes_video_files(string name, string? mime, bool expected) =>
        Assert.Equal(expected, MediaUtil.IsVideo(name, mime));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65_000, "1:05")]
    [InlineData(3_725_000, "1:02:05")]
    public void Formats_time(long ms, string expected) => Assert.Equal(expected, MediaUtil.FormatTime(ms));

    [Theory]
    [InlineData(2_000, 600_000, 0)]
    [InlineData(120_000, 600_000, 120_000)]
    [InlineData(595_000, 600_000, 0)]
    public void Resume_position_ignores_start_and_end(long saved, long duration, long expected) =>
        Assert.Equal(expected, MediaUtil.ResumePosition(saved, duration));

    [Fact]
    public void Video_state_round_trips()
    {
        var s = new VideoState
        {
            PositionMs = 123456,
            Pitch = -2.5f,
            Roll = 1.25f,
            Yaw = 30,
            FormatOverride = new VideoFormat(Projection.Fisheye, StereoMode.SideBySide, true, 200),
        };
        var r = VideoState.Parse(s.Serialize());
        Assert.Equal(123456, r.PositionMs);
        Assert.Equal(-2.5f, r.Pitch);
        Assert.Equal(1.25f, r.Roll);
        Assert.Equal(30f, r.Yaw);
        Assert.Equal(s.FormatOverride, r.FormatOverride);
        Assert.Null(VideoState.Parse("pos=5").FormatOverride);
        Assert.Equal(0, VideoState.Parse("garbage;;=").PositionMs);
    }

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-180, 180)]
    [InlineData(540, 180)]
    [InlineData(-45, -45)]
    public void Normalizes_angles(float input, float expected) =>
        Assert.Equal(expected, VideoState.NormalizeAngle(input), 3);

    [Fact]
    public void Eye_rects_for_stereo_layouts()
    {
        var sbs = new VideoFormat(Projection.Dome180, StereoMode.SideBySide);
        Assert.Equal(new UvRect(0.5f, 1f, 0f, 0f), sbs.EyeRect(0));
        Assert.Equal(new UvRect(0.5f, 1f, 0.5f, 0f), sbs.EyeRect(1));
        // Top-bottom: left eye is the top half, which is v in [0.5, 1] in GL texture space.
        var tb = new VideoFormat(Projection.Sphere360, StereoMode.TopBottom);
        Assert.Equal(new UvRect(1f, 0.5f, 0f, 0.5f), tb.EyeRect(0));
        var rl = sbs with { SwapEyes = true };
        Assert.Equal(sbs.EyeRect(1), rl.EyeRect(0));
        var mono = VideoFormat.Default;
        Assert.Equal(mono.EyeRect(0), mono.EyeRect(1));
    }

    [Fact]
    public void Eye_aspect_handles_half_and_full_stereo()
    {
        Assert.Equal(16f / 9f, new VideoFormat(Projection.Flat, StereoMode.SideBySide).EyeAspect(1920, 1080), 3); // half SBS
        Assert.Equal(16f / 9f, new VideoFormat(Projection.Flat, StereoMode.SideBySide).EyeAspect(3840, 1080), 3); // full SBS
        Assert.Equal(16f / 9f, new VideoFormat(Projection.Flat, StereoMode.TopBottom).EyeAspect(1920, 1080), 3); // half TB
        Assert.Equal(16f / 9f, new VideoFormat(Projection.Flat, StereoMode.TopBottom).EyeAspect(1920, 2160), 3); // full TB
    }

    [Fact]
    public void Format_serialization_round_trips_all_manual_choices()
    {
        foreach (var f in VideoFormat.ManualChoices)
            Assert.Equal(f, VideoFormat.TryParse(f.Serialize()));
        Assert.Null(VideoFormat.TryParse("nonsense"));
    }
}
