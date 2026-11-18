using VrView.Core;
using Xunit;

namespace VrView.Core.Tests;

public class WebInputTests
{
    [Fact]
    public void Js_strings_cannot_break_out_of_the_script()
    {
        Assert.Equal("\"a\\\"b\\\\c\\n\\u003c/script>\\u2028\"", WebInput.JsString("a\"b\\c\n</script>\u2028"));
    }

    [Fact]
    public void Caret_is_drawn_where_it_is()
    {
        Assert.Equal("ab▏c", WebInput.WithCaret("abc", 2));
        Assert.Equal("abc▏", WebInput.WithCaret("abc", -1));
        Assert.Equal("abc▏", WebInput.WithCaret("abc", 9));
    }
}
