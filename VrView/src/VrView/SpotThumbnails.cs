using System.Collections.Concurrent;
using Android.Content;
using Android.Graphics;
using Android.Media;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>
/// Small flat thumbnails of a video's spots, decoded in the background with MediaMetadataRetriever.
/// For VR formats only the center of one eye is kept, so they look like a normal picture.
/// Bitmaps are never recycled explicitly: the GL thread may still be painting an old one.
/// </summary>
internal sealed class SpotThumbnails(Context context)
{
    private const int MaxWidth = 320, MaxHeight = 240;

    private readonly ConcurrentDictionary<long, Bitmap> _cache = new();
    private readonly ConcurrentDictionary<long, byte> _pending = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _location;
    private VideoFormat _format;
    private int _generation;

    /// <summary>Changes whenever a thumbnail arrives (part of the menu model, so the panel repaints).</summary>
    public int Version { get; private set; }

    /// <summary>Raised on a worker thread when a thumbnail is ready.</summary>
    public event Action? Changed;

    public Bitmap? Get(long ms) => _cache.TryGetValue(ms, out var b) ? b : null;

    /// <summary>Switches to another video or format; thumbnails of the previous one are dropped.</summary>
    public void SetVideo(string location, VideoFormat format)
    {
        // Fine tuning does not change the thumbnail crop.
        format = format.WithoutTuning();
        if (location == _location && format == _format) return;
        Interlocked.Increment(ref _generation);
        _location = location;
        _format = format;
        _cache.Clear();
        _pending.Clear();
        Version++;
    }

    public void Request(IEnumerable<long> spots)
    {
        if (_location is not { } location) return;
        int generation = _generation;
        var format = _format;
        foreach (long ms in spots)
        {
            if (_cache.ContainsKey(ms) || !_pending.TryAdd(ms, 0)) continue;
            _ = Task.Run(() => LoadAsync(ms, location, format, generation));
        }
    }

    private async Task LoadAsync(long ms, string location, VideoFormat format, int generation)
    {
        await _gate.WaitAsync();
        try
        {
            if (generation != _generation) return;
            var bitmap = Decode(location, ms, format);
            if (bitmap is null || generation != _generation) return;
            _cache[ms] = bitmap;
            Version++;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException)
        {
            Android.Util.Log.Warn("VrView", $"Thumbnail at {ms} ms failed: {ex.Message}");
        }
        finally
        {
            _pending.TryRemove(ms, out _);
            _gate.Release();
        }
    }

    private Bitmap? Decode(string location, long ms, VideoFormat format)
    {
        using var retriever = new MediaMetadataRetriever();
        retriever.SetDataSource(context, Uri.Parse(location)!);
        long us = ms * 1000;
        using var frame = OperatingSystem.IsAndroidVersionAtLeast(27)
            ? retriever.GetScaledFrameAtTime(us, Option.Closest, 1280, 1280)
            : retriever.GetFrameAtTime(us, Option.Closest);
        if (frame is null) return null;

        var (fx0, fy0, fx1, fy1) = format.ThumbnailCrop();
        int x = (int)(fx0 * frame.Width), y = (int)(fy0 * frame.Height);
        int w = Math.Max(1, (int)((fx1 - fx0) * frame.Width)), h = Math.Max(1, (int)((fy1 - fy0) * frame.Height));
        float scale = Math.Min(1f, Math.Min((float)MaxWidth / w, (float)MaxHeight / h));
        var matrix = new Matrix();
        matrix.SetScale(scale, scale);
        return Bitmap.CreateBitmap(frame, x, y, Math.Min(w, frame.Width - x), Math.Min(h, frame.Height - y), matrix, true);
    }
}
