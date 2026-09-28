using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using WinNote.Models;
using WinNote.Services;
using static WinNote.Interop.NativeMethods;

namespace WinNote;

public sealed partial class MainWindow : Window
{
    private const double DefaultWidthDip = 380, DefaultHeightDip = 480;
    private const double MinWidthDip = 260, MinHeightDip = 200;

    private readonly SettingsStore _settings;
    private readonly HotkeyService _hotkey;
    private readonly nint _hwnd;
    private readonly OverlappedPresenter _presenter;
    private bool _placed;
    private bool _exiting;
    private long _lastAutoHideTicks;

    internal MainWindow(SettingsStore settings, HotkeyService hotkey)
    {
        _settings = settings;
        _hotkey = hotkey;
        InitializeComponent();

        // Set in code rather than XAML: keeps the XAML parser from having to activate a custom type
        // (which fails under Native AOT trimming).
        SystemBackdrop = new Controls.ActiveMicaBackdrop(Root);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        AppWindow.Title = "WinNote";
        AppWindow.SetIcon(AppPaths.IconFile);
        AppWindow.IsShownInSwitchers = false; // no taskbar button / Alt+Tab entry: it's a flyout-style tool

        // Frameless look: no system caption row or caption buttons, but keep the resize border.
        // Create our own presenter instead of casting AppWindow.Presenter: that downcast fails under Native AOT.
        _presenter = OverlappedPresenter.Create();
        AppWindow.SetPresenter(_presenter);
        _presenter.IsMinimizable = false;
        _presenter.IsMaximizable = false;
        _presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        SetDwmInt(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

        IsPinned = settings.Settings.Pinned;

        Activated += OnActivated;
        AppWindow.Closing += OnClosing;
        AppWindow.Changed += OnAppWindowChanged;
        Root.ActualThemeChanged += (_, _) => ApplyDwmTheme();
        ApplyDwmTheme();

        ApplyAppearance();
        InitializeNotes();
    }

    public bool IsPinned
    {
        get => _presenter.IsAlwaysOnTop;
        set
        {
            _presenter.IsAlwaysOnTop = value;
            PinIconOff.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            PinIconOn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            UpdatePinnedBorder();
            string label = value ? "Unpin" : "Pin on top";
            ToolTipService.SetToolTip(PinButton, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PinButton, label);
            if (_settings.Settings.Pinned != value)
            {
                _settings.Settings.Pinned = value;
                _settings.Save();
            }
            // Unpinning a window that is sitting in the background makes it behave like a flyout again.
            if (!value && AppWindow.IsVisible && !IsForeground)
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AutoHideIfFocusLeft);
        }
    }

    private bool IsForeground => GetForegroundWindow() == _hwnd;

