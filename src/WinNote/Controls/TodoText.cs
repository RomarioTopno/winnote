namespace WinNote.Controls;

/// <summary>
/// Pure text rules for todo lines, working on editor text ('\r' line separators).
/// A todo line is: optional indent (spaces/tabs), then "[ ]" or "[x]"/"[X]", then a space or end of line.
/// </summary>
internal static class TodoText
{
    public const char Sep = '\r';

    public readonly record struct Line(int Start, int End)
    {
        public int Length => End - Start;
    }

    /// <param name="MarkerStart">Index of '['.</param>
    /// <param name="ContentStart">First index after "[ ] " (or line end if the line is just "[ ]").</param>
    public readonly record struct Todo(int MarkerStart, bool IsChecked, int ContentStart)
    {
        public int MarkerEnd => MarkerStart + 3; // exclusive, after ']'
    }

    public static Line LineAt(string text, int position)
    {
        position = Math.Clamp(position, 0, text.Length);
        int start = position == 0 ? 0 : text.LastIndexOf(Sep, position - 1) + 1;
        int end = text.IndexOf(Sep, position);
        return new Line(start, end < 0 ? text.Length : end);
    }

    public static IEnumerable<Line> Lines(string text)
    {
        int start = 0;
        while (true)
        {
            int end = text.IndexOf(Sep, start);
            if (end < 0)
            {
                yield return new Line(start, text.Length);
                yield break;
            }
            yield return new Line(start, end);
            start = end + 1;
        }
    }

    private static int IndentEnd(string text, Line line)
    {
        int i = line.Start;
        while (i < line.End && (text[i] == ' ' || text[i] == '\t')) i++;
        return i;
    }

    public static bool TryParse(string text, Line line, out Todo todo)
    {
        todo = default;
        int i = IndentEnd(text, line);
        if (i + 3 > line.End || text[i] != '[' || text[i + 2] != ']') return false;
        char state = text[i + 1];
        if (state is not (' ' or 'x' or 'X')) return false;
        if (i + 3 < line.End && text[i + 3] != ' ') return false;
        todo = new Todo(i, state != ' ', Math.Min(i + 4, line.End));
        return true;
    }

    public static bool TryParseAt(string text, int position, out Line line, out Todo todo)
    {
        line = LineAt(text, position);
        return TryParse(text, line, out todo);
    }

    public static string Toggle(string text, Todo todo) =>
        string.Concat(text.AsSpan(0, todo.MarkerStart + 1), todo.IsChecked ? " " : "x", text.AsSpan(todo.MarkerStart + 2));

    /// <summary>Turns a plain line into an unchecked todo (marker goes after the indent).</summary>
    public static string MakeTodo(string text, Line line, out int insertedAt)
    {
        insertedAt = IndentEnd(text, line);
        return text.Insert(insertedAt, "[ ] ");
    }

    /// <summary>Removes the marker (and its trailing space) from a todo line, keeping the indent.</summary>
    public static string RemoveMarker(string text, Todo todo) =>
        text.Remove(todo.MarkerStart, todo.ContentStart - todo.MarkerStart);

    /// <summary>
    /// Shorthands typed at the start of a line: "[] ", "- [ ] ", "* [ ] " become "[ ] ",
    /// "- [x] " / "* [x] " become "[x] ". <paramref name="caret"/> must be right after the typed space.
    /// </summary>
    public static string? AutoConvert(string text, int caret, out int newCaret)
    {
        newCaret = caret;
        var line = LineAt(text, caret);
        int indentEnd = IndentEnd(text, line);
        if (caret <= indentEnd) return null;

        string typed = text[indentEnd..caret];
        string? replacement = typed switch
        {
            "[] " => "[ ] ",
            "- [ ] " or "* [ ] " or "- [] " or "* [] " => "[ ] ",
            "- [x] " or "* [x] " or "- [X] " or "* [X] " => "[x] ",
            _ => null,
        };
        if (replacement is null) return null;
        newCaret = indentEnd + replacement.Length;
        return string.Concat(text.AsSpan(0, indentEnd), replacement, text.AsSpan(caret));
    }

    public static bool HasCompleted(string text)
    {
        foreach (var line in Lines(text))
            if (TryParse(text, line, out var todo) && todo.IsChecked) return true;
        return false;
    }

    /// <summary>Deletes every checked todo line.</summary>
    public static string RemoveCompleted(string text)
    {
        var kept = new List<string>();
        foreach (var line in Lines(text))
            if (!(TryParse(text, line, out var todo) && todo.IsChecked))
                kept.Add(text[line.Start..line.End]);
        return string.Join(Sep, kept);
    }
}
