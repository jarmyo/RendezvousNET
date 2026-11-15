using System.Globalization;

namespace VrView.Core;

/// <summary>
/// The interface language. Texts are written in English in the code (<c>T("Choose folder")</c>) and
/// translated from <see cref="Translations"/>; a text or language without a translation stays in
/// English. It lives here, not in Android resources, because most texts are built in this library.
/// </summary>
public static class Loc
{
    public const string DefaultLanguage = "en";

    /// <summary>Supported languages, default first (also listed in res/xml/locales_config.xml).</summary>
    public static readonly string[] Languages = [DefaultLanguage, "es", "fr", "de"];

    /// <summary>Two-letter language in use; anything unsupported falls back to English.</summary>
    public static string Language { get; set; } = DefaultLanguage;

    public static string T(string english) => Translate(english, Language);

    /// <summary>Translates a format string and fills in its {0} {1}… placeholders.</summary>
    public static string F(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    public static string BookmarkCount(int n) => n == 1 ? T("1 bookmark") : F("{0} bookmarks", n);

    public static string VideoCount(int n) => n == 1 ? T("1 video") : F("{0} videos", n);

    public static string Translate(string english, string language)
    {
        if (!Translations.Rows.TryGetValue(english, out var row)) return english;
        return language switch { "es" => row.Es, "fr" => row.Fr, "de" => row.De, _ => english };
    }

    /// <summary>English keys of every translated text.</summary>
    public static IEnumerable<string> Keys => Translations.Rows.Keys;
}
