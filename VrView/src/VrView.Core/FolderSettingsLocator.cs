namespace VrView.Core;

/// <summary>
/// Finds which <see cref="FolderSettings.FileName"/> may describe a video seen from outside the app (e.g. on
/// a computer): the file lives in the folder picked in the viewer, which can be the video's folder or any
/// folder above it, and the video is keyed by its path relative to that folder.
/// </summary>
public static class FolderSettingsLocator
{
    /// <summary>
    /// Folders that may hold the settings file, nearest first, with the key the video would have in each.
    /// Works with "/" and "\" paths; <paramref name="maxLevels"/> limits how far up to look.
    /// </summary>
    public static IEnumerable<(string Folder, string Key)> Candidates(string videoPath, int maxLevels = 8)
    {
        var separators = new List<int>();
        for (int i = 0; i < videoPath.Length; i++)
            if (videoPath[i] is '/' or '\\') separators.Add(i);
        int levels = 0;
        for (int k = separators.Count - 1; k >= 0 && levels < maxLevels; k--, levels++)
        {
            int sep = separators[k];
            string key = videoPath[(sep + 1)..].Replace('\\', '/');
            if (key.Length == 0) yield break;
            // Keep the root itself ("/" or "C:\") as a folder.
            string folder = sep == 0 ? videoPath[..1] : videoPath[..sep];
            if (folder.EndsWith(':')) folder += videoPath[sep];
            yield return (folder, key);
        }
    }

    /// <summary>
    /// The candidate whose parsed settings know the video, or null. <paramref name="read"/> returns the
    /// settings of a folder (null when it has no file).
    /// </summary>
    public static (string Folder, string Key, FolderSettings Settings)? Find(string videoPath, Func<string, FolderSettings?> read)
    {
        foreach (var (folder, key) in Candidates(videoPath))
            if (read(folder) is { } settings && settings.Videos.ContainsKey(key))
                return (folder, key, settings);
        return null;
    }
}
