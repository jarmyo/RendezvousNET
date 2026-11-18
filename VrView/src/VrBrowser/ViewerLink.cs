using Android.App;
using Android.Content;
using Android.Widget;
using VrView.Core;

namespace VrView;

/// <summary>
/// VrBrowser's link to VrView: the viewer setup comes from VrView (configured once, there) and web
/// videos are played in VrView's player, with the browser's cookies so the sites accept them.
/// </summary>
internal static class ViewerLink
{
    private const string ImportedKey = "importedFromVrView";

    /// <summary>
    /// Copies VrView's viewer and controls settings into this app's settings. The first time it also
    /// brings the favorites and saved web videos VrView had while the browser was part of it.
    /// Without VrView (or with a VrView signed with another key) the settings stay as they are.
    /// </summary>
    public static void SyncSettings(Context context)
    {
        var prefs = context.GetSharedPreferences("vrview", FileCreationMode.Private)!;
        bool firstTime = !prefs.GetBoolean(ImportedKey, false);
        var uri = Android.Net.Uri.Parse("content://" + AppLinks.SettingsAuthority)!;
        try
        {
            using var cursor = context.ContentResolver!.Query(uri, null, null, null, null);
            if (cursor is null) return;
            var edit = prefs.Edit()!;
            while (cursor.MoveToNext())
            {
                string key = cursor.GetString(0) ?? "", type = cursor.GetString(1) ?? "", value = cursor.GetString(2) ?? "";
                if (!AppLinks.IsShared(key) || (AppLinks.IsWeb(key) && !firstTime)) continue;
                switch (type)
                {
                    case "b": edit.PutBoolean(key, value == "1"); break;
                    case "i" when int.TryParse(value, out int i): edit.PutInt(key, i); break;
                    case "f" when float.TryParse(value, System.Globalization.NumberStyles.Float,
                                                 System.Globalization.CultureInfo.InvariantCulture, out float f):
                        edit.PutFloat(key, f); break;
                    case "s": edit.PutString(key, value); break;
                    case "-": edit.Remove(key); break; // not set in VrView: back to the default
                }
            }
            edit.PutBoolean(ImportedKey, true);
            edit.Apply();
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException or Java.Lang.Exception)
        {
            // VrView missing, too old, or signed with another key.
        }
    }

    /// <summary>Plays web videos in VrView's player, from <paramref name="startIndex"/>.</summary>
    public static void Play(Activity activity, IList<MediaItem> items, int startIndex)
    {
        var intent = new Intent();
        intent.SetClassName(AppLinks.ViewerPackage, AppLinks.PlayerActivity);
        intent.PutExtra(AppLinks.ExtraLocations, items.Select(i => i.Location).ToArray());
        intent.PutExtra(AppLinks.ExtraNames, items.Select(i => i.Name).ToArray());
        intent.PutExtra(AppLinks.ExtraSizes, items.Select(i => i.SizeBytes).ToArray());
        intent.PutExtra(AppLinks.ExtraPages, items.Select(i => i.PageUrl ?? "").ToArray());
        intent.PutExtra(AppLinks.ExtraKeys, items.Select(i => i.StateKey ?? "").ToArray());
        intent.PutExtra(AppLinks.ExtraHeaders,
            items.Select(i => AppLinks.EncodeHeaders(WebHeaders.For(activity, i.Location, i.PageUrl))).ToArray());
        intent.PutExtra(AppLinks.ExtraStart, startIndex);
        try
        {
            activity.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            new AlertDialog.Builder(activity)
                .SetTitle(T("VrView is not installed"))!
                .SetMessage(T("Web videos play in the VrView app. Install it to watch them with its player."))!
                .SetPositiveButton(T("Close"), (_, _) => { })!
                .Show();
        }
    }

    /// <summary>True when VrView is installed (its viewer setup and player are available).</summary>
    public static bool ViewerInstalled(Context context)
    {
        try
        {
            context.PackageManager!.GetPackageInfo(AppLinks.ViewerPackage, 0);
            return true;
        }
        catch (Android.Content.PM.PackageManager.NameNotFoundException)
        {
            return false;
        }
    }
}
