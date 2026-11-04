using Android.Content;
using VrView.Core;

namespace VrView;

/// <summary>
/// Where per-video and per-folder state lives: in the picked folder's <see cref="FolderSettings.FileName"/>
/// for videos of a folder (survives reinstalling the app), in the app preferences otherwise. Older
/// preference entries are moved into the folder file the first time they are saved. Named profiles
/// only exist in folder files.
/// </summary>
internal sealed class VideoStore(Context context, AppSettings settings)
{
    private static readonly IReadOnlyDictionary<string, VideoFormat> NoProfiles = new Dictionary<string, VideoFormat>();
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

    public FolderEntry? GetFolder(MediaItem item)
    {
        if (File(item) is { } file && file.Data.GetFolder(item.Name) is { } entry) return entry;
        return settings.GetFolderFormat(MediaUtil.FolderKey(item.Location)) is { } f ? new FolderEntry { Format = f } : null;
    }

    public void SetFolder(MediaItem item, FolderEntry? entry, bool flush = true)
    {
        if (File(item) is { CanWrite: true } file)
        {
            file.Data.SetFolder(item.Name, entry);
            file.MarkDirty();
            if (flush) file.Flush();
        }
        else settings.SetFolderFormat(MediaUtil.FolderKey(item.Location), entry?.Format);
    }

    public IReadOnlyDictionary<string, VideoFormat> Profiles(MediaItem item) =>
        File(item) is { } file ? file.Data.Profiles : NoProfiles;

    /// <summary>Profiles need a writable folder file.</summary>
    public bool CanUseProfiles(MediaItem item) => File(item) is { CanWrite: true };

    public void SetProfile(MediaItem item, string name, VideoFormat format, bool flush = true) =>
        Change(item, d => d.Profiles[name] = format, flush);

    public string NewProfileName(MediaItem item, string baseName) =>
        File(item) is { } file ? file.Data.NewProfileName(baseName) : baseName;

    public void DeleteProfile(MediaItem item, string name) => Change(item, d => d.DeleteProfile(name), true);

    public bool RenameProfile(MediaItem item, string name, string newName)
    {
        bool ok = false;
        Change(item, d => ok = d.RenameProfile(name, newName), true);
        return ok;
    }

    /// <summary>Where this video's settings are kept, for the Info window.</summary>
    public string StorageText(MediaItem item) => File(item) switch
    {
        { CanWrite: true } => $"{FolderSettings.FileName} en la carpeta elegida",
        { } => "ajustes de la app (la carpeta es de solo lectura: vuelve a elegirla)",
        _ => "ajustes de la app (video abierto fuera de una carpeta)",
    };

    /// <summary>True when the item's folder can only be read (settings then stay in the app).</summary>
    public bool IsReadOnlyFolder(MediaItem item) => File(item) is { CanWrite: false };

    public static void Flush() => FolderSettingsFile.FlushAll();

    private void Change(MediaItem item, Action<FolderSettings> change, bool flush)
    {
        if (File(item) is not { CanWrite: true } file) return;
        change(file.Data);
        file.MarkDirty();
        if (flush) file.Flush();
    }

    private FolderSettingsFile? File(MediaItem item) =>
        MediaUtil.TreeOf(item.Location) is { } tree ? FolderSettingsFile.For(_resolver, tree) : null;
}
