using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using F1TelemetryOverlay.Core;
using WpfBrush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPen = System.Windows.Media.Pen;

namespace F1TelemetryOverlay.Wpf;

/// <summary>Compact, independently movable tyre-surface and brake-temperature overlay.</summary>
public partial class TemperatureWindow : Window
{
    internal const double BaseWidth = 164;
    internal const double BaseHeight = 116;
    internal const double DefaultTopOffset = 220;

    private readonly TemperatureSurface _surface;
    private bool _locked;
    private bool _persistedLocked;
    private bool _arranging;
    private bool _dragging;
    private Point _dragStart;
    private double _windowStartLeft;
    private double _windowStartTop;

    public TemperatureWindow()
    {
        InitializeComponent();
        _surface = Surface;
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += VisibilityChanged;
        Closed += WindowClosed;
        _surface.MouseLeftButtonDown += SurfaceMouseLeftButtonDown;
        _surface.MouseMove += SurfaceMouseMove;
        _surface.MouseLeftButtonUp += SurfaceMouseLeftButtonUp;
        _surface.LostMouseCapture += SurfaceLostMouseCapture;
        _surface.MouseRightButtonUp += SurfaceMouseRightButtonUp;
    }

    internal TemperatureWindow(App app) : this() => Initialize(app);

    internal event Action<double, double>? DragCompleted;

    internal void Initialize(App app)
    {
        _surface.Initialize(app.Settings.TemperatureOverlay);
        ApplySettings(app.Settings);
        SetLocked(app.Settings.TemperatureOverlay.Locked);
    }

    internal void ApplySettings(AppSettings settings)
    {
        OverlayWidgetSettings widget = settings.TemperatureOverlay;
        _surface.ApplySettings(widget);
        Width = BaseWidth * widget.Scale;
        Height = BaseHeight * widget.Scale;
        Opacity = widget.Opacity;
        SetLocked(widget.Locked);
    }

    internal void SetLocked(bool locked)
    {
        _persistedLocked = locked;
        _locked = _arranging ? false : locked;
        _surface.SetLocked(_locked);
        Cursor = _locked ? WpfCursors.Arrow : WpfCursors.SizeAll;
    }

    internal void SetArrangeMode(bool arranging)
    {
        _arranging = arranging;
        _locked = arranging ? false : _persistedLocked;
        _surface.SetArrangeMode(arranging);
        _surface.SetLocked(_locked);
        Cursor = _locked ? WpfCursors.Arrow : WpfCursors.SizeAll;
    }

    internal void UpdateTemperatures(TemperatureTelemetry telemetry) => _surface.SetTelemetry(telemetry);

    internal void ClearTemperatures() => _surface.ClearTelemetry();

    internal void ShowInactive()
    {
        EnsureVisiblePosition();
        if (!IsVisible) Show();
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) NativeMethods.ShowWindow(handle, 4);
    }

    internal void EnsureVisiblePosition()
    {
        if (!IsFinite(Width) || Width <= 0) Width = BaseWidth;
        if (!IsFinite(Height) || Height <= 0) Height = BaseHeight;

        Rect virtualScreen = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        double right = Left + Width;
        double bottom = Top + Height;
        bool invalid = !IsFinite(Left) || !IsFinite(Top) || !IsFinite(right) || !IsFinite(bottom);
        bool outside = virtualScreen.Width <= 0 || virtualScreen.Height <= 0
            || right <= virtualScreen.Left || Left >= virtualScreen.Right
            || bottom <= virtualScreen.Top || Top >= virtualScreen.Bottom;
        if (!invalid && !outside) return;

        Rect area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0) return;
        Left = area.Left + Math.Max(0, area.Width - Width - 40);
        Top = area.Top + Math.Clamp(DefaultTopOffset, 0, Math.Max(0, area.Height - Height));
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        IntPtr styles = TyreWearNativeMethods.GetWindowLongPtr(handle, TyreWearNativeMethods.GwlExStyle);
        TyreWearNativeMethods.SetWindowLongPtr(handle, TyreWearNativeMethods.GwlExStyle,
            new IntPtr(styles.ToInt64() | TyreWearNativeMethods.WsExNoActivate | TyreWearNativeMethods.WsExToolWindow));
    }

    private void VisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue) _surface.StartRendering();
        else _surface.StopRendering();
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        StopDragging();
        _surface.StopRendering();
    }

    private void SurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked || e.ChangedButton != MouseButton.Left) return;
        _dragging = true;
        _dragStart = PointerPositionInDips(e);
        _windowStartLeft = Left;
        _windowStartTop = Top;
        _surface.CaptureMouse();
        e.Handled = true;
    }

    private void SurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _locked || e.LeftButton != MouseButtonState.Pressed) return;
        Point current = PointerPositionInDips(e);
        Left = Math.Round(_windowStartLeft + current.X - _dragStart.X);
        Top = Math.Round(_windowStartTop + current.Y - _dragStart.Y);
        e.Handled = true;
    }

    private Point PointerPositionInDips(MouseEventArgs e)
    {
        Point screenPixels = PointToScreen(e.GetPosition(this));
        Matrix fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        return fromDevice.Transform(screenPixels);
    }

    private void SurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        StopDragging();
        e.Handled = true;
    }

    private void SurfaceLostMouseCapture(object sender, MouseEventArgs e) => StopDragging();

    private void SurfaceMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopDragging();
        if (App.Current is App app) app.ShowControlMenu();
        e.Handled = true;
    }

    private void StopDragging()
    {
        if (!_dragging) return;
        _dragging = false;
        if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();
        DragCompleted?.Invoke(Left, Top);
    }

    private static bool IsFinite(double value) => double.IsFinite(value);
}

