using System.Text;

namespace WinNote.Services;

/// <summary>
/// Crash-safe file writes: write a temp file, flush it to disk, then atomically replace the
/// target (keeping the previous version as .bak). Reads fall back to the .bak if needed.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(contents);
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }

    /// <summary>Reads the file; if it is missing, empty or rejected by <paramref name="parse"/>, tries the .bak.</summary>
    public static T? Read<T>(string path, Func<string, T?> parse) where T : class
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                string text = File.ReadAllText(candidate, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (parse(text) is { } value) return value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read {candidate}: {ex.Message}");
            }
        }
        return null;
    }
}
