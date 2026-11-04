using System.Globalization;
using Android.App;
using Android.Graphics;
using Android.Media;
using Android.Views;
using Android.Widget;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>The windows (player, format editor, spots, info, settings, library, tracks, calibration) and their actions.</summary>
public partial class PlayerActivity
{
    // Open windows, innermost last. They are shown in the headset, or on the touch screen when
    // opened from the touch controls (or when not using a headset).
    private readonly List<WindowKind> _windows = [];
    private bool _touchWindows;
    private int _settingsTab, _calibrationStep = 1;
    private UiWindow? _ui;

    // Format
    private FormatChoice _choice = FormatResolver.Resolve(new FormatInputs(null, null, null, new Dictionary<string, VideoFormat>(), "", 0, 0, null));
    private FormatScope? _editScope;

    // Spots: snapshots for the windows, replaced only when the spots change.
    private long[] _spots = [];
    private string[] _spotNames = [];
    private float[] _spotTicks = [];
    private long _spotTicksDuration;
    private int _spotPage;
    private bool _spotDeleteMode;

    // Tracks
    private (int Index, string Label)[] _audioTracks = [], _subtitleTracks = [];
    private int _selectedAudio = -1, _selectedSubtitle = -1;
    private List<(string Name, Uri Uri)> _subtitleFiles = [];
    private readonly List<string> _addedSubtitleFiles = [];
    private string? _subtitle;

    private VideoFormat CurrentFormat => _choice.Format;
    private FormatScope EditScope => _editScope ?? _choice.DefaultScope;

    // ---- Windows ----

    private void OpenWindow(WindowKind kind, Source source)
    {
        bool touch = source == Source.Touch || !_settings.StereoView;
        if (_windows.Count == 0 || touch != _touchWindows)
        {
            _windows.Clear();
            _touchWindows = touch;
        }
        if (_windows.Count > 0 && _windows[^1] == kind) return;
        _windows.Remove(kind);
        _windows.Add(kind);
        switch (kind)
        {
            case WindowKind.Format:
                _editScope = null;
                _frames.RequestRaw(Position);
                break;
            case WindowKind.Library:
                EnsureLibrary();
                break;
            case WindowKind.Calibration:
                _calibrationStep = 1;
                break;
        }
        if (touch) ShowOverlay(false);
        PushConfig();
        RefreshUi();
    }

    private void CloseWindow(bool all)
    {
        if (_windows.Count == 0) return;
        if (all) _windows.Clear();
        else _windows.RemoveAt(_windows.Count - 1);
        VideoStore.Flush();
        PushConfig();
        RefreshUi();
    }

    private WindowKind? TopWindow => _windows.Count > 0 ? _windows[^1] : null;

