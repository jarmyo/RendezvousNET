using Android.Content;
using VrView.Core;

namespace VrView;

/// <summary>Persistent preferences (global options, last folder, per-video state).</summary>
internal sealed class AppSettings
{
    private readonly ISharedPreferences _prefs;
    private readonly ISharedPreferences _videos;

    public AppSettings(Context context)
    {
        _prefs = context.GetSharedPreferences("vrview", FileCreationMode.Private)!;
        _videos = context.GetSharedPreferences("vrview_videos", FileCreationMode.Private)!;
    }

    public string? LastFolderUri
    {
        get => _prefs.GetString("folder", null);
        set => Edit(e => e.PutString("folder", value));
    }

    public string? LastVideoUri
    {
        get => _prefs.GetString("lastVideo", null);
        set => Edit(e => e.PutString("lastVideo", value));
    }

    public string? LastVideoName
    {
        get => _prefs.GetString("lastVideoName", null);
        set => Edit(e => e.PutString("lastVideoName", value));
    }

    public bool IncludeSubfolders
    {
        get => _prefs.GetBoolean("subfolders", false);
        set => Edit(e => e.PutBoolean("subfolders", value));
    }

    public bool Shuffle
    {
        get => _prefs.GetBoolean("shuffle", false);
        set => Edit(e => e.PutBoolean("shuffle", value));
    }

    public RepeatMode Repeat
    {
        get => (RepeatMode)_prefs.GetInt("repeat", (int)RepeatMode.All);
        set => Edit(e => e.PutInt("repeat", (int)value));
    }

    /// <summary>True = split screen for a headset; false = single view ("magic window").</summary>
    public bool StereoView
    {
        get => _prefs.GetBoolean("stereo", true);
        set => Edit(e => e.PutBoolean("stereo", value));
    }

    /// <summary>Vertical field of view in degrees (zoom).</summary>
    public float Fov
    {
        get => _prefs.GetFloat("fov", 95f);
        set => Edit(e => e.PutFloat("fov", value));
    }

    /// <summary>Horizontal lens-center offset per eye, in NDC units.</summary>
    public float IpdShift
    {
        get => _prefs.GetFloat("ipd", 0f);
        set => Edit(e => e.PutFloat("ipd", value));
    }

    public int LensLevel
    {
        get => _prefs.GetInt("lens", 0);
        set => Edit(e => e.PutInt("lens", value));
    }

    public bool GazeClick
    {
        get => _prefs.GetBoolean("gaze", true);
        set => Edit(e => e.PutBoolean("gaze", value));
    }

    /// <summary>0 = small, 1 = medium, 2 = large controls (VR panel and touch overlay).</summary>
    public int ControlsSize
    {
        get => _prefs.GetInt("controlsSize", 1);
        set => Edit(e => e.PutInt("controlsSize", value));
    }

    /// <summary>Format chosen by hand for a folder; used for its videos without format tags in the name.</summary>
    public VideoFormat? GetFolderFormat(string folderKey) =>
        VideoFormat.TryParse(_videos.GetString("folder:" + folderKey, null));

    public void SetFolderFormat(string folderKey, VideoFormat? format)
    {
        var e = _videos.Edit()!;
        if (format is { } f) e.PutString("folder:" + folderKey, f.Serialize());
        else e.Remove("folder:" + folderKey);
        e.Apply();
    }

    public VideoState GetVideo(string location) => VideoState.Parse(_videos.GetString(location, null));

    public void SaveVideo(string location, VideoState state) =>
        _videos.Edit()!.PutString(location, state.Serialize())!.Apply();

    private void Edit(Action<ISharedPreferencesEditor> change)
    {
        var e = _prefs.Edit()!;
        change(e);
        e.Apply();
    }
}
