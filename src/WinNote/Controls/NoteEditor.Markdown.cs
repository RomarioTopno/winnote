using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace WinNote.Controls;

// Live Markdown formatting: the text stays plain Markdown; syntax characters are dimmed and the
// constructs they mark are formatted (headings larger/bold, emphasis, code, links, quotes, lists).
public sealed partial class NoteEditor
{
    private bool _markdownEnabled = true;

    /// <summary>Live Markdown formatting on/off (Settings). Todo styling is independent of this.</summary>
    public bool MarkdownEnabled
    {
        get => _markdownEnabled;
        set
        {
            if (_markdownEnabled == value) return;
            _markdownEnabled = value;
            Restyle();
        }
    }

    private Color CodeColor => ActualTheme == ElementTheme.Dark ? Color.FromArgb(255, 230, 180, 140) : Color.FromArgb(255, 163, 21, 21);

    /// <summary>Called from <see cref="Restyle"/> after the reset and before todo styling.</summary>
    private void ApplyMarkdown(RichEditTextDocument doc, string text)
    {
        if (!_markdownEnabled) return;
        foreach (var span in MarkdownText.Parse(text))
        {
            if (span.Length <= 0) continue;
            var f = doc.GetRange(span.Start, span.End).CharacterFormat;
            switch (span.Kind)
            {
                case MdKind.Marker:
                case MdKind.Rule:
                case MdKind.LinkUrl:
                    f.ForegroundColor = _palette.Dim;
                    break;
                case MdKind.Heading:
                    // Bold only: RichEditBox renders every run at the control's font size and family
                    // (per-run Size/Name are ignored), so headings can't be made larger.
                    f.Bold = FormatEffect.On;
                    break;
                case MdKind.Bold:
                    f.Bold = FormatEffect.On;
                    break;
                case MdKind.Italic:
                    f.Italic = FormatEffect.On;
                    break;
                case MdKind.Strike:
                    f.Strikethrough = FormatEffect.On;
                    break;
                case MdKind.Code:
                    f.ForegroundColor = CodeColor;
                    break;
                case MdKind.LinkText:
                    f.ForegroundColor = _palette.Accent;
                    f.Underline = UnderlineType.Single;
                    break;
                case MdKind.Quote:
                    f.Italic = FormatEffect.On;
                    f.ForegroundColor = _palette.Dim;
                    break;
                case MdKind.ListMarker:
                    f.ForegroundColor = _palette.Accent;
                    f.Bold = FormatEffect.On;
                    break;
            }
        }
    }

    /// <summary>Ctrl+Click: opens the link under the caret (http, https, mailto only).</summary>
    private bool TryOpenLinkAtCaret()
    {
        if (!_markdownEnabled) return false;
        string? url = MarkdownText.LinkAt(EditorText, Document.Selection.StartPosition);
        if (url is null) return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Services.ErrorLog.Write($"Opening link {url}", ex);
            return false;
        }
    }
}
