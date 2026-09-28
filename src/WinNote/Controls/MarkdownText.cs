namespace WinNote.Controls;

public enum MdKind
{
    Marker,         // syntax characters (**, *, ~~, `, #, >, brackets, ...): shown dimmed
    Heading,        // heading text; Level = 1..6
    Bold,
    Italic,
    Strike,
    Code,           // inline code or a line inside a fenced block
    LinkText,       // [text](..) text, or a bare URL; Url set
    LinkUrl,        // the (url) part of [text](url); Url set
    Quote,          // text of a "> " line
    ListMarker,     // "-", "*", "+" or "1." starting a list item
    Rule,           // --- / *** / ___ line
}

/// <param name="Start">Index in the text (editor positions: '\r' or '\n' separated, 1 char each).</param>
public readonly record struct MdSpan(int Start, int Length, MdKind Kind, int Level = 0, string? Url = null)
{
    public int End => Start + Length;
}

/// <summary>
/// Finds Markdown constructs for live formatting. The text is never changed; spans only drive
/// display formatting. Covers the common subset: ATX headings, bold/italic/strike, inline and
/// fenced code, links and bare URLs, block quotes, bullet/numbered list markers, rules.
/// </summary>
public static class MarkdownText
{
    public static List<MdSpan> Parse(string text)
    {
        var spans = new List<MdSpan>();
        bool inFence = false;
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            int lineEnd = text.IndexOfAny(['\r', '\n'], lineStart);
            if (lineEnd < 0) lineEnd = text.Length;
            ParseLine(text, lineStart, lineEnd, spans, ref inFence);
            lineStart = lineEnd + 1;
        }
        return spans;
    }

    private static void ParseLine(string text, int start, int end, List<MdSpan> spans, ref bool inFence)
    {
        int i = start;
        while (i < end && (text[i] == ' ' || text[i] == '\t')) i++;
        string trimmed = text[i..end].TrimEnd();

        // Fenced code blocks: ``` / ~~~ lines toggle; everything inside is code, nothing else parsed.
        if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
        {
            spans.Add(new MdSpan(i, end - i, MdKind.Marker));
            inFence = !inFence;
            return;
        }
        if (inFence)
        {
            if (end > start) spans.Add(new MdSpan(start, end - start, MdKind.Code));
            return;
        }
        if (i >= end) return;

        // Horizontal rule: 3+ of the same -, * or _ (spaces allowed), nothing else.
        if (IsRule(trimmed))
        {
            spans.Add(new MdSpan(i, end - i, MdKind.Rule));
            return;
        }

        // Heading: 1-6 '#' then a space.
        int hashes = 0;
        while (i + hashes < end && text[i + hashes] == '#') hashes++;
        if (hashes is >= 1 and <= 6 && (i + hashes == end || text[i + hashes] == ' '))
        {
            int content = Math.Min(end, i + hashes + 1);
            spans.Add(new MdSpan(i, content - i, MdKind.Marker));
            if (content < end) spans.Add(new MdSpan(content, end - content, MdKind.Heading, hashes));
            ParseInline(text, content, end, spans);
            return;
        }

        // Block quote: "> text" (possibly nested "> > ").
        if (text[i] == '>')
        {
            int content = i;
            while (content < end && (text[content] == '>' || text[content] == ' ')) content++;
            spans.Add(new MdSpan(i, content - i, MdKind.Marker));
            if (content < end) spans.Add(new MdSpan(content, end - content, MdKind.Quote));
            ParseInline(text, content, end, spans);
            return;
        }

        // List item: "- ", "* ", "+ " or "12. " / "12) ".
        int contentStart = i;
        if ((text[i] is '-' or '*' or '+') && i + 1 < end && text[i + 1] == ' ')
        {
            spans.Add(new MdSpan(i, 1, MdKind.ListMarker));
            contentStart = i + 2;
        }
        else
        {
            int d = i;
            while (d < end && char.IsAsciiDigit(text[d]) && d - i < 9) d++;
            if (d > i && d + 1 < end && (text[d] is '.' or ')') && text[d + 1] == ' ')
            {
                spans.Add(new MdSpan(i, d + 1 - i, MdKind.ListMarker));
                contentStart = d + 2;
            }
        }
        ParseInline(text, contentStart, end, spans);
    }

    private static bool IsRule(string s)
    {
        if (s.Length < 3) return false;
        char c = s[0];
        if (c is not ('-' or '*' or '_')) return false;
        int count = 0;
        foreach (char ch in s)
        {
            if (ch == c) count++;
            else if (ch != ' ') return false;
        }
        return count >= 3;
    }

    /// <summary>Inline constructs inside [start, end).</summary>
    private static void ParseInline(string text, int start, int end, List<MdSpan> spans)
    {
        int i = start;
        while (i < end)
        {
            char c = text[i];

            // Inline code: `code` (content is not parsed further).
            if (c == '`')
            {
                int close = text.IndexOf('`', i + 1, end - i - 1);
                if (close > i + 1)
                {
                    spans.Add(new MdSpan(i, 1, MdKind.Marker));
                    spans.Add(new MdSpan(i + 1, close - i - 1, MdKind.Code));
                    spans.Add(new MdSpan(close, 1, MdKind.Marker));
                    i = close + 1;
                    continue;
                }
            }

            // Link: [text](url)
            if (c == '[')
            {
                int closeText = text.IndexOf(']', i + 1, end - i - 1);
                if (closeText > i + 1 && closeText + 1 < end && text[closeText + 1] == '(')
                {
                    int closeUrl = text.IndexOf(')', closeText + 2, end - closeText - 2);
                    if (closeUrl > closeText + 2)
                    {
                        string url = text[(closeText + 2)..closeUrl].Trim();
                        spans.Add(new MdSpan(i, 1, MdKind.Marker));
                        spans.Add(new MdSpan(i + 1, closeText - i - 1, MdKind.LinkText, Url: url));
                        spans.Add(new MdSpan(closeText, 2, MdKind.Marker));
                        spans.Add(new MdSpan(closeText + 2, closeUrl - closeText - 2, MdKind.LinkUrl, Url: url));
                        spans.Add(new MdSpan(closeUrl, 1, MdKind.Marker));
                        i = closeUrl + 1;
                        continue;
                    }
                }
            }

            // Bare URL: http(s)://... up to whitespace (trailing punctuation excluded).
            if ((c == 'h' || c == 'H') && (i == start || !char.IsLetterOrDigit(text[i - 1])) && StartsWithUrl(text, i, end))
            {
                int e = i;
                while (e < end && !char.IsWhiteSpace(text[e])) e++;
                while (e > i && text[e - 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or '"' or '\'') e--;
                spans.Add(new MdSpan(i, e - i, MdKind.LinkText, Url: text[i..e]));
                i = e;
                continue;
            }

            // Emphasis: ***x*** / **x** / __x__ / ~~x~~ / *x* / _x_
            if (c is '*' or '_' or '~')
            {
                int run = 1;
                while (i + run < end && text[i + run] == c && run < 3) run++;
                if (c == '~' && run < 2) { i++; continue; }
                if (c == '~') run = 2;

                if (TryEmphasis(text, i, end, c, run, spans, out int next))
                {
                    i = next;
                    continue;
                }
                i += run;
                continue;
            }
            i++;
        }
    }

    private static bool StartsWithUrl(string text, int i, int end) =>
        (end - i > 8 && string.Compare(text, i, "https://", 0, 8, StringComparison.OrdinalIgnoreCase) == 0) ||
        (end - i > 7 && string.Compare(text, i, "http://", 0, 7, StringComparison.OrdinalIgnoreCase) == 0);

    /// <summary>Opening delimiter at i (length run); finds the matching closer and emits spans.</summary>
    private static bool TryEmphasis(string text, int i, int end, char c, int run, List<MdSpan> spans, out int next)
    {
        next = i;
        int innerStart = i + run;
        // Opener must be followed by non-space; '_' must not be inside a word.
        if (innerStart >= end || char.IsWhiteSpace(text[innerStart])) return false;
        if (c == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1])) return false;

        string delim = new string(c, run);
        int search = innerStart + 1;
        while (search <= end - run)
        {
            int close = text.IndexOf(delim, search, end - search, StringComparison.Ordinal);
            if (close < 0) return false;
            bool closerOk = !char.IsWhiteSpace(text[close - 1])
                && (close + run >= end || text[close + run] != c)            // not part of a longer run
                && (c != '_' || close + run >= end || !char.IsLetterOrDigit(text[close + run]));
            if (closerOk)
            {
                spans.Add(new MdSpan(i, run, MdKind.Marker));
                if (c == '~') spans.Add(new MdSpan(innerStart, close - innerStart, MdKind.Strike));
                else
                {
                    if (run >= 2) spans.Add(new MdSpan(innerStart, close - innerStart, MdKind.Bold));
                    if (run is 1 or 3) spans.Add(new MdSpan(innerStart, close - innerStart, MdKind.Italic));
                }
                ParseInline(text, innerStart, close, spans); // nested emphasis, code, links
                spans.Add(new MdSpan(close, run, MdKind.Marker));
                next = close + run;
                return true;
            }
            search = close + 1;
        }
        return false;
    }

    /// <summary>The link (URL) at a text position, for Ctrl+Click; null if none or not a web/mail link.</summary>
    public static string? LinkAt(string text, int position)
    {
        foreach (var span in Parse(text))
        {
            if (span.Kind is MdKind.LinkText or MdKind.LinkUrl && span.Url is { } url
                && position >= span.Start && position <= span.End && IsOpenableUrl(url))
                return url;
        }
        return null;
    }

    public static bool IsOpenableUrl(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
}