    private void HandleAction(MenuAction action, float fraction, Source source, int arg = 0)
    {
        _overlayShownAt = Java.Lang.JavaSystem.CurrentTimeMillis();
        switch (action)
        {
            // Transport
            case MenuAction.Seek: SeekTo((long)(fraction * Duration)); break;
            case MenuAction.PlayPause: TogglePlay(); break;
            case MenuAction.Back10: SeekTo(Position - 10_000); break;
            case MenuAction.Fwd10: SeekTo(Position + 10_000); break;
            case MenuAction.Prev:
                if (Position > 5_000) SeekTo(0);
                else { SaveCurrentPosition(); _playlist.Previous(); OpenCurrent(); }
                break;
            case MenuAction.Next:
                SaveCurrentPosition();
                _playlist.Next();
                OpenCurrent();
                break;
            case MenuAction.VolDown:
            case MenuAction.VolUp:
                _audio?.AdjustStreamVolume(Android.Media.Stream.Music,
                    action == MenuAction.VolUp ? Adjust.Raise : Adjust.Lower,
                    source == Source.Touch ? VolumeNotificationFlags.ShowUi : 0);
                break;
            case MenuAction.Shuffle:
                _playlist.Shuffle = !_playlist.Shuffle;
                _settings.Shuffle = _playlist.Shuffle;
                break;
            case MenuAction.Repeat:
                _playlist.Repeat = Playlist.NextRepeatMode(_playlist.Repeat);
                _settings.Repeat = _playlist.Repeat;
                break;
            case MenuAction.Recenter:
                _renderer.TouchPitch = 0;
                _renderer.RequestRecenter();
                break;
            case MenuAction.Exit: Finish(); return;

            // Windows
            case MenuAction.Open: OpenWindow((WindowKind)arg, source); return;
            case MenuAction.Tab: _settingsTab = arg; break;
            case MenuAction.Hide: CloseWindow(all: true); return;
            case MenuAction.Back:
            case MenuAction.Done: CloseWindow(all: false); return;

            // Horizon
            case MenuAction.PitchUp: AdjustHorizon(0.5f, 0, 0); break;
            case MenuAction.PitchDown: AdjustHorizon(-0.5f, 0, 0); break;
            case MenuAction.RollLeft: AdjustHorizon(0, 0.5f, 0); break;
            case MenuAction.RollRight: AdjustHorizon(0, -0.5f, 0); break;
            case MenuAction.YawLeft: AdjustHorizon(0, 0, 5f); break;
            case MenuAction.YawRight: AdjustHorizon(0, 0, -5f); break;
            case MenuAction.ResetHorizon:
                _state.Pitch = _state.Roll = _state.Yaw = 0;
                SaveVideoState();
                break;
            case MenuAction.SetHorizon: StartHorizonCapture(); return;

            // Spots
            case MenuAction.SpotAdd: AddSpot(); break;
            case MenuAction.SpotPrev: JumpToSpot(SpotList.Previous(_state.Spots, Position), T("No bookmarks before this point")); break;
            case MenuAction.SpotNext: JumpToSpot(SpotList.Next(_state.Spots, Position), T("No bookmarks after this point")); break;
            case MenuAction.SpotPagePrev: _spotPage = Math.Max(0, _spotPage - 1); break;
            case MenuAction.SpotPageNext: _spotPage = Math.Min(SpotsModel().PageCount - 1, _spotPage + 1); break;
            case MenuAction.SpotDeleteMode: _spotDeleteMode = !_spotDeleteMode; break;
            case MenuAction.SpotSlot: UseSpot(_spotPage * Core.SpotsModel.PerPage + arg); break;

            // Format editor
            case MenuAction.FormatProjection:
                EditFormat(f => f with { Projection = (Projection)arg });
                break;
            case MenuAction.FormatEyes:
                EditFormat(f => f with { Stereo = (StereoMode)arg, SwapEyes = (StereoMode)arg != StereoMode.Mono && f.SwapEyes });
                break;
            case MenuAction.FormatSwap: EditFormat(f => f with { SwapEyes = f.IsStereo && !f.SwapEyes }); break;
            case MenuAction.FormatLens: EditFormat(f => f with { Lens = (FisheyeLens)(((int)f.Lens + 1) % 3) }); break;
            case MenuAction.ParamSet:
            case MenuAction.ParamMinus:
            case MenuAction.ParamPlus:
            case MenuAction.ParamReset:
                ChangeParam((Param)arg, action, fraction);
                break;
            case MenuAction.NadirToggle: EditFormat(f => f with { NadirDeg = f.NadirDeg > 0 ? 0 : 30 }); break;
            case MenuAction.HorizonToggle:
                if (_state.HasHorizon)
                {
                    _state.Pitch = _state.Roll = _state.Yaw = 0;
                    SaveVideoState();
                }
                else { StartHorizonCapture(); return; }
                break;
            case MenuAction.FormatScope: SetScope((FormatScope)arg); break;
            case MenuAction.FormatAuto: FormatToAuto(); break;

            // Settings
            case MenuAction.StereoView: if ((arg == 0) != _settings.StereoView) ToggleStereoView(); break;
            case MenuAction.LensLevel: _settings.LensLevel = arg; break;
            case MenuAction.GazeClick: _settings.GazeClick = !_settings.GazeClick; break;
            case MenuAction.ControlsSize:
                _settings.ControlsSize = arg;
                ApplyOverlaySize();
                break;
            case MenuAction.EdgeButtons: _settings.EdgeButtonsOn = !_settings.EdgeButtonsOn; break;
            case MenuAction.EdgeFollowHead: _settings.EdgeFollowHead = arg == 1; break;
            case MenuAction.LookDownMenu: _settings.LookDownMenu = !_settings.LookDownMenu; break;
            case MenuAction.TrackingMode:
                _settings.Tracking = (TrackingMode)arg;
                _tracker.Mode = (TrackingMode)arg;
                break;
            case MenuAction.Calibrate:
                _tracker.Calibrate();
                Toast.MakeText(this, T("Keep the phone still for 5 seconds"), ToastLength.Short)!.Show();
                break;
            case MenuAction.AutoRecenter: _settings.AutoRecenter = !_settings.AutoRecenter; break;
            case MenuAction.Quality: _settings.HighQuality = arg == 0; break;
            case MenuAction.MeshDetail: _settings.DenseMesh = arg == 1; break;
            case MenuAction.ShowFps: _settings.ShowFps = !_settings.ShowFps; break;
            case MenuAction.ViewerReset:
                _settings.ViewerUrl = null;
                if (_settings.LensLevel == 3) _settings.LensLevel = 0;
                break;
            case MenuAction.ProfileUse: UseProfile(arg, source); break;
            case MenuAction.ProfileDelete: DeleteProfile(arg); break;
            case MenuAction.ClearVideo: ClearVideoData(); break;
            case MenuAction.ClearFolder:
                if (_stateItem is { } folderItem) _store.SetFolder(folderItem, null);
                RefreshFormat();
                break;
            case MenuAction.CalibrationStep:
                if (arg > 3) { CloseWindow(all: false); return; }
                _calibrationStep = Math.Clamp(arg, 1, 3);
                break;

            // Library
            case MenuAction.LibraryFolder: LoadLibraryFolder(arg); break;
            case MenuAction.LibraryFilter:
                if (_library is { } lf) { lf.Filter = arg; lf.Page = 0; RebuildLibraryView(); }
                break;
            case MenuAction.LibrarySort:
                if (_library is { } ls) { ls.Sort = (ls.Sort + 1) % LibraryModel.SortLabels.Length; RebuildLibraryView(); }
                break;
            case MenuAction.LibraryPagePrev: if (_library is { } lp) lp.Page = Math.Max(0, lp.Page - 1); break;
            case MenuAction.LibraryPageNext: if (_library is { } ln) ln.Page++; break;
            case MenuAction.LibraryItem: PlayFromLibrary(arg); return;
            case MenuAction.LibraryContinue: ContinueLast(); return;

            // Tracks
            case MenuAction.AudioTrack: SelectAudio(arg); break;
            case MenuAction.SubtitleTrack: SelectSubtitle(arg); break;
        }
        PushConfig();
        RefreshUi();
    }

