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
    public void RemoveCompleted_takes_sub_items_with_a_checked_parent()
    {
        string text = E("[x] trip\n\t[ ] passport\n\t[x] tickets\n[ ] groceries\n\t[x] milk\n\t[ ] bread");
        Assert.Equal("[ ] groceries\n\t[ ] bread", P(TodoText.RemoveCompleted(text)));
    }

    [Fact]
    public void RemoveCompleted_subtree_stops_at_blank_line_or_same_level()
    {
        string text = E("[x] a\n\t[ ] a1\n\n\tindented after blank\n[ ] b");
        Assert.Equal("\n\tindented after blank\n[ ] b", P(TodoText.RemoveCompleted(text)));
    }

    [Theory]
    [InlineData("text", 0)]
    [InlineData("  text", 2)]
    [InlineData("\ttext", 4)]
    [InlineData("  \ttext", 4)]   // tab advances to the next stop
    [InlineData("\t\t[ ] x", 8)]
    [InlineData("    [ ] x", 4)]  // four spaces == one tab
    public void IndentWidth_counts_columns(string line, int expected)
    {
        Assert.Equal(expected, TodoText.IndentWidth(line, TodoText.LineAt(line, 0)));
    }

    [Fact]
    public void IndentLines_indents_caret_line_and_moves_caret()
    {
        string text = E("[ ] a\n[ ] b");
        int start = 8, end = 8; // inside "b"
        Assert.Equal("[ ] a\n\t[ ] b", P(TodoText.IndentLines(text, ref start, ref end)));
        Assert.Equal((9, 9), (start, end));
    }

    [Fact]
    public void IndentLines_indents_every_selected_line()
    {
        string text = E("one\ntwo\nthree");
        int start = 1, end = 9; // from "one" into "three"
        Assert.Equal("\tone\n\ttwo\n\tthree", P(TodoText.IndentLines(text, ref start, ref end)));
        Assert.Equal((2, 12), (start, end));
    }

    [Fact]
    public void IndentLines_ignores_line_where_selection_merely_ends()
    {
        string text = E("one\ntwo");
        int start = 0, end = 4; // whole first line incl. separator, ends at start of "two"
        Assert.Equal("\tone\ntwo", P(TodoText.IndentLines(text, ref start, ref end)));
    }

    [Theory]
    [InlineData("\t[ ] a", "[ ] a")]
    [InlineData("\t\t[ ] a", "\t[ ] a")]
    [InlineData("    [ ] a", "[ ] a")]
    [InlineData("  [ ] a", "[ ] a")]
    [InlineData("      x", "  x")]
    [InlineData("[ ] a", "[ ] a")] // nothing to remove
    public void OutdentLines_removes_one_level(string line, string expected)
    {
        int start = line.Length, end = line.Length;
        Assert.Equal(expected, TodoText.OutdentLines(line, ref start, ref end));
        Assert.Equal(expected.Length, start); // caret stays at the end of the line
    }

    [Fact]
    public void OutdentLines_caret_inside_removed_indent_moves_to_line_start()
    {
        string text = E("x\n\t\ty");
        int start = 3, end = 3; // between the two tabs
        Assert.Equal("x\n\ty", P(TodoText.OutdentLines(text, ref start, ref end)));
        Assert.Equal(2, start);
    }

    [Fact]
    public void ToggleWithChildren_checks_and_unchecks_the_whole_subtree()
    {
        string text = E("[ ] trip\n\t[ ] passport\n\t\t[ ] renew\n\tnote line\n\t[ ] tickets\n[ ] other");
        var line = TodoText.LineAt(text, 0);
        TodoText.TryParse(text, line, out var todo);

        string checkedText = TodoText.ToggleWithChildren(text, line, todo);
        Assert.Equal("[x] trip\n\t[x] passport\n\t\t[x] renew\n\tnote line\n\t[x] tickets\n[ ] other", P(checkedText));

        TodoText.TryParse(checkedText, line, out var again);
        Assert.Equal(text, TodoText.ToggleWithChildren(checkedText, line, again));
    }

    [Fact]
    public void ToggleWithChildren_on_a_child_leaves_siblings_and_parent_alone()
    {
        string text = E("[ ] parent\n\t[ ] a\n\t[ ] b");
        var line = TodoText.LineAt(text, 12);
        TodoText.TryParse(text, line, out var todo);
        Assert.Equal("[ ] parent\n\t[x] a\n\t[ ] b", P(TodoText.ToggleWithChildren(text, line, todo)));
    }

    [Fact]
    public void ToggleWithChildren_stops_at_blank_line()
    {
        string text = E("[ ] a\n\n\t[ ] not a child");
        var line = TodoText.LineAt(text, 0);
        TodoText.TryParse(text, line, out var todo);
        Assert.Equal("[x] a\n\n\t[ ] not a child", P(TodoText.ToggleWithChildren(text, line, todo)));
    }

    [Fact]
    public void LeadingWhitespace_returns_indent()
    {
        string text = E("x\n\t  y");
        Assert.Equal("\t  ", TodoText.LeadingWhitespace(text, TodoText.LineAt(text, 3)));
    }

    [Fact]
    public void HasCompleted_detects_checked_items()
    {
        Assert.True(TodoText.HasCompleted(E("[ ] a\n[x] b")));
        Assert.False(TodoText.HasCompleted(E("[ ] a\ntext [x]")));
        Assert.False(TodoText.HasCompleted(""));
    }
}
