using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinNote.Controls;

/// <summary>
/// Mica that stays "active" when the window loses focus. The stock MicaBackdrop switches to a flat
/// fallback colour on inactive windows, which makes a pinned note look disabled.
/// </summary>
public sealed partial class ActiveMicaBackdrop : SystemBackdrop
{
    private readonly FrameworkElement _themeSource;
    private MicaController? _controller;
    private SystemBackdropConfiguration? _config;

    /// <param name="themeSource">Element whose ActualTheme the backdrop follows (the window's root).</param>
    public ActiveMicaBackdrop(FrameworkElement themeSource) => _themeSource = themeSource;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!MicaController.IsSupported()) return;

        _config = new SystemBackdropConfiguration { IsInputActive = true };
        _themeSource.ActualThemeChanged += OnThemeChanged;
        OnThemeChanged(_themeSource, null);

        _controller = new MicaController();
        _controller.SetSystemBackdropConfiguration(_config);
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _themeSource.ActualThemeChanged -= OnThemeChanged;
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }

    /// <summary>
    /// We drive the controller with our own configuration, so ignore the default one. The base
    /// implementation applies it to the target and throws ArgumentException ("The parameter is
    /// incorrect") on some configuration changes, which crashed the app (error.log, 2026-10-04/28).
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
    }

    private void OnThemeChanged(FrameworkElement sender, object? args)
    {
        if (_config is null) return;
        _config.Theme = sender.ActualTheme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
    }
}
