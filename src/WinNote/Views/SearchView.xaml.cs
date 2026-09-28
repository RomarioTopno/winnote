using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.ViewManagement;
using WinNote.Services;

namespace WinNote.Views;

/// <summary>Search panel: a query box and a list of matching lines across all notes.</summary>
public sealed partial class SearchView : UserControl
{
    private static readonly UISettings s_uiSettings = new();
    private readonly Func<IReadOnlyList<string>> _getNotes;
    private List<SearchHit> _hits = [];

    /// <summary>A result was chosen (Enter or click). Args: all hits of the search, and the chosen one's index.</summary>
    public event Action<IReadOnlyList<SearchHit>, int>? HitChosen;

    public string Query => QueryBox.Text;

    /// <param name="getNotes">Returns the current text of every note, in list order.</param>
    public SearchView(Func<IReadOnlyList<string>> getNotes)
    {
        _getNotes = getNotes;
        InitializeComponent();
    }

    /// <summary>Called whenever the panel opens: re-runs the query (notes may have changed) and focuses the box.</summary>
    public void Activate()
    {
        Refresh();
        QueryBox.Focus(FocusState.Programmatic);
        QueryBox.SelectAll();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        string query = QueryBox.Text;
        _hits = NoteSearch.Find(_getNotes(), query);
        Results.Items.Clear();

        if (query.Trim().Length == 0)
        {
            Summary.Text = "Type to search every note. Enter opens a result; F3 / Shift+F3 jump between matches later.";
            return;
        }
        if (_hits.Count == 0)
        {
            Summary.Text = "No matches";
            return;
        }

        int notes = _hits.Select(h => h.NoteIndex).Distinct().Count();
        Summary.Text = $"{_hits.Count}{(_hits.Count >= 200 ? "+" : "")} {(_hits.Count == 1 ? "match" : "matches")} in {notes} {(notes == 1 ? "note" : "notes")}";

        var highlight = new SolidColorBrush(ActualTheme == ElementTheme.Dark
            ? s_uiSettings.GetColorValue(UIColorType.AccentLight2)
            : s_uiSettings.GetColorValue(UIColorType.AccentDark1));
        foreach (var hit in _hits) Results.Items.Add(BuildItem(hit, highlight));
        Results.SelectedIndex = 0;
    }

    private static UIElement BuildItem(SearchHit hit, Brush highlight)
    {
        var title = new TextBlock
        {
            Text = hit.NoteTitle,
            FontSize = 12,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        string s = hit.Snippet;
        int m = Math.Clamp(hit.SnippetMatchStart, 0, s.Length);
        int len = Math.Clamp(hit.Length, 0, s.Length - m);
        line.Inlines.Add(new Run { Text = s[..m] });
        line.Inlines.Add(new Run { Text = s.Substring(m, len), Foreground = highlight, FontWeight = FontWeights.SemiBold });
        line.Inlines.Add(new Run { Text = s[(m + len)..] });

        var panel = new StackPanel { Padding = new Thickness(0, 4, 0, 4), Spacing = 1, Tag = hit };
        panel.Children.Add(title);
        panel.Children.Add(line);
        AutomationPropertiesHelper(panel, $"{hit.NoteTitle}: {s}");
        return panel;
    }

    private static void AutomationPropertiesHelper(UIElement element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);

    private void QueryBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                Move(+1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                Move(-1);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                Choose(Math.Max(0, Results.SelectedIndex));
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_hits.Count == 0) return;
        int index = Math.Clamp(Results.SelectedIndex + delta, 0, _hits.Count - 1);
        Results.SelectedIndex = index;
        Results.ScrollIntoView(Results.Items[index]);
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e)
    {
        int index = Results.Items.IndexOf(e.ClickedItem);
        Choose(index >= 0 ? index : Results.SelectedIndex);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _hits.Count) return;
        HitChosen?.Invoke(_hits, index);
    }
}
