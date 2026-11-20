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

    [Fact]
    public void Page_box_contains_its_edges_only()
    {
        var box = new PageBox(0.2f, 0.3f, 0.4f, 0.5f);
        Assert.True(box.Contains(0.3f, 0.4f));
        Assert.True(box.Contains(0.2f, 0.5f));
        Assert.False(box.Contains(0.1f, 0.4f));
        Assert.False(box.Contains(0.3f, 0.6f));
        Assert.Equal(box, new PageBox(0.2f, 0.3f, 0.4f, 0.5f)); // a new lookup of the same element keeps the dwell going
    }
}
