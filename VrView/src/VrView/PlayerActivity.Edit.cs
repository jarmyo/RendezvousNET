using Android.Widget;
using VrView.Core;

namespace VrView;

/// <summary>
/// Edit mode: marks parts of the video for removal ("cortes") instead of spots, shows them on the seek
/// bars, and skips them while playing when "Saltar cortes" is on (in both modes, so the result can be
/// previewed). The phone only marks; VrCut removes the parts on a computer and moves the spots.
/// </summary>
public partial class PlayerActivity
{
    private bool _editMode;
    private long _cutStart = -1;
    private int _cutPage;
    private bool _cutDeleteMode;
    // The cuts were marked on another version of the file (its duration changed): shown, never skipped.
    private bool _cutsStale;
    // Position at the previous tick, to tell playing into a cut from jumping into it.
    private long _lastTickPosition = -1;
    // Right after a seek the player may still report the old position for a moment.
    private long _seekedAt;
    private const long SeekSettleMs = 1_000;

    private void NoteSeek() => _seekedAt = Android.OS.SystemClock.ElapsedRealtime();

    // Snapshots for the windows, replaced only when the cuts change.
    private CutRange[] _cuts = [];
    private float[] _cutRanges = [];
    private long _cutRangesDuration = -1;

    private void ToggleEditMode()
    {
        _editMode = !_editMode;
        _cutStart = -1;
        _cutDeleteMode = false;
        _spotDeleteMode = false;
        // The spots window belongs to watching and the cuts window to editing.
        int i = _windows.IndexOf(_editMode ? WindowKind.Spots : WindowKind.Cuts);
        if (i >= 0) _windows[i] = _editMode ? WindowKind.Cuts : WindowKind.Spots;
        string text = _editMode
            ? "✂ Modo edición: marca las partes a quitar. Se cortan en la computadora con VrCut."
            : "Modo visión" + (_state.Cuts.Count > 0 && _settings.SkipCuts ? ": las partes marcadas se saltan" : "");
        if (_editMode && _stateItem is { } item && (item.IsRemote || MediaUtil.TreeOf(item.Location) is null))
            text += " (este video no está en una carpeta elegida: VrCut no verá los cortes)";
        Toast.MakeText(this, text, ToastLength.Long)!.Show();
    }

    /// <summary>"✂ Inicio de corte" / "✂ Fin del corte".</summary>
    private void MarkCut()
    {
        if (!_prepared) return;
        long pos = Position;
        if (_cutStart < 0)
        {
            _cutStart = pos;
            Toast.MakeText(this, $"Corte desde {MediaUtil.FormatTime(pos)}: ve al final de la parte y pulsa «✂ Fin del corte»", ToastLength.Short)!.Show();
            return;
        }
        long start = _cutStart;
        _cutStart = -1;
        AddCut(start, pos);
    }

    private void AddCut(long a, long b)
    {
        if (!_prepared) return;
        long duration = Duration;
        if (_cutsStale)
        {
            // Old cuts of another version of the file cannot be mixed with new ones.
            _state.Cuts.Clear();
            _cutsStale = false;
            Toast.MakeText(this, "Se borraron los cortes viejos (eran de otra versión del archivo)", ToastLength.Short)!.Show();
        }
        bool first = _state.Cuts.Count == 0;
        if (!CutList.Add(_state.Cuts, a, b, duration))
        {
            Toast.MakeText(this, "La parte marcada es demasiado corta", ToastLength.Short)!.Show();
            return;
        }
        if (first || _state.CutsDurationMs <= 0) _state.CutsDurationMs = duration;
        SaveVideoState();
        SpotsChanged();
        long from = Math.Min(a, b), to = Math.Min(Math.Max(a, b), duration > 0 ? duration : long.MaxValue);
        int index = CutList.IndexAt(_state.Cuts, from);
        if (index >= 0) _cutPage = index / EditModel.PerPage;
        Toast.MakeText(this, $"✂ Se quitará {MediaUtil.FormatTime(from)} → {MediaUtil.FormatTime(to)}", ToastLength.Short)!.Show();
    }

    private void JumpToCut(CutRange? cut, string noneMessage)
    {
        if (cut is { } c) SeekTo(c.FromMs);
        else Toast.MakeText(this, _state.Cuts.Count == 0 ? "Aún no hay cortes" : noneMessage, ToastLength.Short)!.Show();
    }

    /// <summary>A cut thumbnail was chosen: jump to its start, or delete it in delete mode.</summary>
    private void UseCut(int index)
    {
        if (index < 0 || index >= _state.Cuts.Count) return;
        var cut = _state.Cuts[index];
        if (_cutDeleteMode)
        {
            _state.Cuts.RemoveAt(index);
            if (_state.Cuts.Count == 0) { _state.CutsDurationMs = 0; _cutDeleteMode = false; _cutsStale = false; }
            SaveVideoState();
            SpotsChanged();
            _cutPage = Math.Min(_cutPage, Math.Max(0, (_state.Cuts.Count - 1) / EditModel.PerPage));
            Toast.MakeText(this, $"Corte borrado ({MediaUtil.FormatTime(cut.FromMs)} → {MediaUtil.FormatTime(cut.ToMs)})", ToastLength.Short)!.Show();
            return;
        }
        SeekTo(cut.FromMs);
        CloseWindow(all: true);
    }

