using Android.Content;
using VrView.Core;

namespace VrView;

/// <summary>
/// Where per-video state lives: in the picked folder's <see cref="FolderSettings.FileName"/> for videos
/// of a folder (survives reinstalling the app), in the app preferences otherwise. Older preference
/// entries are moved into the folder file the first time they are saved.
/// </summary>
internal sealed class VideoStore(Context context, AppSettings settings)
{
    private readonly ContentResolver _resolver = context.ContentResolver!;

    public VideoState Get(MediaItem item)
    {
        if (File(item) is not { } file) return settings.GetVideo(item.Location);
        var fromFile = file.Data.GetVideo(item.Name);
        var fromPrefs = settings.GetVideo(item.Location);
        if (!file.CanWrite && !fromPrefs.IsEmpty) return fromPrefs;
        return fromFile ?? fromPrefs;
    }

    /// <param name="flush">Write the folder file now (for explicit changes; positions are batched).</param>
    public void Save(MediaItem item, VideoState state, bool flush = false)
    {
        if (File(item) is { CanWrite: true } file)
        {
            file.Data.SetVideo(item.Name, state);
            file.MarkDirty();
            settings.RemoveVideo(item.Location);
            if (flush) file.Flush();
        }
        else settings.SaveVideo(item.Location, state);
    }

    public VideoFormat? GetFolderFormat(MediaItem item) =>
        File(item) is { } file && file.Data.GetFolderFormat(item.Name) is { } f
            ? f
            : settings.GetFolderFormat(MediaUtil.FolderKey(item.Location));

    public void SetFolderFormat(MediaItem item, VideoFormat? format)
    {
        if (File(item) is { CanWrite: true } file)
        {
            file.Data.SetFolderFormat(item.Name, format);
            file.MarkDirty();
            file.Flush();
        }
        else settings.SetFolderFormat(MediaUtil.FolderKey(item.Location), format);
    }

    /// <summary>True when the item's folder can only be read (settings then stay in the app).</summary>
    public bool IsReadOnlyFolder(MediaItem item) => File(item) is { CanWrite: false };

    public static void Flush() => FolderSettingsFile.FlushAll();

    private FolderSettingsFile? File(MediaItem item) =>
        MediaUtil.TreeOf(item.Location) is { } tree ? FolderSettingsFile.For(_resolver, tree) : null;
}
