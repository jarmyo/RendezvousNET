using VrView.Core;
using Xunit;

namespace VrView.Core.Tests;

public class AppLinksTests
{
    [Fact]
    public void Headers_survive_the_trip_between_the_apps()
    {
        var sent = new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (Linux; Android 16) Chrome/140",
            ["Referer"] = "https://example.com/watch?v=1",
            ["Cookie"] = "a=1; b=x:y",
        };
        var got = AppLinks.DecodeHeaders(AppLinks.EncodeHeaders(sent));
        Assert.Equal(sent, got);
    }

    [Fact]
    public void Line_breaks_cannot_add_headers()
    {
        var got = AppLinks.DecodeHeaders(AppLinks.EncodeHeaders(new Dictionary<string, string> { ["Cookie"] = "a=1\r\nEvil: 1" }));
        Assert.Single(got);
        Assert.Equal("a=1 Evil: 1", got["Cookie"]);
    }

    [Fact]
    public void Only_viewer_and_web_settings_are_shared()
    {
        Assert.True(AppLinks.IsShared("fov"));
        Assert.True(AppLinks.IsShared("viewerUrl"));
        Assert.True(AppLinks.IsShared("webFormat.example.com"));
        Assert.True(AppLinks.IsWeb("webBookmarks"));
        Assert.False(AppLinks.IsShared("folder"));
        Assert.False(AppLinks.IsShared("lastVideo"));
        Assert.Empty(AppLinks.DecodeHeaders(null));
    }
}
