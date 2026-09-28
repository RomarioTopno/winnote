using WinNote.Controls;

namespace WinNote.Tests;

public class TodoTextTests
{
    // Tests are written with '\n'; the editor uses '\r' as its line separator.
    private static string E(string text) => text.Replace('\n', '\r');
    private static string P(string? text) => text?.Replace('\r', '\n') ?? "<null>";

    [Theory]
    [InlineData("[ ] milk", false)]
    [InlineData("[x] milk", true)]
    [InlineData("[X] milk", true)]
    [InlineData("  [ ] indented", false)]
    [InlineData("\t[x] tabbed", true)]
    [InlineData("[ ]", false)] // bare marker at end of line
    public void Parses_todo_lines(string line, bool isChecked)
    {
        Assert.True(TodoText.TryParseAt(line, 0, out _, out var todo));
        Assert.Equal(isChecked, todo.IsChecked);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("[y] not a state")]
    [InlineData("[ ]x no space after marker")]
    [InlineData("- [ ] markdown style (converted on typing, not parsed)")]
    [InlineData("text [ ] marker mid-line")]
    [InlineData("")]
    public void Ignores_non_todo_lines(string line)
    {
        Assert.False(TodoText.TryParseAt(line, 0, out _, out _));
    }

    [Fact]
    public void Marker_and_content_positions()
    {
        string text = E("hello\n  [ ] milk");
        Assert.True(TodoText.TryParseAt(text, 10, out var line, out var todo));
        Assert.Equal(6, line.Start);
        Assert.Equal(8, todo.MarkerStart);
        Assert.Equal(11, todo.MarkerEnd);
        Assert.Equal(12, todo.ContentStart);
    }

    [Fact]
    public void LineAt_finds_line_bounds_at_edges()
    {
        string text = E("ab\ncd\n\nef");
        Assert.Equal(new TodoText.Line(0, 2), TodoText.LineAt(text, 0));
        Assert.Equal(new TodoText.Line(0, 2), TodoText.LineAt(text, 2));   // at the separator
        Assert.Equal(new TodoText.Line(3, 5), TodoText.LineAt(text, 3));
        Assert.Equal(new TodoText.Line(6, 6), TodoText.LineAt(text, 6));   // empty line
        Assert.Equal(new TodoText.Line(7, 9), TodoText.LineAt(text, 99));  // clamped
    }

    [Fact]
    public void Lines_enumerates_every_line_including_empty_ones()
    {
        var lines = TodoText.Lines(E("a\n\nb\n")).ToList();
        Assert.Equal(4, lines.Count);
        Assert.Equal(0, lines[^1].Length);
    }

    [Theory]
    [InlineData("[ ] milk", "[x] milk")]
    [InlineData("[x] milk", "[ ] milk")]
    [InlineData("[X] milk", "[ ] milk")]
    [InlineData("  [ ] eggs", "  [x] eggs")]
    public void Toggle_flips_state_and_keeps_everything_else(string before, string after)
    {
        TodoText.TryParseAt(before, 0, out _, out var todo);
        Assert.Equal(after, TodoText.Toggle(before, todo));
    }

    [Fact]
    public void Toggle_only_touches_its_own_line()
    {
        string text = E("[ ] a\n[ ] b\n[ ] c");
        TodoText.TryParseAt(text, 7, out _, out var todo);
        Assert.Equal("[ ] a\n[x] b\n[ ] c", P(TodoText.Toggle(text, todo)));
    }

    [Fact]
    public void MakeTodo_inserts_after_indent()
    {
        string text = E("a\n  buy");
        string result = TodoText.MakeTodo(text, TodoText.LineAt(text, 4), out int at);
        Assert.Equal("a\n  [ ] buy", P(result));
        Assert.Equal(4, at);
    }

    [Fact]
    public void RemoveMarker_keeps_indent_and_content()
    {
        TodoText.TryParseAt("  [x] item", 0, out _, out var todo);
        Assert.Equal("  item", TodoText.RemoveMarker("  [x] item", todo));
    }

    [Theory]
    [InlineData("[] ", 3, "[ ] ", 4)]
    [InlineData("- [ ] ", 6, "[ ] ", 4)]
    [InlineData("* [ ] ", 6, "[ ] ", 4)]
    [InlineData("- [] ", 5, "[ ] ", 4)]
    [InlineData("- [x] ", 6, "[x] ", 4)]
    [InlineData("* [X] ", 6, "[x] ", 4)]
    [InlineData("  - [ ] rest", 8, "  [ ] rest", 6)]
    public void AutoConvert_shorthands(string text, int caret, string expected, int expectedCaret)
    {
        Assert.Equal(expected, TodoText.AutoConvert(text, caret, out int newCaret));
        Assert.Equal(expectedCaret, newCaret);
    }

    [Theory]
    [InlineData("a [] ", 5)]      // not at line start
    [InlineData("[ ] ", 4)]       // already a todo
    [InlineData("hello ", 6)]
    [InlineData("", 0)]
    public void AutoConvert_leaves_other_text_alone(string text, int caret)
    {
        Assert.Null(TodoText.AutoConvert(text, caret, out _));
    }

    [Fact]
    public void AutoConvert_works_on_later_lines()
    {
        Assert.Equal("x\n[ ] ", P(TodoText.AutoConvert(E("x\n[] "), 5, out int caret)));
        Assert.Equal(6, caret);
    }

    [Fact]
    public void RemoveCompleted_drops_only_checked_lines()
    {
        string text = E("Groceries\n[x] done\n[ ] open\n  [X] also done\nplain [x] not a todo");
        Assert.Equal("Groceries\n[ ] open\nplain [x] not a todo", P(TodoText.RemoveCompleted(text)));
    }

    [Fact]
    public void HasCompleted_detects_checked_items()
    {
        Assert.True(TodoText.HasCompleted(E("[ ] a\n[x] b")));
        Assert.False(TodoText.HasCompleted(E("[ ] a\ntext [x]")));
        Assert.False(TodoText.HasCompleted(""));
    }
}
