namespace WinNote.Services;

/// <summary>One matching line in a note.</summary>
/// <param name="NoteIndex">Index of the note in the note list.</param>
/// <param name="Start">Position of the match in the note text (same index in the editor).</param>
/// <param name="Length">Length of the match.</param>
/// <param name="Snippet">The matching line, trimmed and shortened around the match if long.</param>
/// <param name="SnippetMatchStart">Where the match starts inside <paramref name="Snippet"/>.</param>
/// <param name="NoteTitle">The note's first non-empty line (without a todo marker).</param>
public readonly record struct SearchHit(int NoteIndex, int Start, int Length, string Snippet, int SnippetMatchStart, string NoteTitle);

/// <summary>Case-insensitive text search across all notes: one hit per matching line.</summary>
public static class NoteSearch
{
    public const int MaxSnippetLength = 80;
    private const int ContextBefore = 20; // keep the match near the start so it's visible in a narrow window

    public static List<SearchHit> Find(IReadOnlyList<string> notes, string query, int maxHits = 200)
    {
        var hits = new List<SearchHit>();
        query = query.Trim();
        if (query.Length == 0) return hits;

        for (int n = 0; n < notes.Count && hits.Count < maxHits; n++)
        {
            string text = notes[n];
            string? title = null;
            int lineStart = 0;
            while (lineStart <= text.Length && hits.Count < maxHits)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = text.Length;

                int match = text.IndexOf(query, lineStart, lineEnd - lineStart, StringComparison.OrdinalIgnoreCase);
                if (match >= 0)
                {
                    title ??= TitleOf(text);
                    var (snippet, offset) = MakeSnippet(text, lineStart, lineEnd, match, query.Length);
                    hits.Add(new SearchHit(n, match, query.Length, snippet, offset, title));
                }
                lineStart = lineEnd + 1;
            }
        }
        return hits;
    }

    /// <summary>First non-empty line, without a todo marker; "(empty note)" if there is none.</summary>
    public static string TitleOf(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length >= 3 && line[0] == '[' && line[2] == ']' && line[1] is ' ' or 'x' or 'X')
                line = line[3..].Trim();
            if (line.Length == 0) continue;
            return line.Length > 40 ? line[..40].TrimEnd() + "…" : line;
        }
        return "(empty note)";
    }

    private static (string Snippet, int MatchOffset) MakeSnippet(string text, int lineStart, int lineEnd, int match, int length)
    {
        // Trim leading indentation (tabs/spaces) so indented lines don't start with a gap.
        int start = lineStart;
        while (start < match && (text[start] == ' ' || text[start] == '\t')) start++;
        int end = lineEnd;
        while (end > match + length && char.IsWhiteSpace(text[end - 1])) end--;

        if (end - start <= MaxSnippetLength)
            return (text[start..end], match - start);

        int from = Math.Max(start, match - ContextBefore);
        int to = Math.Min(end, from + MaxSnippetLength);
        string prefix = from > start ? "…" : "";
        string suffix = to < end ? "…" : "";
        return (prefix + text[from..to] + suffix, match - from + prefix.Length);
    }
}