    private void StartHorizonCapture()
    {
        CloseWindow(all: true);
        ShowOverlay(false);
        _glView.QueueEvent(() => _renderer.StartHorizonCapture());
        if (!_settings.StereoView)
            Toast.MakeText(this, T("Tilt the phone until the line matches the horizon, then hold still"), ToastLength.Long)!.Show();
    }

    private void AdjustHorizon(float pitch, float roll, float yaw)
    {
        _state.Pitch = Math.Clamp(_state.Pitch + pitch, -90f, 90f);
        _state.Roll = VideoState.NormalizeAngle(_state.Roll + roll);
        _state.Yaw = VideoState.NormalizeAngle(_state.Yaw + yaw);
        SaveVideoState();
    }

    private void ToggleStereoView()
    {
        _settings.StereoView = !_settings.StereoView;
        _renderer.TouchYaw = 0;
        _renderer.TouchPitch = 0;
        _renderer.RequestRecenter();
        if (_windows.Count > 0) _touchWindows = !_settings.StereoView || _touchWindows;
        PushConfig();
        RefreshUi();
    }

    // ---- Format ----

    /// <summary>Works out the format again from this video, its folder, the file and its name.</summary>
    private void RefreshFormat()
    {
        if (_stateItem is not { } item) return;
        _choice = FormatResolver.Resolve(new FormatInputs(_state.FormatOverride, _state.Profile, _store.GetFolder(item),
            _store.Profiles(item), item.Name, _videoWidth, _videoHeight, MediaInfoReader.FormatOf(_spherical)));
        PushConfig();
    }

    /// <summary>Applies a change to the format where the editor saves (this video, the folder or a profile).</summary>
    private void EditFormat(Func<VideoFormat, VideoFormat> change, bool flush = true)
    {
        if (_stateItem is not { } item) return;
        var f = change(CurrentFormat);
        if (f.Projection == Projection.Mesh && _spherical?.LeftMesh is null) return;
        bool tags = FormatDetector.HasTags(item.Name);
        switch (EditScope)
        {
            case FormatScope.Folder:
                _store.SetFolder(item, new FolderEntry { Format = f }, flush);
                // The folder does not apply to names with format tags: keep this video in step.
                _state.FormatOverride = tags ? f : null;
                _state.Profile = null;
                break;
            case FormatScope.Profile when _store.CanUseProfiles(item):
                {
                    string name = _store.GetFolder(item)?.Profile ?? _state.Profile ?? _store.NewProfileName(item, f.ShortLabel);
                    _store.SetProfile(item, name, f, flush);
                    if (_store.GetFolder(item)?.Profile != name) _store.SetFolder(item, new FolderEntry { Profile = name }, flush);
                    _state.FormatOverride = null;
                    _state.Profile = tags ? name : null;
                    break;
                }
            default:
                _state.FormatOverride = f;
                _state.Profile = null;
                break;
        }
        _store.Save(item, _state, flush);
        RefreshFormat();
        SpotsChanged();
    }

    private void SetScope(FormatScope scope)
    {
        if (_stateItem is { } item && scope == FormatScope.Profile && !_store.CanUseProfiles(item))
        {
            Toast.MakeText(this, T("Profiles are saved in vrview.json: choose the folder in the list so it can be written to."), ToastLength.Long)!.Show();
            return;
        }
        _editScope = scope;
        // Moves the format as it is now to the chosen place.
        EditFormat(f => f);
        Toast.MakeText(this, scope switch
        {
            FormatScope.Folder => T("Format saved for the whole folder"),
            FormatScope.Profile => F("Format saved in the profile “{0}”", _choice.Profile),
            _ => T("Format saved for this video only"),
        }, ToastLength.Short)!.Show();
    }

    /// <summary>"Back to automatic": forgets the format where the editor saves.</summary>
    private void FormatToAuto()
    {
        if (_stateItem is not { } item) return;
        if (EditScope is FormatScope.Folder or FormatScope.Profile) _store.SetFolder(item, null);
        _state.FormatOverride = null;
        _state.Profile = null;
        _store.Save(item, _state, flush: true);
        _editScope = null;
        RefreshFormat();
        SpotsChanged();
        Toast.MakeText(this, F("Format: {0} ({1})", CurrentFormat.Label, _choice.Note), ToastLength.Short)!.Show();
    }

