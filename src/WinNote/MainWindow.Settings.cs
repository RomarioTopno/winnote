using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinNote.Services;
using WinNote.Views;

namespace WinNote;

// Hosting the settings page, and applying appearance settings.
public sealed partial class MainWindow
{
    private SettingsView? _settingsView; // created on first open
    private bool _settingsOpen;

    private bool IsRecordingHotkey => _settingsView?.IsRecordingHotkey == true;

    /// <summary>Theme and editor font from settings; called at startup and when they change.</summary>
    private void ApplyAppearance()
    {
        var s = _settings.Settings;
        Root.RequestedTheme = s.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // Tray menu is a native Win32 menu: match it to the chosen theme.
        Interop.NativeMethods.SetMenuAppMode(s.Theme switch { "Dark" => 2, "Light" => 3, _ => 1 });
        string family = SettingsView.EffectiveFont(s.FontFamily);
        if (Editor.FontFamily.Source != family) Editor.FontFamily = new FontFamily(family);
        double size = Math.Clamp(s.FontSize, 8, 48);
        if (Editor.FontSize != size) Editor.FontSize = size;
    }

    public void OpenSettings()
    {
        if (_settingsOpen) return;
        if (_settingsView is null)
        {
            _settingsView = new SettingsView(_settings, _hotkey, ApplyAppearance, CloseSettings);
            SettingsHost.Child = _settingsView;
        }
        _settingsView.Populate();
        _settingsOpen = true;
        SettingsHost.Visibility = Visibility.Visible;
        Editor.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Collapsed;
        HideToast(expired: true);
        _settingsView.FocusFirst();
    }

    public void CloseSettings()
    {
        if (!_settingsOpen) return;
        _settingsView?.StopRecording(apply: null);
        _settingsOpen = false;
        SettingsHost.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Footer.Visibility = Visibility.Visible;
        FocusEditor();
    }

    /// <summary>From the tray menu.</summary>
    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            StartupService.SetEnabled(enabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ShowToast($"Couldn't change startup setting: {ex.Message}");
        }
        if (_settingsOpen) _settingsView!.Populate();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsOpen) CloseSettings(); else OpenSettings();
    }
}
