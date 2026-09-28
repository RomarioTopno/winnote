using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.System;
using WinNote.Controls;
using WinNote.Services;
using static WinNote.Interop.NativeMethods;

namespace WinNote.Views;

/// <summary>Settings page shown inside the main window. Created on first use.</summary>
public sealed partial class SettingsView : UserControl
{
    private static readonly (string Label, string Family)[] s_fonts =
    [
        ("Segoe UI Variable", "Segoe UI Variable Text"),
        ("Cascadia Mono", "Cascadia Mono"),
        ("Consolas", "Consolas"),
    ];
    private static readonly int[] s_fontSizes = [11, 12, 13, 14, 15, 16, 18, 20, 22, 24];
    private static readonly int[] s_autoDeleteDays = [0, 1, 3, 7, 14, 30, 90];

    private readonly SettingsStore _settings;
    private readonly HotkeyService _hotkey;
    private readonly Action _appearanceChanged;
    private readonly Action _close;
    private bool _populating;

    /// <summary>True while the shortcut recorder is waiting for a key; it then receives every key, Esc included.</summary>
    public bool IsRecordingHotkey { get; private set; }

    internal SettingsView(SettingsStore settings, HotkeyService hotkey, Action appearanceChanged, Action close)
    {
        _settings = settings;
        _hotkey = hotkey;
        _appearanceChanged = appearanceChanged;
        _close = close;
        InitializeComponent();

        foreach (var (label, family) in s_fonts)
        {
            bool installed = IsFontInstalled(family);
            FontCombo.Items.Add(new ComboBoxItem
            {
                Content = installed ? label : $"{label} (not installed)",
                Tag = family,
                IsEnabled = installed,
            });
        }
        foreach (int size in s_fontSizes)
            SizeCombo.Items.Add(new ComboBoxItem { Content = size.ToString(), Tag = size });
        foreach (int days in s_autoDeleteDays)
            AutoDeleteCombo.Items.Add(new ComboBoxItem { Content = days switch { 0 => "Never", 1 => "1 day", _ => $"{days} days" }, Tag = days });

        var version = typeof(SettingsView).Assembly.GetName().Version;
        VersionText.Text = $"WinNote {version?.ToString(3)} · {AppPaths.DataDir}";
    }

    /// <summary>Refreshes all controls from the current settings (called each time the page opens).</summary>
    public void Populate()
    {
        _populating = true;
        try
        {
            var s = _settings.Settings;
            UpdateHotkeyDisplay();
            ThemeCombo.SelectedIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
            FontCombo.SelectedIndex = Math.Max(0, Array.FindIndex(s_fonts, f => f.Family == EffectiveFont(s.FontFamily)));
            SizeCombo.SelectedIndex = IndexOfNearest(s_fontSizes, (int)Math.Round(s.FontSize));
            AutoDeleteCombo.SelectedIndex = IndexOfNearest(s_autoDeleteDays, s.AutoDeleteDays);
            StartupToggle.IsOn = StartupService.IsEnabled;
        }
        finally
        {
            _populating = false;
        }
    }

    public void FocusFirst() => HotkeyButton.Focus(FocusState.Programmatic);

    /// <summary>The font actually used: falls back to Segoe UI Variable if the chosen one is missing.</summary>
    internal static string EffectiveFont(string family) => IsFontInstalled(family) ? family : "Segoe UI Variable Text";

