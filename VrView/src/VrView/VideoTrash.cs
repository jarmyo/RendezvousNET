using Android.Content;
using Android.Provider;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>
/// Deletes a video picked through the Storage Access Framework. Videos that Android's media library
/// knows go to the system trash, where they can be restored for 30 days; Android asks the user to
/// confirm. Elsewhere (cloud folders, old Android) there is no trash, so the app asks before
/// deleting for good.
/// </summary>
internal static class VideoTrash
{
    public const int RequestCode = 40;

    /// <summary>Goes up on every deletion so the video list knows to read its folder again.</summary>
    public static int Version { get; private set; }

    private static (MediaItem Item, Action Done)? _pending;

    /// <param name="deleted">Runs on the UI thread once the video is gone.</param>
    public static void Delete(Activity activity, MediaItem item, Action deleted)
    {
        var document = Uri.Parse(item.Location)!;
        string name = System.IO.Path.GetFileName(item.Name);
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && MediaUri(activity, document) is { } media)
        {
            try
            {
                var request = MediaStore.CreateTrashRequest(activity.ContentResolver!, [media], true);
                _pending = (item, deleted);
#pragma warning disable CA1422 // The result arrives in OnActivityResult, like the folder picker's.
                activity.StartIntentSenderForResult(request.IntentSender, RequestCode, null, 0, 0, 0);
#pragma warning restore CA1422
                return;
            }
            catch (Java.Lang.Exception) { _pending = null; }
        }
        new AlertDialog.Builder(activity)
            .SetTitle(F("Delete “{0}”?", name))!
            .SetMessage(T("This folder has no trash, so the video will be deleted permanently."))!
            .SetPositiveButton(T("Delete permanently"), (_, _) =>
            {
                try
                {
                    if (!DocumentsContract.DeleteDocument(activity.ContentResolver!, document))
                        throw new Java.IO.IOException(name);
                    Finished(activity, F("Deleted: {0}", name), deleted);
                }
                catch (Java.Lang.Exception ex)
                {
                    Toast.MakeText(activity, F("Could not delete the video: {0}", ex.Message), ToastLength.Long)!.Show();
                }
            })!
            .SetNegativeButton(T("Cancel"), (_, _) => { })!
            .Show();
    }

    /// <summary>Call from OnActivityResult; true when the result was the trash confirmation.</summary>
    public static bool HandleResult(Activity activity, int requestCode, Result resultCode)
    {
        if (requestCode != RequestCode) return false;
        var pending = _pending;
        _pending = null;
        if (resultCode == Result.Ok && pending is { } p)
            Finished(activity, F("Moved to the trash: {0}", System.IO.Path.GetFileName(p.Item.Name)), p.Done);
        return true;
    }

    private static void Finished(Activity activity, string message, Action deleted)
    {
        Version++;
        Toast.MakeText(activity, message, ToastLength.Short)!.Show();
        deleted();
    }

    /// <summary>The media library's address of a picked document, or null when it is not indexed there.</summary>
    private static Uri? MediaUri(Context context, Uri document)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
        try { return MediaStore.GetMediaUri(context, document); }
        catch (Java.Lang.Exception) { return null; }
    }
}
