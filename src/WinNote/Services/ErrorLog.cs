namespace WinNote.Services;

/// <summary>Appends unexpected exceptions to %LOCALAPPDATA%\WinNote\error.log (local only, capped at ~256 KB).</summary>
internal static class ErrorLog
{
    private static string LogFile => Path.Combine(AppPaths.DataDir, "error.log");

    public static void Write(string context, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"{context}: {ex}");
        try
        {
            var info = new FileInfo(LogFile);
            if (info.Exists && info.Length > 256 * 1024) info.Delete();
            File.AppendAllText(LogFile, $"[{DateTimeOffset.Now:u}] {context}\n{ex}\n\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