    private bool HandleEditAction(MenuAction action, int arg)
    {
        switch (action)
        {
            case MenuAction.EditMode: ToggleEditMode(); break;
            case MenuAction.Back1: SeekTo(Position - 1_000); break;
            case MenuAction.Fwd1: SeekTo(Position + 1_000); break;
            case MenuAction.CutMark: MarkCut(); break;
            case MenuAction.CutCancel: _cutStart = -1; break;
            case MenuAction.CutToHere: AddCut(0, Position); break;
            case MenuAction.CutFromHere: AddCut(Position, Duration); break;
            case MenuAction.CutPrev: JumpToCut(CutList.Previous(_state.Cuts, Position), "No hay cortes antes"); break;
            case MenuAction.CutNext: JumpToCut(CutList.Next(_state.Cuts, Position), "No hay cortes después"); break;
            case MenuAction.CutSkip:
                _settings.SkipCuts = !_settings.SkipCuts;
                Toast.MakeText(this, _settings.SkipCuts ? "Las partes marcadas se saltan al reproducir" : "Las partes marcadas se ven", ToastLength.Short)!.Show();
                break;
            case MenuAction.CutPagePrev: _cutPage = Math.Max(0, _cutPage - 1); break;
            case MenuAction.CutPageNext: _cutPage = Math.Min(Math.Max(0, (_cuts.Length - 1) / EditModel.PerPage), _cutPage + 1); break;
            case MenuAction.CutDeleteMode: _cutDeleteMode = !_cutDeleteMode; break;
            case MenuAction.CutSlot: UseCut(_cutPage * EditModel.PerPage + arg); break;
            default: return false;
        }
        return true;
    }

    /// <summary>Called every tick: skips a marked part that playback just entered.</summary>
    private void SkipCutsWhilePlaying()
    {
        if (!_prepared || _player is null) return;
        long pos = Position;
        long previous = _lastTickPosition;
        _lastTickPosition = pos;
        if (Android.OS.SystemClock.ElapsedRealtime() - _seekedAt < SeekSettleMs) return;
        if (!IsPlaying || !_settings.SkipCuts || _cutsStale || _state.Cuts.Count == 0 || previous < 0) return;
        if (CutList.SkipTarget(_state.Cuts, previous, pos) is not { } to) return;
        long duration = Duration;
        if (duration > 0 && to >= duration - 500)
        {
            // The part runs to the end: the video is over.
            OnCompletion();
            return;
        }
        SeekTo(to);
        if (!_editMode || TopWindow is null)
            Toast.MakeText(this, $"⏭ Parte marcada para cortar (hasta {MediaUtil.FormatTime(to)})", ToastLength.Short)!.Show();
    }

    /// <summary>Where to start a video: past a marked part that contains the start (e.g. the intro).</summary>
    private long StartPositionPastCuts(long start, long duration)
    {
        _cutsStale = _state.Cuts.Count > 0 && !CutList.BelongsTo(_state.CutsDurationMs, duration);
        if (_cutsStale)
        {
            Toast.MakeText(this, "Los cortes de este video son de otra versión del archivo (cambió su duración): no se saltan", ToastLength.Long)!.Show();
            return start;
        }
        if (!_settings.SkipCuts) return start;
        int i = CutList.IndexAt(_state.Cuts, start);
        return i >= 0 && _state.Cuts[i].ToMs < duration - 500 ? _state.Cuts[i].ToMs : start;
    }

    private void ResetEditForNewVideo()
    {
        _cutStart = -1;
        _cutPage = 0;
        _cutDeleteMode = false;
        _cutsStale = false;
        _lastTickPosition = -1;
    }

    /// <summary>Refreshes the cut snapshots (with the spots: <see cref="SpotsChanged"/>).</summary>
    private void CutsChanged()
    {
        _cuts = _state.Cuts.ToArray();
        _cutRangesDuration = -1;
    }

    private EditModel BuildEditModel(long duration)
    {
        if (duration != _cutRangesDuration)
        {
            _cutRangesDuration = duration;
            _cutRanges = duration > 0
                ? _cuts.SelectMany(c => new[] { Math.Clamp((float)c.FromMs / duration, 0f, 1f), Math.Clamp((float)c.ToMs / duration, 0f, 1f) }).ToArray()
                : [];
        }
        float pending = _cutStart >= 0 && duration > 0 ? Math.Clamp((float)_cutStart / duration, 0f, 1f) : -1;
        return new EditModel(_editMode, _cuts, _cutRanges, _cutStart, pending, _settings.SkipCuts, _cutPage, _cutDeleteMode,
            duration, _cutsStale, _frames.Version);
    }

    // ---- Touch overlay: the buttons of each mode ----

    private readonly List<Android.Views.View> _viewModeButtons = [], _editModeButtons = [];
    private Button? _editButton, _cutButton, _skipButton;
    private bool? _overlayEditMode;

    private void UpdateOverlayMode()
    {
        if (_editButton is null) return;
        _editButton.Text = _editMode ? "✓ Terminar edición" : "✂ Editar";
        _cutButton!.Text = _cutStart >= 0 ? "✂ Fin del corte" : "✂ Inicio de corte";
        _skipButton!.Text = _settings.SkipCuts ? "Saltar cortes: sí" : "Saltar cortes: no";
        if (_overlayEditMode == _editMode) return;
        _overlayEditMode = _editMode;
        foreach (var v in _viewModeButtons) v.Visibility = _editMode ? Android.Views.ViewStates.Gone : Android.Views.ViewStates.Visible;
        foreach (var v in _editModeButtons) v.Visibility = _editMode ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone;
    }
}
