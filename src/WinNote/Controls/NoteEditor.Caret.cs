using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace WinNote.Controls;

// Custom caret: RichEditBox's own caret is a thin bar in the text colour with no way to restyle it,
// so it is hidden (CaretType.Null) and a thicker accent-coloured bar is drawn on an overlay Canvas
// at the selection's position instead. It blinks at the system rate and, like the Windows caret,
// stops blinking (stays on) a few seconds after the last move.
public sealed partial class NoteEditor
{
    private const double CaretWidthDip = 3;
    private static readonly TimeSpan s_blinkTimeout = TimeSpan.FromSeconds(5);

    private Canvas? _caretLayer;
    private readonly Rectangle _caret = new() { Width = CaretWidthDip, RadiusX = 1, RadiusY = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private DispatcherQueueTimer? _blinkTimer;
    private long _blinkSinceTicks;
    private bool _hasFocus;
    private bool _windowActive = true;
    private bool _caretQueued;
    private ScrollViewer? _scroller;

    /// <summary>Hosts the caret on <paramref name="layer"/>, a Canvas laid over the editor.</summary>
    internal void AttachCaretLayer(Canvas layer)
    {
        _caretLayer = layer;
        layer.Children.Add(_caret);
        Document.CaretType = CaretType.Null;

        uint blinkMs = Interop.NativeMethods.GetCaretBlinkTime();
        _blinkTimer = DispatcherQueue.CreateTimer();
        _blinkTimer.IsRepeating = true;
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(blinkMs is 0 or uint.MaxValue ? 530 : blinkMs);
        _blinkTimer.Tick += (_, _) => Blink(blinkMs != uint.MaxValue);

        GotFocus += (_, _) => { _hasFocus = true; QueueCaretUpdate(); };
        LostFocus += (_, _) => { _hasFocus = false; QueueCaretUpdate(); };
        SelectionChanged += (_, _) => QueueCaretUpdate();
        TextChanged += (_, _) => QueueCaretUpdate();
        SizeChanged += (_, _) => QueueCaretUpdate();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => QueueCaretUpdate());
        ActualThemeChanged += (_, _) => QueueCaretUpdate();
    }

    /// <summary>The window's activation state: an inactive window shows no caret.</summary>
    internal bool WindowActive
    {
        set
        {
            if (_windowActive == value) return;
            _windowActive = value;
            QueueCaretUpdate();
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_scroller is not null) _scroller.ViewChanged -= OnScrollerViewChanged;
        _scroller = GetTemplateChild("ContentElement") as ScrollViewer;
        if (_scroller is not null) _scroller.ViewChanged += OnScrollerViewChanged;
    }

    private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => QueueCaretUpdate();

    /// <summary>Repositions after layout has caught up with the change (formatting, wrapping, scrolling).</summary>
    private void QueueCaretUpdate()
    {
        if (_caretLayer is null || _caretQueued) return;
        _caretQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _caretQueued = false;
            UpdateCaret();
        });
    }

    private void UpdateCaret()
    {
        if (_caretLayer is null || _blinkTimer is null) return;
        Rect caret = default;
        bool show = _hasFocus && _windowActive && Visibility == Visibility.Visible && Document.Selection.Length == 0
                    && !_composing && TryGetCaretBounds(out caret);
        if (!show)
        {
            _blinkTimer.Stop();
            _caret.Visibility = Visibility.Collapsed;
            return;
        }

        _caret.Fill = new SolidColorBrush(_palette.Accent);
        _caret.Height = caret.Height;
        Canvas.SetLeft(_caret, caret.X);
        Canvas.SetTop(_caret, caret.Y);
        // Every move restarts the blink cycle with the caret visible.
        _caret.Visibility = Visibility.Visible;
        _blinkSinceTicks = Environment.TickCount64;
        _blinkTimer.Stop();
        _blinkTimer.Start();
    }

    private void Blink(bool blinks)
    {
        if (!blinks || Environment.TickCount64 - _blinkSinceTicks > s_blinkTimeout.TotalMilliseconds)
        {
            _caret.Visibility = Visibility.Visible;
            _blinkTimer!.Stop();
            return;
        }
        _caret.Visibility = _caret.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Caret rectangle in the caret layer's coordinates; false if it is scrolled out of view.</summary>
    private bool TryGetCaretBounds(out Rect bounds)
    {
        bounds = default;
        if (_caretLayer is null || _scroller is null) return false;
        Document.Selection.GetRect(PointOptions.ClientCoordinates, out Rect r, out _);
        if (r.Height <= 0) return false;

        // ClientCoordinates (DIPs) are relative to the text area, which sits inside the scroller's padding.
        var origin = _scroller.TransformToVisual(_caretLayer).TransformPoint(new Point(_scroller.Padding.Left, _scroller.Padding.Top));
        bounds = new Rect(origin.X + r.X - CaretWidthDip / 2, origin.Y + r.Y, CaretWidthDip, r.Height);

        // Hide rather than draw over the title bar or footer when the caret is scrolled out.
        double top = origin.Y - _scroller.Padding.Top, bottom = top + _scroller.ViewportHeight;
        return bounds.Y >= top - 1 && bounds.Y + bounds.Height <= bottom + 1;
    }
}
