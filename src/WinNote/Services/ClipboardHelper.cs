using Windows.ApplicationModel.DataTransfer;

namespace WinNote.Services;

internal static class ClipboardHelper
{
    /// <summary>Puts plain text ('\n' line endings are converted to CRLF) on the clipboard.</summary>
    public static bool SetText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text.Replace("\n", "\r\n"));
            Clipboard.SetContent(package);
            Clipboard.Flush(); // keep it available after WinNote exits
            return true;
        }
        catch (Exception ex)
        {
            // The clipboard can be briefly locked by another process.
            System.Diagnostics.Debug.WriteLine($"Clipboard failed: {ex.Message}");
            return false;
        }
    }
}
