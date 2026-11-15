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
                .SetTitle("Aún no hay videos web")!
                .SetMessage("En el navegador, reproduce un video en su página, pulsa «🎬 Videos» y elige «★ Guardar en Videos web». " +
                            "Los videos guardados también aparecen en la Biblioteca del visor.")!
                .SetPositiveButton("Entendido", (_, _) => { })!
                .Show();
            return;
        }
        string[] labels = videos.Select(v =>
        {
            string kind = v.Kind switch { StreamKind.Hls => "stream", StreamKind.Dash => "DASH, no compatible", _ => "archivo" };
            string host = System.Uri.TryCreate(v.PageUrl, UriKind.Absolute, out var u) ? u.Host : "";
            return $"{v.Title}\n{kind} · {host}";
        }).ToArray();
        var dialog = new AlertDialog.Builder(activity)
            .SetTitle("★ Videos web · mantén pulsado para más")!
            .SetItems(labels, (_, e) => Play(activity, settings, videos[e.Which]))!
            .SetNegativeButton("Cerrar", (_, _) => { })!
            .Create()!;
        dialog.Show();
        dialog.ListView!.ItemLongClick += (_, e) =>
        {
            var video = videos[e.Position];
            dialog.Dismiss();
            new AlertDialog.Builder(activity)
                .SetTitle(video.Title)!
                .SetItems(["Abrir su página (renueva el enlace si expiró)", "Renombrar…", "Borrar"], (_, a) =>
                {
                    switch (a.Which)
                    {
                        case 0: openPage(video.PageUrl); break;
                        case 1: Rename(activity, settings, video.Id, video.Title); break;
                        default:
                            var b = settings.Bookmarks;
                            b.Videos.RemoveAll(v => v.Id == video.Id);
                            settings.Bookmarks = b;
                            Toast.MakeText(activity, $"«{video.Title}» borrado de Videos web", ToastLength.Short)!.Show();
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
            Toast.MakeText(activity, "El reproductor del sistema no puede con DASH; mira si la página ofrece otra versión", ToastLength.Long)!.Show();
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
            .SetTitle("Nombre del video")!
            .SetMessage("Las etiquetas de formato (180, 360, LR, TB…) en el nombre ayudan a mostrarlo bien.")!
            .SetView(input)!
            .SetPositiveButton("Guardar", (_, _) =>
            {
                var b = settings.Bookmarks;
                if (b.GetVideo(id) is { } v && (input.Text ?? "").Trim() is { Length: > 0 } t) v.Title = t;
                settings.Bookmarks = b;
            })!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .Show();
    }
}
