using static WinNote.Interop.NativeMethods;
using WinNote.Interop;

namespace WinNote.Services;

internal readonly record struct TrayMenuItem(int Id, string Text, bool Checked = false, bool IsDefault = false, bool Enabled = true)
{
    public static TrayMenuItem Separator => new(0, "");
    public bool IsSeparator => Id == 0;
}

/// <summary>Notification-area icon (Shell_NotifyIcon) with a native popup menu.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 1;
    private const uint IconId = 1;

    private readonly HostWindow _host;
    private readonly string _tooltip;
    private nint _icon;
    private bool _added;

    public event Action? Activated;
    /// <summary>Right-click / menu key; args are the screen anchor point.</summary>
    public event Action<int, int>? ContextMenuRequested;

    public TrayIcon(HostWindow host, string iconPath, string tooltip)
    {
        _host = host;
        _tooltip = tooltip;
        _icon = LoadImage(0, iconPath, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
        Add();
    }

    private void Add()
    {
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        NOTIFYICONDATAW.Copy(_tooltip, data.szTip, 128);
        _added = Shell_NotifyIcon(NIM_ADD, &data);

        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _host.Handle,
        uID = IconId,
    };

    /// <summary>Returns true if the message belonged to the tray icon.</summary>
    public bool HandleMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == HostWindow.WM_TASKBARCREATED)
        {
            Add(); // Explorer restarted; our icon is gone.
            return true;
        }
        if (msg != CallbackMessage) return false;

        // NOTIFYICON_VERSION_4: LOWORD(lParam) = event, wParam = anchor x/y.
        switch ((uint)LoWord(lParam))
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                Activated?.Invoke();
                break;
            case WM_CONTEXTMENU:
                ContextMenuRequested?.Invoke(LoWord(wParam), HiWord(wParam));
                break;
        }
        return true;
    }

    /// <summary>Shows a native menu and returns the chosen item id (0 if dismissed).</summary>
    public int ShowMenu(int x, int y, IReadOnlyList<TrayMenuItem> items)
    {
        nint menu = CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    AppendMenu(menu, MF_SEPARATOR, 0, null);
                    continue;
                }
                uint flags = MF_STRING | (item.Checked ? MF_CHECKED : 0) | (item.Enabled ? 0 : MF_GRAYED);
                AppendMenu(menu, flags, (nuint)item.Id, item.Text);
                if (item.IsDefault) SetMenuDefaultItem(menu, (uint)item.Id, 0);
            }

            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_host.Handle);
            int id = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, x, y, _host.Handle, 0);
            PostMessage(_host.Handle, WM_NULL, 0, 0);
            return id;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void ShowBalloon(string title, string text, bool warning = false)
    {
        if (!_added) return;
        var data = NewData();
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = (warning ? NIIF_WARNING : NIIF_INFO) | NIIF_NOSOUND;
        NOTIFYICONDATAW.Copy(title, data.szInfoTitle, 64);
        NOTIFYICONDATAW.Copy(text, data.szInfo, 256);
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Shell_NotifyIcon(NIM_DELETE, &data);
            _added = false;
        }
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }
}
