using System.Globalization;

namespace VrView.Core;

/// <summary>Small helpers for file lists and time display.</summary>
public static class MediaUtil
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".mov", ".3gp", ".ts", ".m2ts", ".mts", ".avi",
    };

    /// <summary>True for files the player should list (by MIME type or, as a fallback, by extension).</summary>
    public static bool IsVideo(string name, string? mimeType = null)
    {
        if (!string.IsNullOrEmpty(mimeType) && mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return true;
        return VideoExtensions.Contains(Path.GetExtension(name ?? ""));
    }

    /// <summary>Formats milliseconds as "m:ss" or "h:mm:ss".</summary>
    public static string FormatTime(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds);
    }

    /// <summary>
    /// The picked folder (SAF tree URI) a document URI was built from, or null when the location
    /// is not inside a picked folder (e.g. a video opened from another app).
    /// </summary>
    public static string? TreeOf(string location)
    {
        int doc = location.IndexOf("/document/", StringComparison.Ordinal);
        return doc > 0 && location.IndexOf("/tree/", StringComparison.Ordinal) is var tree && tree >= 0 && tree < doc
            ? location[..doc]
            : null;
    }

    /// <summary>
    /// Identifies the folder a video lives in, from its location (a SAF document URI such as
    /// <c>content://…/tree/primary%3AMovies/document/primary%3AMovies%2FVR%2Fclip.mp4</c>, or a plain path/URI).
    /// Providers with opaque document ids fall back to the picked tree, so all its videos share one key.
    /// </summary>
    public static string FolderKey(string location)
    {
        const string documentMarker = "/document/";
        int doc = location.IndexOf(documentMarker, StringComparison.Ordinal);
        if (doc >= 0)
        {
            int slash = location.LastIndexOf("%2F", StringComparison.OrdinalIgnoreCase);
            return slash > doc ? location[..slash] : location[..doc];
        }
        int last = location.LastIndexOf('/');
        return last > 0 ? location[..last] : location;
    }

    /// <summary>
    /// Decides where to resume a video: returns the saved position unless it is near the
    /// start or the end (then starts from zero).
    /// </summary>
    public static long ResumePosition(long savedMs, long durationMs)
    {
        if (savedMs < 5_000) return 0;
        if (durationMs > 0 && savedMs > durationMs - 10_000) return 0;
        return savedMs;
    }
}

/// <summary>
/// "Natural" ordering for file names, so "clip2" sorts before "clip10". Case-insensitive.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0') si++;
                while (sj < y.Length && y[sj] == '0') sj++;
                int ei = si, ej = sj;
                while (ei < x.Length && char.IsDigit(x[ei])) ei++;
                while (ej < y.Length && char.IsDigit(y[ej])) ej++;
                int lenX = ei - si, lenY = ej - sj;
                if (lenX != lenY) return lenX.CompareTo(lenY);
                int c = string.CompareOrdinal(x, si, y, sj, lenX);
                if (c != 0) return c;
                i = ei; j = ej;
            }
            else
            {
                int c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
