using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using F1TelemetryOverlay.Core;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfCursors = System.Windows.Input.Cursors;
using WpfPen = System.Windows.Media.Pen;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace F1TelemetryOverlay.Wpf;

/// <summary>Compact, independently movable four-wheel tyre-wear overlay.</summary>
public partial class TyreWearWindow : Window
{
    internal const double BaseSize = 128;
    internal const double DefaultTopOffset = 72;

    private readonly TyreWearSurface _surface;
    private bool _locked;
    private bool _dragging;
    private Point _dragStart;
    private double _windowStartLeft;
    private double _windowStartTop;

    public TyreWearWindow()
    {
        InitializeComponent();
        _surface = Surface;
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += VisibilityChanged;
        _surface.MouseLeftButtonDown += SurfaceMouseLeftButtonDown;
        _surface.MouseMove += SurfaceMouseMove;
        _surface.MouseLeftButtonUp += SurfaceMouseLeftButtonUp;
        _surface.MouseRightButtonUp += SurfaceMouseRightButtonUp;
    }

    internal TyreWearWindow(App app) : this() => Initialize(app);

    internal event Action<double, double>? DragCompleted;

    internal void Initialize(App app)
    {
        _surface.Initialize(app.Settings.TyreWearOverlay);
        ApplySettings(app.Settings);
        SetLocked(app.Settings.TyreWearOverlay.Locked);
    }

    internal void ApplySettings(AppSettings settings)
    {
        OverlayWidgetSettings widget = settings.TyreWearOverlay;
        _surface.ApplySettings(widget);
        Width = BaseSize * widget.Scale;
        Height = BaseSize * widget.Scale;
        Opacity = widget.Opacity;
        SetLocked(widget.Locked);
    }

    internal void SetLocked(bool locked)
    {
        _locked = locked;
        _surface.SetLocked(locked);
        Cursor = locked ? WpfCursors.Arrow : WpfCursors.SizeAll;
    }

    internal void UpdateWear(TyreWearTelemetry telemetry) => _surface.SetTelemetry(telemetry);

    internal void ClearWear() => _surface.ClearTelemetry();

    internal void ShowInactive()
    {
        EnsureVisiblePosition();
        if (!IsVisible) Show();
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) NativeMethods.ShowWindow(handle, 4);
    }

    internal void EnsureVisiblePosition()
    {
        if (!IsFinite(Width) || Width <= 0) Width = BaseSize;
        if (!IsFinite(Height) || Height <= 0) Height = BaseSize;

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
        _surface.ReleaseMouseCapture();
        DragCompleted?.Invoke(Left, Top);
    }

    private static bool IsFinite(double value) => double.IsFinite(value);
}

internal sealed class TyreWearSurface : FrameworkElement
{
    private const double CellGap = 6;
    private const double DiscAlpha = 105;
    private const double RingAlpha = 145;
    private const double MutedAlpha = 175;
    private readonly object _telemetryGate = new();
    private DispatcherTimer? _timer;
    private TyreWearTelemetry? _telemetry;
    private OverlayWidgetSettings _settings = OverlayWidgetSettings.DefaultTyreWear;

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
        Cursor = locked ? WpfCursors.Arrow : WpfCursors.SizeAll;
        InvalidateVisual();
    }

    internal void SetTelemetry(TyreWearTelemetry telemetry)
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
                Interval = TimeSpan.FromMilliseconds(33),
            };
            _timer.Tick += (_, _) => InvalidateVisual();
        }
        _timer.Start();
    }

    internal void StopRendering() => _timer?.Stop();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        TyreWearTelemetry? telemetry;
        lock (_telemetryGate) telemetry = _telemetry;
        IReadOnlyList<double?> values = TyreWearVisuals.DisplayOrder(telemetry);
        double cellWidth = (width - CellGap) / 2;
        double cellHeight = (height - CellGap) / 2;
        for (int index = 0; index < 4; index++)
        {
            int row = index / 2;
            int column = index % 2;
            Rect cell = new(column * (cellWidth + CellGap), row * (cellHeight + CellGap), cellWidth, cellHeight);
            DrawWheel(drawingContext, cell, values[index]);
        }
    }

    // Keep the complete compact widget draggable, including the small gaps
    // between discs, without adding visible chrome or a background rectangle.
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    private static void DrawWheel(DrawingContext drawingContext, Rect cell, double? value)
    {
        Point center = new(cell.Left + (cell.Width / 2), cell.Top + (cell.Height / 2));
        double radius = Math.Max(1, Math.Min(cell.Width, cell.Height) / 2 - 5);
        drawingContext.DrawEllipse(Brush(Color.FromArgb((byte)DiscAlpha, 8, 13, 19)), null,
            center, radius + 2, radius + 2);
        drawingContext.DrawEllipse(null,
            new WpfPen(Brush(Color.FromArgb((byte)RingAlpha, 176, 188, 201)), 1.1), center, radius, radius);

        bool hasValue = value.HasValue && double.IsFinite(value.Value);
        if (hasValue)
        {
            double liveValue = value.GetValueOrDefault();
            double sweep = TyreWearVisuals.SweepDegrees(liveValue);
            WpfPen progressPen = new(Brush(ParseColor(TyreWearVisuals.ColorFor(liveValue))),
                Math.Max(3.2, radius * 0.14))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            if (sweep >= 359.9)
            {
                drawingContext.DrawEllipse(null, progressPen, center, radius, radius);
            }
            else if (sweep > 0)
            {
                drawingContext.DrawGeometry(null, progressPen, Arc(center, radius, sweep));
            }
        }

        string text = TyreWearVisuals.DisplayText(value);
        double fontSize = Math.Clamp(radius * 0.52, 8, 18);
        FormattedText formatted = new(text, CultureInfo.InvariantCulture, WpfFlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            fontSize, Brush(hasValue ? Color.FromArgb(242, 244, 247, 250) : Color.FromArgb((byte)MutedAlpha, 182, 192, 203)), 1);
        drawingContext.DrawText(formatted,
            new Point(center.X - (formatted.Width / 2), center.Y - (formatted.Height / 2)));
    }

    private static Geometry Arc(Point center, double radius, double sweep)
    {
        Point start = new(center.X, center.Y - radius);
        double endRadians = (-90 + sweep) * Math.PI / 180;
        Point end = new(center.X + Math.Cos(endRadians) * radius, center.Y + Math.Sin(endRadians) * radius);
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, sweep > 180,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void InvalidateVisualOnDispatcher()
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else _ = Dispatcher.BeginInvoke(InvalidateVisual, DispatcherPriority.Render);
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)WpfColorConverter.ConvertFromString(value)!; }
        catch (FormatException) { return Colors.White; }
    }

    private static SolidColorBrush Brush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
