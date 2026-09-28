using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace WinNote.Controls;

// Todo lines: "[ ] task" / "[x] done". The text stays plain; we only colour the marker,
// strike through checked items, and make the marker clickable.
public sealed partial class NoteEditor
{
    private static readonly UISettings s_uiSettings = new();
    private (Color Text, Color Dim, Color Accent) _palette;
    private bool _composing;      // IME composition in progress: don't touch formatting
    private bool _pointerDownOnText;
    private Windows.Foundation.Point _pointerDownAt;
    private float? _uncheckedPad; // extra spacing (pt) so "[ ]" is as wide as "[x]"

    private void InitializeTodos()
    {
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressedAny), handledEventsToo: true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleasedAny), handledEventsToo: true);
        TextCompositionStarted += (_, _) => _composing = true;
        TextCompositionEnded += (_, _) => { _composing = false; Restyle(); };
        ActualThemeChanged += (_, _) => { UpdatePalette(); Restyle(); };
        s_uiSettings.ColorValuesChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => { UpdatePalette(); Restyle(); });
        RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => { _uncheckedPad = null; Restyle(); });
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => { _uncheckedPad = null; Restyle(); });
        UpdatePalette();
    }

    /// <summary>
    /// RichEditBox renders every run in the control's font (per-run font names are ignored), so
    /// markers can't be monospace. Instead widen the space in "[ ]" to match "[x]", so item text
    /// lines up whether an item is checked or not.
    /// </summary>
    private float UncheckedPad()
    {
        if (_uncheckedPad is { } pad) return pad;
        var probe = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeights.Bold,
        };
        double Width(string s)
        {
            probe.Text = s;
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        }
        double diffDip = Width("[x]") - Width("[ ]");
        _uncheckedPad = pad = (float)Math.Max(0, diffDip * 72.0 / 96.0); // DIPs → points
        return pad;
    }

    private void UpdatePalette()
    {
        bool dark = ActualTheme == ElementTheme.Dark;
        // RichEdit colours are opaque, so these are Fluent's primary/tertiary text colours
        // pre-blended over a typical Mica background.
        _palette = dark
            ? (Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 150, 150, 150), s_uiSettings.GetColorValue(UIColorType.AccentLight2))
            : (Color.FromArgb(255, 27, 27, 27), Color.FromArgb(255, 135, 135, 135), s_uiSettings.GetColorValue(UIColorType.AccentDark1));
    }

    /// <summary>Re-applies all display formatting. Formatting is never part of the saved text.</summary>
    internal void Restyle()
    {
        if (_composing) return;
        string text = EditorText;
        var doc = Document;
        doc.BatchDisplayUpdates();
        try
        {
            // Reset everything (this also strips formatting that arrived via drag & drop).
            var all = doc.GetRange(0, text.Length + 1).CharacterFormat;
            all.ForegroundColor = _palette.Text;
            all.Strikethrough = FormatEffect.Off;
            all.Bold = FormatEffect.Off;
            all.Italic = FormatEffect.Off;
            all.Underline = UnderlineType.None;
            all.Spacing = 0;

            foreach (var line in TodoText.Lines(text))
            {
                if (!TodoText.TryParse(text, line, out var todo)) continue;

                var marker = doc.GetRange(todo.MarkerStart, todo.MarkerEnd).CharacterFormat;
                marker.ForegroundColor = todo.IsChecked ? _palette.Dim : _palette.Accent;
                marker.Bold = FormatEffect.On;
                if (!todo.IsChecked)
                    doc.GetRange(todo.MarkerStart + 1, todo.MarkerStart + 2).CharacterFormat.Spacing = UncheckedPad();

                if (todo.IsChecked && todo.ContentStart < line.End)
                {
                    var content = doc.GetRange(todo.ContentStart, line.End).CharacterFormat;
                    content.ForegroundColor = _palette.Dim;
                    content.Strikethrough = FormatEffect.On;
                }
            }
        }
        finally
        {
            doc.ApplyDisplayUpdates();
        }
    }

    /// <summary>Called for every recorded text change with the text before it.</summary>
    private void OnTextEdited(string before, string after)
    {
        // "[] " / "- [ ] " typed at a line start â†’ "[ ] ". Only for a single typed space.
        int caret = Document.Selection.StartPosition;
        if (after.Length == before.Length + 1 && caret > 0 && caret <= after.Length && after[caret - 1] == ' '
            && TodoText.AutoConvert(after, caret, out int newCaret) is { } converted)
        {
            // Defer: don't rewrite the document from inside its own change notification.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (EditorText == after) ApplyCommand(converted, newCaret, newCaret);
            });
        }
    }

    // ---- Keyboard ----

    /// <summary>Handles Enter / Backspace / Ctrl+Enter on todo lines. Returns true if handled.</summary>
    private bool HandleTodoKey(VirtualKey key, bool ctrl, bool shift)
    {
        var sel = Document.Selection;
        int start = sel.StartPosition, end = sel.EndPosition;
        string text = EditorText;

        if (key == VirtualKey.Enter && ctrl && !shift)
        {
            ToggleOrMakeTodo(text, start, end);
            return true;
        }

        if (!TodoText.TryParseAt(text, start, out var line, out var todo)) return false;
        bool caretInContent = start >= todo.ContentStart && end <= line.End;

        if (key == VirtualKey.Enter && !ctrl && !shift && caretInContent)
        {
            if (start == end && string.IsNullOrWhiteSpace(text[todo.ContentStart..line.End]))
            {
                // Enter on an empty todo ends the list: the line becomes empty.
                ApplyCommand(text.Remove(line.Start, line.Length), line.Start, line.Start);
            }
            else
            {
                // Continue the list with the same indent.
                InsertAtSelection("\r" + text[line.Start..todo.MarkerStart] + "[ ] ");
            }
            return true;
        }

        if (key == VirtualKey.Back && !ctrl && start == end && start == todo.ContentStart)
        {
            // Backspace right after the marker removes the marker, not just the space.
            ApplyCommand(TodoText.RemoveMarker(text, todo), todo.MarkerStart, todo.MarkerStart);
            return true;
        }
        return false;
    }

    /// <summary>Ctrl+Enter: toggles todo lines in the selection; turns a plain line into a todo.</summary>
    private void ToggleOrMakeTodo(string text, int start, int end)
    {
        if (TodoText.TryParseAt(text, start, out var line, out var todo))
        {
            ApplyCommand(TodoText.Toggle(text, todo), start, end);
        }
        else
        {
            string updated = TodoText.MakeTodo(text, line, out int at);
            int shift = 4;
            ApplyCommand(updated, start >= at ? start + shift : start, end >= at ? end + shift : end);
        }
    }

    // ---- Clicking the marker ----

    private void OnPointerPressedAny(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        _pointerDownOnText = point.Properties.IsLeftButtonPressed && e.KeyModifiers == VirtualKeyModifiers.None;
        _pointerDownAt = point.Position;
    }

    private void OnPointerReleasedAny(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerDownOnText) return;
        _pointerDownOnText = false;

        var up = e.GetCurrentPoint(this).Position;
        if (Math.Abs(up.X - _pointerDownAt.X) > 4 || Math.Abs(up.Y - _pointerDownAt.Y) > 4) return; // drag-select

        // RichEditBox has already moved the caret to the click position.
        var sel = Document.Selection;
        if (sel.Length != 0) return;
        int caret = sel.StartPosition;
        string text = EditorText;
        if (TodoText.TryParseAt(text, caret, out _, out var todo) && caret >= todo.MarkerStart && caret <= todo.MarkerEnd)
            ApplyCommand(TodoText.Toggle(text, todo), caret, caret);
    }

    // ---- Commands for the window ----

    public bool HasCompletedTodos => TodoText.HasCompleted(EditorText);

    public void ClearCompletedTodos()
    {
        string text = EditorText;
        string updated = TodoText.RemoveCompleted(text);
        int caret = Math.Min(Document.Selection.StartPosition, updated.Length);
        ApplyCommand(updated, caret, caret);
    }
}
