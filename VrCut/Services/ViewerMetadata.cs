using System.Text;
using Core = VrView.Core;

namespace VrCut.Services;

/// <summary>
/// The viewer's <c>vrview.json</c> entry of a video: spots ("marcas") and parts marked for removal in the
/// headset's edit mode. The file lives in the folder picked in the viewer (the video's folder or one above
/// it), on this computer or on the phone.
/// </summary>
public sealed class ViewerMetadata
{
    /// <summary>Folder holding the file (local path or phone path).</summary>
    public required string Folder { get; init; }

    /// <summary>The video's key in the file: its path relative to <see cref="Folder"/>.</summary>
    public required string Key { get; init; }

    public required bool OnPhone { get; init; }

    public required Core.FolderSettings Settings { get; set; }

    public Core.VideoState State => Settings.GetVideo(Key) ?? new Core.VideoState();

    public string JsonPath => OnPhone
        ? Folder.TrimEnd('/') + "/" + Core.FolderSettings.FileName
        : Path.Combine(Folder, Core.FolderSettings.FileName);

    /// <summary>Key of another file in the same folder as the video (e.g. the edited copy).</summary>
    public string SiblingKey(string fileName)
    {
        string folder = Core.FolderSettings.FolderOf(Key);
        return folder.Length == 0 ? fileName : folder + "/" + fileName;
    }
}

public static class ViewerMetadataService
{
    /// <summary>Looks for the viewer's file that knows this video, or null.</summary>
    public static async Task<ViewerMetadata?> FindAsync(string videoPath, bool onPhone, string tempDir, CancellationToken ct)
    {
        if (!onPhone)
        {
            var found = Core.FolderSettingsLocator.Find(videoPath, folder =>
            {
                string path = Path.Combine(folder, Core.FolderSettings.FileName);
                return File.Exists(path) ? Core.FolderSettings.Parse(File.ReadAllText(path)) : null;
            });
            return found is { } f ? new ViewerMetadata { Folder = f.Folder, Key = f.Key, OnPhone = false, Settings = f.Settings } : null;
        }

        var candidates = Core.FolderSettingsLocator.Candidates(videoPath).ToList();
        string JsonOf(string folder) => folder.TrimEnd('/') + "/" + Core.FolderSettings.FileName;
        var existing = await AdbService.ExistingFilesAsync(candidates.Select(c => JsonOf(c.Folder)));
        foreach (var (folder, key) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!existing.Contains(JsonOf(folder)))
                continue;
            var settings = Core.FolderSettings.Parse(await AdbService.ReadTextAsync(JsonOf(folder), tempDir, ct));
            if (settings.Videos.ContainsKey(key))
                return new ViewerMetadata { Folder = folder, Key = key, OnPhone = true, Settings = settings };
        }
        return null;
    }

    /// <summary>
    /// Records the edit in the viewer's file: reads it again (the viewer may have changed it since the
    /// import), moves the spots and the resume position, clears the applied cuts and writes it back.
    /// </summary>
    public static async Task<Core.EditResult> ApplyEditAsync(ViewerMetadata meta, string targetKey,
        IReadOnlyList<Core.CutRange> removed, string tempDir, CancellationToken ct)
    {
        string text = meta.OnPhone
            ? await AdbService.ReadTextAsync(meta.JsonPath, tempDir, ct)
            : await File.ReadAllTextAsync(meta.JsonPath, ct);
        var settings = Core.FolderSettings.Parse(text);
        var result = settings.ApplyEdit(meta.Key, targetKey, removed);
        string json = settings.ToJson();
        if (meta.OnPhone)
        {
            await AdbService.WriteTextAsync(meta.JsonPath, json, tempDir, ct);
        }
        else
        {
            // Write next to it and swap, so a crash never leaves half a file.
            string tmp = meta.JsonPath + ".tmp";
            await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false), ct);
            File.Move(tmp, meta.JsonPath, overwrite: true);
        }
        meta.Settings = settings;
        return result;
    }
}
