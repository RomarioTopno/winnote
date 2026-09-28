using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using WinNote.Services;
using WinNote.Views;

namespace WinNote;

// Search across all notes: Ctrl+F panel, jump to a match, F3 / Shift+F3 for next/previous.
public sealed partial class MainWindow
{
    private SearchView? _searchView; // created on first open
    private bool _searchOpen;
    private string _lastQuery = "";

    private IReadOnlyList<string> NoteTexts() => _notes.Notes.Select(n => n.Text).ToList();

    public void OpenSearch()
    {
        if (_searchOpen) return;
        CloseSettings();
        if (_searchView is null)
        {
            _searchView = new SearchView(NoteTexts);
            _searchView.HitChosen += (hits, index) => GoToHit(hits[index]);
            SearchHost.Child = _searchView;
        }
        _searchOpen = true;
        SearchHost.Visibility = Visibility.Visible;
        Editor.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Collapsed;
        HideToast(expired: true);
        _searchView.Activate();
    }

    public void CloseSearch()
    {
        if (!_searchOpen) return;
        _lastQuery = _searchView!.Query.Trim();
        _searchOpen = false;
        SearchHost.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Footer.Visibility = Visibility.Visible;
        FocusEditor();
    }

    private void GoToHit(SearchHit hit)
    {
        CloseSearch();
        if (hit.NoteIndex < 0 || hit.NoteIndex >= _notes.Notes.Count) return;
        if (hit.NoteIndex != _index) LoadNote(hit.NoteIndex);
        var selection = Editor.Document.Selection;
        selection.SetRange(hit.Start, hit.Start + hit.Length);
        selection.ScrollIntoView(PointOptions.None);
        FocusEditor();
    }

    /// <summary>F3 / Shift+F3: next / previous match of the last search, wrapping around all notes.</summary>
    private void StepSearchHit(int direction)
    {
        if (_lastQuery.Length == 0)
        {
            OpenSearch();
            return;
        }
        // Search again: notes may have changed since the panel was closed.
        var hits = NoteSearch.Find(NoteTexts(), _lastQuery, maxHits: int.MaxValue);
        if (hits.Count == 0)
        {
            ShowToast($"No matches for “{_lastQuery}”", milliseconds: 2000);
            return;
        }
        int caret = Editor.Document.Selection.StartPosition;
        int Compare(SearchHit h) => h.NoteIndex != _index ? h.NoteIndex.CompareTo(_index) : h.Start.CompareTo(caret);

        SearchHit target = direction > 0
            ? hits.FirstOrDefault(h => Compare(h) > 0, hits[0])       // after the caret, else wrap to first
            : hits.LastOrDefault(h => Compare(h) < 0, hits[^1]);      // before the caret, else wrap to last
        GoToHit(target);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchOpen) CloseSearch(); else OpenSearch();
    }
}
