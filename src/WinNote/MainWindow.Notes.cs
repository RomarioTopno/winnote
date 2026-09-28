using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinNote.Controls;
using WinNote.Models;
using WinNote.Services;

namespace WinNote;

// Notes: navigation, create/delete (with undo toast), copy/export, autosave, auto-delete.
public sealed partial class MainWindow
{
    private NoteStore _notes = null!;
    private readonly Dictionary<string, EditHistory> _histories = [];
    private int _index;
    private DispatcherQueueTimer? _toastTimer;
    private Action? _toastAction;

    private Note CurrentNote => _notes.Notes[_index];

    private void InitializeNotes()
    {
        _notes = NoteStore.Load();
        _notes.PurgeOlderThan(_settings.Settings.AutoDeleteDays);
        if (_notes.Notes.Count == 0) _notes.Add(0);

        int saved = _notes.Notes.FindIndex(n => n.Id == _settings.Settings.CurrentNoteId);
        Editor.UserTextChanged += (_, _) => _notes.UpdateText(CurrentNote, Editor.PlainText);
        LoadNote(saved >= 0 ? saved : _notes.Notes.Count - 1);
        RemoveBlankNotes();
    }

    private EditHistory HistoryFor(Note note)
    {
        if (!_histories.TryGetValue(note.Id, out var history))
            _histories[note.Id] = history = new EditHistory(NoteEditor.ToEditorText(note.Text));
        return history;
    }

    private void LoadNote(int index)
    {
        _index = Math.Clamp(index, 0, _notes.Notes.Count - 1);
        Editor.Load(HistoryFor(CurrentNote));
        UpdateNavigation();
        if (_settings.Settings.CurrentNoteId != CurrentNote.Id)
        {
            _settings.Settings.CurrentNoteId = CurrentNote.Id;
            _settings.Save();
        }
    }

    private void UpdateNavigation()
    {
        int count = _notes.Notes.Count;
        PageText.Text = $"{_index + 1} / {count}";
        PrevButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < count - 1;
    }

    private void Navigate(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _notes.Notes.Count || newIndex == _index) return;
        LoadNote(newIndex);
        FocusEditor();
    }

    private void NewNote()
    {
        _notes.Add(_notes.Notes.Count);
        Navigate(_notes.Notes.Count - 1);
    }

    /// <summary>
    /// Drops empty notes except the one being shown. Runs when the window hides (and on startup),
    /// never while the user is moving between notes, so the list doesn't shift under them.
    /// </summary>
    private void RemoveBlankNotes()
    {
        var current = CurrentNote;
        var blanks = _notes.Notes.Where(n => n.IsBlank && n != current).ToList();
        if (blanks.Count == 0) return;
        foreach (var note in blanks)
        {
            _notes.Remove(note);
            _histories.Remove(note.Id);
        }
        LoadNote(_notes.Notes.IndexOf(current));
    }

    private void DeleteCurrentNote()
    {
        var note = CurrentNote;
        int index = _index;
        if (note.IsBlank && _notes.Notes.Count == 1) return; // nothing to delete

        _notes.Remove(note);
        if (_notes.Notes.Count == 0) _notes.Add(0);
        LoadNote(Math.Min(index, _notes.Notes.Count - 1));
        FocusEditor();

        if (note.IsBlank)
        {
            _histories.Remove(note.Id);
            return;
        }

        ShowToast("Note deleted", "Undo", () =>
        {
            // If deleting left only a fresh blank note behind, replace it.
            if (_notes.Notes.Count == 1 && CurrentNote.IsBlank)
            {
                _histories.Remove(CurrentNote.Id);
                _notes.Remove(CurrentNote);
            }
            _notes.Insert(index, note);
            LoadNote(_notes.Notes.IndexOf(note));
            FocusEditor();
        }, onExpired: () => _histories.Remove(note.Id));
    }

    /// <summary>Applies the auto-delete setting; called on startup and whenever the window is shown.</summary>
    private void PurgeOldNotes()
    {
        var current = CurrentNote;
        if (_notes.PurgeOlderThan(_settings.Settings.AutoDeleteDays) == 0) return;

        foreach (var id in _histories.Keys.Where(id => !_notes.Notes.Exists(n => n.Id == id)).ToList())
            _histories.Remove(id);
        if (_notes.Notes.Count == 0) _notes.Add(0);
        int stillThere = _notes.Notes.IndexOf(current);
        LoadNote(stillThere >= 0 ? stillThere : _notes.Notes.Count - 1);
    }

    private void FocusEditor() => Editor.Focus(FocusState.Programmatic);

    // ---- Toast ----

    private Action? _toastExpired;

    private void ShowToast(string message, string? actionText = null, Action? action = null, Action? onExpired = null, int milliseconds = 5000)
    {
        _toastExpired?.Invoke(); // a previous toast's action is no longer offered
        _toastExpired = onExpired;
        _toastAction = action;

        ToastText.Text = message;
        ToastActionButton.Content = actionText;
        ToastActionButton.Visibility = actionText is null ? Visibility.Collapsed : Visibility.Visible;
        Toast.Visibility = Visibility.Visible;

        if (_toastTimer is null)
        {
            _toastTimer = DispatcherQueue.CreateTimer();
            _toastTimer.IsRepeating = false;
            _toastTimer.Tick += (_, _) => HideToast(expired: true);
        }
        _toastTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast(bool expired)
    {
        _toastTimer?.Stop();
        Toast.Visibility = Visibility.Collapsed;
        if (expired) _toastExpired?.Invoke();
        _toastExpired = null;
        _toastAction = null;
    }

    private void ToastActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        HideToast(expired: false);
        action?.Invoke();
    }

    // ---- Copy / export ----

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentNote.IsBlank) return;
        if (ClipboardHelper.SetText(CurrentNote.Text)) ShowToast("Copied to clipboard", milliseconds: 1500);
    }

    private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var note = CurrentNote;
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = SuggestFileName(note.Text),
                DefaultFileExtension = ".txt",
            };
            // Explicit List<string> (not a collection expression): CsWinRT's AOT generator needs to
            // see the concrete type to emit the COM wrapper that passes it to WinRT.
            picker.FileTypeChoices.Add("Text file", new List<string> { ".txt" });
            var result = await picker.PickSaveFileAsync();
            if (result is null) return;

            await File.WriteAllTextAsync(result.Path, note.Text.Replace("\n", "\r\n"), new UTF8Encoding(false));
            ShowToast($"Saved {Path.GetFileName(result.Path)}", milliseconds: 2500);
        }
        catch (Exception ex)
        {
            ShowToast($"Export failed: {ex.Message}", milliseconds: 5000);
        }
    }

    /// <summary>First meaningful line of the note, made safe for a file name.</summary>
    private static string SuggestFileName(string text)
    {
        string line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (line.StartsWith("[ ] ") || line.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase)) line = line[4..];
        var invalid = Path.GetInvalidFileNameChars();
        line = new string(line.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        if (line.Length > 40) line = line[..40].TrimEnd();
        return line.Length > 0 ? line : "Note";
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) => DeleteCurrentNote();

    private void MoreMenu_Opening(object sender, object e) => ClearCompletedItem.IsEnabled = Editor.HasCompletedTodos;

    private void ClearCompletedItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.ClearCompletedTodos(); // undoable with Ctrl+Z
        FocusEditor();
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSettings();
        CloseSearch();
        NewNote();
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(_index - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(_index + 1);
}
