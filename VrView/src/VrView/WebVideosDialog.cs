using Android.App;
using Android.Widget;
using VrView.Core;

namespace VrView;

/// <summary>The saved web videos: tap to watch (the list plays as a playlist), hold for page, rename and delete.</summary>
internal static class WebVideosDialog
{
    /// <param name="openPage">Opens a video's page in the browser (to renew an expired address).</param>
    public static void Show(Activity activity, AppSettings settings, Action<string> openPage)
    {
        var videos = settings.Bookmarks.Videos;
        if (videos.Count == 0)
        {
            new AlertDialog.Builder(activity)
                .SetTitle(T("No web videos yet"))!
                .SetMessage(T("In the browser, play a video on its page, press “🎬 Videos” and choose “★ Save to Web videos”. Saved videos also appear in the viewer's Library."))!
                .SetPositiveButton(T("Got it"), (_, _) => { })!
                .Show();
            return;
        }
        string[] labels = videos.Select(v =>
        {
            string kind = v.Kind switch { StreamKind.Hls => T("stream"), StreamKind.Dash => T("DASH, not supported"), _ => T("file") };
            string host = System.Uri.TryCreate(v.PageUrl, UriKind.Absolute, out var u) ? u.Host : "";
            return $"{v.Title}\n{kind} · {host}";
        }).ToArray();
        var dialog = new AlertDialog.Builder(activity)
            .SetTitle(T("★ Web videos · long-press for more"))!
            .SetItems(labels, (_, e) => Play(activity, settings, videos[e.Which]))!
            .SetNegativeButton(T("Close"), (_, _) => { })!
            .Create()!;
        dialog.Show();
        dialog.ListView!.ItemLongClick += (_, e) =>
        {
            var video = videos[e.Position];
            dialog.Dismiss();
            new AlertDialog.Builder(activity)
                .SetTitle(video.Title)!
                .SetItems([T("Open its page (renews the link if it expired)"), T("Rename…"), T("Delete")], (_, a) =>
                {
                    switch (a.Which)
                    {
                        case 0: openPage(video.PageUrl); break;
                        case 1: Rename(activity, settings, video.Id, video.Title); break;
                        default:
                            var b = settings.Bookmarks;
                            b.Videos.RemoveAll(v => v.Id == video.Id);
                            settings.Bookmarks = b;
                            Toast.MakeText(activity, F("“{0}” removed from Web videos", video.Title), ToastLength.Short)!.Show();
                            break;
                    }
                })!
                .Show();
        };
    }

    private static void Play(Activity activity, AppSettings settings, VideoBookmark video)
    {
        if (video.Kind == StreamKind.Dash)
        {
            Toast.MakeText(activity, T("The system player cannot play DASH; check whether the page offers another version"), ToastLength.Long)!.Show();
            return;
        }
        var items = WebBookmarkItems.All(settings.Bookmarks);
        int index = Math.Max(0, items.FindIndex(i => i.StateKey == video.StateKey));
        activity.StartActivity(PlayerActivity.CreateIntent(activity, items, index));
    }

    private static void Rename(Activity activity, AppSettings settings, string id, string title)
    {
        var input = new EditText(activity) { Text = title };
        input.SetSingleLine(true);
        new AlertDialog.Builder(activity)
            .SetTitle(T("Video name"))!
            .SetMessage(T("Format tags in the name (180, 360, LR, TB…) help show it correctly."))!
            .SetView(input)!
            .SetPositiveButton(T("Save"), (_, _) =>
            {
                var b = settings.Bookmarks;
                if (b.GetVideo(id) is { } v && (input.Text ?? "").Trim() is { Length: > 0 } t) v.Title = t;
                settings.Bookmarks = b;
            })!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }
}
