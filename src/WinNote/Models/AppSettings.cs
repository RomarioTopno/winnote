namespace WinNote.Models;

public sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0001; // Ctrl+Alt
    public uint HotkeyKey { get; set; } = 0x4E;                   // N

    /// <summary>Window bounds in physical pixels; null until the window was first moved/resized.</summary>
    public WindowBounds? Window { get; set; }
    public bool Pinned { get; set; }

    public string? CurrentNoteId { get; set; }

    /// <summary>Delete notes not modified for this many days; 0 = never.</summary>
    public int AutoDeleteDays { get; set; }

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public string FontFamily { get; set; } = "Segoe UI Variable Text";
    public double FontSize { get; set; } = 15;

    /// <summary>Live Markdown formatting in the editor.</summary>
    public bool MarkdownFormatting { get; set; } = true;

    /// <summary>Hand unused RAM back to Windows a few seconds after the window hides (no UI; settings.json only).</summary>
    public bool TrimMemoryWhenHidden { get; set; } = true;
}

public sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