    private void ChangeParam(Param p, MenuAction action, float fraction)
    {
        var spec = Core.Params.Spec(p);
        if (Core.Params.IsFormatParam(p))
        {
            // Sliders and auto-repeating − / + write the file later (periodic flush / closing the window).
            bool flush = action == MenuAction.ParamReset;
            EditFormat(f => action switch
            {
                MenuAction.ParamSet => Core.Params.Set(f, p, spec.FromFraction(fraction)),
                MenuAction.ParamMinus => Core.Params.Step(f, p, -1),
                MenuAction.ParamPlus => Core.Params.Step(f, p, +1),
                _ => Core.Params.Reset(f, p),
            }, flush);
            return;
        }
        float value = GetSetting(p);
        value = action switch
        {
            MenuAction.ParamSet => spec.FromFraction(fraction),
            MenuAction.ParamMinus => spec.Clamp(value - spec.Step),
            MenuAction.ParamPlus => spec.Clamp(value + spec.Step),
            _ => spec.Standard,
        };
        switch (p)
        {
            case Param.Ipd: _settings.IpdShift = value / 100f; break;
            case Param.Zoom: _settings.Fov = value; break;
            case Param.MenuShift: _settings.MenuShift = value / 100f; break;
            case Param.Opacity:
                _settings.ControlsOpacity = value / 100f;
                ApplyOverlaySize();
                break;
            case Param.DwellTime: _settings.DwellSeconds = value; break;
            case Param.EdgeAngle: _settings.EdgeButtonsAngle = value; break;
            case Param.SubtitleSize: _settings.SubtitleSize = value; break;
        }
    }

    private float GetSetting(Param p) => p switch
    {
        Param.Ipd => _settings.IpdShift * 100f,
        Param.Zoom => _settings.Fov,
        Param.MenuShift => _settings.MenuShift * 100f,
        Param.Opacity => _settings.ControlsOpacity * 100f,
        Param.DwellTime => _settings.DwellSeconds,
        Param.EdgeAngle => _settings.EdgeButtonsAngle,
        Param.SubtitleSize => _settings.SubtitleSize,
        _ => 0,
    };

    private string[] ProfileNames() => _stateItem is { } item ? [.. _store.Profiles(item).Keys] : [];

    private void UseProfile(int index, Source source)
    {
        var names = ProfileNames();
        if (_stateItem is not { } item || index < 0 || index >= names.Length) return;
        string name = names[index];
        if (source == Source.Touch)
        {
            // On the touch screen a profile can also be renamed.
            new AlertDialog.Builder(this)
                .SetTitle(F("Profile “{0}”", name))!
                .SetItems([T("Use in this folder"), T("Rename…")], (_, e) =>
                {
                    if (e.Which == 0) ApplyProfileToFolder(item, name);
                    else RenameProfile(item, name);
                })!
                .Show();
            return;
        }
        ApplyProfileToFolder(item, name);
    }

    private void ApplyProfileToFolder(MediaItem item, string name)
    {
        _store.SetFolder(item, new FolderEntry { Profile = name });
        Toast.MakeText(this, F("The folder uses the profile “{0}” (videos without tags in their name)", name), ToastLength.Short)!.Show();
        RefreshFormat();
        RefreshUi();
    }

    private void RenameProfile(MediaItem item, string name)
    {
        var input = new EditText(this) { Text = name };
        input.SetSingleLine(true);
        new AlertDialog.Builder(this)
            .SetTitle(T("New profile name"))!
            .SetView(input)!
            .SetPositiveButton(T("Save"), (_, _) =>
            {
                if (!_store.RenameProfile(item, name, input.Text ?? ""))
                    Toast.MakeText(this, T("That name already exists or is empty"), ToastLength.Short)!.Show();
                RefreshFormat();
                RefreshUi();
            })!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }

    private void DeleteProfile(int index)
    {
        var names = ProfileNames();
        if (_stateItem is not { } item || index < 0 || index >= names.Length) return;
        _store.DeleteProfile(item, names[index]);
        _state = _store.Get(item);
        RefreshFormat();
        Toast.MakeText(this, F("Profile “{0}” deleted (whatever used it keeps its format)", names[index]), ToastLength.Short)!.Show();
    }

    private void ClearVideoData()
    {
        if (_stateItem is not { } item) return;
        _state = new VideoState();
        _store.Save(item, _state, flush: true);
        RefreshFormat();
        SpotsChanged();
        Toast.MakeText(this, T("This video's data deleted"), ToastLength.Short)!.Show();
    }

    // ---- Spots ----

    private void AddSpot()
    {
        if (!_prepared) return;
        long pos = Position;
        if (!SpotList.Add(_state.Spots, pos))
        {
            Toast.MakeText(this, T("There is already a bookmark here"), ToastLength.Short)!.Show();
            return;
        }
        SaveVideoState();
        SpotsChanged();
        // Show the page that contains the new spot.
        _spotPage = Array.IndexOf(_spots, pos) / Core.SpotsModel.PerPage;
        Toast.MakeText(this, F("Bookmark at {0}", MediaUtil.FormatTime(pos)), ToastLength.Short)!.Show();
    }

    private void JumpToSpot(long? spot, string noneMessage)
    {
        if (spot is { } ms) SeekTo(ms);
        else Toast.MakeText(this, _state.Spots.Count == 0 ? T("No bookmarks yet") : noneMessage, ToastLength.Short)!.Show();
    }

