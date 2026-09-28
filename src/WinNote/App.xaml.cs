using Microsoft.UI.Xaml;
using WinNote.Services;
using static WinNote.Interop.NativeMethods;

namespace WinNote;

public partial class App : Application
{
    private enum TrayCommand { Show = 1, TogglePin, Settings, StartWithWindows, Exit }

    private readonly bool _startHidden;
    private HostWindow? _host;
    private TrayIcon? _tray;
    private HotkeyService? _hotkey;
    private MainWindow? _window;

    internal SettingsStore Settings { get; private set; } = null!;

    public App(bool startHidden)
    {
        _startHidden = startHidden;
        InitializeComponent();
        // Closing the window only hides it; the app lives until "Exit" is chosen.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => ErrorLog.Write("Unhandled exception", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings = SettingsStore.Load();

        _host = new HostWindow();
        _host.Message += OnHostMessage;

        _hotkey = new HotkeyService(_host);
        _window = new MainWindow(Settings, _hotkey);
        _hotkey.Pressed += () => _window.Toggle();

        _tray = new TrayIcon(_host, AppPaths.IconFile, "WinNote");
        _tray.Activated += () => _window.ToggleFromTray();
        _tray.ContextMenuRequested += ShowTrayMenu;

        try { StartupService.RefreshPath(); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }

        var hotkey = new Hotkey(Settings.Settings.HotkeyModifiers, Settings.Settings.HotkeyKey);
        if (!_hotkey.TryRegister(hotkey, out string? error))
            _tray.ShowBalloon("WinNote shortcut unavailable", error!, warning: true);

        if (!_startHidden) _window.ShowAndFocus();
        else _window.ScheduleMemoryTrim(TimeSpan.FromSeconds(5));
    }

    private bool OnHostMessage(uint msg, nint wParam, nint lParam)
    {
        if (_hotkey!.HandleMessage(msg, wParam)) return true;
        if (_tray!.HandleMessage(msg, wParam, lParam)) return true;
        switch (msg)
        {
            case HostWindow.WM_APP_SHOW:
                _window!.ShowAndFocus();
                return true;
            case WM_CLOSE: // e.g. "taskkill /im WinNote.exe" (without /f), installers, scripts
                Quit();
                return true;
            case WM_ENDSESSION when wParam != 0: // logoff / shutdown: the process may end after this returns
                _window!.PrepareForExit();
                Settings.SaveNow();
                return true;
        }
        return false;
    }

    private void ShowTrayMenu(int x, int y)
    {
        var window = _window!;
        string hotkeyHint = _hotkey!.Current is { } hk ? $"\t{hk}" : "";
        var items = new List<TrayMenuItem>
        {
            new((int)TrayCommand.Show, "Show" + hotkeyHint, IsDefault: true),
            new((int)TrayCommand.TogglePin, "Pin on top", Checked: window.IsPinned),
            new((int)TrayCommand.Settings, "Settings…"),
            new((int)TrayCommand.StartWithWindows, "Start with Windows", Checked: StartupService.IsEnabled),
            TrayMenuItem.Separator,
            new((int)TrayCommand.Exit, "Exit"),
        };

        switch ((TrayCommand)_tray!.ShowMenu(x, y, items))
        {
            case TrayCommand.Show: window.ShowAndFocus(); break;
            case TrayCommand.TogglePin:
                window.IsPinned = !window.IsPinned;
                if (window.IsPinned) window.ShowAndFocus(); // pinning something you can't see is pointless
                break;
            case TrayCommand.Settings:
                window.ShowAndFocus();
                window.OpenSettings();
                break;
            case TrayCommand.StartWithWindows: window.SetStartWithWindows(!StartupService.IsEnabled); break;
            case TrayCommand.Exit: Quit(); break;
        }
    }

    public void Quit()
    {
        _window?.PrepareForExit();
        Settings.SaveNow();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _window?.Close();
        _host?.Dispose();
        Exit();
    }
}
