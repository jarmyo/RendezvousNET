using Android.Content;
using Android.Database;
using VrView.Core;

namespace VrView;

/// <summary>
/// Gives VrBrowser the viewer setup (lenses, field of view, controls…), so the viewer is configured
/// once, here; and, for its first start, the favorites and web videos saved while the browser was part
/// of VrView. Read-only, and only for apps signed with the same key (see the manifest's permission).
/// Rows: key, type (b, i, f, s, or "-" when not set), value.
/// </summary>
[ContentProvider([AppLinks.SettingsAuthority], Name = "net.rendezvous.vrview.SharedSettingsProvider",
    Exported = true, ReadPermission = AppLinks.SettingsPermission, WritePermission = AppLinks.SettingsPermission)]
public class SharedSettingsProvider : ContentProvider
{
    public override bool OnCreate() => true;

    public override ICursor? Query(Android.Net.Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder)
    {
        var prefs = Context!.GetSharedPreferences("vrview", FileCreationMode.Private)!;
        var all = prefs.All ?? new Dictionary<string, object?>();
        var cursor = new MatrixCursor(["key", "type", "value"]);
        var keys = AppLinks.ViewKeys.Concat(all.Keys.Where(AppLinks.IsWeb)).Distinct();
        foreach (var key in keys)
        {
            all.TryGetValue(key, out var value);
            var (type, text) = value switch
            {
                Java.Lang.Boolean b => ("b", b.BooleanValue() ? "1" : "0"),
                Java.Lang.Integer i => ("i", i.IntValue().ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Java.Lang.Float f => ("f", f.FloatValue().ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
                bool b => ("b", b ? "1" : "0"),
                int i => ("i", i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                float f => ("f", f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
                null => ("-", ""),
                _ => ("s", value.ToString() ?? ""),
            };
            cursor.AddRow(new Java.Lang.Object[] { new Java.Lang.String(key), new Java.Lang.String(type), new Java.Lang.String(text) });
        }
        return cursor;
    }

    public override string? GetType(Android.Net.Uri uri) => null;

    public override Android.Net.Uri? Insert(Android.Net.Uri uri, ContentValues? values) => null;

    public override int Delete(Android.Net.Uri uri, string? selection, string[]? selectionArgs) => 0;

    public override int Update(Android.Net.Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}
