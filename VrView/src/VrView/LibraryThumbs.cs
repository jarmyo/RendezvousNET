using System.Collections.Concurrent;
using Android.Content;
using Android.Graphics;
using Android.Media;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>
/// Thumbnails and durations of library videos. Each is decoded once and kept as a small JPEG in the
/// app cache (named after the location), so the library opens fast the next time.
/// </summary>
internal sealed class LibraryThumbs(Context context)
{
    private readonly ConcurrentDictionary<string, (Bitmap? Bitmap, long DurationMs)> _memory = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dir = System.IO.Path.Combine(context.CacheDir!.AbsolutePath, "library");

    public int Version { get; private set; }
    public event Action? Changed;

    public Bitmap? Get(string location) => _memory.TryGetValue(location, out var e) ? e.Bitmap : null;

    public long Duration(string location) => _memory.TryGetValue(location, out var e) ? e.DurationMs : 0;

    public void Request(MediaItem item, VideoFormat format)
    {
        if (_memory.ContainsKey(item.Location) || !_pending.TryAdd(item.Location, 0)) return;
        _ = Task.Run(() => LoadAsync(item.Location, format));
    }

    private async Task LoadAsync(string location, VideoFormat format)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_dir);
            string key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(location + "|" + format.WithoutTuning().Serialize())));
            var cached = Directory.EnumerateFiles(_dir, key + "_*.jpg").FirstOrDefault();
            if (cached is not null && long.TryParse(System.IO.Path.GetFileNameWithoutExtension(cached).Split('_')[1], out long d))
            {
                _memory[location] = (BitmapFactory.DecodeFile(cached), d);
            }
            else
            {
                using var retriever = new MediaMetadataRetriever();
                retriever.SetDataSource(context, Uri.Parse(location)!);
                long duration = long.TryParse(retriever.ExtractMetadata(MetadataKey.Duration), out var ms) ? ms : 0;
                long at = duration > 0 ? duration / 7 : 0;
                using var frame = OperatingSystem.IsAndroidVersionAtLeast(27)
                    ? retriever.GetScaledFrameAtTime(at * 1000, Option.ClosestSync, 1024, 1024)
                    : retriever.GetFrameAtTime(at * 1000, Option.ClosestSync);
                Bitmap? thumb = frame is null ? null : VideoFrames.CropScale(frame, format, 400, 300);
                if (thumb is not null)
                {
                    using var file = File.Create(System.IO.Path.Combine(_dir, $"{key}_{duration}.jpg"));
                    thumb.Compress(Bitmap.CompressFormat.Jpeg!, 80, file);
                }
                _memory[location] = (thumb, duration);
            }
            Version++;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or ArgumentException)
        {
            _memory[location] = (null, 0);
            Android.Util.Log.Warn("VrView", $"Library thumbnail failed: {ex.Message}");
        }
        finally
        {
            _pending.TryRemove(location, out _);
            _gate.Release();
        }
    }
}
