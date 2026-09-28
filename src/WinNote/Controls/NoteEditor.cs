using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using WinNote.Services;

namespace WinNote.Controls;

/// <summary>
/// RichEditBox locked down to plain text: pastes are inserted as plain text, formatting shortcuts
/// are swallowed, and undo/redo uses <see cref="EditHistory"/> instead of the built-in stack.
/// Internally the document uses '\r' as line separator (RichEdit's native form); <see cref="PlainText"/>
/// converts to '\n'. Character indices are identical in both forms.
/// </summary>
public sealed partial class NoteEditor : RichEditBox
{
    private EditHistory _history = new("");
    private bool _commandPending; // next text change is a command (own undo step)
    private bool _applying;       // we're applying undo/redo; don't record

    /// <summary>Raised after any change of the text (typing, paste, undo, commands).</summary>
    public event EventHandler? UserTextChanged;

    public NoteEditor()
    {
        AcceptsReturn = true;
        TextWrapping = TextWrapping.Wrap;
        IsSpellCheckEnabled = false;
        IsTextPredictionEnabled = false;
        DisabledFormattingAccelerators = DisabledFormattingAccelerators.All;
        ClipboardCopyFormat = RichEditClipboardFormat.PlainText;
        SelectionFlyout = null; // the floating mini toolbar offers bold/italic
        ContextFlyout = BuildContextMenu();

        TextChanged += OnTextChanged;
        SelectionChanged += (_, _) => _history.Caret = Document.Selection.EndPosition;
        Paste += OnPaste;
        PreviewKeyDown += OnPreviewKeyDown;
        Document.UndoLimit = 0; // we own undo
        InitializeTodos();
    }

    /// <summary>Every text change ends here: refresh todo styling, then notify the window (autosave).</summary>
    private void RaiseTextChanged()
    {
        Restyle();
        UserTextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Editor text with '\r' separators (no trailing paragraph mark).</summary>
    internal string EditorText
    {
        get
        {
            Document.GetText(TextGetOptions.None, out string text);
            if (text.EndsWith('\r')) text = text[..^1];
            return text.Replace('\v', '\r');
        }
    }

    /// <summary>Text with '\n' line endings, as stored.</summary>
    public string PlainText => EditorText.Replace('\r', '\n');

    internal EditHistory History => _history;

    /// <summary>Shows a note: its text and its undo history.</summary>
    internal void Load(EditHistory history)
    {
        _history = history;
        _applying = true;
        Document.SetText(TextSetOptions.None, history.Current);
        _applying = false;
        history.Reset(EditorText);
        int caret = history.Caret < 0 ? history.Current.Length : Math.Min(history.Caret, history.Current.Length);
        Document.Selection.SetRange(caret, caret);
        history.Caret = caret;
        Restyle();
    }

    internal static string ToEditorText(string plain) => plain.Replace('\n', '\r');

    public void Undo() => ApplyHistory(_history.Undo());

    public void Redo() => ApplyHistory(_history.Redo());

    private void ApplyHistory(string? target)
    {
        if (target is null) return;
        string before = EditorText;
        _applying = true;
        ApplyDiff(before, target);
        _applying = false;
        _history.Reset(EditorText);
        int caret = EditHistory.ChangedRegionEnd(before, target);
        Document.Selection.SetRange(caret, caret);
        RaiseTextChanged();
    }

    /// <summary>Replaces the whole text as a single undoable command, then sets the selection.</summary>
    internal void ApplyCommand(string newText, int selStart, int selEnd)
    {
        string before = EditorText;
        if (before == newText) return;
        _commandPending = true;
        ApplyDiff(before, newText);
        _history.Record(EditorText, isCommand: true);
        _commandPending = false;
        Document.Selection.SetRange(selStart, selEnd);
        RaiseTextChanged();
    }

    /// <summary>Rewrites only the span that differs, so scroll position and caret stay stable.</summary>
    private void ApplyDiff(string before, string after)
    {
        int prefix = 0, max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;

        var range = Document.GetRange(prefix, before.Length - suffix);
        range.SetText(TextSetOptions.None, after.Substring(prefix, after.Length - suffix - prefix));
    }

    private void OnTextChanged(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        string text = EditorText;
        if (text == _history.Current) return; // formatting-only change or already recorded
        string before = _history.Current;
        _history.Record(text, _commandPending);
        _commandPending = false;
        RaiseTextChanged();
        OnTextEdited(before, text);
    }

    private void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true; // never let rich content in
        PastePlainText();
    }

