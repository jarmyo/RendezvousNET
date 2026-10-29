using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using VrCut.Services;

namespace VrCut;

public sealed record BrowserEntry(string Name, string FullPath, bool IsFolder, long Size, DateTime Modified)
{
    public string Icon => IsFolder ? "📁" : "🎬";
    public string DetailText => IsFolder
        ? $"{Modified:dd MMM yyyy HH:mm}"
        : $"{Modified:dd MMM yyyy HH:mm}  ·  {MainWindow.FormatSize(Size)}";
}

public partial class MainWindow : Window
{
    private const int ThumbnailCount = 32;
    private const int ThumbnailHeight = 90;
    private static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov"];

    private readonly ObservableCollection<BrowserEntry> _entries = [];
    private List<BrowserEntry> _allEntries = [];
    private int _sortKey; // 0 = name, 1 = date, 2 = size
    private bool _sortDescending;
    private readonly ObservableCollection<CutRange> _cuts = [];
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"vrcut-{Environment.ProcessId}");

    private bool _deviceMode;
    private string _localFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    private string _deviceFolder = "/sdcard";

    // Current video: _localFile is what ffmpeg reads; _remoteFile is set when it came from the phone.
    private string? _localFile;
    private string? _remoteFile;
    private VideoInfo? _info;
    private double[] _keyframes = [];
    private CancellationTokenSource? _loadCts;
    private bool _busy;

    private double? _pendingPreview;
    private bool _previewBusy;

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _entries;
        CutList.ItemsSource = _cuts;
        Timeline.Seek += (_, t) => OnPositionChanged(t);
        KeyDown += OnWindowKeyDown;
        Closed += (_, _) => CleanupCache();

        if (string.IsNullOrEmpty(_localFolder) || !Directory.Exists(_localFolder))
            _localFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var missing = new[] { "ffmpeg", "ffprobe" }.Where(t => ProcessRunner.Find(t) is null).ToList();
        SetStatus(missing.Count > 0
            ? $"⚠ Falta instalar: {string.Join(", ", missing)}. Sin eso no se pueden ver ni cortar videos."
            : "Elige una carpeta y selecciona un video.");

        Run(() => LoadFolderAsync(_localFolder));
    }

    // ---------- Folder browsing ----------

    private string CurrentFolder => _deviceMode ? _deviceFolder : _localFolder;

    private async Task LoadFolderAsync(string folder)
    {
        List<BrowserEntry> items;
        if (_deviceMode)
        {
            if (await AdbService.GetDeviceAsync() is null)
            {
                _entries.Clear();
                SetStatus("📱 No hay teléfono conectado. Conéctalo por USB, activa 'Depuración USB' y acepta el aviso en la pantalla. Luego pulsa ⟳.");
                return;
            }
            var remote = await AdbService.ListAsync(folder);
            items = remote.Select(r => new BrowserEntry(r.Name, r.FullPath, r.IsFolder, r.Size, r.Modified)).ToList();
            _deviceFolder = folder.Length > 1 ? folder.TrimEnd('/') : folder;
        }
        else
        {
            var dir = new DirectoryInfo(folder);
            items = dir.EnumerateDirectories()
                .Where(d => !d.Name.StartsWith('.') && !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => new BrowserEntry(d.Name, d.FullName, true, 0, d.LastWriteTime))
                .Concat(dir.EnumerateFiles().Select(f => new BrowserEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime)))
                .ToList();
            _localFolder = dir.FullName;
        }

        _allEntries = items
            .Where(e => e.IsFolder || VideoExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
            .ToList();
        ApplySort();
        PathBox.Text = CurrentFolder;
        SetStatus($"{_allEntries.Count(e => !e.IsFolder)} videos en {CurrentFolder}");
    }

    private void ApplySort()
    {
        // Folders always stay on top; the chosen key orders folders and videos separately.
        IOrderedEnumerable<BrowserEntry> ordered = _allEntries.OrderByDescending(e => e.IsFolder);
        ordered = (_sortKey, _sortDescending) switch
        {
            (1, false) => ordered.ThenBy(e => e.Modified),
            (1, true) => ordered.ThenByDescending(e => e.Modified),
            (2, false) => ordered.ThenBy(e => e.Size),
            (2, true) => ordered.ThenByDescending(e => e.Size),
            (_, false) => ordered.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            (_, true) => ordered.ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        var keepSelected = FileList.SelectedItems?.Cast<BrowserEntry>().Select(e => e.FullPath).ToHashSet() ?? [];

        _entries.Clear();
        foreach (var e in ordered.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            _entries.Add(e);
        foreach (var e in _entries.Where(e => keepSelected.Contains(e.FullPath)))
            FileList.SelectedItems?.Add(e);

        SortDirButton.Content = (_sortKey, _sortDescending) switch
        {
            (1, false) => "↑ Antiguo",
            (1, true) => "↓ Reciente",
            (2, false) => "↑ Chico",
            (2, true) => "↓ Grande",
            (_, false) => "↑ A-Z",
            (_, true) => "↓ Z-A",
        };
        UpdateFileButtons();
    }

    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SortDirButton is null)
            return; // Fired during InitializeComponent.
        _sortKey = SortBox.SelectedIndex;
        // Newest and largest first are the useful defaults for date and size.
        _sortDescending = _sortKey != 0;
        ApplySort();
    }

    private void OnSortDirection(object? sender, RoutedEventArgs e)
    {
        _sortDescending = !_sortDescending;
        ApplySort();
    }

    private void OnSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PathBox is null)
            return; // Fired during InitializeComponent.

        _deviceMode = SourceBox.SelectedIndex == 1;
        PickFolderButton.Content = _deviceMode ? "📱 Ir a almacenamiento" : "📂 Elegir carpeta…";
        if (_deviceMode && !AdbService.IsAvailable)
        {
            _entries.Clear();
            SetStatus("⚠ No se encontró 'adb'. Instala Android platform-tools (ver README).");
            return;
        }
        Run(() => LoadFolderAsync(CurrentFolder));
    }

    private async void OnPickFolder(object? sender, RoutedEventArgs e)
    {
        if (_deviceMode)
        {
            Run(() => LoadFolderAsync("/sdcard"));
            return;
        }

        var start = await StorageProvider.TryGetFolderFromPathAsync(_localFolder);
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Carpeta con tus videos",
            AllowMultiple = false,
            SuggestedStartLocation = start
        });
        if (result.Count > 0 && result[0].TryGetLocalPath() is string path)
            Run(() => LoadFolderAsync(path));
    }

    private void OnFolderUp(object? sender, RoutedEventArgs e)
    {
        string? parent = _deviceMode
            ? (_deviceFolder.LastIndexOf('/') is > 0 and var i ? _deviceFolder[..i] : "/")
            : Directory.GetParent(_localFolder)?.FullName;
        if (parent is not null)
            Run(() => LoadFolderAsync(parent));
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Run(() => LoadFolderAsync(CurrentFolder));

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(PathBox.Text))
        {
            string path = PathBox.Text.Trim();
            Run(() => LoadFolderAsync(path));
            e.Handled = true;
        }
    }

    private IReadOnlyList<BrowserEntry> SelectedEntries =>
        FileList.SelectedItems?.Cast<BrowserEntry>().ToList() ?? [];

    private BrowserEntry? SingleSelected => SelectedEntries is [var only] ? only : null;

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SingleSelected is { IsFolder: true } folder)
            Run(() => LoadFolderAsync(folder.FullPath));
        else
            OnImport(sender, e);
    }

    private void OnFileSelected(object? sender, SelectionChangedEventArgs e) => UpdateFileButtons();

    private void UpdateFileButtons()
    {
        var selected = SelectedEntries;
        ImportButton.IsEnabled = !_busy && SingleSelected is { IsFolder: false };
        RenameButton.IsEnabled = !_busy && SingleSelected is not null;
        DeleteButton.IsEnabled = !_busy && selected.Count > 0 && selected.All(x => !x.IsFolder);
    }

    private void OnFileListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when SingleSelected is { IsFolder: true } folder:
                Run(() => LoadFolderAsync(folder.FullPath));
                break;
            case Key.Enter: OnImport(sender, e); break;
            case Key.F2: OnRename(sender, e); break;
            case Key.Delete or Key.Back: OnDelete(sender, e); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------- Import, rename and delete ----------

    private string? CurrentSourcePath => _remoteFile ?? _localFile;

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_busy || SingleSelected is not { IsFolder: false } entry)
            return;
        if (_info is not null && _cuts.Count > 0 && entry.FullPath != CurrentSourcePath &&
            !await Dialogs.ConfirmAsync(this, "Cortes sin exportar",
                $"Tienes {_cuts.Count} corte(s) marcados en el video actual que no has exportado. ¿Descartarlos e importar '{entry.Name}'?",
                "Descartar e importar"))
            return;
        Run(() => OpenVideoAsync(entry));
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (_busy || SingleSelected is not { } entry)
            return;

        int nameLength = entry.IsFolder ? entry.Name.Length : Path.GetFileNameWithoutExtension(entry.Name).Length;
        string? newName = (await Dialogs.PromptAsync(this, "Renombrar", $"Nuevo nombre para '{entry.Name}':",
            entry.Name, nameLength))?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == entry.Name)
            return;
        if (newName.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            SetStatus("❌ El nombre no puede contener / \\ ni :");
            return;
        }

        Run(async () =>
        {
            if (_deviceMode)
                await AdbService.RenameAsync(entry.FullPath, newName);
            else
                LocalFiles.Rename(entry.FullPath, newName);

            // Keep the open video working under its new name.
            string newPath = _deviceMode
                ? entry.FullPath[..(entry.FullPath.LastIndexOf('/') + 1)] + newName
                : Path.Combine(Path.GetDirectoryName(entry.FullPath)!, newName);
            if (entry.FullPath == CurrentSourcePath)
            {
                if (_remoteFile is not null)
                    _remoteFile = newPath;
                else
                    _localFile = newPath;
                InfoText.Text = InfoText.Text?.Replace(entry.Name, newName);
            }

            await LoadFolderAsync(CurrentFolder);
            if (_entries.FirstOrDefault(x => x.FullPath == newPath) is { } renamed)
                FileList.SelectedItem = renamed;
            SetStatus($"✏️ Renombrado: {entry.Name} → {newName}");
        });
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        var files = SelectedEntries.Where(x => !x.IsFolder).ToList();
        if (_busy || files.Count == 0)
            return;

        string what = files.Count == 1 ? $"'{files[0].Name}'" : $"{files.Count} videos";
        string total = FormatSize(files.Sum(f => f.Size));
        string where = _deviceMode
            ? "Se borrará DEFINITIVAMENTE del teléfono (Android no tiene papelera por USB)."
            : LocalFiles.UsesTrash ? "Se moverá a la papelera." : "Se borrará definitivamente.";
        if (!await Dialogs.ConfirmAsync(this, "Borrar", $"¿Borrar {what} ({total})?\n\n{where}", "Borrar", danger: true))
            return;

        Run(async () =>
        {
            SetBusy(true);
            try
            {
                foreach (var f in files)
                {
                    SetStatus($"Borrando {f.Name}…");
                    if (f.FullPath == CurrentSourcePath)
                        CloseVideo();
                    if (_deviceMode)
                        await AdbService.DeleteAsync(f.FullPath);
                    else
                        await LocalFiles.MoveToTrashAsync(f.FullPath);
                }
            }
            finally
            {
                SetBusy(false);
            }
            await LoadFolderAsync(CurrentFolder);
            SetStatus($"🗑 Borrado: {what} ({total})");
        });
    }

    private void CloseVideo()
    {
        _loadCts?.Cancel();
        _localFile = null;
        _remoteFile = null;
        _info = null;
        _keyframes = [];
        _cuts.Clear();
        Timeline.Reset(0, 0);
        UpdateCuts();
        (PreviewImage.Source as IDisposable)?.Dispose();
        PreviewImage.Source = null;
        PlaceholderText.IsVisible = true;
        InfoText.Text = "";
        TimeText.Text = TimeFormat.Format(0);
        ExportButton.IsEnabled = false;
    }

    // ---------- Loading a video ----------

    private async Task OpenVideoAsync(BrowserEntry entry)
    {
        CloseVideo();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;

        string local = entry.FullPath;
        if (_deviceMode)
        {
            local = await PullFromDeviceAsync(entry, ct);
            _remoteFile = entry.FullPath;
        }
        else
        {
            _remoteFile = null;
        }
        ct.ThrowIfCancellationRequested();

        _localFile = local;
        var info = _info = await FfmpegService.GetInfoAsync(local);
        ct.ThrowIfCancellationRequested();
        if (info.Duration <= 0)
            throw new InvalidOperationException("No se pudo leer la duración del video.");

        InfoText.Text = $"{entry.Name}  ·  {info.Width}×{info.Height}  ·  {TimeFormat.Format(info.Duration)}  ·  {FormatSize(entry.Size)}";
        PlaceholderText.IsVisible = false;
        Timeline.Reset(info.Duration, ThumbnailCount);
        OnPositionChanged(0);
        UpdateCuts();
        ExportButton.IsEnabled = true;
        SetStatus("Generando miniaturas…");

        var keyframesTask = LoadKeyframesAsync(local, info, ct);
        await LoadThumbnailsAsync(local, info, ct);
        await keyframesTask;
        SetStatus("Mueve el cabezal, marca inicio (I) y fin (O) de cada parte a borrar y pulsa Exportar.");
    }

    private async Task<string> PullFromDeviceAsync(BrowserEntry entry, CancellationToken ct)
    {
        string srcDir = Path.Combine(_cacheDir, "src");
        string local = Path.Combine(srcDir, entry.Name);
        if (File.Exists(local) && new FileInfo(local).Length == entry.Size)
            return local;

        // Keep only one pulled video in the cache; they can be several GB.
        if (Directory.Exists(srcDir))
            Directory.Delete(srcDir, true);
        Directory.CreateDirectory(srcDir);

        var pull = AdbService.PullAsync(entry.FullPath, local, ct);
        Progress.IsVisible = true;
        try
        {
            while (!pull.IsCompleted)
            {
                long done = File.Exists(local) ? new FileInfo(local).Length : 0;
                double frac = entry.Size > 0 ? (double)done / entry.Size : 0;
                Progress.Value = frac;
                SetStatus($"Copiando del teléfono… {frac:P0} de {FormatSize(entry.Size)}");
                await Task.WhenAny(pull, Task.Delay(400, ct));
            }
            await pull;
        }
        finally
        {
            Progress.IsVisible = false;
        }
        return local;
    }

    private async Task LoadKeyframesAsync(string file, VideoInfo info, CancellationToken ct)
    {
        try
        {
            var kf = await FfmpegService.GetKeyframesAsync(file, info.StartTime, ct);
            if (ct.IsCancellationRequested)
                return;
            _keyframes = kf;
            Timeline.Keyframes = kf;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadThumbnailsAsync(string file, VideoInfo info, CancellationToken ct)
    {
        bool leftEye = LeftEyeBox.IsChecked == true;
        using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 2, 2, 6));
        var tasks = Enumerable.Range(0, ThumbnailCount).Select(async i =>
        {
            await gate.WaitAsync(ct);
            try
            {
                double t = (i + 0.5) * info.Duration / ThumbnailCount;
                var bytes = await FfmpegService.GrabFrameAsync(file, t, ThumbnailHeight, leftEye, ct);
                if (bytes.Length > 0 && !ct.IsCancellationRequested)
                    Timeline.SetThumbnail(i, new Bitmap(new MemoryStream(bytes)));
            }
            finally
            {
                gate.Release();
            }
        });
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnLeftEyeChanged(object? sender, RoutedEventArgs e)
    {
        if (_localFile is null || _info is null || _loadCts is null)
            return;
        var ct = _loadCts.Token;
        string file = _localFile;
        var info = _info;
        RequestPreview(Timeline.Position);
        Run(() => LoadThumbnailsAsync(file, info, ct));
    }

    // ---------- Playhead and preview ----------

    private void OnPositionChanged(double t)
    {
        TimeText.Text = TimeFormat.Format(t);
        RequestPreview(t);
    }

    private void MoveTo(double t)
    {
        if (_info is null)
            return;
        Timeline.Position = t;
        OnPositionChanged(Timeline.Position);
    }

    private async void RequestPreview(double t)
    {
        _pendingPreview = t;
        if (_previewBusy)
            return; // The running loop will pick up the latest requested time.

        _previewBusy = true;
        try
        {
            while (_pendingPreview is double time && _localFile is string file && _info is VideoInfo info)
            {
                _pendingPreview = null;
                int height = Math.Clamp(info.Height, 240, 720);
                double clamped = Math.Min(time, Math.Max(0, info.Duration - 0.1));
                var bytes = await FfmpegService.GrabFrameAsync(file, clamped, height, LeftEyeBox.IsChecked == true);
                if (bytes.Length > 0 && file == _localFile)
                {
                    var old = PreviewImage.Source as Bitmap;
                    PreviewImage.Source = new Bitmap(new MemoryStream(bytes));
                    old?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo mostrar el cuadro: " + ex.Message);
        }
        finally
        {
            _previewBusy = false;
        }
    }

    private void OnBack10(object? sender, RoutedEventArgs e) => MoveTo(Timeline.Position - 10);
    private void OnBack1(object? sender, RoutedEventArgs e) => MoveTo(Timeline.Position - 1);
    private void OnForward1(object? sender, RoutedEventArgs e) => MoveTo(Timeline.Position + 1);
    private void OnForward10(object? sender, RoutedEventArgs e) => MoveTo(Timeline.Position + 10);

    private void OnPrevKeyframe(object? sender, RoutedEventArgs e)
    {
        double pos = Timeline.Position;
        MoveTo(_keyframes.LastOrDefault(k => k < pos - 0.01, 0));
    }

    private void OnNextKeyframe(object? sender, RoutedEventArgs e)
    {
        double pos = Timeline.Position;
        MoveTo(_keyframes.FirstOrDefault(k => k > pos + 0.01, _info?.Duration ?? 0));
    }

    // ---------- Cuts ----------

    private void OnMarkIn(object? sender, RoutedEventArgs e)
    {
        if (_info is null)
            return;
        Timeline.MarkIn = Timeline.Position;
        SetStatus($"Inicio marcado en {TimeFormat.Format(Timeline.Position)}. Mueve el cabezal al final de la escena y pulsa Marcar fin (O).");
    }

    private void OnMarkOut(object? sender, RoutedEventArgs e)
    {
        if (_info is null)
            return;
        if (Timeline.MarkIn is not double markIn)
        {
            SetStatus("Primero marca el inicio (I) de la parte que quieres borrar.");
            return;
        }
        AddCut(markIn, Timeline.Position);
        Timeline.MarkIn = null;
    }

    private void OnCutToHere(object? sender, RoutedEventArgs e)
    {
        if (_info is not null)
            AddCut(0, Timeline.Position);
    }

    private void OnCutFromHere(object? sender, RoutedEventArgs e)
    {
        if (_info is not null)
            AddCut(Timeline.Position, _info.Duration);
    }

    private void AddCut(double a, double b)
    {
        if (_info is null)
            return;
        double from = Math.Min(a, b), to = Math.Max(a, b);
        if (from < 0.05)
            from = 0;
        if (to > _info.Duration - 0.05)
            to = _info.Duration;
        else if (SnapBox.IsChecked == true && _keyframes.Length > 0)
            // With stream copy the next kept part must start on a keyframe; snapping forward
            // guarantees nothing of the removed scene is left in the output.
            to = _keyframes.FirstOrDefault(k => k >= to - 0.001, _info.Duration);

        if (to - from < 0.05)
        {
            SetStatus("La parte marcada es demasiado corta.");
            return;
        }

        int index = 0;
        while (index < _cuts.Count && _cuts[index].From < from)
            index++;
        _cuts.Insert(index, new CutRange(from, to));
        UpdateCuts();
        SetStatus($"Se borrará {TimeFormat.Format(from)} → {TimeFormat.Format(to)}.");
    }

    private void OnRemoveCut(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CutRange cut })
        {
            _cuts.Remove(cut);
            UpdateCuts();
        }
    }

    private void OnCutDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CutList.SelectedItem is CutRange cut)
            MoveTo(cut.From);
    }

    private void UpdateCuts()
    {
        Timeline.Cuts = _cuts.ToList();
        if (_info is null || _cuts.Count == 0)
        {
            SummaryText.Text = "Sin cortes todavía.";
            return;
        }
        double kept = CutEngine.ComputeKeepRanges(_cuts, _info.Duration).Sum(k => k.To - k.From);
        long size = _entries.FirstOrDefault(x => x.FullPath == (_remoteFile ?? _localFile))?.Size ?? 0;
        string estimate = size > 0 ? $"\nTamaño aprox.: {FormatSize(size)} → {FormatSize((long)(size * kept / _info.Duration))}" : "";
        SummaryText.Text = $"Duración: {TimeFormat.Format(_info.Duration)} → {TimeFormat.Format(kept)}  " +
                           $"(se quitan {TimeFormat.Format(_info.Duration - kept)}){estimate}";
    }

    // ---------- Export ----------

    private void OnExport(object? sender, RoutedEventArgs e) => Run(ExportAsync);

    private async Task ExportAsync()
    {
        if (_localFile is null || _info is null)
            return;
        if (_cuts.Count == 0)
        {
            SetStatus("No hay cortes. Marca al menos una parte para borrar.");
            return;
        }

        string source = _localFile;
        string? remote = _remoteFile;
        bool replace = ReplaceRadio.IsChecked == true;
        string name = Path.GetFileName(remote ?? source);
        // Prefix (not suffix) so VR tags at the end of the name (_180_LR, _360_TB…) keep working.
        string outName = replace ? name : "cut_" + name;
        var keep = CutEngine.ComputeKeepRanges(_cuts, _info.Duration);
        long inSize = new FileInfo(source).Length;

        SetBusy(true);
        try
        {
            var progress = new Progress<(double Fraction, string Message)>(p =>
            {
                Progress.Value = p.Fraction;
                SetStatus(p.Message);
            });
            string workDir = Path.Combine(_cacheDir, "work");
            string output = remote is null
                ? Path.Combine(Path.GetDirectoryName(source)!, outName)
                : Path.Combine(_cacheDir, "out", outName);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            if (replace)
            {
                // Release the file before overwriting it.
                _localFile = null;
                (PreviewImage.Source as IDisposable)?.Dispose();
                PreviewImage.Source = null;
            }

            await CutEngine.ExportAsync(source, output, keep, workDir, progress, CancellationToken.None);
            long outSize = new FileInfo(output).Length;
            string sizes = $"{FormatSize(inSize)} → {FormatSize(outSize)}";

            if (remote is not null)
            {
                string target = replace ? remote : remote[..(remote.LastIndexOf('/') + 1)] + outName;
                Progress.IsIndeterminate = true;
                SetStatus($"Enviando al teléfono: {target}…");
                await AdbService.PushAsync(output, target, CancellationToken.None);
                File.Delete(output);
                if (replace)
                    File.Delete(source); // Cached copy is stale now.
                SetStatus($"✅ Listo en el teléfono: {target}  ({sizes})");
            }
            else
            {
                SetStatus($"✅ Guardado: {output}  ({sizes})");
            }
        }
        finally
        {
            Progress.IsIndeterminate = false;
            SetBusy(false);
        }

        if (replace)
            CloseVideo();
        string status = StatusText.Text ?? "";
        await LoadFolderAsync(CurrentFolder);
        SetStatus(status);
    }

    // ---------- Keyboard ----------

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox || _info is null)
            return;

        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.I: OnMarkIn(null, e); break;
            case Key.O: OnMarkOut(null, e); break;
            case Key.Left: MoveTo(Timeline.Position - step); break;
            case Key.Right: MoveTo(Timeline.Position + step); break;
            case Key.Home: MoveTo(0); break;
            case Key.End: MoveTo(_info.Duration); break;
            case Key.Escape: Timeline.MarkIn = null; break;
            case Key.Delete or Key.Back when CutList.SelectedItem is CutRange cut:
                _cuts.Remove(cut);
                UpdateCuts();
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------- Helpers ----------

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ExportButton.IsEnabled = !busy && _info is not null;
        FileList.IsEnabled = !busy;
        SourceBox.IsEnabled = !busy;
        UpdateFileButtons();
        Progress.IsVisible = busy;
        Progress.Value = 0;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus("❌ " + ex.Message);
        }
    }

    private void CleanupCache()
    {
        _loadCts?.Cancel();
        try
        {
            if (Directory.Exists(_cacheDir))
                Directory.Delete(_cacheDir, true);
        }
        catch (IOException)
        {
            // A process may still hold a file; the OS temp cleanup will take care of it.
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        _ => $"{bytes / 1024.0:F0} KB"
    };
}
