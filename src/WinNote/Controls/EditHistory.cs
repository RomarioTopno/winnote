namespace WinNote.Controls;

/// <summary>
/// Per-note undo/redo of whole-text snapshots. Notes are small, so snapshots are cheap and make
/// undo independent of RichEditBox formatting (which we apply for todo styling, and which the
/// built-in undo stack would otherwise record).
/// Consecutive typing is grouped: a new undo step starts after a pause, after ~4 s of continuous
/// typing, on a new line, or on any command (paste, todo toggle, ...).
/// </summary>
internal sealed class EditHistory
{
    private const int MaxSteps = 200;
    private const long PauseMs = 1000, MaxGroupMs = 4000;

    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private long _lastEditTicks, _groupStartTicks;
    private bool _breakGroup = true;

    public string Current { get; private set; }

    /// <summary>Caret position to restore when the note is shown again (in-memory only).</summary>
    public int Caret { get; set; } = -1;

    public EditHistory(string text) => Current = text;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records a change from <see cref="Current"/> to <paramref name="text"/>.</summary>
    public void Record(string text, bool isCommand)
    {
        if (text == Current) return;
        long now = Environment.TickCount64;

        bool newLine = text.Length > Current.Length && CountNewlines(text) > CountNewlines(Current);
        bool startNewGroup = _breakGroup || isCommand || newLine
            || now - _lastEditTicks > PauseMs || now - _groupStartTicks > MaxGroupMs;

        if (startNewGroup)
        {
            _undo.Add(Current);
            if (_undo.Count > MaxSteps) _undo.RemoveAt(0);
            _groupStartTicks = now;
        }
        _redo.Clear();
        Current = text;
        _lastEditTicks = now;
        // A command or newline is its own step; the next keystroke starts fresh.
        _breakGroup = isCommand || newLine;
    }

    /// <summary>Accepts a text the editor produced itself (e.g. load normalisation) without an undo step.</summary>
    public void Reset(string text) => Current = text;

    public string? Undo()
    {
        if (_undo.Count == 0) return null;
        _redo.Add(Current);
        Current = Pop(_undo);
        _breakGroup = true;
        return Current;
    }

    public string? Redo()
    {
        if (_redo.Count == 0) return null;
        _undo.Add(Current);
        Current = Pop(_redo);
        _breakGroup = true;
        return Current;
    }

    private static string Pop(List<string> list)
    {
        var s = list[^1];
        list.RemoveAt(list.Count - 1);
        return s;
    }

    private static int CountNewlines(string s)
    {
        int n = 0;
        foreach (char c in s) if (c == '\r') n++;
        return n;
    }

    /// <summary>End of the region where <paramref name="after"/> differs from <paramref name="before"/>: where the caret belongs after undo/redo.</summary>
    public static int ChangedRegionEnd(string before, string after)
    {
        int prefix = 0, max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        return after.Length - suffix;
    }
}
