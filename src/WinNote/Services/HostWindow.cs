using System.Runtime.InteropServices;
using WinNote.Interop;
using static WinNote.Interop.NativeMethods;

namespace WinNote.Services;

/// <summary>
/// Invisible top-level Win32 window that owns the global hotkey and the tray icon.
/// It is a real (hidden) top-level window rather than HWND_MESSAGE so it receives the
/// "TaskbarCreated" broadcast after Explorer restarts, and so a second instance can find it.
/// Its messages are pumped by the WinUI dispatcher loop because it lives on the UI thread.
/// </summary>
internal sealed unsafe class HostWindow : IDisposable
{
    public static readonly string ClassName = AppPaths.InstanceName + ".Host"; // separate per instance (see AppPaths.InstanceName)
    public const uint WM_APP_SHOW = WM_APP + 2; // posted by a second instance

    public static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    private static HostWindow? s_instance;

    public nint Handle { get; }

    /// <summary>Raised for every message; return true to mark it handled.</summary>
    public event Func<uint, nint, nint, bool>? Message;

    public HostWindow()
    {
        s_instance = this;
        nint hInstance = GetModuleHandle(null);
        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = hInstance,
                lpszClassName = className,
            };
            if (RegisterClassEx(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()}).");
        }

        Handle = CreateWindowEx(WS_EX_TOOLWINDOW, ClassName, "WinNote", WS_POPUP, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (Handle == 0)
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()}).");
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var self = s_instance;
        if (self is not null && self.Handle == hwnd && self.Message is { } handler)
        {
            try
            {
                if (handler(msg, wParam, lParam)) return 0;
            }
            catch (Exception ex)
            {
                // Never let an exception cross the native boundary.
                ErrorLog.Write($"Host window message 0x{msg:X4}", ex);
                return 0;
            }
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle != 0) DestroyWindow(Handle);
        s_instance = null;
    }
}
