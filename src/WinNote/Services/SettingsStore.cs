using System.Text.Json;
using Microsoft.UI.Dispatching;
using WinNote.Models;

namespace WinNote.Services;

internal static class AppPaths
{
    /// <summary>
    /// Names the data folder, single-instance lock, host window and startup entry. "WinNote" for
    /// the real app; "WinNote.Dev" for Debug builds; the WINNOTE_INSTANCE environment variable
    /// overrides both, so any build can run as a separate test instance next to the installed one.
    /// </summary>
    public static readonly string InstanceName =
        Environment.GetEnvironmentVariable("WINNOTE_INSTANCE") is { Length: > 0 } custom ? custom :
#if DEBUG
        "WinNote.Dev";
#else
        "WinNote";
#endif

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstanceName);

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string NotesFile => Path.Combine(DataDir, "notes.json");

    public static string IconFile => Path.Combine(AppContext.BaseDirectory, "Assets", "WinNote.ico");

    /// <summary>Single-colour tray icon: dark glyph for a light taskbar, white glyph for a dark one.</summary>
    public static string TrayIconFile(bool lightTaskbar) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", lightTaskbar ? "TrayLight.ico" : "TrayDark.ico");
}

/// <summary>Loads/saves <see cref="AppSettings"/>; saves are debounced so window drags don't hammer the disk.</summary>
internal sealed class SettingsStore
{
    private readonly DispatcherQueueTimer _saveTimer;

    public AppSettings Settings { get; }

    private SettingsStore(AppSettings settings)
    {
        Settings = settings;
        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public static SettingsStore Load()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var settings = AtomicFile.Read(AppPaths.SettingsFile,
            text => JsonSerializer.Deserialize(text, WinNoteJsonContext.Default.AppSettings));
        return new SettingsStore(settings ?? new AppSettings());
    }

    public void Save()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            AtomicFile.WriteAllText(AppPaths.SettingsFile,
                JsonSerializer.Serialize(Settings, WinNoteJsonContext.Default.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Saving settings failed: {ex.Message}");
        }
    }
}
