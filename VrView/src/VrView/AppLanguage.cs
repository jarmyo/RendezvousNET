using Android.Content;
using VrView.Core;

namespace VrView;

internal static class AppLanguage
{
    /// <summary>
    /// Picks the interface language from the phone's language list, or from Android's per-app
    /// language setting, which arrives the same way. Unsupported languages give English.
    /// </summary>
    public static void Apply(Context context) =>
        Loc.Language = context.Resources!.Configuration!.Locales.GetFirstMatch(Loc.Languages)?.Language ?? Loc.DefaultLanguage;
}
