using VrView.Core;

namespace VrView.Core.Tests;

public class PlaylistTests
{
    private static List<MediaItem> Items(int n) =>
        Enumerable.Range(0, n).Select(i => new MediaItem($"uri{i}", $"v{i}.mp4")).ToList();

    [Fact]
    public void Plays_in_order_and_stops_at_end_when_repeat_off()
    {
        var p = new Playlist(Items(3), 0, repeat: RepeatMode.Off);
        Assert.Equal(0, p.CurrentIndex);
        Assert.True(p.AdvanceOnCompletion()); Assert.Equal(1, p.CurrentIndex);
        Assert.True(p.AdvanceOnCompletion()); Assert.Equal(2, p.CurrentIndex);
        Assert.False(p.AdvanceOnCompletion()); Assert.Equal(2, p.CurrentIndex);
    }

    [Fact]
    public void Repeat_all_wraps_and_repeat_one_stays()
    {
        var p = new Playlist(Items(2), 1, repeat: RepeatMode.All);
        Assert.True(p.AdvanceOnCompletion());
        Assert.Equal(0, p.CurrentIndex);

        p.Repeat = RepeatMode.One;
        Assert.True(p.AdvanceOnCompletion());
        Assert.Equal(0, p.CurrentIndex);
        // Manual "next" still moves when repeating one.
        Assert.True(p.Next());
        Assert.Equal(1, p.CurrentIndex);
    }

    [Fact]
    public void Next_and_previous_wrap_around()
    {
        var p = new Playlist(Items(3), 0, repeat: RepeatMode.Off);
        p.Previous();
        Assert.Equal(2, p.CurrentIndex);
        p.Next();
        Assert.Equal(0, p.CurrentIndex);
    }

    [Fact]
    public void Shuffle_keeps_current_first_and_visits_every_item_once()
    {
        var p = new Playlist(Items(20), 7, shuffle: false, repeat: RepeatMode.Off, random: new Random(42));
        p.Shuffle = true;
        Assert.Equal(7, p.CurrentIndex);
        var seen = new List<int> { p.CurrentIndex };
        while (p.AdvanceOnCompletion()) seen.Add(p.CurrentIndex);
        Assert.Equal(20, seen.Count);
        Assert.Equal(Enumerable.Range(0, 20), seen.OrderBy(x => x));
        Assert.NotEqual(Enumerable.Range(0, 20).ToList(), seen); // actually shuffled (seeded)
    }

    [Fact]
    public void Disabling_shuffle_restores_natural_order_on_current_item()
    {
        var p = new Playlist(Items(10), 0, shuffle: true, random: new Random(1));
        p.Next(); p.Next();
        int current = p.CurrentIndex;
        p.Shuffle = false;
        Assert.Equal(current, p.CurrentIndex);
        p.Next();
        Assert.Equal((current + 1) % 10, p.CurrentIndex);
    }

    [Fact]
    public void Previous_walks_back_through_shuffled_history()
    {
        var p = new Playlist(Items(10), 0, shuffle: true, random: new Random(3));
        var a = p.CurrentIndex; p.Next();
        var b = p.CurrentIndex; p.Next();
        p.Previous(); Assert.Equal(b, p.CurrentIndex);
        p.Previous(); Assert.Equal(a, p.CurrentIndex);
    }

    [Fact]
    public void Reshuffle_on_wrap_does_not_repeat_last_item_immediately()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var p = new Playlist(Items(4), 0, shuffle: true, repeat: RepeatMode.All, random: new Random(seed));
            for (int i = 0; i < 3; i++) p.AdvanceOnCompletion();
            int last = p.CurrentIndex;
            Assert.True(p.AdvanceOnCompletion());
            Assert.NotEqual(last, p.CurrentIndex);
        }
    }

    [Fact]
    public void JumpTo_selects_item_in_shuffled_order()
    {
        var p = new Playlist(Items(5), 0, shuffle: true, random: new Random(5));
        p.JumpTo(3);
        Assert.Equal(3, p.CurrentIndex);
        Assert.Equal("v3.mp4", p.Current!.Name);
    }

    [Fact]
    public void Empty_playlist_is_safe()
    {
        var p = new Playlist(Array.Empty<MediaItem>(), shuffle: true);
        Assert.Equal(-1, p.CurrentIndex);
        Assert.Null(p.Current);
        Assert.False(p.Next());
        Assert.False(p.AdvanceOnCompletion());
    }

    [Fact]
    public void Repeat_mode_cycles()
    {
        Assert.Equal(RepeatMode.All, Playlist.NextRepeatMode(RepeatMode.Off));
        Assert.Equal(RepeatMode.One, Playlist.NextRepeatMode(RepeatMode.All));
        Assert.Equal(RepeatMode.Off, Playlist.NextRepeatMode(RepeatMode.One));
    }
}