    internal static bool IsFontInstalled(string family)
    {
        string name = family == "Segoe UI Variable Text" ? "Segoe UI Variable" : family;
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key?.GetValueNames().Any(v => v.StartsWith(name, StringComparison.OrdinalIgnoreCase)) == true) return true;
        }
        return false;
    }

    private static int IndexOfNearest(int[] values, int value)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
        return best;
    }

    // ---- Appearance & behaviour ----

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || ThemeCombo.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        _settings.Settings.Theme = theme;
        _settings.Save();
        _appearanceChanged();
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || FontCombo.SelectedItem is not ComboBoxItem { Tag: string family }) return;
        _settings.Settings.FontFamily = family;
        _settings.Save();
        _appearanceChanged();
    }

    private void SizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || SizeCombo.SelectedItem is not ComboBoxItem { Tag: int size }) return;
        _settings.Settings.FontSize = size;
        _settings.Save();
        _appearanceChanged();
    }

    private void AutoDeleteCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || AutoDeleteCombo.SelectedItem is not ComboBoxItem { Tag: int days }) return;
        _settings.Settings.AutoDeleteDays = days;
        _settings.Save();
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_populating) return;
        try
        {
            StartupService.SetEnabled(StartupToggle.IsOn);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        _populating = true;
        StartupToggle.IsOn = StartupService.IsEnabled; // reflect what actually happened
        _populating = false;
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start("explorer.exe", $"\"{AppPaths.DataDir}\"");

    private void Back_Click(object sender, RoutedEventArgs e) => _close();

    // ---- Shortcut recorder ----

    private Hotkey SavedHotkey => new(_settings.Settings.HotkeyModifiers, _settings.Settings.HotkeyKey);

    private void UpdateHotkeyDisplay(string? status = null, bool isError = false)
    {
        HotkeyButton.Content = IsRecordingHotkey ? "Press a shortcut…" : SavedHotkey.ToString();

        if (status is null)
        {
            if (IsRecordingHotkey) status = "Press Esc to cancel.";
            else if (_hotkey.LastError is { } error) (status, isError) = (error, true);
        }
        HotkeyStatus.Text = status ?? "";
        HotkeyStatus.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        if (isError)
        {
            // Fluent "critical" text colour for the page's actual (possibly overridden) theme.
            HotkeyStatus.Foreground = new SolidColorBrush(ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 255, 153, 164)
                : Windows.UI.Color.FromArgb(255, 196, 43, 28));
        }
        else
        {
            HotkeyStatus.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecordingHotkey) return;
        IsRecordingHotkey = true;
        _hotkey.Unregister(); // otherwise pressing the current combo would just toggle the window
        UpdateHotkeyDisplay();
    }

    private void HotkeyButton_LostFocus(object sender, RoutedEventArgs e) => StopRecording(apply: null);

    private void HotkeyButton_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsRecordingHotkey) return;
        e.Handled = true;

        var key = e.Key;
        if (key == VirtualKey.Escape)
        {
            StopRecording(apply: null);
            return;
        }
        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu)
            return; // wait for the actual key

        uint mods = 0;
        if (NoteEditor.IsKeyDown(VirtualKey.Control)) mods |= MOD_CONTROL;
        if (NoteEditor.IsKeyDown(VirtualKey.Menu)) mods |= MOD_ALT;
        if (NoteEditor.IsKeyDown(VirtualKey.Shift)) mods |= MOD_SHIFT;
        if (NoteEditor.IsKeyDown(VirtualKey.LeftWindows) || NoteEditor.IsKeyDown(VirtualKey.RightWindows)) mods |= MOD_WIN;

        var candidate = new Hotkey(mods, (uint)key);
        if (!candidate.IsValid)
        {
            UpdateHotkeyDisplay($"{candidate} needs Ctrl, Alt or Win as well.", isError: true);
            return; // keep recording
        }
        StopRecording(apply: candidate);
    }

    /// <summary>
    /// Ends recording (no-op if not recording). With <paramref name="apply"/>, registers the new combo,
    /// or keeps the old one and explains why if another app already owns it.
    /// </summary>
    internal void StopRecording(Hotkey? apply)
    {
        if (!IsRecordingHotkey) return;
        IsRecordingHotkey = false;
        ApplyHotkey(apply);
    }

    private void ApplyHotkey(Hotkey? apply)
    {
        var previous = SavedHotkey;
        if (apply is { } hk && hk != previous)
        {
            if (_hotkey.TryRegister(hk, out string? error))
            {
                _settings.Settings.HotkeyModifiers = hk.Modifiers;
                _settings.Settings.HotkeyKey = hk.Key;
                _settings.Save();
                UpdateHotkeyDisplay($"Saved. Press {hk} anywhere to show or hide WinNote.");
                return;
            }
            bool restored = _hotkey.TryRegister(previous, out _);
            UpdateHotkeyDisplay(restored ? $"{error} Keeping {previous}." : error, isError: true);
            return;
        }
        _hotkey.TryRegister(previous, out _); // re-register (also retries a combo that failed at startup)
        UpdateHotkeyDisplay();
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        IsRecordingHotkey = false;
        ApplyHotkey(Hotkey.Default);
    }
}
