using WinNote.Controls;

namespace WinNote.Tests;

public class EditHistoryTests
{
    [Fact]
    public void Commands_are_separate_undo_steps()
    {
        var h = new EditHistory("");
        h.Record("a", isCommand: true);
        h.Record("ab", isCommand: true);

        Assert.Equal("a", h.Undo());
        Assert.Equal("", h.Undo());
        Assert.Null(h.Undo());
    }

    [Fact]
    public void Quick_typing_is_grouped_into_one_step()
    {
        var h = new EditHistory("");
        h.Record("h", isCommand: false);
        h.Record("he", isCommand: false);
        h.Record("hel", isCommand: false);

        Assert.Equal("", h.Undo());
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void A_new_line_starts_a_new_step()
    {
        var h = new EditHistory("");
        h.Record("abc", isCommand: false);
        h.Record("abc\r", isCommand: false); // Enter
        h.Record("abc\rd", isCommand: false);

        Assert.Equal("abc\r", h.Undo());
        Assert.Equal("abc", h.Undo());
        Assert.Equal("", h.Undo());
    }

    [Fact]
    public void Redo_restores_and_new_edit_clears_redo()
    {
        var h = new EditHistory("x");
        h.Record("xy", isCommand: true);
        Assert.Equal("x", h.Undo());
        Assert.Equal("xy", h.Redo());
        Assert.Equal("x", h.Undo());

        h.Record("xz", isCommand: true);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void Recording_the_same_text_is_ignored()
    {
        var h = new EditHistory("same");
        h.Record("same", isCommand: true);
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Reset_changes_current_without_an_undo_step()
    {
        var h = new EditHistory("a");
        h.Reset("a normalised");
        Assert.Equal("a normalised", h.Current);
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void History_is_capped()
    {
        var h = new EditHistory("");
        for (int i = 1; i <= 250; i++) h.Record(new string('x', i), isCommand: true);
        int steps = 0;
        while (h.Undo() is not null) steps++;
        Assert.Equal(200, steps);
    }

    [Theory]
    [InlineData("hello world", "hello brave world", 12)] // caret after the inserted word
    [InlineData("hello brave world", "hello world", 6)]  // caret where text was removed
    [InlineData("abc", "abc", 3)]
    [InlineData("", "new", 3)]
    public void ChangedRegionEnd_puts_caret_after_the_change(string before, string after, int expected)
    {
        Assert.Equal(expected, EditHistory.ChangedRegionEnd(before, after));
    }
}
