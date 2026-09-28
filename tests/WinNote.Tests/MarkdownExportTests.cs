using WinNote.Services;

namespace WinNote.Tests;

public class MarkdownExportTests
{
    [Fact]
    public void Todos_become_task_list_items_with_nesting()
    {
        string note = "# Trip\n[ ] Flights\n\t[x] Compare\n\t\t[ ] Deep\n[x] Pack";
        Assert.Equal("# Trip\n- [ ] Flights\n  - [x] Compare\n    - [ ] Deep\n- [x] Pack", MarkdownExport.FromNote(note));
    }

    [Fact]
    public void Uppercase_X_and_bare_marker()
    {
        Assert.Equal("- [x] Done\n- [ ]", MarkdownExport.FromNote("[X] Done\n[ ]"));
    }

    [Fact]
    public void Plain_lines_keep_their_line_breaks()
    {
        Assert.Equal("line one  \nline two\n\nnew paragraph", MarkdownExport.FromNote("line one\nline two\n\nnew paragraph"));
    }

    [Fact]
    public void Block_elements_do_not_get_hard_breaks()
    {
        string note = "# Title\ntext\n- item\n> quote\n---\n1. first";
        Assert.Equal(note, MarkdownExport.FromNote(note));
    }

    [Fact]
    public void Code_blocks_are_kept_verbatim()
    {
        string note = "```\n[ ] not a todo\n\tindented\n```";
        Assert.Equal(note, MarkdownExport.FromNote(note));
    }

    [Fact]
    public void Existing_markdown_is_kept()
    {
        Assert.Equal("**bold** and [link](https://x.y)", MarkdownExport.FromNote("**bold** and [link](https://x.y)"));
    }

    [Fact]
    public void Windows_line_endings_are_normalised()
    {
        Assert.Equal("- [ ] a\n- [x] b", MarkdownExport.FromNote("[ ] a\r\n[x] b"));
    }
}