    /// <summary>A spot thumbnail was chosen: jump to it, or delete it in delete mode.</summary>
    private void UseSpot(int index)
    {
        if (index < 0 || index >= _state.Spots.Count) return;
        long ms = _state.Spots[index];
        if (_spotDeleteMode)
        {
            _state.Spots.RemoveAt(index);
            _state.SpotNames.Remove(ms);
            SaveVideoState();
            SpotsChanged();
            _spotPage = Math.Min(_spotPage, SpotsModel().PageCount - 1);
            if (_state.Spots.Count == 0) _spotDeleteMode = false;
            Toast.MakeText(this, F("Bookmark deleted ({0})", MediaUtil.FormatTime(ms)), ToastLength.Short)!.Show();
            return;
        }
        SeekTo(ms);
        CloseWindow(all: true);
    }

    /// <summary>Touch only: names the spot at the current position (creating it if needed).</summary>
    private void NameSpot()
    {
        if (!_prepared) return;
        long pos = Position;
        long? near = _state.Spots.Cast<long?>().FirstOrDefault(s => Math.Abs(s!.Value - pos) < 3000);
        long at = near ?? pos;
        var input = new EditText(this) { Text = _state.SpotNames.GetValueOrDefault(at, "") };
        input.SetSingleLine(true);
        new AlertDialog.Builder(this)
            .SetTitle(F("Bookmark name at {0}", MediaUtil.FormatTime(at)))!
            .SetView(input)!
            .SetPositiveButton(T("Save"), (_, _) =>
            {
                if (near is null) SpotList.Add(_state.Spots, at);
                string name = (input.Text ?? "").Trim();
                if (name.Length > 0) _state.SpotNames[at] = name;
                else _state.SpotNames.Remove(at);
                SaveVideoState();
                SpotsChanged();
                RefreshUi();
            })!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }

    /// <summary>Refreshes the spot snapshots and asks for any missing thumbnails.</summary>
    private void SpotsChanged()
    {
        _spots = _state.Spots.ToArray();
        _spotNames = _spots.Select(s => _state.SpotNames.GetValueOrDefault(s, "")).ToArray();
        _spotTicksDuration = -1;
        if (_stateItem is { } item)
        {
            _frames.SetVideo(item.Location, CurrentFormat);
            _frames.Request(_spots);
        }
    }

    private SpotsModel SpotsModel() => new(_spots, _spotNames, _spotPage, _spotDeleteMode, _frames.Version);

    // ---- Pictures ----

    private Bitmap? Picture(UiElement e) => e.Kind switch
    {
        UiKind.Preview => _frames.Raw,
        UiKind.Thumb when e.Action == MenuAction.SpotSlot => _frames.Get(e.Ms),
        UiKind.Thumb when e.Action == MenuAction.LibraryItem && _library is { } lib && e.Ms >= 0 && e.Ms < lib.View.Count =>
            _libThumbs.Get(lib.View[(int)e.Ms].Location),
        _ => null,
    };

    /// <summary>Frame for the preview above the seek bar (GL thread; decoding is started if needed).</summary>
    private Bitmap? SeekPreview(long ms)
    {
        var bmp = _frames.Get(ms);
        if (bmp is null) _frames.Request([ms]);
        return bmp;
    }

    private void PicturesChanged()
    {
        _renderer.PicturesVersion++;
        _windowView.Repaint();
        if (_library is not null) RefreshUi();
    }

    // ---- Tracks ----

    private void ReadTracks()
    {
        if (_player is null || !_prepared) return;
        try
        {
            var infos = _player.GetTrackInfo() ?? [];
            var audio = new List<(int, string)>();
            var subs = new List<(int, string)>();
            for (int i = 0; i < infos.Length; i++)
            {
                var t = infos[i];
                string lang = t.Language is { Length: > 0 } l && l != "und" ? new CultureInfo(l).NativeName : "";
                if (t.TrackType == MediaTrackType.Audio) audio.Add((i, lang.Length > 0 ? lang : F("Track {0}", audio.Count + 1)));
                else if (t.TrackType == MediaTrackType.Timedtext) subs.Add((i, lang.Length > 0 ? lang : F("Subtitles {0}", subs.Count + 1)));
            }
            // Files added with AddTimedTextSource come last, in the order they were added.
            int embedded = subs.Count - _addedSubtitleFiles.Count;
            for (int k = Math.Max(0, embedded); k < subs.Count; k++)
                subs[k] = (subs[k].Item1, _addedSubtitleFiles[k - embedded]);
            _audioTracks = audio.Count > 1 ? audio.ToArray() : [];
            _subtitleTracks = subs.ToArray();
            if (OperatingSystem.IsAndroidVersionAtLeast(21) && _audioTracks.Length > 0)
            {
                int selected = _player.GetSelectedTrack(MediaTrackType.Audio);
                _selectedAudio = Array.FindIndex(_audioTracks, a => a.Index == selected);
            }
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or CultureNotFoundException)
        {
            Android.Util.Log.Warn("VrView", "Track info failed: " + ex.Message);
        }
    }

