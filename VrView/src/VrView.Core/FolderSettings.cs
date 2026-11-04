using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrView.Core;

/// <summary>
/// Settings kept in a <see cref="FileName"/> file inside the picked folder, so they survive
/// reinstalling the app and travel with the videos: named format profiles, the format or profile of
/// each (sub)folder, and per video its format, horizon, resume position and spots. Videos are keyed
/// by their path relative to the picked folder (e.g. "VR/clip_180_LR.mp4"). Version 1 files are read
/// and rewritten in the version 2 layout.
/// </summary>
public sealed class FolderSettings
{
    public const string FileName = "vrview.json";
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Profile name → complete format (e.g. "Cámara 200°").</summary>
    public SortedDictionary<string, VideoFormat> Profiles { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Relative folder ("" = the picked folder) → format or profile chosen by hand.</summary>
    public SortedDictionary<string, FolderEntry> Folders { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Relative video path → remembered state.</summary>
    public SortedDictionary<string, VideoEntry> Videos { get; set; } = new(StringComparer.Ordinal);

    public VideoState? GetVideo(string relativePath)
    {
        if (!Videos.TryGetValue(relativePath, out var e)) return null;
        var state = new VideoState
        {
            PositionMs = Math.Max(0, e.Position),
            FormatOverride = e.Format,
            Profile = e.Profile,
            Pitch = e.Horizon?.Pitch ?? e.Pitch,
            Roll = e.Horizon?.Roll ?? e.Roll,
            Yaw = e.Horizon?.Yaw ?? e.Yaw,
        };
        foreach (var s in e.Spots ?? [])
            if (SpotList.Add(state.Spots, s.At) && !string.IsNullOrWhiteSpace(s.Name))
                state.SpotNames[Math.Max(0, s.At)] = s.Name!.Trim();
        return state;
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
            Format = state.FormatOverride,
            Profile = string.IsNullOrEmpty(state.Profile) ? null : state.Profile,
            Horizon = state.HasHorizon
                ? new HorizonEntry { Pitch = MathF.Round(state.Pitch, 2), Roll = MathF.Round(state.Roll, 2), Yaw = MathF.Round(state.Yaw, 2) }
                : null,
            Spots = state.Spots.Count > 0
                ? state.Spots.Select(s => new SpotEntry { At = s, Name = state.SpotNames.GetValueOrDefault(s) }).ToList()
                : null,
        };
    }

    public FolderEntry? GetFolder(string relativeVideoPath) =>
        Folders.TryGetValue(FolderOf(relativeVideoPath), out var f) && !f.IsEmpty ? f : null;

    public void SetFolder(string relativeVideoPath, FolderEntry? entry)
    {
        string folder = FolderOf(relativeVideoPath);
        if (entry is { IsEmpty: false }) Folders[folder] = entry;
        else Folders.Remove(folder);
    }

    /// <summary>The folder's format: its profile when it uses one, otherwise its own format.</summary>
    public VideoFormat? GetFolderFormat(string relativeVideoPath) => GetFolder(relativeVideoPath) is { } f ? Resolve(f) : null;

    public void SetFolderFormat(string relativeVideoPath, VideoFormat? format) =>
        SetFolder(relativeVideoPath, format is null ? null : new FolderEntry { Format = format });

    public VideoFormat? Resolve(FolderEntry entry) =>
        entry.Profile is { } p && Profiles.TryGetValue(p, out var pf) ? pf : entry.Format;

    /// <summary>Removes a profile; folders and videos that used it keep a copy of its format.</summary>
    public void DeleteProfile(string name)
    {
        if (!Profiles.Remove(name, out var format)) return;
        foreach (var f in Folders.Values.Where(f => f.Profile == name))
        {
            f.Profile = null;
            f.Format = format;
        }
        foreach (var v in Videos.Values.Where(v => v.Profile == name))
        {
            v.Profile = null;
            v.Format ??= format;
        }
    }

    /// <summary>Renames a profile and every reference to it. False when the new name is taken.</summary>
    public bool RenameProfile(string name, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || Profiles.ContainsKey(newName) || !Profiles.Remove(name, out var format)) return false;
        Profiles[newName] = format;
        foreach (var f in Folders.Values.Where(f => f.Profile == name)) f.Profile = newName;
        foreach (var v in Videos.Values.Where(v => v.Profile == name)) v.Profile = newName;
        return true;
    }

    /// <summary>A free profile name based on <paramref name="baseName"/> ("180° SBS", "180° SBS 2", …).</summary>
    public string NewProfileName(string baseName)
    {
        if (!Profiles.ContainsKey(baseName)) return baseName;
        for (int i = 2; ; i++)
            if (!Profiles.ContainsKey($"{baseName} {i}")) return $"{baseName} {i}";
    }

    /// <summary>"VR/sub/clip.mp4" → "VR/sub"; "clip.mp4" → "".</summary>
    public static string FolderOf(string relativePath)
    {
        int slash = relativePath.LastIndexOf('/');
        return slash > 0 ? relativePath[..slash] : "";
    }

    public string ToJson()
    {
        Version = CurrentVersion;
        return JsonSerializer.Serialize(this, FolderSettingsJson.Default.FolderSettings);
    }

    /// <summary>Parses the file; an empty, missing or damaged file gives empty settings.</summary>
    public static FolderSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new FolderSettings();
        try
        {
            var s = JsonSerializer.Deserialize(json, FolderSettingsJson.Default.FolderSettings) ?? new FolderSettings();
            // Deserialization replaces the dictionaries; restore the ordinal comparer.
            s.Profiles = new SortedDictionary<string, VideoFormat>(s.Profiles ?? new(), StringComparer.Ordinal);
            s.Folders = new SortedDictionary<string, FolderEntry>(s.Folders ?? new(), StringComparer.Ordinal);
            s.Videos = new SortedDictionary<string, VideoEntry>(s.Videos ?? new(), StringComparer.Ordinal);
            // Version 1 stored the horizon as loose values.
            foreach (var v in s.Videos.Values)
            {
                if (v.Horizon is null && (v.Pitch != 0 || v.Roll != 0 || v.Yaw != 0))
                    v.Horizon = new HorizonEntry { Pitch = v.Pitch, Roll = v.Roll, Yaw = v.Yaw };
                v.Pitch = v.Roll = v.Yaw = 0;
            }
            return s;
        }
        catch (JsonException)
        {
            return new FolderSettings();
        }
    }
}

/// <summary>What a folder uses for its videos without format tags: a profile or its own format.</summary>
[JsonConverter(typeof(FolderEntryJsonConverter))]
public sealed class FolderEntry
{
    public string? Profile { get; set; }
    public VideoFormat? Format { get; set; }
    [JsonIgnore] public bool IsEmpty => string.IsNullOrEmpty(Profile) && Format is null;
}

/// <summary>One video in <see cref="FolderSettings"/>. Times in milliseconds, angles in degrees.</summary>
public sealed class VideoEntry
{
    public long Position { get; set; }
    public VideoFormat? Format { get; set; }
    public string? Profile { get; set; }
    public HorizonEntry? Horizon { get; set; }
    public List<SpotEntry>? Spots { get; set; }

    // Version 1 (read only; moved into Horizon when parsing).
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public float Yaw { get; set; }
}

public sealed class HorizonEntry
{
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public float Yaw { get; set; }
}

[JsonConverter(typeof(SpotEntryJsonConverter))]
public sealed class SpotEntry
{
    public long At { get; set; }
    public string? Name { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(FolderSettings))]
internal sealed partial class FolderSettingsJson : JsonSerializerContext
{
}
