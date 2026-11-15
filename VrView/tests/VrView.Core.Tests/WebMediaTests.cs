using VrView.Core;

namespace VrView.Core.Tests;

public class WebMediaTests
{
    [Theory]
    [InlineData("https://cdn.example.com/v/playa_180_LR.mp4?token=abc", StreamKind.File)]
    [InlineData("https://cdn.example.com/hls/master.m3u8", StreamKind.Hls)]
    [InlineData("https://cdn.example.com/dash/manifest.mpd?x=1", StreamKind.Dash)]
    [InlineData("https://cdn.example.com/watch?id=5", null)]
    [InlineData("https://cdn.example.com/hls/seg-00012.mp4", null)]
    [InlineData("https://cdn.example.com/hls/init.mp4", null)]
    [InlineData("https://cdn.example.com/frag/17.mp4", null)]
    [InlineData("blob:https://example.com/123", null)]
    public void Classify_recognizes_videos_and_skips_pieces_of_streams(string url, StreamKind? kind) =>
        Assert.Equal(kind, MediaSniffer.Classify(url));

    [Fact]
    public void Classify_uses_the_mime_type_when_the_url_says_nothing()
    {
        Assert.Equal(StreamKind.Hls, MediaSniffer.Classify("https://x.com/play?id=1", "application/vnd.apple.mpegurl"));
        Assert.Equal(StreamKind.File, MediaSniffer.Classify("https://x.com/file?id=1", "video/mp4"));
    }

    [Fact]
    public void DisplayName_keeps_format_tags_of_the_file()
    {
        Assert.Equal("playa_180_LR.mp4", MediaSniffer.DisplayName("https://x.com/v/playa_180_LR.mp4?t=1", "Playa al atardecer"));
        Assert.Equal("Playa al atardecer.m3u8", MediaSniffer.DisplayName("https://x.com/hls/master.m3u8", "Playa al atardecer"));
        Assert.Equal("master.m3u8", MediaSniffer.DisplayName("https://x.com/hls/master.m3u8", ""));
    }

    [Fact]
    public void Detected_list_replaces_the_same_video_with_a_new_token()
    {
        var list = new DetectedMediaList();
        Assert.True(list.Add("https://x.com/a.mp4?t=1", "https://x.com/page", "Page"));
        Assert.True(list.Add("https://x.com/master.m3u8", "https://x.com/page", "Page"));
        Assert.True(list.Add("https://x.com/a.mp4?t=2", "https://x.com/page", "Page"));
        Assert.False(list.Add("https://x.com/a.mp4?t=2", "https://x.com/page", "Page"));
        Assert.False(list.Add("https://x.com/style.css", "https://x.com/page", "Page"));
        Assert.Equal(["https://x.com/a.mp4?t=2", "https://x.com/master.m3u8"], list.Items.Select(i => i.Url));
    }

    [Fact]
    public void Saving_a_video_again_from_its_page_renews_the_address()
    {
        var b = new WebBookmarks();
        var first = b.SaveVideo(new DetectedMedia("https://cdn.x.com/v.m3u8?exp=1", StreamKind.Hls, "https://x.com/video/9", "Nueve"), "Nueve", 1000);
        var again = b.SaveVideo(new DetectedMedia("https://cdn2.x.com/v.m3u8?exp=2", StreamKind.Hls, "https://x.com/video/9#play", "Nueve"), "", 2000);
        Assert.Same(first, again);
        Assert.Single(b.Videos);
        Assert.Equal("https://cdn2.x.com/v.m3u8?exp=2", b.Videos[0].MediaUrl);
        Assert.Equal("web:" + first.Id, first.StateKey);
    }

    [Fact]
    public void Bookmarks_round_trip_through_json()
    {
        var b = new WebBookmarks();
        Assert.True(b.ToggleSite("https://vr.example.com/", "VR Example"));
        b.SaveVideo(new DetectedMedia("https://x.com/a_360_TB.mp4", StreamKind.File, "https://x.com/p", "P"), "A", 5);
        b.SaveVideo(new DetectedMedia("https://x.com/b.m3u8", StreamKind.Hls, "https://x.com/q", "Q"), "B", 6);

        var back = WebBookmarks.Parse(b.ToJson());
        Assert.Equal("VR Example", back.Sites[0].Title);
        Assert.NotNull(back.FindSite("https://vr.example.com"));
        Assert.Equal(["B", "A"], back.Videos.Select(v => v.Title));
        Assert.Equal(StreamKind.Hls, back.Videos[0].Kind);
        Assert.Equal(StreamKind.File, back.Videos[1].Kind);
        Assert.False(back.ToggleSite("https://vr.example.com", ""));
        Assert.Empty(back.Sites);
        Assert.Empty(WebBookmarks.Parse("{ broken").Videos);
    }

    [Theory]
    [InlineData("example.com/vr", "https://example.com/vr")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("videos vr 180", "https://duckduckgo.com/?q=videos%20vr%20180")]
    public void Typed_addresses_become_urls(string input, string url) => Assert.Equal(url, WebAddress.FromInput(input));
}