    /// <summary>Subtitle files next to the video become selectable tracks.</summary>
    private void AddSubtitleFiles()
    {
        if (_player is null || !_prepared) return;
        bool added = false;
        foreach (var (name, uri) in _subtitleFiles)
        {
            if (_addedSubtitleFiles.Contains(name)) continue;
            _addedSubtitleFiles.Add(name);
            try
            {
                _player.AddTimedTextSource(this, uri, "application/x-subrip");
                added = true;
            }
            catch (Exception ex) when (ex is Java.Lang.Exception or ArgumentException)
            {
                _addedSubtitleFiles.Remove(name);
                Android.Util.Log.Warn("VrView", $"Subtitle {name} failed: {ex.Message}");
            }
        }
        if (added) ReadTracks();
    }

    private void SelectAudio(int index)
    {
        if (_player is null || index < 0 || index >= _audioTracks.Length) return;
        try
        {
            _player.SelectTrack(_audioTracks[index].Index);
            _selectedAudio = index;
        }
        catch (Java.Lang.Exception ex) { Toast.MakeText(this, F("Could not change the audio: {0}", ex.Message), ToastLength.Short)!.Show(); }
    }

    private void SelectSubtitle(int index)
    {
        if (_player is null) return;
        try
        {
            if (_selectedSubtitle >= 0 && _selectedSubtitle < _subtitleTracks.Length)
                _player.DeselectTrack(_subtitleTracks[_selectedSubtitle].Index);
            _selectedSubtitle = -1;
            _subtitle = null;
            if (index >= 0 && index < _subtitleTracks.Length)
            {
                _player.SelectTrack(_subtitleTracks[index].Index);
                _selectedSubtitle = index;
            }
        }
        catch (Java.Lang.Exception ex) { Toast.MakeText(this, F("Could not change the subtitles: {0}", ex.Message), ToastLength.Short)!.Show(); }
    }

    private void OnTimedText(string? text)
    {
        _subtitle = string.IsNullOrWhiteSpace(text) ? null : text;
        PushConfig();
    }

    // ---- Library ----

    private sealed class LibraryState
    {
        public List<string> Trees = [];
        public string[] Names = [];
        public int Folder;
        public List<MediaItem> Items = [];
        public List<MediaItem> View = [];
        public bool Loading;
        public int Filter, Sort, Page;
        public CancellationTokenSource? Scan;
    }

    private LibraryState? _library;

    private void EnsureLibrary()
    {
        if (_library is not null) return;
        var lib = _library = new LibraryState();
        var allowed = ContentResolver!.PersistedUriPermissions.Where(p => p.IsReadPermission).Select(p => p.Uri!.ToString()!).ToHashSet();
        lib.Trees = _settings.RecentFolders.Where(allowed.Contains).ToList();
        string? currentTree = Current is { } c ? MediaUtil.TreeOf(c.Location) : null;
        if (currentTree is not null && !lib.Trees.Contains(currentTree)) lib.Trees.Insert(0, currentTree);
        lib.Names = lib.Trees.Select(t => FolderScanner.FolderName(ContentResolver!, Uri.Parse(t)!)).ToArray();
        // Start with the playing list itself.
        lib.Folder = currentTree is null ? -1 : lib.Trees.IndexOf(currentTree);
        lib.Items = _playlist.Items.ToList();
        RebuildLibraryView();
    }