    public void ShowAndFocus()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_placed)
        {
            ApplyInitialPlacement();
            _placed = true;
        }
        bool wasHidden = !AppWindow.IsVisible;
        if (wasHidden) PurgeOldNotes();
        Activate();
        SetForegroundWindow(_hwnd);
        FocusEditor();
        if (wasHidden)
        {
            PlayShowAnimation();
            TraceShow(start);
        }
    }

    public void HideWindow()
    {
        AppWindow.Hide();
        RemoveBlankNotes();
        _notes.FlushInBackground();
        ScheduleMemoryTrim(TimeSpan.FromSeconds(3));
    }

    /// <summary>Hotkey: hide if visible and focused, otherwise show and focus.</summary>
    public void Toggle()
    {
        if (AppWindow.IsVisible && IsForeground) HideWindow();
        else ShowAndFocus();
    }

    /// <summary>
    /// Tray click: visible → hide, hidden → show. Clicking the tray icon takes focus away from us
    /// before the click arrives, so an unpinned window has usually just auto-hidden; in that case
    /// the click's intent was "hide" and we must not bring it straight back.
    /// </summary>
    public void ToggleFromTray()
    {
        if (AppWindow.IsVisible) HideWindow();
        else if (Environment.TickCount64 - _lastAutoHideTicks > 400) ShowAndFocus();
    }

    public void PrepareForExit()
    {
        _exiting = true;
        SaveBounds();
        _notes.Flush();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated) return;
        if (IsPinned || !AppWindow.IsVisible || _exiting) return;

        // Decide after the activation change has settled, so we can see who took the focus.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AutoHideIfFocusLeft);
    }

    /// <summary>Flyout behaviour: an unpinned window hides once focus goes to another app.</summary>
    private void AutoHideIfFocusLeft()
    {
        if (IsPinned || !AppWindow.IsVisible || _exiting) return;

        nint foreground = GetForegroundWindow();
        // Still us, or one of our own owned windows (file dialog, popup): stay.
        if (foreground == _hwnd || (foreground != 0 && GetAncestor(foreground, GA_ROOTOWNER) == _hwnd)) return;

        _lastAutoHideTicks = Environment.TickCount64;
        HideWindow();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true; // Alt+F4 / close just hides
        HideWindow();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((args.DidPositionChange || args.DidSizeChange) && sender.IsVisible && _placed)
            SaveBounds();
    }

    private void SaveBounds()
    {
        if (!_placed) return;
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        var bounds = _settings.Settings.Window ??= new WindowBounds();
        if (bounds.X == pos.X && bounds.Y == pos.Y && bounds.Width == size.Width && bounds.Height == size.Height) return;
        (bounds.X, bounds.Y, bounds.Width, bounds.Height) = (pos.X, pos.Y, size.Width, size.Height);
        _settings.Save();
    }

    private void ApplyInitialPlacement()
    {
        double dpiScale = GetDpiForWindow(_hwnd) / 96.0;
        _presenter.PreferredMinimumWidth = (int)(MinWidthDip * dpiScale);
        _presenter.PreferredMinimumHeight = (int)(MinHeightDip * dpiScale);

        if (_settings.Settings.Window is { } b && b.Width > 0 && b.Height > 0)
        {
            var rect = new RectInt32(b.X, b.Y, b.Width, b.Height);
            if (IsTitleBarOnScreen(rect))
            {
                AppWindow.MoveAndResize(rect);
                return;
            }
        }

        // First run or saved monitor is gone: center on the primary monitor.
        var work = DisplayArea.Primary.WorkArea;
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int w = Math.Min((int)(DefaultWidthDip * scale), work.Width);
        int h = Math.Min((int)(DefaultHeightDip * scale), work.Height);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }

    /// <summary>True if enough of the title bar is visible on some monitor to grab and drag it.</summary>
    private static bool IsTitleBarOnScreen(RectInt32 rect)
    {
        var titleStrip = new RectInt32(rect.X, rect.Y, rect.Width, 40);
        var area = DisplayArea.GetFromRect(titleStrip, DisplayAreaFallback.None);
        if (area is null) return false;
        var work = area.WorkArea;
        int visibleW = Math.Min(rect.X + rect.Width, work.X + work.Width) - Math.Max(rect.X, work.X);
        int visibleH = Math.Min(rect.Y + 40, work.Y + work.Height) - Math.Max(rect.Y, work.Y);
        return visibleW >= 100 && visibleH >= 20;
    }

    private void ApplyDwmTheme() =>
        SetDwmInt(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, Root.ActualTheme == ElementTheme.Dark ? 1 : 0);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = Controls.NoteEditor.IsKeyDown(VirtualKey.Control);
        bool shift = Controls.NoteEditor.IsKeyDown(VirtualKey.Shift);
        bool alt = Controls.NoteEditor.IsKeyDown(VirtualKey.Menu); // Ctrl+Alt = AltGr: never ours

        if (IsRecordingHotkey) return; // the shortcut recorder gets every key, Esc included

        if (e.Key == VirtualKey.Escape && !ctrl && !alt)
        {
            if (_searchOpen) CloseSearch();
            else if (_settingsOpen) CloseSettings();
            else HideWindow();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.F && ctrl && !alt && !shift)
        {
            if (_searchOpen) CloseSearch(); else OpenSearch();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.F3 && !ctrl && !alt && !_searchOpen && !_settingsOpen)
        {
            StepSearchHit(shift ? -1 : +1);
            e.Handled = true;
            return;
        }
        if (!ctrl || alt || _settingsOpen || _searchOpen) return;

        switch (e.Key)
        {
            case VirtualKey.N when !shift:
                NewNote();
                break;
            case VirtualKey.W when !shift:
            case VirtualKey.Delete when !shift:
                DeleteCurrentNote();
                break;
            // Ctrl+Shift+←/→ still select by word.
            case VirtualKey.Left when !shift:
            case VirtualKey.PageUp:
                Navigate(_index - 1);
                break;
            case VirtualKey.Right when !shift:
            case VirtualKey.PageDown:
                Navigate(_index + 1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => IsPinned = !IsPinned;

    private void HideButton_Click(object sender, RoutedEventArgs e) => HideWindow();
}
