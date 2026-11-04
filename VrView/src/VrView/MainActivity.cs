using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>
/// Start screen: pick a folder, see its videos (with detected format and resume position),
/// toggle shuffle/repeat and start the playlist.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true,
    Name = "net.rendezvous.vrview.MainActivity",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout)]
public class MainActivity : Activity
{
    private const int RequestPickFolder = 1;
    private const int RequestQrImage = 2;

    private AppSettings _settings = null!;
    private VideoStore _store = null!;
    private readonly List<MediaItem> _items = new();
    private VideoListAdapter _adapter = null!;
    private TextView _folderText = null!, _emptyText = null!;
    private Button _viewerButton = null!;
    private Button _playButton = null!, _shuffleButton = null!, _repeatButton = null!, _resumeButton = null!;
    private CheckBox _subfolders = null!;
    private ProgressBar _progress = null!;
    private CancellationTokenSource? _scan;
    private TipJar _tipJar = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AppLanguage.Apply(this);
        _settings = new AppSettings(this);
        _store = new VideoStore(this, _settings);
        SetContentView(BuildLayout());
        UpdateToggleLabels();
        _tipJar = new TipJar(this);
        _ = _tipJar.FinishPendingAsync();

        var folder = _settings.LastFolderUri;
        if (folder is not null && HasPermission(folder))
        {
            _settings.AddRecentFolder(folder);
            LoadFolder(Uri.Parse(folder)!);
        }
        else ShowEmpty(T("Choose a folder with your VR videos to build the playlist."));
    }

    protected override void OnResume()
    {
        base.OnResume();
        _adapter.NotifyDataSetChanged(); // refresh resume positions
        UpdateResumeButton();
        UpdateToggleLabels();
    }

    protected override void OnDestroy()
    {
        _scan?.Cancel();
        _tipJar.Close();
        base.OnDestroy();
    }

    // ---- UI ----

    private int Dp(float v) => (int)(v * Resources!.DisplayMetrics!.Density + 0.5f);

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(16), Dp(16), Dp(16), Dp(8));
        root.SetBackgroundColor(Color.Rgb(18, 24, 32));

        var title = new TextView(this) { Text = "VrView" };
        title.SetTextSize(Android.Util.ComplexUnitType.Sp, 26);
        title.SetTypeface(null, TypefaceStyle.Bold);
        title.SetTextColor(Color.White);
        root.AddView(title);

        _folderText = new TextView(this) { Text = T("No folder") };
        _folderText.SetTextColor(Color.Rgb(176, 190, 197));
        _folderText.SetPadding(0, 0, 0, Dp(8));
        root.AddView(_folderText);

        var row1 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var pick = MakeButton(T("Choose folder"), (_, _) => PickFolder());
        _playButton = MakeButton(T("▶ Play"), (_, _) => PlayAll());
        row1.AddView(pick, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        row1.AddView(_playButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        root.AddView(row1);

        var row2 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _shuffleButton = MakeButton("", (_, _) =>
        {
            _settings.Shuffle = !_settings.Shuffle;
            UpdateToggleLabels();
        });
        _repeatButton = MakeButton("", (_, _) =>
        {
            _settings.Repeat = Playlist.NextRepeatMode(_settings.Repeat);
            UpdateToggleLabels();
        });
        row2.AddView(_shuffleButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        row2.AddView(_repeatButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        root.AddView(row2);

        _viewerButton = MakeButton("", (_, _) => ViewerMenu());
        root.AddView(_viewerButton);

        _subfolders = new CheckBox(this) { Text = T("Include subfolders"), Checked = _settings.IncludeSubfolders };
        _subfolders.SetTextColor(Color.White);
        _subfolders.CheckedChange += (_, e) =>
        {
            _settings.IncludeSubfolders = e.IsChecked;
            if (_settings.LastFolderUri is { } f && HasPermission(f)) LoadFolder(Uri.Parse(f)!);
        };
        root.AddView(_subfolders);

        _resumeButton = MakeButton("", (_, _) => Resume());
        _resumeButton.Visibility = ViewStates.Gone;
        root.AddView(_resumeButton);

        _progress = new ProgressBar(this) { Indeterminate = true, Visibility = ViewStates.Gone };
        root.AddView(_progress);

        _emptyText = new TextView(this);
        _emptyText.SetTextColor(Color.Rgb(176, 190, 197));
        _emptyText.SetPadding(0, Dp(24), 0, 0);
        _emptyText.Gravity = GravityFlags.CenterHorizontal;
        root.AddView(_emptyText);

        var list = new ListView(this);
        _adapter = new VideoListAdapter(this, _items, _store);
        list.Adapter = _adapter;
        list.ItemClick += (_, e) => Play(e.Position);
        root.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var help = new TextView(this)
        {
            Text = T("In the viewer: tap the screen (viewer button) or look down to open the menu; aim with the center dot and wait, or tap again, to press."),
        };
        help.SetTextColor(Color.Rgb(120, 134, 140));
        help.SetTextSize(Android.Util.ComplexUnitType.Sp, 12);
        root.AddView(help);

        root.AddView(MakeButton(T("🍺 Buy me a beer"), (_, _) => TipMenu()));
        return root;
    }

    private Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetAllCaps(false);
        b.Click += onClick;
        return b;
    }

    private void UpdateToggleLabels()
    {
        var viewer = _settings.ViewerUrl is { } url ? ViewerProfile.FromUrl(url) : null;
        _viewerButton.Text = viewer is null ? T("Viewer: generic · scan its QR code")
            : F("Viewer: {0} · change", $"{viewer.Vendor} {viewer.Model}".Trim());
        _shuffleButton.Text = _settings.Shuffle ? T("Shuffle: on") : T("Shuffle: off");
        _repeatButton.Text = Playlist.RepeatLabel(_settings.Repeat);
    }

    private void UpdateResumeButton()
    {
        string? uri = _settings.LastVideoUri;
        if (uri is null) { _resumeButton.Visibility = ViewStates.Gone; return; }
        string name = _settings.LastVideoName ?? T("last video");
        var state = _store.Get(new MediaItem(uri, name));
        _resumeButton.Text = state.PositionMs > 0
            ? F("Continue: {0} ({1})", name, MediaUtil.FormatTime(state.PositionMs))
            : F("Continue: {0}", name);
        _resumeButton.Visibility = ViewStates.Visible;
    }

    private void ShowEmpty(string message)
    {
        _emptyText.Text = message;
        _emptyText.Visibility = ViewStates.Visible;
    }

    // ---- Folder handling ----

    private void PickFolder()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        // Write access lets the app keep its settings in vrview.json inside the folder.
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPersistableUriPermission);
        try
        {
#pragma warning disable CA1422 // StartActivityForResult is fine for this simple flow.
            StartActivityForResult(intent, RequestPickFolder);
#pragma warning restore CA1422
        }
        catch (ActivityNotFoundException)
        {
            Toast.MakeText(this, T("This device has no folder picker."), ToastLength.Long)!.Show();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == RequestQrImage && resultCode == Result.Ok && data?.Data is { } image)
        {
            ReadQrImage(image);
            return;
        }
        if (requestCode != RequestPickFolder || resultCode != Result.Ok || data?.Data is not { } tree) return;
        try
        {
            ContentResolver!.TakePersistableUriPermission(tree, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch (Java.Lang.SecurityException)
        {
            // Some providers do not offer persistable grants; the folder still works this session.
        }
        _settings.LastFolderUri = tree.ToString();
        _settings.AddRecentFolder(tree.ToString()!);
        LoadFolder(tree);
    }

    private bool HasPermission(string treeUri) =>
        ContentResolver!.PersistedUriPermissions.Any(p => p.IsReadPermission && p.Uri?.ToString() == treeUri);

    private void LoadFolder(Uri tree)
    {
        _scan?.Cancel();
        var cts = _scan = new CancellationTokenSource();
        bool recursive = _settings.IncludeSubfolders;
        _progress.Visibility = ViewStates.Visible;
        _emptyText.Visibility = ViewStates.Gone;
        _folderText.Text = F("Folder: {0}", FolderScanner.FolderName(ContentResolver!, tree));

        Task.Run(() =>
        {
            List<MediaItem>? found = null;
            string? error = null;
            try
            {
                // Read vrview.json again: it may have been edited or copied in from another device.
                FolderSettingsFile.Reload(ContentResolver!, tree.ToString()!);
                found = FolderScanner.Scan(ContentResolver!, tree, recursive, cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { error = ex.Message; }

            RunOnUiThread(() =>
            {
                if (cts.IsCancellationRequested) return;
                _progress.Visibility = ViewStates.Gone;
                _items.Clear();
                if (found is not null) _items.AddRange(found);
                _adapter.NotifyDataSetChanged();
                _folderText.Text = F("Folder: {0}", FolderScanner.FolderName(ContentResolver!, tree)) + " · " + VideoCount(_items.Count);
                if (error is not null) ShowEmpty(F("Could not read the folder: {0}", error));
                else if (_items.Count == 0) ShowEmpty(T("No videos in this folder."));
            });
        });
    }

    private void TipMenu()
    {
        new AlertDialog.Builder(this)
            .SetTitle(T("🍺 Buy me a beer"))!
            .SetMessage(T("The app is free and fully unlocked. If you enjoy it, you can buy the developer a beer."))!
            .SetPositiveButton(T("🍺 Buy me a beer"), (_, _) => _ = _tipJar.BuyAsync())!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }

    // ---- Viewer profile (Cardboard QR code) ----

    private void ViewerMenu()
    {
        new AlertDialog.Builder(this)
            .SetTitle(T("Viewer profile"))!
            .SetMessage(T("The QR code printed on many viewers holds their lenses and field of view. Take a photo of it (or find it online) and choose it here."))!
            .SetItems([T("Choose a photo of the QR code"), T("Type the code's address"), T("Remove the profile")], (_, e) =>
            {
                switch (e.Which)
                {
                    case 0:
                        var intent = new Intent(Intent.ActionGetContent);
                        intent.SetType("image/*");
                        intent.AddCategory(Intent.CategoryOpenable);
#pragma warning disable CA1422
                        StartActivityForResult(Intent.CreateChooser(intent, T("Photo of the QR code")), RequestQrImage);
#pragma warning restore CA1422
                        break;
                    case 1: AskViewerUrl(); break;
                    default:
                        _settings.ViewerUrl = null;
                        if (_settings.LensLevel == 3) _settings.LensLevel = 0;
                        UpdateToggleLabels();
                        break;
                }
            })!
            .Show();
    }

    private void AskViewerUrl()
    {
        var input = new EditText(this) { Hint = "https://google.com/cardboard/cfg?p=…" };
        input.SetSingleLine(true);
        new AlertDialog.Builder(this)
            .SetTitle(T("QR code address"))!
            .SetView(input)!
            .SetPositiveButton(T("Use"), (_, _) => ApplyViewerUrl(input.Text ?? ""))!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }

    private void ReadQrImage(Uri image)
    {
        Toast.MakeText(this, T("Reading the code…"), ToastLength.Short)!.Show();
        Task.Run(() =>
        {
            string? text = null;
            try
            {
                using var stream = ContentResolver!.OpenInputStream(image);
                var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                BitmapFactory.DecodeStream(stream, null, options);
                int sample = 1;
                while (Math.Max(options.OutWidth, options.OutHeight) / sample > 2000) sample *= 2;
                using var stream2 = ContentResolver!.OpenInputStream(image);
                using var bmp = BitmapFactory.DecodeStream(stream2, null, new BitmapFactory.Options { InSampleSize = sample });
                if (bmp is not null)
                {
                    var pixels = new int[bmp.Width * bmp.Height];
                    bmp.GetPixels(pixels, 0, bmp.Width, 0, 0, bmp.Width, bmp.Height);
                    text = QrDecoder.Decode(pixels, bmp.Width, bmp.Height);
                }
            }
            catch (Exception ex) when (ex is Java.Lang.Exception or IOException) { }
            RunOnUiThread(() =>
            {
                if (text is null) Toast.MakeText(this, T("No QR code found in the photo"), ToastLength.Long)!.Show();
                else ApplyViewerUrl(text);
            });
        });
    }

    private void ApplyViewerUrl(string url)
    {
        url = url.Trim();
        Task.Run(async () =>
        {
            // Old viewers print short links (goo.gl/…) that redirect to the real address.
            if (ViewerProfile.IsShortLink(url))
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    using var response = await http.GetAsync(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url);
                    url = response.RequestMessage?.RequestUri?.ToString() ?? url;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
            }
            var profile = ViewerProfile.FromUrl(url);
            RunOnUiThread(() =>
            {
                if (profile is null)
                {
                    Toast.MakeText(this, T("That code is not from a Cardboard viewer (or the short link no longer works)"), ToastLength.Long)!.Show();
                    return;
                }
                var m = Resources!.DisplayMetrics!;
                float w = Math.Max(m.WidthPixels, m.HeightPixels) / m.Xdpi * 0.0254f, h = Math.Min(m.WidthPixels, m.HeightPixels) / m.Ydpi * 0.0254f;
                var (fov, ipd, _, _) = profile.ToRenderParams(w, h);
                _settings.ViewerUrl = profile.ToUrl();
                _settings.LensLevel = 3;
                _settings.Fov = Math.Clamp(MathF.Round(fov), 40f, 130f);
                _settings.IpdShift = Math.Clamp(MathF.Round(ipd, 2), -0.3f, 0.3f);
                UpdateToggleLabels();
                Toast.MakeText(this, F("Viewer set up: {0} {1}", profile.Vendor, profile.Model), ToastLength.Long)!.Show();
            });
        });
    }

    // ---- Playback ----

    private void PlayAll()
    {
        if (_items.Count == 0) { PickFolder(); return; }
        int start = _settings.Shuffle ? Random.Shared.Next(_items.Count) : 0;
        Play(start);
    }

    private void Play(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        StartActivity(PlayerActivity.CreateIntent(this, _items, index));
    }

    private void Resume()
    {
        string? uri = _settings.LastVideoUri;
        if (uri is null) return;
        int idx = _items.FindIndex(i => i.Location == uri);
        if (idx >= 0) { Play(idx); return; }
        var single = new List<MediaItem> { new(uri, _settings.LastVideoName ?? T("video")) };
        StartActivity(PlayerActivity.CreateIntent(this, single, 0));
    }
}

/// <summary>Two-line list rows: file name, then detected format / size / resume point.</summary>
internal sealed class VideoListAdapter : BaseAdapter<MediaItem>
{
    private readonly Context _context;
    private readonly List<MediaItem> _items;
    private readonly VideoStore _store;

    public VideoListAdapter(Context context, List<MediaItem> items, VideoStore store)
    {
        _context = context;
        _items = items;
        _store = store;
    }

    public override MediaItem this[int position] => _items[position];
    public override int Count => _items.Count;
    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var row = convertView as LinearLayout;
        TextView title, detail;
        if (row is null)
        {
            float d = _context.Resources!.DisplayMetrics!.Density;
            row = new LinearLayout(_context) { Orientation = Orientation.Vertical };
            row.SetPadding((int)(4 * d), (int)(10 * d), (int)(4 * d), (int)(10 * d));
            title = new TextView(_context);
            title.SetTextColor(Color.White);
            title.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
            detail = new TextView(_context);
            detail.SetTextColor(Color.Rgb(130, 177, 255));
            detail.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
            row.AddView(title);
            row.AddView(detail);
        }
        else
        {
            title = (TextView)row.GetChildAt(0)!;
            detail = (TextView)row.GetChildAt(1)!;
        }

        var item = _items[position];
        var state = _store.Get(item);
        var choice = FormatResolver.Resolve(new FormatInputs(state.FormatOverride, state.Profile, _store.GetFolder(item),
            _store.Profiles(item), item.Name, 0, 0, null));
        string formatText = choice.Source == FormatSource.Auto ? T("automatic format") : $"{choice.Format.Label} ({choice.Note})";
        var parts = new List<string> { formatText };
        if (item.SizeBytes > 0) parts.Add($"{item.SizeBytes / (1024.0 * 1024.0):0} MB");
        if (state.PositionMs > 0) parts.Add(F("continue at {0}", MediaUtil.FormatTime(state.PositionMs)));
        if (state.Spots.Count > 0) parts.Add(BookmarkCount(state.Spots.Count));
        title.Text = item.Name;
        detail.Text = string.Join(" · ", parts);
        return row;
    }
}
