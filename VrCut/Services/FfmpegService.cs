using System.Globalization;

namespace VrCut.Services;

public sealed record VideoInfo(double Duration, double StartTime, int Width, int Height);

public static class FfmpegService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(double v) => v.ToString("0.######", Inv);

    public static async Task<VideoInfo> GetInfoAsync(string file)
    {
        string text = await ProcessRunner.RunAsync("ffprobe",
        [
            "-v", "error", "-select_streams", "v:0",
            "-show_entries", "format=duration,start_time:stream=width,height",
            "-of", "default=nw=1", file
        ]);

        var values = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0])
            .ToDictionary(g => g.Key, g => g.First()[1]);

        double D(string key) => values.TryGetValue(key, out var s) && double.TryParse(s, Inv, out var v) ? v : 0;
        return new VideoInfo(D("duration"), D("start_time"), (int)D("width"), (int)D("height"));
    }

    /// <summary>Keyframe times relative to the start of the file (same reference ffmpeg -ss uses).</summary>
    public static async Task<double[]> GetKeyframesAsync(string file, double startTime, CancellationToken ct)
    {
        // Reading packet flags only (no decoding) keeps this fast even for long videos.
        string text = await ProcessRunner.RunAsync("ffprobe",
        [
            "-v", "error", "-select_streams", "v:0",
            "-show_entries", "packet=pts_time,flags", "-of", "csv=p=0", file
        ], ct: ct);

        var list = new List<double>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length >= 2 && parts[1].Contains('K') && double.TryParse(parts[0], Inv, out var t))
                list.Add(Math.Max(0, t - startTime));
        }
        list.Sort();
        return list.ToArray();
    }

    /// <summary>Returns one JPEG frame at the given time, or an empty array when no frame could be decoded.</summary>
    public static Task<byte[]> GrabFrameAsync(string file, double time, int height, bool leftEyeOnly, CancellationToken ct = default)
    {
        string filter = (leftEyeOnly ? "crop=iw/2:ih:0:0," : "") + $"scale=-2:{height}";
        return ProcessRunner.RunBinaryAsync("ffmpeg",
        [
            "-hide_banner", "-loglevel", "error",
            "-ss", Num(Math.Max(0, time)), "-i", file,
            "-frames:v", "1", "-an", "-vf", filter,
            "-f", "image2pipe", "-c:v", "mjpeg", "-q:v", "4", "-"
        ], ct);
    }
}
