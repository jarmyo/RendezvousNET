using System.Text;

namespace VrView.Core;

/// <summary>Typed text in the VR browser: sent into the page as script, and shown with its caret.</summary>
public static class WebInput
{
    /// <summary>A JavaScript string literal with <paramref name="s"/> (safe inside any script).</summary>
    public static string JsString(string s)
    {
        var b = new StringBuilder("\"", s.Length + 2);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '<': b.Append("\\u003c"); break;
                default:
                    if (c < ' ' || c == '\u2028' || c == '\u2029') b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        return b.Append('"').ToString();
    }

    /// <summary>Text with the caret mark at <paramref name="caret"/> (negative or past the end: at the end).</summary>
    public static string WithCaret(string text, int caret, string mark = "▏") =>
        caret < 0 || caret >= text.Length ? text + mark : text[..caret] + mark + text[caret..];
}
