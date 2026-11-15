using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace VrView.Core.Tests;

public class LocTests
{
    private static readonly Regex Call = new("""\b[TF]\(\s*"((?:[^"\\]|\\.)*)"(?=\s*[,)])""");
    private static readonly Regex Placeholder = new(@"\{\d+[^}]*\}");
    private static readonly string[] Translated = Loc.Languages.Where(l => l != Loc.DefaultLanguage).ToArray();

    private static string Root([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        Path.GetFileName(f) != "Translations.cs");

    /// <summary>Source text without comments, so a Spanish word in a comment does not count.</summary>
    private static string Code(string file) =>
        string.Join('\n', File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//")));

    [Fact]
    public void Every_text_used_in_the_code_has_a_row()
    {
        var keys = Loc.Keys.ToHashSet();
        var missing = SourceFiles().SelectMany(f => Call.Matches(Code(f)).Select(m => Regex.Unescape(m.Groups[1].Value)))
            .Where(k => !keys.Contains(k)).Distinct().ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_row_is_used_in_the_code()
    {
        string all = string.Join('\n', SourceFiles().Select(Code));
        var unused = Loc.Keys.Where(k => !all.Contains('"' + k.Replace("\"", "\\\"") + '"')).ToList();
        Assert.Empty(unused);
    }

    [Fact]
    public void Slider_names_have_rows()
    {
        var keys = Loc.Keys.ToHashSet();
        foreach (var p in Enum.GetValues<Param>()) Assert.Contains(Params.Spec(p).Name, keys);
    }

    [Fact]
    public void Translations_keep_the_placeholders_and_are_valid_format_strings()
    {
        object[] args = [1.5, 2.5, 3.5];
        foreach (string key in Loc.Keys)
        {
            var expected = Placeholder.Matches(key).Select(m => m.Value).Order().ToArray();
            foreach (string language in Translated)
            {
                string text = Loc.Translate(key, language);
                Assert.False(string.IsNullOrWhiteSpace(text), key);
                Assert.Equal(text.Trim(), text);
                Assert.Equal(expected, Placeholder.Matches(text).Select(m => m.Value).Order().ToArray());
                if (expected.Length > 0) _ = string.Format(text, args);
            }
        }
    }

    [Fact]
    public void Unknown_texts_and_languages_stay_in_english()
    {
        Assert.Equal("Library", Loc.Translate("Library", "pt"));
        Assert.Equal("Not in the table", Loc.Translate("Not in the table", "es"));
        Assert.Equal("Biblioteca", Loc.Translate("Library", "es"));
        Assert.Equal("Bibliothèque", Loc.Translate("Library", "fr"));
        Assert.Equal("Mediathek", Loc.Translate("Library", "de"));
    }

    [Fact]
    public void No_spanish_text_is_left_in_the_code()
    {
        var spanish = new Regex("""
            "[^"\n]*[áéíóúñ¿¡«»][^"\n]*"
            """.Trim());
        var left = SourceFiles().SelectMany(f => spanish.Matches(Code(f)).Select(m => Path.GetFileName(f) + ": " + m.Value)).ToList();
        Assert.Empty(left);
    }

    [Fact]
    public void Language_list_matches_the_android_locale_config()
    {
        string config = File.ReadAllText(Path.Combine(Root(), "src", "VrView", "Resources", "xml", "locales_config.xml"));
        foreach (string language in Loc.Languages) Assert.Contains($"android:name=\"{language}\"", config);
    }

    [Theory]
    [InlineData("title.txt", 30)]
    [InlineData("short_description.txt", 80)]
    [InlineData("full_description.txt", 4000)]
    public void Store_texts_fit_the_play_limits(string file, int limit)
    {
        var dirs = Directory.GetDirectories(Path.Combine(Root(), "fastlane", "metadata", "android"));
        Assert.Equal(5, dirs.Length);
        foreach (string dir in dirs)
        {
            int length = File.ReadAllText(Path.Combine(dir, file)).TrimEnd().EnumerateRunes().Count();
            Assert.InRange(length, 1, limit);
        }
    }
}
