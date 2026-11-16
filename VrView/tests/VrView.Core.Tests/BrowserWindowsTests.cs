using VrView.Core;
using Xunit;

namespace VrView.Core.Tests;

public class BrowserWindowsTests
{
    private static List<SiteBookmark> Sites(int n) =>
        Enumerable.Range(0, n).Select(i => new SiteBookmark { Title = "Site " + i, Url = $"https://site{i}.example/" }).ToList();

    public static IEnumerable<object[]> Models()
    {
        var bar = new BrowserUiModel(BrowserPane.Bar, "Page", "https://example.com/", VideoCount: 2);
        yield return [bar];
        yield return [bar with { Progress = 40, CanGoBack = true, SiteSaved = true }];
        yield return [bar with { Fullscreen = true, Format = new VideoFormat(Projection.Dome180, StereoMode.SideBySide) }];
        yield return [bar with { Fullscreen = true, BigScreen = true, Playing = true }];
        yield return [bar with { Pane = BrowserPane.Formats, Fullscreen = true }];
        yield return [bar with { Pane = BrowserPane.Sites, Sites = Sites(0) }];
        yield return [bar with { Pane = BrowserPane.Sites, Sites = Sites(5) }];
        yield return [bar with { Pane = BrowserPane.Sites, Sites = Sites(20), SitesPage = 2 }];
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void Browser_windows_fit_and_do_not_overlap(BrowserUiModel m)
    {
        var win = Windows.Browser(m);
        Assert.Equal(WindowKind.Browser, win.Kind);
        foreach (var e in win.Elements)
            Assert.True(e.Rect.X0 >= 0 && e.Rect.X1 <= 1 && e.Rect.Y0 >= 0 && e.Rect.Y1 <= 1 && e.Rect.X1 > e.Rect.X0, $"{e.Action} {e.Text}");
        var clickable = win.Elements.Where(e => e.Action != MenuAction.None).ToList();
        for (int i = 0; i < clickable.Count; i++)
            for (int j = i + 1; j < clickable.Count; j++)
            {
                var a = clickable[i].Rect; var c = clickable[j].Rect;
                Assert.False(a.X0 < c.X1 - 1e-4 && c.X0 < a.X1 - 1e-4 && a.Y0 < c.Y1 - 1e-4 && c.Y0 < a.Y1 - 1e-4,
                    $"{clickable[i].Text} overlaps {clickable[j].Text}");
            }
    }

    [Fact]
    public void Fullscreen_offers_every_format_and_marks_the_current_one()
    {
        var current = new VideoFormat(Projection.Sphere360, StereoMode.TopBottom);
        var win = Windows.Browser(new BrowserUiModel(BrowserPane.Formats, "", "", Fullscreen: true, Format: current));
        var tiles = win.Elements.Where(e => e.Action == MenuAction.WebFormat).ToList();
        Assert.Equal(BrowserUiModel.Formats.Count, tiles.Count);
        Assert.Single(tiles, t => t.Active);
        Assert.Equal(current, BrowserUiModel.Formats[tiles.Single(t => t.Active).Arg]);
    }

    [Fact]
    public void Sites_are_paged_and_point_to_their_index()
    {
        var m = new BrowserUiModel(BrowserPane.Sites, "", "", Sites: Sites(11), SitesPage: 1);
        var cells = Windows.Browser(m).Elements.Where(e => e.Action == MenuAction.WebSite).ToList();
        Assert.Equal(3, cells.Count);
        Assert.Equal(8, cells[0].Arg);
        Assert.Equal("Site 10", cells[2].Text);
    }

    [Fact]
    public void Player_button_needs_a_detected_video_and_the_bar_cannot_be_closed()
    {
        var none = Windows.Browser(new BrowserUiModel(BrowserPane.Bar, "", ""));
        Assert.False(none.Find(MenuAction.WebPlayer)!.Enabled);
        Assert.DoesNotContain(none.Elements, e => e.Action == MenuAction.Hide);
        var some = Windows.Browser(new BrowserUiModel(BrowserPane.Bar, "", "", VideoCount: 1));
        Assert.True(some.Find(MenuAction.WebPlayer)!.Enabled);
        Assert.Contains("(1)", some.Find(MenuAction.WebPlayer)!.Text);
    }
}
