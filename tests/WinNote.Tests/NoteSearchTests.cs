using WinNote.Services;

namespace WinNote.Tests;

public class NoteSearchTests
{
    private static readonly string[] Notes =
    [
        "Groceries\n[ ] milk\n[x] Oat MILK\n\t[ ] bread",
        "Work\nCall Milkman about invoice",
        "",
        "No match here",
    ];

    [Fact]
    public void Finds_case_insensitive_matches_across_notes()
    {
        var hits = NoteSearch.Find(Notes, "milk");
        Assert.Equal(3, hits.Count);
        Assert.Equal([0, 0, 1], hits.Select(h => h.NoteIndex));
    }

    [Fact]
    public void Hit_positions_point_at_the_match_in_the_note()
    {
        foreach (var hit in NoteSearch.Find(Notes, "milk"))
            Assert.Equal("milk", Notes[hit.NoteIndex].Substring(hit.Start, hit.Length), ignoreCase: true);
    }

    [Fact]
    public void Snippet_is_the_line_with_match_offset()
    {
        var hit = NoteSearch.Find(Notes, "invoice").Single();
        Assert.Equal("Call Milkman about invoice", hit.Snippet);
        Assert.Equal("invoice", hit.Snippet.Substring(hit.SnippetMatchStart, hit.Length));
        Assert.Equal("Work", hit.NoteTitle);
    }

    [Fact]
    public void One_hit_per_line_even_with_repeated_matches()
    {
        var hits = NoteSearch.Find(["ab ab ab\nab"], "ab");
        Assert.Equal(2, hits.Count);
        Assert.Equal(0, hits[0].Start);
        Assert.Equal(9, hits[1].Start);
    }

    [Fact]
    public void Indented_lines_are_trimmed_in_snippet()
    {
        var hit = NoteSearch.Find(Notes, "bread").Single();
        Assert.Equal("[ ] bread", hit.Snippet);
        Assert.Equal(4, hit.SnippetMatchStart);
        Assert.Equal("bread", Notes[0].Substring(hit.Start, hit.Length));
    }

    [Fact]
    public void Long_lines_are_shortened_around_the_match()
    {
        string line = new string('a', 100) + "NEEDLE" + new string('b', 100);
        var hit = NoteSearch.Find([line], "needle").Single();
        Assert.StartsWith("…", hit.Snippet);
        Assert.EndsWith("…", hit.Snippet);
        Assert.True(hit.Snippet.Length <= NoteSearch.MaxSnippetLength + 2);
        Assert.Equal("NEEDLE", hit.Snippet.Substring(hit.SnippetMatchStart, hit.Length));
    }

    [Fact]
    public void Match_at_the_end_of_a_long_line_stays_near_the_snippet_start()
    {
        string line = new string('a', 200) + " needle";
        var hit = NoteSearch.Find([line], "needle").Single();
        Assert.True(hit.SnippetMatchStart <= 22, $"match at {hit.SnippetMatchStart}");
        Assert.Equal("needle", hit.Snippet.Substring(hit.SnippetMatchStart, hit.Length));
    }

    [Fact]
    public void Match_near_the_start_of_a_long_line_has_no_leading_ellipsis()
    {
        string line = "NEEDLE" + new string('b', 200);
        var hit = NoteSearch.Find([line], "needle").Single();
        Assert.Equal(0, hit.SnippetMatchStart);
        Assert.EndsWith("…", hit.Snippet);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_finds_nothing(string query)
    {
        Assert.Empty(NoteSearch.Find(Notes, query));
    }

    [Fact]
    public void Query_is_trimmed()
    {
        Assert.Single(NoteSearch.Find(Notes, "  invoice  "));
    }

    [Fact]
    public void Matches_can_span_the_todo_marker_text()
    {
        Assert.Single(NoteSearch.Find(Notes, "[x] oat"));
    }

    [Fact]
    public void Respects_max_hits()
    {
        var many = Enumerable.Repeat("x", 50).Select((s, i) => $"x line {i}").ToArray();
        Assert.Equal(10, NoteSearch.Find(many, "x", maxHits: 10).Count);
    }

    [Theory]
    [InlineData("[ ] Buy milk\nmore", "Buy milk")]
    [InlineData("\n\n  Title  \nbody", "Title")]
    [InlineData("", "(empty note)")]
    [InlineData("   \n\t", "(empty note)")]
    public void TitleOf_uses_first_non_empty_line(string text, string expected)
    {
        Assert.Equal(expected, NoteSearch.TitleOf(text));
    }

    [Fact]
    public void TitleOf_shortens_long_titles()
    {
        string title = NoteSearch.TitleOf(new string('t', 60));
        Assert.Equal(41, title.Length);
        Assert.EndsWith("…", title);
    }
}
