using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WinNote.Services;

namespace WinNote;

// Show animation and optional timing trace.
public sealed partial class MainWindow
{
    private static readonly bool s_perfTrace = Environment.GetEnvironmentVariable("WINNOTE_PERF") == "1";
    private static readonly Windows.UI.ViewManagement.UISettings s_uiSettings = new();

    private Visual? _rootVisual;
    private ScalarKeyFrameAnimation? _fadeIn;
    private Vector3KeyFrameAnimation? _slideIn;

    /// <summary>
    /// Content fades/slides in on the compositor thread; the window itself is already shown at full
    /// size, so this never delays it appearing. Skipped when Windows animations are turned off.
    /// </summary>
    private void PlayShowAnimation()
    {
        if (!s_uiSettings.AnimationsEnabled) return;

        if (_rootVisual is null)
        {
            _rootVisual = ElementCompositionPreview.GetElementVisual(Root);
            ElementCompositionPreview.SetIsTranslationEnabled(Root, true);
            var compositor = _rootVisual.Compositor;
            var easeOut = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

            _fadeIn = compositor.CreateScalarKeyFrameAnimation();
            _fadeIn.InsertKeyFrame(0f, 0f);
            _fadeIn.InsertKeyFrame(1f, 1f, easeOut);
            _fadeIn.Duration = TimeSpan.FromMilliseconds(120);

            _slideIn = compositor.CreateVector3KeyFrameAnimation();
            _slideIn.InsertKeyFrame(0f, new Vector3(0, 10, 0));
            _slideIn.InsertKeyFrame(1f, Vector3.Zero, easeOut);
            _slideIn.Duration = TimeSpan.FromMilliseconds(160);
        }
        _rootVisual.StartAnimation("Opacity", _fadeIn);
        _rootVisual.StartAnimation("Translation", _slideIn);
    }

    private bool _accentWatch;

    /// <summary>
    /// While pinned, the window gets a 1 px border in the system accent colour (Windows 11 DWM
    /// border colour), so a pinned note is recognisable even when its title bar is covered.
    /// </summary>
    private void UpdatePinnedBorder()
    {
        if (!_accentWatch)
        {
            _accentWatch = true;
            s_uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePinnedBorder);
        }
        int colorRef = unchecked((int)Interop.NativeMethods.DWMWA_COLOR_DEFAULT);
        if (IsPinned)
        {
            var accent = s_uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            colorRef = accent.R | (accent.G << 8) | (accent.B << 16); // COLORREF is 0x00BBGGRR
        }
        Interop.NativeMethods.SetDwmInt(_hwnd, Interop.NativeMethods.DWMWA_BORDER_COLOR, colorRef);
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _trimTimer;

    /// <summary>
    /// A few seconds after hiding, give Windows back the pages we're not using. Most of WinNote's
    /// working set is WinUI/DirectComposition start-up data that is touched once; pages that are
    /// needed again are soft-faulted back from the standby list on the next show (measured: no
    /// visible delay). Private commit is unchanged; this lowers the RAM actually held.
    /// </summary>
    internal void ScheduleMemoryTrim(TimeSpan delay)
    {
        if (!_settings.Settings.TrimMemoryWhenHidden) return;
        if (_trimTimer is null)
        {
            _trimTimer = DispatcherQueue.CreateTimer();
            _trimTimer.IsRepeating = false;
            _trimTimer.Tick += (_, _) =>
            {
                if (AppWindow.IsVisible) return;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                Interop.NativeMethods.SetProcessWorkingSetSize(Interop.NativeMethods.GetCurrentProcess(), -1, -1);
            };
        }
        _trimTimer.Interval = delay;
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    /// <summary>With WINNOTE_PERF=1, logs the time from the show request to the first rendered frame.</summary>
    private void TraceShow(long startTimestamp)
    {
        if (!s_perfTrace) return;
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            double ms = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataDir, "perf.log"),
                    $"[{DateTimeOffset.Now:u}] show -> first frame: {ms:F1} ms\n");
            }
            catch (IOException) { }
        }
        CompositionTarget.Rendering += OnRendering;
    }
}