    private void LoadLibraryFolder(int index)
    {
        if (_library is not { } lib || index < 0 || index >= lib.Trees.Count) return;
        lib.Scan?.Cancel();
        var cts = lib.Scan = new CancellationTokenSource();
        lib.Folder = index;
        lib.Page = 0;
        lib.Loading = true;
        lib.Items = [];
        RebuildLibraryView();
        var tree = Uri.Parse(lib.Trees[index])!;
        bool recursive = _settings.IncludeSubfolders;
        Task.Run(() =>
        {
            List<MediaItem> found;
            try
            {
                FolderSettingsFile.Reload(ContentResolver!, tree.ToString()!);
                found = FolderScanner.Scan(ContentResolver!, tree, recursive, cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is Java.Lang.Exception or IOException) { found = []; }
            RunOnUiThread(() =>
            {
                if (cts.IsCancellationRequested || _library != lib) return;
                lib.Loading = false;
                lib.Items = found;
                RebuildLibraryView();
                RefreshUi();
            });
        });
    }

    private VideoFormat LibraryFormat(MediaItem item, VideoState st) =>
        FormatResolver.Resolve(new FormatInputs(st.FormatOverride, st.Profile, _store.GetFolder(item), _store.Profiles(item),
            item.Name, 0, 0, null)).Format;

    private void RebuildLibraryView()
    {
        if (_library is not { } lib) return;
        var rows = lib.Items.Select(i => (Item: i, State: _store.Get(i))).ToList();
        rows = rows.Where(r => lib.Filter switch
        {
            1 => LibraryFormat(r.Item, r.State).Projection is Projection.Dome180 or Projection.Fisheye or Projection.Mesh,
            2 => LibraryFormat(r.Item, r.State).Projection is Projection.Sphere360 or Projection.Eac or Projection.Cubemap,
            3 => LibraryFormat(r.Item, r.State).Projection == Projection.Flat,
            4 => r.State.PositionMs == 0 && r.State.Spots.Count == 0,
            5 => r.State.Spots.Count > 0,
            _ => true,
        }).ToList();
        rows = lib.Sort switch
        {
            1 => rows.OrderByDescending(r => Progress(r.Item, r.State)).ToList(),
            2 => rows.OrderByDescending(r => r.Item.SizeBytes).ToList(),
            _ => rows.OrderBy(r => r.Item.Name, NaturalStringComparer.Instance).ToList(),
        };
        lib.View = rows.Select(r => r.Item).ToList();
        int pages = Math.Max(1, (lib.View.Count + LibraryModel.PerPage - 1) / LibraryModel.PerPage);
        lib.Page = Math.Clamp(lib.Page, 0, pages - 1);
    }

    private float Progress(MediaItem item, VideoState st)
    {
        long d = _libThumbs.Duration(item.Location);
        return d > 0 ? Math.Clamp((float)st.PositionMs / d, 0f, 1f) : st.PositionMs > 0 ? 0.01f : 0f;
    }

    private LibraryModel BuildLibraryModel()
    {
        if (_library is not { } lib) return LibraryModel.Empty;
        int pages = Math.Max(1, (lib.View.Count + LibraryModel.PerPage - 1) / LibraryModel.PerPage);
        var page = lib.View.Skip(lib.Page * LibraryModel.PerPage).Take(LibraryModel.PerPage).ToList();
        var entries = page.Select((item, i) =>
        {
            var st = _store.Get(item);
            var f = LibraryFormat(item, st);
            _libThumbs.Request(item, f);
            long d = _libThumbs.Duration(item.Location);
            var parts = new List<string>();
            if (d > 0) parts.Add(MediaUtil.FormatTime(d));
            if (st.Spots.Count > 0) parts.Add(BookmarkCount(st.Spots.Count));
            else if (st.PositionMs == 0) parts.Add(T("new"));
            string name = item.Name[(item.Name.LastIndexOf('/') + 1)..];
            return new LibraryEntry(lib.Page * LibraryModel.PerPage + i, System.IO.Path.GetFileNameWithoutExtension(name),
                f.ShortLabel, string.Join(" · ", parts), Progress(item, st), item.Location == Current?.Location);
        }).ToArray();
        return new LibraryModel(lib.Names, Math.Max(0, lib.Folder), lib.Filter, lib.Sort, lib.Page, pages, lib.View.Count,
            entries, _settings.LastVideoName, _playlist.Shuffle, _playlist.Repeat, lib.Loading, _libThumbs.Version);
    }

    private void PlayFromLibrary(int slot)
    {
        if (_library is not { } lib) return;
        int index = lib.Page * LibraryModel.PerPage + slot;
        if (index < 0 || index >= lib.View.Count) return;
        CloseWindow(all: true);
        PlayItems(lib.View, index);
    }

    private void ContinueLast()
    {
        string? uri = _settings.LastVideoUri;
        if (uri is null) return;
        CloseWindow(all: true);
        int idx = _playlist.Items.ToList().FindIndex(i => i.Location == uri);
        if (idx >= 0)
        {
            if (idx != _playlist.CurrentIndex) { SaveCurrentPosition(); _playlist.JumpTo(idx); OpenCurrent(); }
            return;
        }
        PlayItems([new MediaItem(uri, _settings.LastVideoName ?? T("video"))], 0);
    }

    // ---- Info ----

    private string[] BuildInfo()
    {
        var item = _stateItem;
        var f = _facts;
        var rows = new List<string>();
        void Row(string key, string value) { rows.Add(key); rows.Add(value); }
        string name = item?.Name ?? "";
        Row(T("File"), name);
        if (f is null) Row(T("Reading"), T("file information…"));
        else
        {
            Row(T("Size"), $"{FormatBytes(f.SizeBytes)} · {MediaUtil.FormatTime(f.DurationMs > 0 ? f.DurationMs : Duration)}");
            string depth = f.BitDepth > 8 ? $" {f.BitDepth} bit" : "";
            int w = f.Width > 0 ? f.Width : _videoWidth, h = f.Height > 0 ? f.Height : _videoHeight;
            Row(T("Video"), $"{w}×{h} · {(f.FrameRate > 0 ? f.FrameRate.ToString("0.##", CultureInfo.InvariantCulture) + " fps" : "? fps")} · {f.Codec}{depth}");
            if (f.Bitrate > 0) Row(T("Bitrate"), $"{f.Bitrate / 1e6:0.0} Mb/s");
            Row(T("Decoder"), f.Decoder.Length == 0 ? T("none found: this phone may not be able to play this video")
                : f.HardwareDecoder ? F("hardware ✓ ({0})", f.Decoder) : F("software, may be slow ({0})", f.Decoder));
            Row(T("Frames shown"), f.FrameRate > 0 ? F("{0:0.#} per second of {1:0.#}", _renderer.VideoFps, f.FrameRate) : F("{0:0.#} per second", _renderer.VideoFps));
            Row(T("VR metadata"), f.Spherical is { } s ? SphericalText(s) : T("none"));
        }
        var cf = CurrentFormat;
        string coverage = cf.IsEquirect ? F(", coverage {0:0}°×{1:0}°", cf.EquirectHFov, cf.EquirectVFov)
            : cf.Projection == Projection.Fisheye ? F(", lens {0:0}° {1}", cf.FisheyeFov, Core.Params.LensLabel(cf.Lens)) : "";
        Row(T("Format"), $"{cf.ShortLabel}{coverage}" + (cf.NadirDeg > 0 ? F(", cover {0:0}°", cf.NadirDeg) : ""));
        Row(T("Comes from"), _choice.Candidates.First(c => c.Used).Text);
        Row(T("Horizon"), _state.HasHorizon ? F("level {0:+0.0;-0.0}°, tilt {1:+0.0;-0.0}°, turn {2:+0;-0}°", _state.Roll, _state.Pitch, _state.Yaw) : T("no correction"));
        Row(T("Bookmarks"), _spots.Length == 0 ? T("no bookmarks") : $"{_spots.Length} · " + string.Join(", ", _spots.Take(6).Select(MediaUtil.FormatTime)));
        Row(T("Saved in"), item is null ? "" : _store.StorageText(item));
        Row(T("Sensor"), Windows.TrackingText(_tracker.Status));
        return rows.ToArray();
    }

    private static string SphericalText(SphericalInfo s)
    {
        string stereo = s.Stereo switch { StereoMode.SideBySide => T("side by side"), StereoMode.TopBottom => T("top-bottom"), StereoMode.Mono => "2D", _ => "?" };
        string projection = s.Projection switch
        {
            SphericalProjection.Equirect => $"equirectangular {s.HorizontalCoverageDeg:0}°×{s.VerticalCoverageDeg:0}°",
            SphericalProjection.Cubemap => "cubemap",
            SphericalProjection.Mesh => s.LeftMesh is null ? T("mesh (could not be read)") : T("embedded mesh (VR180)"),
            _ => T("stereo only"),
        };
        return $"{projection}, {stereo}";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        <= 0 => "?",
        < 1L << 30 => $"{bytes / (1024.0 * 1024):0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };

    // ---- Publishing state ----

    private void PushConfig()
    {
        var (sw, sh) = ScreenMeters();
        var view = _settings.View(sw, sh);
        bool calibrating = TopWindow == WindowKind.Calibration && !_touchWindows;
        var mesh = CurrentFormat.Projection == Projection.Mesh ? _spherical : null;
        _renderer.Config = new RenderConfig(CurrentFormat, _videoWidth, _videoHeight,
            _state.Pitch, _state.Roll, _state.Yaw, view, calibrating ? _calibrationStep : 0,
            mesh?.LeftMesh, mesh?.RightMesh, _subtitle);
        _renderer.DurationMsForPreview = (int)Math.Min(int.MaxValue, Duration);
    }

    private void RefreshUi()
    {
        if (_overlay is null || _renderer is null) return;
        var (sw, sh) = ScreenMeters();
        var view = _settings.View(sw, sh);
        int vol = 0;
        if (_audio is not null)
        {
            int max = _audio.GetStreamMaxVolume(Android.Media.Stream.Music);
            vol = max > 0 ? _audio.GetStreamVolume(Android.Media.Stream.Music) * 100 / max : 0;
        }
        var tracking = _tracker.Status;
        long duration = Duration;
        if (duration != _spotTicksDuration)
        {
            _spotTicksDuration = duration;
            _spotTicks = duration > 0 ? _spots.Select(s => Math.Clamp((float)s / duration, 0f, 1f)).ToArray() : [];
        }
        // Rounded to whole seconds so the window is only repainted when the text changes.
        var player = new PlayerStatus(Current?.Name ?? "", Position / 1000 * 1000, duration, IsPlaying, vol,
            Math.Max(0, _playlist.Order.ToList().IndexOf(_playlist.CurrentIndex)), _playlist.Count,
            $"{CurrentFormat.Label} ({_choice.Note})", Windows.SensorChip(tracking), MathF.Round(_renderer.VideoFps));
        UpdateOverlay(player);
        UpdateSessionState();

        var top = TopWindow;
        if (top is null)
        {
            _ui = null;
            _renderer.Window = null;
            _windowView.Window = null;
            _windowView.Visibility = ViewStates.Gone;
            return;
        }
        var item = _stateItem;
        var editor = item is null ? null : new FormatEditorModel(item.Name, _choice, EditScope, _store.GetFolder(item)?.Profile,
            FormatDetector.HasTags(item.Name), _state.Pitch, _state.Roll, _state.Yaw, _spherical?.LeftMesh is not null);
        var state = new UiState(top.Value, _settingsTab, player, _spotTicks, editor, SpotsModel(), view, tracking,
            top == WindowKind.Info ? BuildInfo() : [],
            top == WindowKind.Library ? BuildLibraryModel() : LibraryModel.Empty,
            new TracksModel(_audioTracks.Select(a => a.Label).ToArray(), _selectedAudio,
                _subtitleTracks.Select(s => s.Label).ToArray(), _selectedSubtitle),
            ProfileNames(), item is null ? "" : _store.StorageText(item), _calibrationStep);
        var window = Windows.Build(state);
        if (!Windows.SameContent(window, _ui)) _ui = window;
        if (_touchWindows)
        {
            _renderer.Window = null;
            _windowView.SetPreviewFormat(CurrentFormat);
            _windowView.Window = _ui;
            _windowView.Visibility = ViewStates.Visible;
        }
        else
        {
            _renderer.Window = _ui;
            _windowView.Window = null;
            _windowView.Visibility = ViewStates.Gone;
        }
    }
}
