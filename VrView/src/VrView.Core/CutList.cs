namespace VrView.Core;

/// <summary>A part of a video marked for removal, in milliseconds: from <see cref="FromMs"/> (included) to <see cref="ToMs"/> (excluded).</summary>
public readonly record struct CutRange(long FromMs, long ToMs)
{
    public long LengthMs => ToMs - FromMs;
    public bool Contains(long ms) => ms >= FromMs && ms < ToMs;
}

/// <summary>
/// Helpers for the parts of a video marked for removal ("cortes"). The viewer only marks them (and can
/// skip them while playing); VrCut removes them on a computer and moves every remembered time with
/// <see cref="Remap"/>, so a spot at 8:00 ends up at 5:00 once 0:00–3:00 is cut.
/// </summary>
public static class CutList
{
    /// <summary>Shorter parts are ignored (a double press, not a scene).</summary>
    public const long MinLengthMs = 500;

    /// <summary>
    /// Durations of the file when the cuts were marked and now may differ by this much (container
    /// rounding); a larger difference means the cuts belong to another version of the file.
    /// </summary>
    public const long DurationToleranceMs = 1_500;

    /// <summary>Sorted, overlapping or touching parts merged, clamped to [0, duration] (0 = unknown duration).</summary>
    public static List<CutRange> Normalize(IEnumerable<CutRange> cuts, long durationMs = 0)
    {
        var sorted = cuts
            .Select(c => new CutRange(Math.Max(0, Math.Min(c.FromMs, c.ToMs)), Math.Max(c.FromMs, c.ToMs)))
            .Select(c => durationMs > 0 ? new CutRange(Math.Min(c.FromMs, durationMs), Math.Min(c.ToMs, durationMs)) : c)
            .Where(c => c.LengthMs > 0)
            .OrderBy(c => c.FromMs)
            .ToList();
        var result = new List<CutRange>();
        foreach (var c in sorted)
        {
            if (result.Count > 0 && c.FromMs <= result[^1].ToMs)
                result[^1] = result[^1] with { ToMs = Math.Max(result[^1].ToMs, c.ToMs) };
            else
                result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// Adds the part between <paramref name="a"/> and <paramref name="b"/> (in any order), merging it with
    /// parts it overlaps. Returns false when it is shorter than <see cref="MinLengthMs"/>.
    /// </summary>
    public static bool Add(List<CutRange> cuts, long a, long b, long durationMs = 0)
    {
        long from = Math.Max(0, Math.Min(a, b)), to = Math.Max(a, b);
        if (durationMs > 0) to = Math.Min(to, durationMs);
        if (to - from < MinLengthMs) return false;
        var merged = Normalize(cuts.Append(new CutRange(from, to)), durationMs);
        cuts.Clear();
        cuts.AddRange(merged);
        return true;
    }

    /// <summary>Index of the part that contains the time, or -1.</summary>
    public static int IndexAt(IReadOnlyList<CutRange> cuts, long ms)
    {
        for (int i = 0; i < cuts.Count; i++)
            if (cuts[i].Contains(ms)) return i;
        return -1;
    }

    public static long RemovedMs(IReadOnlyList<CutRange> cuts) => cuts.Sum(c => c.LengthMs);

    /// <summary>
    /// Where playback should continue when it moved from <paramref name="previousMs"/> to <paramref name="currentMs"/>
    /// by playing (not by seeking): the end of a part it entered, or null when it entered none. Landing
    /// inside a part by a jump is never skipped, so a marked part can still be watched on purpose.
    /// </summary>
    public static long? SkipTarget(IReadOnlyList<CutRange> cuts, long previousMs, long currentMs)
    {
        if (currentMs < previousMs) return null;
        foreach (var c in cuts)
            if (c.FromMs > previousMs && c.FromMs <= currentMs && currentMs < c.ToMs) return c.ToMs;
        return null;
    }

    /// <summary>Start of the first part after the position (ignoring one we are practically on).</summary>
    public static CutRange? Next(IReadOnlyList<CutRange> cuts, long positionMs) =>
        cuts.Cast<CutRange?>().FirstOrDefault(c => c!.Value.FromMs > positionMs + 500);

    /// <summary>The last part that starts before the position (a part just passed is skipped, like a track's "previous").</summary>
    public static CutRange? Previous(IReadOnlyList<CutRange> cuts, long positionMs) =>
        cuts.Cast<CutRange?>().LastOrDefault(c => c!.Value.FromMs < positionMs - 2_000);

    /// <summary>
    /// The time in the edited video of a time of the original, once the <paramref name="removed"/> parts
    /// (sorted, not overlapping) are gone. A time inside a removed part goes to the cut point.
    /// </summary>
    public static long Remap(IReadOnlyList<CutRange> removed, long ms)
    {
        long shift = 0;
        foreach (var c in removed)
        {
            if (ms < c.FromMs) break;
            if (ms < c.ToMs) return c.FromMs - shift;
            shift += c.LengthMs;
        }
        return Math.Max(0, ms - shift);
    }

    /// <summary>The parts of [0, duration] not covered by <paramref name="parts"/> (e.g. kept parts → removed parts).</summary>
    public static List<CutRange> Complement(IEnumerable<CutRange> parts, long durationMs)
    {
        var result = new List<CutRange>();
        long pos = 0;
        foreach (var p in Normalize(parts, durationMs))
        {
            if (p.FromMs > pos) result.Add(new CutRange(pos, p.FromMs));
            pos = Math.Max(pos, p.ToMs);
        }
        if (durationMs > pos) result.Add(new CutRange(pos, durationMs));
        return result;
    }

    /// <summary>True when cuts marked on a file of <paramref name="markedDurationMs"/> (0 = unknown) fit a file of <paramref name="fileDurationMs"/>.</summary>
    public static bool BelongsTo(long markedDurationMs, long fileDurationMs) =>
        markedDurationMs <= 0 || fileDurationMs <= 0 || Math.Abs(markedDurationMs - fileDurationMs) <= DurationToleranceMs;
}
