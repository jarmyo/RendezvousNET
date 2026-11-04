using System.Collections.Concurrent;
using Android.Content;
using Android.Graphics;
using Android.Media;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>
/// Frames of the current video decoded in the background with MediaMetadataRetriever: small flat
/// thumbnails (spots and the preview above the seek bar; for VR formats only the front of one eye)
/// and one raw frame for the format editor. Bitmaps are never recycled explicitly: the GL thread
/// may still be painting an old one.
/// </summary>
internal sealed class VideoFrames(Context context)
{
    private const int MaxWidth = 320, MaxHeight = 240, RawWidth = 720, MaxCached = 96;

    private readonly ConcurrentDictionary<long, Bitmap> _cache = new();
    private readonly ConcurrentDictionary<long, byte> _pending = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _location;
    private VideoFormat _format;
    private int _generation;
    private Bitmap? _raw;
    private long _rawMs = -1;

    /// <summary>Changes whenever a frame arrives (so windows repaint).</summary>
    public int Version { get; private set; }

    /// <summary>Raised on a worker thread when a frame is ready.</summary>
    public event Action? Changed;

    public Bitmap? Get(long ms) => _cache.TryGetValue(ms, out var b) ? b : null;

    /// <summary>The raw frame (whole picture, both eyes) for the format editor.</summary>
    public Bitmap? Raw => _raw;

    /// <summary>Switches to another video or format; frames of the previous one are dropped.</summary>
    public void SetVideo(string location, VideoFormat format)
    {
        // Fine tuning does not change the thumbnail crop.
        format = format.WithoutTuning();
        if (location == _location && format == _format) return;
        bool sameVideo = location == _location;
        Interlocked.Increment(ref _generation);
        _location = location;
        _format = format;
        _cache.Clear();
        _pending.Clear();
        if (!sameVideo) { _raw = null; _rawMs = -1; }
        Version++;
    }

    public void Request(IEnumerable<long> times)
    {
        if (_location is not { } location) return;
        if (_cache.Count > MaxCached) _cache.Clear();
        int generation = _generation;
        var format = _format;
        foreach (long ms in times)
        {
            if (_cache.ContainsKey(ms) || !_pending.TryAdd(ms, 0)) continue;
            _ = Task.Run(() => LoadAsync(ms, location, format, generation, raw: false));
        }
    }

    /// <summary>Decodes the raw frame near <paramref name="ms"/> unless one within 5 s is already there.</summary>
    public void RequestRaw(long ms)
    {
        if (_location is not { } location || (_raw is not null && Math.Abs(_rawMs - ms) < 5000)) return;
        if (!_pending.TryAdd(-1 - ms, 0)) return;
        _rawMs = ms;
        int generation = _generation;
        _ = Task.Run(() => LoadAsync(ms, location, _format, generation, raw: true));
    }

    private async Task LoadAsync(long ms, string location, VideoFormat format, int generation, bool raw)
    {
        await _gate.WaitAsync();
        try
        {
            if (generation != _generation) return;
            var bitmap = Decode(context, location, ms, raw ? null : format, raw ? RawWidth : MaxWidth, raw ? RawWidth : MaxHeight);
            if (bitmap is null || generation != _generation) return;
            if (raw) _raw = bitmap;
            else _cache[ms] = bitmap;
            Version++;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or ArgumentException)
        {
            Android.Util.Log.Warn("VrView", $"Frame at {ms} ms failed: {ex.Message}");
        }
        finally
        {
            _pending.TryRemove(raw ? -1 - ms : ms, out _);
            _gate.Release();
        }
    }

    /// <summary>A frame scaled to fit the box; cropped to a flat view when a format is given.</summary>
    public static Bitmap? Decode(Context context, string location, long ms, VideoFormat? crop, int maxWidth, int maxHeight)
    {
        using var retriever = new MediaMetadataRetriever();
        retriever.SetDataSource(context, Uri.Parse(location)!);
        long us = ms * 1000;
        using var frame = OperatingSystem.IsAndroidVersionAtLeast(27)
            ? retriever.GetScaledFrameAtTime(us, Option.ClosestSync, 1280, 1280)
            : retriever.GetFrameAtTime(us, Option.ClosestSync);
        if (frame is null) return null;
        return CropScale(frame, crop, maxWidth, maxHeight);
    }

    public static Bitmap CropScale(Bitmap frame, VideoFormat? crop, int maxWidth, int maxHeight)
    {
        var (fx0, fy0, fx1, fy1) = crop?.ThumbnailCrop() ?? (0f, 0f, 1f, 1f);
        int x = (int)(fx0 * frame.Width), y = (int)(fy0 * frame.Height);
        int w = Math.Max(1, Math.Min((int)((fx1 - fx0) * frame.Width), frame.Width - x));
        int h = Math.Max(1, Math.Min((int)((fy1 - fy0) * frame.Height), frame.Height - y));
        float scale = Math.Min(1f, Math.Min((float)maxWidth / w, (float)maxHeight / h));
        var matrix = new Matrix();
        matrix.SetScale(scale, scale);
        return Bitmap.CreateBitmap(frame, x, y, w, h, matrix, true)!;
    }
}
