using System.Text;

namespace WinNote.Services;

/// <summary>
/// Converts a note to a standalone Markdown document:
/// - todo lines "[ ] x" / "[x] x" become GitHub task list items "- [ ] x" / "- [x] x";
/// - leading tabs (sub-todo levels) become 2 spaces each, i.e. nested list items;
/// - consecutive plain text lines keep their line breaks (two trailing spaces), because Markdown
///   would otherwise join them into one paragraph.
/// Everything else (headings, emphasis, links, code, ...) is already Markdown and is kept as is.
/// </summary>
public static class MarkdownExport
{
    public static string FromNote(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var output = new List<string>(lines.Length);
        var isCode = new List<bool>(lines.Length); // fence lines and code: never get hard breaks
        bool inFence = false;

        foreach (string raw in lines)
        {
            string trimmedStart = raw.TrimStart(' ', '\t');
            if (trimmedStart.StartsWith("```") || trimmedStart.StartsWith("~~~"))
            {
                inFence = !inFence;
                output.Add(raw);
                isCode.Add(true);
                continue;
            }
            if (inFence)
            {
                output.Add(raw); // code blocks stay verbatim
                isCode.Add(true);
                continue;
            }

            string indent = ConvertIndent(raw[..(raw.Length - trimmedStart.Length)]);
            string body = trimmedStart;
            if (body.Length >= 3 && body[0] == '[' && body[2] == ']' && body[1] is ' ' or 'x' or 'X'
                && (body.Length == 3 || body[3] == ' '))
            {
                string state = body[1] == ' ' ? " " : "x";
                string rest = body.Length > 4 ? body[4..] : "";
                body = $"- [{state}] {rest}".TrimEnd();
            }
            output.Add(indent + body);
            isCode.Add(false);
        }

        // Keep single line breaks inside paragraphs of plain text.
        for (int i = 0; i < output.Count - 1; i++)
        {
            if (!isCode[i] && !isCode[i + 1] && IsPlainParagraphLine(output[i]) && IsPlainParagraphLine(output[i + 1]))
                output[i] = output[i].TrimEnd() + "  ";
        }
        return string.Join("\n", output);
    }

    /// <summary>Leading whitespace with tabs as 2 spaces (one nesting level each).</summary>
    private static string ConvertIndent(string indent)
    {
        var sb = new StringBuilder();
        foreach (char c in indent) sb.Append(c == '\t' ? "  " : " ");
        return sb.ToString();
    }

    /// <summary>A line that Markdown would merge with the next one (not blank, not a block element).</summary>
    private static bool IsPlainParagraphLine(string line)
    {
        string t = line.TrimStart();
        if (t.Length == 0) return false;
        if (t[0] is '#' or '>' or '|') return false;
        if (t.StartsWith("```") || t.StartsWith("~~~")) return false;
        if ((t[0] is '-' or '*' or '+') && (t.Length == 1 || t[1] == ' ')) return false;
        int d = 0;
        while (d < t.Length && char.IsAsciiDigit(t[d])) d++;
        if (d > 0 && d + 1 < t.Length && t[d] is '.' or ')' && t[d + 1] == ' ') return false;
        string compact = t.Replace(" ", "");
        if (compact.Length >= 3 && compact.All(ch => ch == compact[0]) && compact[0] is '-' or '*' or '_' or '=') return false;
        return true;
    }
}