internal sealed class TemperatureSurface : FrameworkElement
{
    private const double CellGap = 6;
    private const double CornerRadius = 7;
    private const byte PanelAlpha = 122;
    private const byte TintAlpha = 29;
    private const byte BorderAlpha = 220;
    private const byte MutedAlpha = 175;

    private readonly object _telemetryGate = new();
    private readonly Func<long> _clock;
    private DispatcherTimer? _timer;
    private TemperatureTelemetry? _telemetry;
    private OverlayWidgetSettings _settings = OverlayWidgetSettings.DefaultTemperature;
    private bool _arranging;
    private bool _locked;

    public TemperatureSurface() : this(static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { }

    internal TemperatureSurface(Func<long> clock) =>
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    internal bool IsRendering => _timer?.IsEnabled == true;
    internal bool IsArranging => _arranging;
    internal bool IsLocked => _locked;

    internal void Initialize(OverlayWidgetSettings settings)
    {
        _settings = settings;
        Cursor = WpfCursors.SizeAll;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    internal void ApplySettings(OverlayWidgetSettings settings)
    {
        _settings = settings;
        InvalidateVisual();
    }

    internal void SetLocked(bool locked)
    {
        _locked = locked;
        Cursor = locked ? WpfCursors.Arrow : WpfCursors.SizeAll;
        InvalidateVisual();
    }

    internal void SetArrangeMode(bool arranging)
    {
        _arranging = arranging;
        InvalidateVisual();
    }

    internal void SetTelemetry(TemperatureTelemetry telemetry)
    {
        lock (_telemetryGate) _telemetry = telemetry;
        InvalidateVisualOnDispatcher();
    }

    internal void ClearTelemetry()
    {
        lock (_telemetryGate) _telemetry = null;
        InvalidateVisualOnDispatcher();
    }

    internal void StartRendering()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(100),
            };
            _timer.Tick += (_, _) => InvalidateVisual();
        }
        _timer.Start();
    }

    internal void StopRendering() => _timer?.Stop();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double actualWidth = ActualWidth;
        double actualHeight = ActualHeight;
        if (actualWidth <= 0 || actualHeight <= 0) return;

        // Draw in one fixed design coordinate space so all content, including
        // four-digit brake values, scales together without spilling at 0.5x.
        drawingContext.PushTransform(new ScaleTransform(
            actualWidth / TemperatureWindow.BaseWidth,
            actualHeight / TemperatureWindow.BaseHeight));
        const double width = TemperatureWindow.BaseWidth;
        const double height = TemperatureWindow.BaseHeight;

        TemperatureTelemetry? telemetry;
        lock (_telemetryGate) telemetry = _telemetry;
        IReadOnlyList<WheelTemperatures?> values = TemperatureVisuals.DisplayOrder(telemetry, _clock());
        double cellWidth = (width - CellGap) / 2;
        double cellHeight = (height - CellGap) / 2;
        for (int index = 0; index < 4; index++)
        {
            int row = index / 2;
            int column = index % 2;
            Rect cell = new(column * (cellWidth + CellGap), row * (cellHeight + CellGap), cellWidth, cellHeight);
            DrawWheel(drawingContext, cell, values[index]);
        }

        if (_arranging)
        {
            drawingContext.DrawRectangle(null,
                new WpfPen(Brush(Color.FromArgb(180, 225, 48, 43)), 1)
                {
                    DashStyle = new DashStyle([4, 4], 0),
                },
                new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1)));
        }
        drawingContext.Pop();
    }

    // Make the transparent gaps draggable without adding visible chrome.
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    private static void DrawWheel(DrawingContext drawingContext, Rect cell, WheelTemperatures? wheel)
    {
        bool hasValue = wheel is not null;
        Color surfaceColor = hasValue
            ? ParseColor(TemperatureVisuals.ColorFor(TemperatureMetric.Surface, wheel!.SurfaceCelsius))
            : Color.FromArgb((byte)MutedAlpha, 116, 129, 143);
        Color brakeColor = hasValue
            ? ParseColor(TemperatureVisuals.ColorFor(TemperatureMetric.Brake, wheel!.BrakeCelsius))
            : Color.FromArgb((byte)MutedAlpha, 116, 129, 143);

        Rect panel = new(cell.Left + 0.5, cell.Top + 0.5,
            Math.Max(0, cell.Width - 1), Math.Max(0, cell.Height - 1));
        drawingContext.DrawRoundedRectangle(
            Brush(Color.FromArgb((byte)PanelAlpha, 8, 13, 19)),
            new WpfPen(Brush(Color.FromArgb((byte)BorderAlpha, surfaceColor.R, surfaceColor.G, surfaceColor.B)), 1.15),
            panel, CornerRadius, CornerRadius);
        if (hasValue)
        {
            drawingContext.DrawRoundedRectangle(
                Brush(Color.FromArgb((byte)TintAlpha, surfaceColor.R, surfaceColor.G, surfaceColor.B)),
                null, new Rect(panel.Left + 1, panel.Top + 1, Math.Max(0, panel.Width - 2), Math.Max(0, panel.Height - 2)),
                CornerRadius - 1, CornerRadius - 1);
        }

        double top = cell.Top + Math.Max(4, cell.Height * 0.10);
        double rowHeight = Math.Max(17, cell.Height * 0.39);
        DrawMetric(drawingContext, cell, top, rowHeight, "T",
            TemperatureVisuals.DisplayText(wheel?.SurfaceCelsius), surfaceColor, hasValue);
        DrawMetric(drawingContext, cell, top + rowHeight, rowHeight, "B",
            TemperatureVisuals.DisplayText(wheel?.BrakeCelsius), brakeColor, hasValue);
    }

    private static void DrawMetric(DrawingContext drawingContext, Rect cell, double top, double rowHeight,
        string glyph, string value, Color color, bool hasValue)
    {
        double glyphRadius = Math.Clamp(rowHeight * 0.22, 3.5, 6);
        Point glyphCenter = new(cell.Left + Math.Max(11, cell.Width * 0.18), top + rowHeight / 2);
        drawingContext.DrawEllipse(
            Brush(Color.FromArgb(hasValue ? (byte)48 : (byte)25, color.R, color.G, color.B)),
            new WpfPen(Brush(Color.FromArgb(hasValue ? (byte)220 : (byte)125, color.R, color.G, color.B)), 0.9),
            glyphCenter, glyphRadius, glyphRadius);

        FormattedText glyphText = Text(glyph, Math.Clamp(rowHeight * 0.25, 6.5, 9),
            Brush(Color.FromArgb(238, 242, 246, 250)), FontWeights.Bold);
        drawingContext.DrawText(glyphText,
            new Point(glyphCenter.X - glyphText.Width / 2, glyphCenter.Y - glyphText.Height / 2));

        FormattedText valueText = Text(value, Math.Clamp(rowHeight * 0.47, 10, 16),
            Brush(hasValue ? Color.FromArgb(246, color.R, color.G, color.B) : Color.FromArgb((byte)MutedAlpha, 182, 192, 203)),
            FontWeights.SemiBold);
        double valueLeft = glyphCenter.X + glyphRadius + Math.Max(5, cell.Width * 0.06);
        drawingContext.DrawText(valueText,
            new Point(valueLeft, top + (rowHeight - valueText.Height) / 2));
    }

    private static FormattedText Text(string value, double size, WpfBrush brush, FontWeight weight) =>
        new(value, CultureInfo.InvariantCulture, WpfFlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, 1);

    private void InvalidateVisualOnDispatcher()
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else _ = Dispatcher.BeginInvoke(InvalidateVisual, DispatcherPriority.Render);
    }

    private static Color ParseColor(string value) => (Color)WpfColorConverter.ConvertFromString(value)!;

    private static SolidColorBrush Brush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
