using VrView.Core;

namespace VrView.Core.Tests;

public class VrMenuTests
{
    [Fact]
    public void Buttons_do_not_overlap_and_stay_inside_panel()
    {
        foreach (MenuPage page in Enum.GetValues<MenuPage>())
        {
            var buttons = VrMenuLayout.Buttons(page);
            foreach (var b in buttons)
            {
                Assert.InRange(b.X0, 0f, 1f); Assert.InRange(b.X1, 0f, 1f);
                Assert.InRange(b.Y0, 0f, 1f); Assert.InRange(b.Y1, 0f, 1f);
                Assert.True(b.X1 > b.X0 && b.Y1 > b.Y0);
            }
            for (int i = 0; i < buttons.Count; i++)
                for (int j = i + 1; j < buttons.Count; j++)
                {
                    var a = buttons[i]; var c = buttons[j];
                    bool overlap = a.X0 < c.X1 && c.X0 < a.X1 && a.Y0 < c.Y1 && c.Y0 < a.Y1;
                    Assert.False(overlap, $"{page}: {a.Action} overlaps {c.Action}");
                }
        }
    }

    [Fact]
    public void Hit_test_finds_buttons_and_seek_fraction()
    {
        var play = VrMenuLayout.Buttons(MenuPage.Main).Single(b => b.Action == MenuAction.PlayPause);
        var hit = VrMenuLayout.HitTest(MenuPage.Main, (play.X0 + play.X1) / 2, (play.Y0 + play.Y1) / 2);
        Assert.Equal(MenuAction.PlayPause, hit.Action);

        var bar = VrMenuLayout.SeekBar;
        var seek = VrMenuLayout.HitTest(MenuPage.Main, bar.X0 + (bar.X1 - bar.X0) * 0.25f, (bar.Y0 + bar.Y1) / 2);
        Assert.Equal(MenuAction.Seek, seek.Action);
        Assert.Equal(0.25f, seek.Fraction, 3);

        Assert.Equal(MenuAction.None, VrMenuLayout.HitTest(MenuPage.Main, 0.5f, 0.05f).Action);
        // The seek bar only exists on the main page.
        Assert.NotEqual(MenuAction.Seek, VrMenuLayout.HitTest(MenuPage.Horizon, 0.5f, 0.3f).Action);
    }

    [Fact]
    public void Dwell_fires_once_for_normal_buttons()
    {
        var d = new GazeDwell { DwellSeconds = 1f, RepeatSeconds = 0.25f };
        int fired = 0;
        d.Update(MenuAction.Next, 0, false, 0.1f); // first frame on target only starts the timer
        for (int i = 0; i < 40; i++) if (d.Update(MenuAction.Next, 0, false, 0.1f)) fired++;
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Dwell_repeats_for_repeatable_buttons_and_resets_on_change()
    {
        var d = new GazeDwell { DwellSeconds = 1f, RepeatSeconds = 0.25f };
        int fired = 0;
        d.Update(MenuAction.Fwd10, 0, true, 0f);
        for (int i = 0; i < 20; i++) if (d.Update(MenuAction.Fwd10, 0, true, 0.1f)) fired++; // 2 s
        Assert.InRange(fired, 4, 5);

        d.Update(MenuAction.Back10, 0, true, 0.1f);
        Assert.Equal(0f, d.Progress);
        Assert.False(d.Update(MenuAction.Back10, 0, true, 0.5f));
        Assert.Equal(0.5f, d.Progress, 3);
    }

    [Fact]
    public void Dwell_on_seek_bar_restarts_when_gaze_moves_to_other_bucket()
    {
        var d = new GazeDwell { DwellSeconds = 1f };
        d.Update(MenuAction.Seek, 10, false, 0f);
        Assert.False(d.Update(MenuAction.Seek, 10, false, 0.9f));
        Assert.False(d.Update(MenuAction.Seek, 11, false, 0.9f));
        Assert.True(d.Update(MenuAction.Seek, 11, false, 1.0f));
    }
}
