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

/// <summary>A point of the VR browser's page (0..1, origin top-left).</summary>
public sealed record PagePoint(float X, float Y);

/// <summary>The box of an element of the VR browser's page (0..1, origin top-left).</summary>
public sealed record PageBox(float X0, float Y0, float X1, float Y1)
{
    public bool Contains(float x, float y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
}
