using System.Globalization;

namespace VrView.Core;

/// <summary>Helpers for the bookmarked positions ("marcas") of a video, kept sorted in milliseconds.</summary>
public static class SpotList
{
    /// <summary>Spots closer than this are considered the same one.</summary>
    public const long MinGapMs = 1_000;

    /// <summary>Adds a spot unless one already exists within <see cref="MinGapMs"/>. Returns whether it was added.</summary>
    public static bool Add(List<long> spots, long ms)
    {
        ms = Math.Max(0, ms);
        if (spots.Any(s => Math.Abs(s - ms) < MinGapMs)) return false;
        int i = spots.BinarySearch(ms);
        spots.Insert(i < 0 ? ~i : i, ms);
        return true;
    }

    /// <summary>
    /// Spot to jump to with "previous": the last one before the position. Like a track's "previous"
    /// button, a spot just passed (less than 2 s ago) is skipped so repeated presses keep going back.
    /// </summary>
    public static long? Previous(IReadOnlyList<long> spots, long positionMs)
    {
        for (int i = spots.Count - 1; i >= 0; i--)
            if (spots[i] < positionMs - 2_000) return spots[i];
        return null;
    }

    /// <summary>First spot after the position (ignoring one we are practically on).</summary>
    public static long? Next(IReadOnlyList<long> spots, long positionMs)
    {
        foreach (var s in spots)
            if (s > positionMs + 500) return s;
        return null;
    }

    public static List<long> Parse(IEnumerable<string> values)
    {
        var list = new List<long>();
        foreach (var v in values)
            if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                Add(list, ms);
        return list;
    }
}
