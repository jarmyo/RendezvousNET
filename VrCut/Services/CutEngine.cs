using System.Text;

namespace VrCut.Services;

public sealed class CutRange(double from, double to)
{
    public double From { get; } = from;
    public double To { get; } = to;
    public string Text => $"{TimeFormat.Format(From)}  →  {TimeFormat.Format(To)}   ({TimeFormat.Format(To - From)})";
}

public static class TimeFormat
{
    public static string Format(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss\.f") : ts.ToString(@"mm\:ss\.f");
    }
}

public static class CutEngine
{
    public static List<(double From, double To)> ComputeKeepRanges(IEnumerable<CutRange> cuts, double duration)
    {
        var keep = new List<(double, double)>();
        double pos = 0;
        foreach (var c in cuts.OrderBy(c => c.From))
        {
            if (c.From > pos + 0.05)
                keep.Add((pos, c.From));
            pos = Math.Max(pos, Math.Min(c.To, duration));
        }
        if (duration > pos + 0.05)
            keep.Add((pos, duration));
        return keep;
    }

    /// <summary>
    /// Copies the kept ranges without re-encoding and joins them into <paramref name="output"/>.
    /// </summary>
    public static async Task ExportAsync(string input, string output, List<(double From, double To)> keep,
        string workDir, IProgress<(double Fraction, string Message)> progress, CancellationToken ct)
    {
        if (keep.Count == 0)
            throw new InvalidOperationException("Los cortes eliminan todo el video.");

        Directory.CreateDirectory(workDir);
        var segments = new List<string>();
        try
        {
            for (int i = 0; i < keep.Count; i++)
            {
                var (from, to) = keep[i];
                progress.Report(((double)i / (keep.Count + 1), $"Copiando parte {i + 1} de {keep.Count}…"));
                // A tiny offset keeps ffmpeg from seeking back to the previous keyframe due to rounding.
                double start = from > 0 ? from + 0.0005 : 0;
                string seg = Path.Combine(workDir, $"seg{i:D3}.mp4");
                await ProcessRunner.RunAsync("ffmpeg",
                [
                    "-hide_banner", "-loglevel", "error", "-y",
                    "-ss", FfmpegService.Num(start), "-i", input, "-t", FfmpegService.Num(to - start),
                    "-map", "0:v", "-map", "0:a?", "-c", "copy", "-map_metadata", "0",
                    "-avoid_negative_ts", "make_zero", "-strict", "unofficial", seg
                ], ct: ct);
                segments.Add(seg);
            }

            progress.Report(((double)keep.Count / (keep.Count + 1), "Uniendo partes…"));
            string list = Path.Combine(workDir, "list.txt");
            await File.WriteAllLinesAsync(list,
                segments.Select(s => $"file '{s.Replace("'", @"'\''")}'"), new UTF8Encoding(false), ct);

            string tmpOut = output + ".part.mp4";
            await ProcessRunner.RunAsync("ffmpeg",
            [
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "concat", "-safe", "0", "-i", list,
                "-map", "0", "-c", "copy", "-map_metadata", "0",
                "-movflags", "+faststart", "-strict", "unofficial", tmpOut
            ], ct: ct);

            if (new FileInfo(tmpOut).Length >= new FileInfo(input).Length)
            {
                File.Delete(tmpOut);
                throw new InvalidOperationException("El resultado no es más pequeño que el original; se canceló.");
            }

            File.Move(tmpOut, output, overwrite: true);
        }
        finally
        {
            foreach (var s in segments)
                File.Delete(s);
        }
    }
}