    public async void PastePlainText()
    {
        try
        {
            var view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text)) return;
            string text = await view.GetTextAsync();
            InsertAtSelection(NoteStore.NormalizeNewlines(text).Replace('\n', '\r'));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Paste failed: {ex.Message}");
        }
    }

    /// <summary>Replaces the selection with <paramref name="text"/> as one undo step; caret ends after it.</summary>
    internal void InsertAtSelection(string text)
    {
        _commandPending = true;
        var sel = Document.Selection;
        sel.SetText(TextSetOptions.None, text);
        sel.Collapse(false);
        // TextChanged may be raised asynchronously; record now so the step is exact.
        _history.Record(EditorText, isCommand: true);
        _commandPending = false;
        RaiseTextChanged();
    }

    internal static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsKeyDown(VirtualKey.Control);
        bool shift = IsKeyDown(VirtualKey.Shift);
        bool alt = IsKeyDown(VirtualKey.Menu); // Ctrl+Alt is AltGr on many layouts: leave it alone

        if (!alt && e.Key is VirtualKey.Enter or VirtualKey.Back or VirtualKey.Tab && HandleTodoKey(e.Key, ctrl, shift))
        {
            e.Handled = true;
            return;
        }

        if (ctrl && !alt)
        {
            switch (e.Key)
            {
                case VirtualKey.Z:
                    if (shift) Redo(); else Undo();
                    e.Handled = true;
                    return;
                case VirtualKey.Y:
                    Redo();
                    e.Handled = true;
                    return;
                // Editing / navigation shortcuts we keep.
                case VirtualKey.A or VirtualKey.C or VirtualKey.X or VirtualKey.V or VirtualKey.Insert:
                case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down:
                case VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown:
                case VirtualKey.Back or VirtualKey.Delete or VirtualKey.Enter:
                case VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu:
                    return;
                default:
                    // Everything else (Ctrl+E/R/L/J alignment, Ctrl+1/2/5 spacing, Ctrl+= subscript, …)
                    // would add formatting to a plain-text note.
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == VirtualKey.Enter && shift && !ctrl)
        {
            // Shift+Enter: plain new line (never continues a todo list). RichEdit would
            // otherwise insert a soft line break (\v).
            e.Handled = true;
            Document.Selection.TypeText("\r");
        }
    }

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string text, string glyph, string accel, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, KeyboardAcceleratorTextOverride = accel };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        var undo = Item("Undo", "", "Ctrl+Z", Undo);
        var redo = Item("Redo", "", "Ctrl+Y", Redo);
        menu.Items.Add(new MenuFlyoutSeparator());
        var cut = Item("Cut", "", "Ctrl+X", () => { CopySelection(); InsertAtSelection(""); });
        var copy = Item("Copy", "", "Ctrl+C", CopySelection);
        var paste = Item("Paste", "", "Ctrl+V", PastePlainText);
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Select all", "", "Ctrl+A", () => Document.Selection.SetRange(0, EditorText.Length + 1));

        menu.Opening += (_, _) =>
        {
            bool hasSelection = Document.Selection.Length != 0;
            undo.IsEnabled = _history.CanUndo;
            redo.IsEnabled = _history.CanRedo;
            cut.IsEnabled = copy.IsEnabled = hasSelection;
            paste.IsEnabled = Clipboard.GetContent().Contains(StandardDataFormats.Text);
        };
        return menu;
    }

    private void CopySelection()
    {
        Document.Selection.GetText(TextGetOptions.None, out string text);
        if (text.Length > 0) ClipboardHelper.SetText(NoteStore.NormalizeNewlines(text));
    }
}
