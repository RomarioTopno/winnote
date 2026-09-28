using Microsoft.Win32;

namespace WinNote.Services;

/// <summary>"Start with Windows" via HKCU\...\Run (no admin rights, per user). Starts hidden in the tray.</summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Where Task Manager's Startup tab records "Disabled" (first byte 0x03) for Run entries.
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = AppPaths.InstanceName;

    private static string Command => $"\"{Environment.ProcessPath}\" --hidden";

    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] state || state.Length == 0 || (state[0] & 1) == 0;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) run.SetValue(ValueName, Command);
        else run.DeleteValue(ValueName, throwOnMissingValue: false);

        // Turning it on in WinNote should override an earlier "Disabled" in Task Manager.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Uninstall cleanup. The installer passes the installing user's SID ([UserSID]) so the entry
    /// is removed from that user's hive (HKEY_USERS\&lt;SID&gt;) even if the custom action's HKCU
    /// were mapped elsewhere; without a SID, HKCU is used.
    /// </summary>
    public static void RemoveForUser(string? userSid)
    {
        using var hive = string.IsNullOrEmpty(userSid) ? null : Registry.Users.OpenSubKey(userSid, writable: true);
        var root = hive ?? Registry.CurrentUser;
        using (var run = root.OpenSubKey(RunKey, writable: true))
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        using (var approved = root.OpenSubKey(ApprovedKey, writable: true))
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>If the exe was moved since autostart was enabled, point the Run entry at the new location.</summary>
    public static void RefreshPath()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (run?.GetValue(ValueName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            run.SetValue(ValueName, Command);
    }
}
