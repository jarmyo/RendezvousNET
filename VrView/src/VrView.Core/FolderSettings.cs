using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrView.Core;

/// <summary>
/// Settings kept in a <see cref="FileName"/> file inside the picked folder, so they survive
/// reinstalling the app and travel with the videos: per-video position, format, horizon and spots,
/// and the format chosen for each (sub)folder. Videos are keyed by their path relative to the
/// picked folder (e.g. "VR/clip_180_LR.mp4").
/// </summary>
public sealed class FolderSettings
{
    public const string FileName = "vrview.json";

    public int Version { get; set; } = 1;

    /// <summary>Relative folder ("" = the picked folder) → format chosen by hand.</summary>
    public SortedDictionary<string, string> Folders { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Relative video path → remembered state.</summary>
    public SortedDictionary<string, VideoEntry> Videos { get; set; } = new(StringComparer.Ordinal);

    public VideoState? GetVideo(string relativePath)
    {
        if (!Videos.TryGetValue(relativePath, out var e)) return null;
        return new VideoState
        {
            PositionMs = Math.Max(0, e.Position),
            FormatOverride = VideoFormat.TryParse(e.Format),
            Pitch = e.Pitch,
            Roll = e.Roll,
            Yaw = e.Yaw,
            Spots = SpotList.Parse((e.Spots ?? []).Select(s => s.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        };
    }

    public void SetVideo(string relativePath, VideoState state)
    {
        if (state.IsEmpty)
        {
            Videos.Remove(relativePath);
            return;
        }
        Videos[relativePath] = new VideoEntry
        {
            Position = state.PositionMs,
            Format = state.FormatOverride?.Serialize(),
            Pitch = MathF.Round(state.Pitch, 2),
            Roll = MathF.Round(state.Roll, 2),
            Yaw = MathF.Round(state.Yaw, 2),
            Spots = state.Spots.Count > 0 ? [.. state.Spots] : null,
        };
    }

    public VideoFormat? GetFolderFormat(string relativeVideoPath) =>
        Folders.TryGetValue(FolderOf(relativeVideoPath), out var f) ? VideoFormat.TryParse(f) : null;

    public void SetFolderFormat(string relativeVideoPath, VideoFormat? format)
    {
        string folder = FolderOf(relativeVideoPath);
        if (format is { } f) Folders[folder] = f.Serialize();
        else Folders.Remove(folder);
    }

    /// <summary>"VR/sub/clip.mp4" → "VR/sub"; "clip.mp4" → "".</summary>
    public static string FolderOf(string relativePath)
    {
        int slash = relativePath.LastIndexOf('/');
        return slash > 0 ? relativePath[..slash] : "";
    }

    public string ToJson() => JsonSerializer.Serialize(this, FolderSettingsJson.Default.FolderSettings);

    /// <summary>Parses the file; an empty, missing or damaged file gives empty settings.</summary>
    public static FolderSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new FolderSettings();
        try
        {
            var s = JsonSerializer.Deserialize(json, FolderSettingsJson.Default.FolderSettings) ?? new FolderSettings();
            // Deserialization replaces the dictionaries; restore the ordinal comparer.
            s.Folders = new SortedDictionary<string, string>(s.Folders ?? new(), StringComparer.Ordinal);
            s.Videos = new SortedDictionary<string, VideoEntry>(s.Videos ?? new(), StringComparer.Ordinal);
            return s;
        }
        catch (JsonException)
        {
            return new FolderSettings();
        }
    }
}

/// <summary>One video in <see cref="FolderSettings"/>. Times in milliseconds, angles in degrees.</summary>
public sealed class VideoEntry
{
    public long Position { get; set; }
    public string? Format { get; set; }
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public float Yaw { get; set; }
    public List<long>? Spots { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(FolderSettings))]
internal sealed partial class FolderSettingsJson : JsonSerializerContext
{
}
