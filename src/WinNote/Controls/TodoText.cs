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

    /// <summary>Deletes every checked todo line together with its sub-items (the deeper-indented lines below it).</summary>
    public static string RemoveCompleted(string text)
    {
        var kept = new List<string>();
        int skipDeeperThan = -1; // inside a removed item's subtree: skip lines indented deeper than this
        foreach (var line in Lines(text))
        {
            int indent = IndentWidth(text, line);
            if (skipDeeperThan >= 0)
            {
                if (!IsBlank(text, line) && indent > skipDeeperThan) continue;
                skipDeeperThan = -1;
            }
            if (TryParse(text, line, out var todo) && todo.IsChecked)
            {
                skipDeeperThan = indent;
                continue;
            }
            kept.Add(text[line.Start..line.End]);
        }
        return string.Join(Sep, kept);
    }

    // ---- Indentation / sub-todos ----

    public const int TabWidth = 4; // columns, for comparing tab and space indents

    /// <summary>Indent of a line in columns (a tab advances to the next multiple of <see cref="TabWidth"/>).</summary>
    public static int IndentWidth(string text, Line line)
    {
        int width = 0;
        for (int i = line.Start; i < line.End; i++)
        {
            if (text[i] == ' ') width++;
            else if (text[i] == '\t') width = (width / TabWidth + 1) * TabWidth;
            else break;
        }
        return width;
    }

    public static bool IsBlank(string text, Line line)
    {
        for (int i = line.Start; i < line.End; i++)
            if (!char.IsWhiteSpace(text[i])) return false;
        return true;
    }

    /// <summary>The leading whitespace of a line (to repeat it on the next line).</summary>
    public static string LeadingWhitespace(string text, Line line)
    {
        int i = line.Start;
        while (i < line.End && (text[i] == ' ' || text[i] == '\t')) i++;
        return text[line.Start..i];
    }

    /// <summary>Lines touched by a selection. A selection ending exactly at a line start doesn't include that line.</summary>
    public static List<Line> LinesInSelection(string text, int start, int end)
    {
        var result = new List<Line>();
        var line = LineAt(text, start);
        while (true)
        {
            result.Add(line);
            if (line.End >= end || line.End >= text.Length) break;
            var next = LineAt(text, line.End + 1);
            if (next.Start >= end && end > start) break;
            line = next;
        }
        return result;
    }

    /// <summary>Adds one tab at the start of every selected line (makes todos into sub-todos).</summary>
    public static string IndentLines(string text, ref int selStart, ref int selEnd)
    {
        var edits = LinesInSelection(text, selStart, selEnd).Select(l => (Pos: l.Start, Remove: 0, Insert: "\t")).ToList();
        return ApplyEdits(text, edits, ref selStart, ref selEnd);
    }

    /// <summary>Removes one indent level (a tab, or up to <see cref="TabWidth"/> spaces) from every selected line.</summary>
    public static string OutdentLines(string text, ref int selStart, ref int selEnd)
    {
        var edits = new List<(int Pos, int Remove, string Insert)>();
        foreach (var line in LinesInSelection(text, selStart, selEnd))
        {
            int remove = 0;
            if (line.Start < line.End && text[line.Start] == '\t') remove = 1;
            else while (remove < TabWidth && line.Start + remove < line.End && text[line.Start + remove] == ' ') remove++;
            if (remove > 0) edits.Add((line.Start, remove, ""));
        }
        return ApplyEdits(text, edits, ref selStart, ref selEnd);
    }

    /// <summary>
    /// Toggles a todo and gives its sub-todos (following lines indented deeper, up to the first blank
    /// line or a line at the same or a shallower level) the same state.
    /// </summary>
    public static string ToggleWithChildren(string text, Line line, Todo todo)
    {
        bool check = !todo.IsChecked;
        var chars = text.ToCharArray();
        chars[todo.MarkerStart + 1] = check ? 'x' : ' ';

        int parentIndent = IndentWidth(text, line);
        int pos = line.End + 1;
        while (pos <= text.Length)
        {
            var child = LineAt(text, pos);
            if (IsBlank(text, child) || IndentWidth(text, child) <= parentIndent) break;
            if (TryParse(text, child, out var childTodo)) chars[childTodo.MarkerStart + 1] = check ? 'x' : ' ';
            if (child.End >= text.Length) break;
            pos = child.End + 1;
        }
        return new string(chars);
    }

    /// <summary>Applies non-overlapping edits and maps a selection through them.</summary>
    private static string ApplyEdits(string text, List<(int Pos, int Remove, string Insert)> edits, ref int selStart, ref int selEnd)
    {
        if (edits.Count == 0) return text;
        int Map(int p)
        {
            int delta = 0;
            foreach (var e in edits)
            {
                if (e.Pos > p) break;
                if (e.Remove == 0) { delta += e.Insert.Length; continue; }          // insert at/before p: shift
                if (p < e.Pos + e.Remove) return e.Pos + delta;                    // inside a removed span
                delta += e.Insert.Length - e.Remove;
            }
            return p + delta;
        }
        int newStart = Map(selStart), newEnd = Map(selEnd);

        var sb = new System.Text.StringBuilder(text.Length + edits.Count);
        int last = 0;
        foreach (var e in edits)
        {
            sb.Append(text, last, e.Pos - last).Append(e.Insert);
            last = e.Pos + e.Remove;
        }
        sb.Append(text, last, text.Length - last);

        (selStart, selEnd) = (newStart, newEnd);
        return sb.ToString();
    }
}
