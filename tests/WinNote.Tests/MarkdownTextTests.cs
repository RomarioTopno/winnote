using WinNote.Controls;

namespace WinNote.Tests;

public class MarkdownTextTests
{
    private static string Text(string s, MdSpan span) => s.Substring(span.Start, span.Length);

    private static List<string> Of(string s, MdKind kind) =>
        MarkdownText.Parse(s).Where(x => x.Kind == kind).Select(x => Text(s, x)).ToList();

    [Theory]
    [InlineData("# Title", 1, "Title")]
    [InlineData("### Third", 3, "Third")]
    [InlineData("###### Six", 6, "Six")]
    [InlineData("  ## Indented", 2, "Indented")]
    public void Headings(string s, int level, string text)
    {
        var heading = MarkdownText.Parse(s).Single(x => x.Kind == MdKind.Heading);
        Assert.Equal(level, heading.Level);
        Assert.Equal(text, Text(s, heading));
        Assert.Contains(MarkdownText.Parse(s), x => x.Kind == MdKind.Marker && Text(s, x).StartsWith('#'));
    }

    [Theory]
    [InlineData("#NoSpace")]
    [InlineData("####### seven")]
    [InlineData("text # not heading")]
    public void Not_headings(string s)
    {
        Assert.DoesNotContain(MarkdownText.Parse(s), x => x.Kind == MdKind.Heading);
    }

    [Fact]
    public void Bold_italic_strike_and_markers()
    {
        string s = "a **bold** b *it* c ~~gone~~ d __also__ e _em_";
        Assert.Equal(["bold", "also"], Of(s, MdKind.Bold));
        Assert.Equal(["it", "em"], Of(s, MdKind.Italic));
        Assert.Equal(["gone"], Of(s, MdKind.Strike));
        Assert.Equal(["**", "**", "*", "*", "~~", "~~", "__", "__", "_", "_"], Of(s, MdKind.Marker));
    }

    [Fact]
    public void Bold_and_italic_together()
    {
        string s = "***both***";
        Assert.Equal(["both"], Of(s, MdKind.Bold));
        Assert.Equal(["both"], Of(s, MdKind.Italic));
    }

    [Fact]
    public void Nested_emphasis()
    {
        string s = "**bold with *italic* inside**";
        Assert.Equal(["bold with *italic* inside"], Of(s, MdKind.Bold));
        Assert.Equal(["italic"], Of(s, MdKind.Italic));
    }

    [Theory]
    [InlineData("snake_case_name")]        // underscores inside words
    [InlineData("2 * 3 * 4")]             // spaced asterisks
    [InlineData("**unclosed")]
    [InlineData("a ~single~ tilde")]
    public void No_false_emphasis(string s)
    {
        var kinds = MarkdownText.Parse(s).Select(x => x.Kind).ToList();
        Assert.DoesNotContain(MdKind.Bold, kinds);
        Assert.DoesNotContain(MdKind.Italic, kinds);
        Assert.DoesNotContain(MdKind.Strike, kinds);
    }

    [Fact]
    public void Inline_code_content_is_not_parsed()
    {
        string s = "run `a **not bold** b` now";
        Assert.Equal(["a **not bold** b"], Of(s, MdKind.Code));
        Assert.Empty(Of(s, MdKind.Bold));
    }

    [Fact]
    public void Links_with_text_and_url()
    {
        string s = "see [the docs](https://example.com/x) please";
        var link = MarkdownText.Parse(s).Single(x => x.Kind == MdKind.LinkText);
        Assert.Equal("the docs", Text(s, link));
        Assert.Equal("https://example.com/x", link.Url);
        Assert.Equal(["https://example.com/x"], Of(s, MdKind.LinkUrl));
    }

    [Fact]
    public void Bare_urls_exclude_trailing_punctuation()
    {
        string s = "Go to https://example.com/page. Or (http://a.b/c)!";
        Assert.Equal(["https://example.com/page", "http://a.b/c"], Of(s, MdKind.LinkText));
    }

    [Fact]
    public void Todo_marker_is_not_a_link()
    {
        Assert.Empty(Of("[ ] buy (milk)", MdKind.LinkText));
        Assert.Empty(Of("[x] done", MdKind.LinkText));
    }

    [Fact]
    public void Quotes_lists_and_rules()
    {
        string s = "> quoted *text*\n- item\n* star item\n12. twelfth\n---\n- - -";
        Assert.Equal(["quoted *text*"], Of(s, MdKind.Quote));
        Assert.Equal(["text"], Of(s, MdKind.Italic));
        Assert.Equal(["-", "*", "12."], Of(s, MdKind.ListMarker));
        Assert.Equal(["---", "- - -"], Of(s, MdKind.Rule));
    }

    [Fact]
    public void Fenced_code_block_lines_are_code_and_not_parsed()
    {
        string s = "```\n# not heading\n**not bold**\n```\n# heading";
        Assert.Equal(["# not heading", "**not bold**"], Of(s, MdKind.Code));
        Assert.Equal(["heading"], Of(s, MdKind.Heading));
    }

    [Fact]
    public void Works_with_editor_line_separators()
    {
        string s = "# A\r**b**";
        Assert.Equal(["A"], Of(s, MdKind.Heading));
        Assert.Equal(["b"], Of(s, MdKind.Bold));
    }

    [Fact]
    public void LinkAt_finds_openable_links_only()
    {
        string s = "x [site](https://a.com) y https://b.com z [bad](javascript:alert(1))";
        Assert.Equal("https://a.com", MarkdownText.LinkAt(s, 4));
        Assert.Equal("https://a.com", MarkdownText.LinkAt(s, 15));
        Assert.Equal("https://b.com", MarkdownText.LinkAt(s, 30));
        Assert.Null(MarkdownText.LinkAt(s, 0));
        Assert.Null(MarkdownText.LinkAt(s, s.IndexOf("bad", StringComparison.Ordinal)));
    }
}
