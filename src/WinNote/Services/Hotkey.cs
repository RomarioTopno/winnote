using System.Runtime.InteropServices;
using static WinNote.Interop.NativeMethods;

namespace WinNote.Services;

/// <summary>A key combination as understood by RegisterHotKey (MOD_* flags + virtual-key code).</summary>
internal readonly record struct Hotkey(uint Modifiers, uint Key)
{
    public static readonly Hotkey Default = new(MOD_CONTROL | MOD_ALT, 0x4E); // Ctrl+Alt+N

    public bool IsValid => Key != 0 && (Modifiers & (MOD_CONTROL | MOD_ALT | MOD_WIN)) != 0;

    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    private static unsafe string KeyName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        switch (vk)
        {
            case 0x20: return "Space";
            case 0x0D: return "Enter";
            case 0x09: return "Tab";
            case 0xC0: return "`";
        }
        uint scan = MapVirtualKey(vk, 0 /* MAPVK_VK_TO_VSC */);
        // Extended keys (arrows, Home/End, Insert/Delete, PgUp/PgDn) need bit 24 set.
        bool extended = vk is >= 0x21 and <= 0x2E;
        char* buffer = stackalloc char[64];
        int len = GetKeyNameText((int)((scan << 16) | (extended ? 1u << 24 : 0)), buffer, 64);
        return len > 0 ? new string(buffer, 0, len) : $"0x{vk:X2}";
    }
}

/// <summary>Registers a single global hotkey on the host window.</summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;
    private readonly HostWindow _host;
    private bool _registered;

    public event Action? Pressed;
    public Hotkey? Current { get; private set; }

    /// <summary>Why the last registration failed (shown in Settings); null if it succeeded.</summary>
    public string? LastError { get; private set; }

    public HotkeyService(HostWindow host) => _host = host;

    /// <summary>Registers <paramref name="hotkey"/>; on failure returns a user-facing message.</summary>
    public bool TryRegister(Hotkey hotkey, out string? error)
    {
        bool ok = TryRegisterCore(hotkey, out error);
        LastError = error;
        return ok;
    }

    private bool TryRegisterCore(Hotkey hotkey, out string? error)
    {
        Unregister();
        error = null;
        if (!hotkey.IsValid)
        {
            error = "Use at least one of Ctrl, Alt or Win together with a key.";
            return false;
        }
        if (!RegisterHotKey(_host.Handle, HotkeyId, hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key))
        {
            int code = Marshal.GetLastPInvokeError();
            error = code == ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{hotkey} is already used by another app or by Windows. Pick a different shortcut in Settings."
                : $"Couldn't register {hotkey} (error {code}).";
            return false;
        }
        _registered = true;
        Current = hotkey;
        return true;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_host.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    public bool HandleMessage(uint msg, nint wParam)
    {
        if (msg != WM_HOTKEY || wParam != HotkeyId) return false;
        Pressed?.Invoke();
        return true;
    }

    public void Dispose() => Unregister();
}
