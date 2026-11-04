using System.Globalization;
using Android.Content;
using Android.Media;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>Facts about a video file for the Info window, read in the background.</summary>
internal sealed record MediaFacts(
    long SizeBytes,
    long DurationMs,
    int Width,
    int Height,
    float FrameRate,
    string Codec,
    int BitDepth,
    long Bitrate,
    string Decoder,
    bool HardwareDecoder,
    SphericalInfo? Spherical)
{
    public static MediaFacts Empty => new(0, 0, 0, 0, 0, "", 0, 0, "", false, null);
}

internal static class MediaInfoReader
{
    /// <summary>Spherical metadata of the file (stereo mode, projection, embedded mesh), or null.</summary>
    public static SphericalInfo? ReadSpherical(Context context, string location)
    {
        try
        {
            using var pfd = context.ContentResolver!.OpenFileDescriptor(Uri.Parse(location)!, "r");
            if (pfd is null) return null;
            using var handle = new SafeFileHandle(pfd.Fd, ownsHandle: false);
            using var stream = new FileStream(handle, FileAccess.Read, 1);
            return SphericalMetadata.Read(stream);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The format the spherical metadata describes, or null.</summary>
    public static VideoFormat? FormatOf(SphericalInfo? s)
    {
        if (s is null || s.Projection == SphericalProjection.None) return null;
        var stereo = s.Stereo ?? StereoMode.Mono;
        return s.Projection switch
        {
            SphericalProjection.Mesh when s.LeftMesh is not null => new VideoFormat(Projection.Mesh, stereo, s.SwapEyes),
            SphericalProjection.Cubemap => new VideoFormat(Projection.Eac, stereo, s.SwapEyes),
            SphericalProjection.Equirect or SphericalProjection.Mesh => s.HorizontalCoverageDeg > 270
                ? new VideoFormat(Projection.Sphere360, stereo, s.SwapEyes,
                    VFov: Standard(s.VerticalCoverageDeg, 180f))
                : new VideoFormat(Projection.Dome180, stereo, s.SwapEyes,
                    HFov: Standard(s.HorizontalCoverageDeg, 180f), VFov: Standard(s.VerticalCoverageDeg, 180f)),
            _ => null,
        };

        static float Standard(float value, float standard) => MathF.Abs(value - standard) < 1f || value <= 0 ? 0f : value;
    }

    public static MediaFacts Read(Context context, MediaItem item, SphericalInfo? spherical)
    {
        var uri = Uri.Parse(item.Location)!;
        long size = item.SizeBytes;
        if (size <= 0)
        {
            try
            {
                using var c = context.ContentResolver!.Query(uri, [IOpenableColumns.Size], null, null, null);
                if (c is not null && c.MoveToFirst() && !c.IsNull(0)) size = c.GetLong(0);
            }
            catch (Java.Lang.Exception) { }
        }

        long duration = 0, bitrate = 0;
        int width = 0, height = 0, depth = 0;
        float fps = 0;
        string mime = "", decoder = "";
        bool hardware = false;
        try
        {
            using var r = new MediaMetadataRetriever();
            r.SetDataSource(context, uri);
            duration = ParseLong(r.ExtractMetadata(MetadataKey.Duration));
            width = (int)ParseLong(r.ExtractMetadata(MetadataKey.VideoWidth));
            height = (int)ParseLong(r.ExtractMetadata(MetadataKey.VideoHeight));
            bitrate = ParseLong(r.ExtractMetadata(MetadataKey.Bitrate));
            float.TryParse(r.ExtractMetadata(MetadataKey.CaptureFramerate), NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
            long frames = OperatingSystem.IsAndroidVersionAtLeast(28) ? ParseLong(r.ExtractMetadata(MetadataKey.VideoFrameCount)) : 0;
            if (fps <= 0 && frames > 0 && duration > 0) fps = frames * 1000f / duration;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or ArgumentException) { }

        try
        {
            using var ex = new MediaExtractor();
            ex.SetDataSource(context, uri, null);
            for (int i = 0; i < ex.TrackCount; i++)
            {
                var f = ex.GetTrackFormat(i);
                string m = f.GetString(MediaFormat.KeyMime) ?? "";
                if (!m.StartsWith("video/", StringComparison.Ordinal)) continue;
                mime = m;
                if (f.ContainsKey(MediaFormat.KeyFrameRate) && fps <= 0)
                {
                    try { fps = f.GetInteger(MediaFormat.KeyFrameRate); }
                    catch (Java.Lang.ClassCastException) { fps = f.GetFloat(MediaFormat.KeyFrameRate); }
                }
                if (OperatingSystem.IsAndroidVersionAtLeast(24) && f.ContainsKey(MediaFormat.KeyProfile))
                {
                    int profile = f.GetInteger(MediaFormat.KeyProfile);
                    // HEVC Main10 / VP9 profile 2 / AV1 Main10 carry 10-bit video.
                    depth = (m == "video/hevc" && profile is 2 or 4096 or 8192) || (m == "video/x-vnd.on2.vp9" && profile >= 4) ? 10 : 8;
                }
                var list = new MediaCodecList(MediaCodecListKind.RegularCodecs);
                decoder = list.FindDecoderForFormat(f) ?? "";
                if (decoder.Length > 0 && OperatingSystem.IsAndroidVersionAtLeast(29))
                    hardware = list.GetCodecInfos()?.FirstOrDefault(c => c.Name == decoder)?.IsHardwareAccelerated == true;
                else hardware = decoder.Length > 0 && !decoder.StartsWith("OMX.google", StringComparison.Ordinal) && !decoder.StartsWith("c2.android", StringComparison.Ordinal);
                break;
            }
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or ArgumentException) { }

        return new MediaFacts(size, duration, width, height, fps, CodecName(mime), depth, bitrate, decoder, hardware, spherical);
    }

    public static string CodecName(string mime) => mime switch
    {
        "video/avc" => "H.264",
        "video/hevc" => "HEVC (H.265)",
        "video/x-vnd.on2.vp9" => "VP9",
        "video/av01" => "AV1",
        "video/x-vnd.on2.vp8" => "VP8",
        "video/mp4v-es" => "MPEG-4",
        "" => "?",
        _ => mime.Replace("video/", ""),
    };

    private static long ParseLong(string? s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
