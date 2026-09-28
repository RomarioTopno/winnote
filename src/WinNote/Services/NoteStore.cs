using System.Text.Json;
using Microsoft.UI.Dispatching;
using WinNote.Models;

namespace WinNote.Services;

/// <summary>
/// Owns the list of notes and persists it to notes.json. Changes are debounced (500 ms);
/// the JSON is produced on the UI thread and written atomically on a background thread.
/// </summary>
internal sealed class NoteStore
{
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly Lock _writeLock = new();
    private long _snapshotSeq;   // incremented per serialized snapshot (UI thread)
    private long _writtenSeq;    // last snapshot written to disk (under _writeLock)
    private bool _dirty;

    public List<Note> Notes { get; }

    private NoteStore(List<Note> notes)
    {
        Notes = notes;
        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveInBackground();
    }

    public static NoteStore Load()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var file = AtomicFile.Read(AppPaths.NotesFile,
            text => JsonSerializer.Deserialize(text, WinNoteJsonContext.Default.NotesFile));
        var notes = file?.Notes ?? [];
        notes.RemoveAll(n => n is null);
        foreach (var n in notes) n.Text = NormalizeNewlines(n.Text ?? "");
        return new NoteStore(notes);
    }

    public static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\v', '\n');

    public Note Add(int index)
    {
        var note = new Note();
        Notes.Insert(Math.Clamp(index, 0, Notes.Count), note);
        MarkDirty();
        return note;
    }

    public void Insert(int index, Note note)
    {
        Notes.Insert(Math.Clamp(index, 0, Notes.Count), note);
        MarkDirty();
    }

    public void Remove(Note note)
    {
        if (Notes.Remove(note)) MarkDirty();
    }

    public void UpdateText(Note note, string text)
    {
        if (note.Text == text) return;
        note.Text = text;
        note.Modified = DateTimeOffset.Now;
        MarkDirty();
    }

    /// <summary>Deletes notes untouched for <paramref name="days"/> days. Returns how many were removed.</summary>
    public int PurgeOlderThan(int days)
    {
        if (days <= 0) return 0;
        var cutoff = DateTimeOffset.Now.AddDays(-days);
        int removed = Notes.RemoveAll(n => n.Modified < cutoff);
        if (removed > 0) MarkDirty();
        return removed;
    }

    public void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private string Snapshot(out long seq)
    {
        _dirty = false;
        seq = ++_snapshotSeq;
        return JsonSerializer.Serialize(new NotesFile { Notes = Notes }, WinNoteJsonContext.Default.NotesFile);
    }

    private void SaveInBackground()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        string json = Snapshot(out long seq);
        _ = Task.Run(() => Write(json, seq));
    }

    /// <summary>Synchronous save, used when hiding the window and on exit.</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        // Also covers a background write that was queued but may not have run yet.
        if (!_dirty && Interlocked.Read(ref _writtenSeq) >= _snapshotSeq) return;
        string json = Snapshot(out long seq);
        Write(json, seq);
    }

    /// <summary>Also schedules a background save right now, without waiting for the debounce.</summary>
    public void FlushInBackground() => SaveInBackground();

    private void Write(string json, long seq)
    {
        lock (_writeLock)
        {
            if (seq <= _writtenSeq) return; // a newer snapshot already hit the disk
            try
            {
                AtomicFile.WriteAllText(AppPaths.NotesFile, json);
                Interlocked.Exchange(ref _writtenSeq, seq);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Saving notes failed: {ex.Message}");
            }
        }
    }
}
