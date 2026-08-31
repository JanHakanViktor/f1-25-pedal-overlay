using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using F1TelemetryOverlay.Core;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace F1TelemetryOverlay.Wpf;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly OverlaySurface _surface;
    private bool _dragging;
    private Point _dragStart;
    private double _windowStartLeft;
    private double _windowStartTop;

    internal MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        // Width is part of the initial window geometry, not just a drawing
        // concern. Set it before the app positions the window so a startup
        // --steering launch preserves its right edge at the correct size.
        Width = App.OverlayWidth + (app.IsSteeringEnabled ? App.SteeringWidth : 0);
        Height = App.OverlayHeight;
        _surface = Surface;
        _surface.Initialize(app);
        IsVisibleChanged += VisibilityChanged;
        SourceInitialized += (_, _) =>
        {
            HwndSource? source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowHook);
        };
        _surface.MouseLeftButtonDown += SurfaceMouseLeftButtonDown;
        _surface.MouseMove += SurfaceMouseMove;
        _surface.MouseLeftButtonUp += SurfaceMouseLeftButtonUp;
        _surface.MouseRightButtonUp += SurfaceMouseRightButtonUp;
    }

    internal void UpdateTelemetry(PedalTelemetry telemetry)
    {
        // SetTelemetry only updates the coalesced target under a lock. Do not
        // enqueue one dispatcher callback per UDP packet: the game can send
        // considerably faster than the overlay renders, and those callbacks
        // would make the graph consume stale telemetry seconds later.
        _surface.SetTelemetry(telemetry);
    }

    internal void SetLocked(bool locked)
    {
        _surface.SetLocked(locked);
    }

    internal void SetSteeringEnabled(bool enabled)
    {
        // WPF can retain a fractional DIP position after composition. Snap the
        // preserved edge down to a physical pixel before changing the width so
        // repeated toggles cannot accumulate a one-pixel drift.
        double right = Math.Floor(Left + Width);
        double nextWidth = App.OverlayWidth + (enabled ? App.SteeringWidth : 0);
        Width = nextWidth;
        Height = App.OverlayHeight;
        Left = right - nextWidth;
        _surface.SetSteeringEnabled(enabled);
    }

    internal void SetSteeringPosition(SteeringPosition position)
    {
        _surface.SetSteeringPosition(position);
    }

    internal void SetDemoEnabled(bool enabled) => _surface.SetDemoEnabled(enabled);

    internal void ApplySettings(AppSettings settings) => _surface.ApplySettings(settings);

    internal void ShowInactive()
    {
        EnsureVisiblePosition();
        if (!IsVisible) Show();
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        // SW_SHOWNOACTIVATE shows a hidden window without activating it. Keep
        // this native step even after WPF Show(): a titled HWND may exist while
        // still being hidden, and the overlay must never steal game focus.
        const int showNoActivate = 4;
        NativeMethods.ShowWindow(handle, showNoActivate);
    }

    internal void EnsureVisiblePosition()
    {
        if (!IsFinite(Width) || Width <= 0)
        {
            Width = App.OverlayWidth + (_app.IsSteeringEnabled ? App.SteeringWidth : 0);
        }
        if (!IsFinite(Height) || Height <= 0) Height = App.OverlayHeight;

        Rect virtualScreen = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        double left = Left;
        double top = Top;
        double right = left + Width;
        double bottom = top + Height;
        bool invalidPosition = !IsFinite(left) || !IsFinite(top)
            || !IsFinite(right) || !IsFinite(bottom);
        bool outsideVirtualScreen = virtualScreen.Width <= 0 || virtualScreen.Height <= 0
            || right <= virtualScreen.Left || left >= virtualScreen.Right
            || bottom <= virtualScreen.Top || top >= virtualScreen.Bottom;
        if (!invalidPosition && !outsideVirtualScreen) return;

        Rect area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0) return;

        Left = area.Left + area.Width - Width - 40d;
        Top = area.Top + Math.Max(0d, (area.Height - Height) / 2d);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private void VisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue) _surface.StartRendering();
        else _surface.StopRendering();
    }

    private void SurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_app.IsLocked || e.ChangedButton != MouseButton.Left) return;
        _dragging = true;
        _dragStart = PointerPositionInDips(e);
        _windowStartLeft = Left;
        _windowStartTop = Top;
        _surface.CaptureMouse();
        e.Handled = true;
    }

    private void SurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _app.IsLocked || e.LeftButton != MouseButtonState.Pressed) return;
        Point current = PointerPositionInDips(e);
        Left = Math.Round(_windowStartLeft + current.X - _dragStart.X);
        Top = Math.Round(_windowStartTop + current.Y - _dragStart.Y);
        e.Handled = true;
    }

    private Point PointerPositionInDips(MouseEventArgs e)
    {
        // PointToScreen returns physical pixels. Window.Left/Top are WPF DIPs,
        // so transform the captured pointer before calculating the delta. This
        // keeps dragging one-for-one on displays using 125%, 150%, or 200% DPI.
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
        _app.ShowControlMenu();
        e.Handled = true;
    }

    private void StopDragging()
    {
        if (!_dragging) return;
        _dragging = false;
        _surface.ReleaseMouseCapture();
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.ShowOverlayMessage)
        {
            _app.ShowOverlay();
            handled = true;
        }

        return IntPtr.Zero;
    }
}

internal sealed class OverlaySurface : FrameworkElement
{
    private const double Padding = 12;
    private const double ColumnGap = 12;
    private const double BarWidth = 30;
    private const double BarInset = 8;
    private const double BarGap = 8;
    private const double BarsTrailingGap = 20;
    // Reserve the bar group plus its explicit 20-DIP window-edge gutter. The
    // shared reservation keeps the graph's right edge and bar origin aligned.
    private const double BarsWidth = BarInset + BarWidth + BarGap + BarWidth
        + BarsTrailingGap - Padding;
    private const double RenderIntervalMilliseconds = 16;
    private const double SampleIntervalMilliseconds = 40;
    private const double InputResponseMilliseconds = 30;
    private const double MaxSteeringDegrees = 180;
    private const double SteeringMarkerArcDegrees = 90;
    // Match the settings window's dark-blue surface instead of compositing a
    // black rectangle over the game. The configured transparency still controls
    // how strongly this colour tints the game behind the overlay.
    private static readonly Color OverlayBackgroundColor = Color.FromRgb(0x11, 0x16, 0x1C);
    private static readonly Color GraphBackgroundColor = Color.FromRgb(0x0D, 0x14, 0x1C);
    private static readonly Color BarBackgroundColor = Color.FromRgb(0x0B, 0x11, 0x18);
    private const string ThrottleColor = "#42e37c";
    private const string BrakeColor = "#ff4261";

    private readonly object _telemetryGate = new();
    private readonly List<HistorySample> _history = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherTimer? _timer;
    private App? _app;
    private PedalTelemetry _targetTelemetry = new(0, 0, 0, 0, BrakeLockup.None, 0);
    private double _shownThrottle;
    private double _shownBrake;
    private double _shownSteering;
    private double _lastRenderAt;
    private double _lastSampleAt = -SampleIntervalMilliseconds;
    private bool _steeringEnabled;
    private SteeringPosition _steeringPosition = SteeringPosition.Left;
    private bool _locked;
    private AppSettings _settings = AppSettings.Default;

    internal void Initialize(App app)
    {
        _app = app;
        _settings = app.Settings;
        _steeringEnabled = app.IsSteeringEnabled;
        _steeringPosition = _settings.SteeringPosition;
        _locked = app.IsLocked;
        Cursor = Cursors.SizeAll;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    internal void StartRendering()
    {
        _timer ??= CreateTimer();
        _timer.Start();
    }

    internal void StopRendering() => _timer?.Stop();

    internal void SetTelemetry(PedalTelemetry telemetry)
    {
        lock (_telemetryGate) _targetTelemetry = telemetry;
    }

    internal void SetLocked(bool locked)
    {
        _locked = locked;
        Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        InvalidateVisual();
    }

    internal void SetSteeringEnabled(bool enabled)
    {
        _steeringEnabled = enabled;
        InvalidateVisual();
    }

    internal void SetSteeringPosition(SteeringPosition position)
    {
        _steeringPosition = position;
        InvalidateVisual();
    }

    internal void SetDemoEnabled(bool enabled)
    {
        _ = enabled;
    }

    internal void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _steeringPosition = settings.SteeringPosition;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        bool hasSteering = _steeringEnabled;
        bool steeringOnRight = hasSteering && _steeringPosition == SteeringPosition.Right;
        double radius = Math.Min(9, height / 2);
        Geometry background = CreateRoundedRectangle(new Rect(0.5, 0.5, width - 1, height - 1),
            hasSteering && !steeringOnRight ? height / 2 : radius,
            steeringOnRight ? height / 2 : radius);
        drawingContext.DrawGeometry(Brush(Color.FromArgb(ClampAlpha(_settings.OverlayTransparency),
                OverlayBackgroundColor.R, OverlayBackgroundColor.G, OverlayBackgroundColor.B)),
            new Pen(Brush(Color.FromArgb(23, 255, 255, 255)), 1), background);

        double contentTop = Padding;
        double contentHeight = Math.Max(1, height - (Padding * 2));
        // Keep the graph and bars as one stable group. The steering column can
        // be placed on either edge without changing the graph's width or the
        // 20-DIP graph-to-bar and bar-to-edge spacing.
        double graphX = hasSteering && !steeringOnRight ? App.SteeringWidth + ColumnGap : Padding;
        double steeringLeft = width - App.SteeringWidth;
        double barsLeft = hasSteering && steeringOnRight
            ? steeringLeft - Padding - BarsWidth
            : width - Padding - BarsWidth;
        double graphWidth = Math.Max(1, barsLeft - ColumnGap - graphX);
        Rect graph = new(graphX, contentTop, graphWidth, contentHeight);
        drawingContext.DrawRectangle(Brush(Color.FromArgb(77,
                GraphBackgroundColor.R, GraphBackgroundColor.G, GraphBackgroundColor.B)),
            new Pen(Brush(Color.FromArgb(28, 255, 255, 255)), 1), graph);

        DrawGraph(drawingContext, graph);
        DrawBars(drawingContext, graph, barsLeft);
        if (hasSteering)
        {
            // Use the full steering-column width for the dial. Centering the
            // square in the fixed-height window gives it a 141-DIP diameter
            // with only 4.5 DIPs of top/bottom breathing room, while the
            // graph and bars retain their shared 12-DIP vertical alignment.
            double steeringSize = Math.Min(App.SteeringWidth, height);
            double steeringTop = Math.Max(0, (height - steeringSize) / 2);
            DrawSteering(drawingContext,
                new Rect(steeringOnRight ? steeringLeft : 0, steeringTop, steeringSize, steeringSize));
        }
    }

    private void RenderTick(object? sender, EventArgs e)
    {
        PedalTelemetry target;
        lock (_telemetryGate) target = _targetTelemetry;

        double now = _clock.Elapsed.TotalMilliseconds;
        double elapsed = _lastRenderAt <= 0
            ? RenderIntervalMilliseconds
            : Math.Clamp(now - _lastRenderAt, 0, 250);
        _lastRenderAt = now;
        double response = 1 - Math.Exp(-elapsed / InputResponseMilliseconds);
        _shownThrottle += (target.Throttle - _shownThrottle) * response;
        _shownBrake += (target.Brake - _shownBrake) * response;
        _shownSteering += (target.Steering - _shownSteering) * response;

        if (now - _lastSampleAt >= SampleIntervalMilliseconds)
        {
            _history.Add(new HistorySample(now, _shownThrottle, _shownBrake, target.BrakeLockup));
            _lastSampleAt = now;
        }

        double cutoff = now - (_settings.GraphDurationSeconds * 1000);
        while (_history.Count > 1 && _history[1].Time < cutoff) _history.RemoveAt(0);
        InvalidateVisual();
    }

    private DispatcherTimer CreateTimer()
    {
        DispatcherTimer timer = new(DispatcherPriority.Render)
        {
            // Render at the display cadence while sampling history at its own
            // lower cadence in RenderTick. This keeps live bars responsive
            // without increasing the graph's retention rate.
            Interval = TimeSpan.FromMilliseconds(RenderIntervalMilliseconds),
        };
        timer.Tick += RenderTick;
        return timer;
    }

    private void DrawGraph(DrawingContext dc, Rect graph)
    {
        if (_history.Count < 1) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        double duration = Math.Max(1, _settings.GraphDurationSeconds * 1000);
        double cutoff = now - duration;

        List<Point> throttlePoints = [];
        List<Point> brakePoints = [];
        foreach (HistorySample sample in _history)
        {
            double x = graph.Left + Math.Clamp((sample.Time - cutoff) / duration, 0, 1) * graph.Width;
            throttlePoints.Add(new Point(x, graph.Top + 2 + ((1 - sample.Throttle) * (graph.Height - 4))));
            brakePoints.Add(new Point(x, graph.Top + 2 + ((1 - sample.Brake) * (graph.Height - 4))));
        }

        // The newest sample is taken just before this frame and therefore can
        // sit a few pixels short of the graph edge. Carry its value to the
        // edge so the graph never appears to stop or leave a trailing gap.
        if (throttlePoints.Count > 0)
        {
            throttlePoints.Add(new Point(graph.Right, throttlePoints[^1].Y));
            brakePoints.Add(new Point(graph.Right, brakePoints[^1].Y));
        }

        DrawPolyline(dc, throttlePoints, ColorFromHex(ThrottleColor));
        DrawBrakePolyline(dc, brakePoints);
    }

    private void DrawBrakePolyline(DrawingContext dc, IReadOnlyList<Point> points)
    {
        if (points.Count == 0) return;
        int start = 0;
        while (start < points.Count - 1)
        {
            BrakeLockup lockup = _history[Math.Min(start, _history.Count - 1)].BrakeLockup;
            int end = start + 1;
            while (end < points.Count && _history[Math.Min(end, _history.Count - 1)].BrakeLockup == lockup) end++;
            Debug.Assert(end > start, "Brake history run must always advance.");
            int segmentStart = Math.Max(0, start - 1);
            int segmentLength = end - segmentStart;
            List<Point> segment = [.. points.Skip(segmentStart).Take(segmentLength)];
            DrawPolyline(dc, segment, BrakeColorFor(lockup));
            // Move to the next contiguous run. The previous point is included
            // only for visual continuity; it must not become the next index.
            start = end;
        }
    }

    private static void DrawPolyline(DrawingContext dc, IReadOnlyList<Point> points, Color color)
    {
        if (points.Count < 2) return;
        Pen glow = new(Brush(Color.FromArgb(65, color.R, color.G, color.B)), 5)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        Pen pen = new(Brush(color), 2.2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        // Drawing every segment independently causes each glow to be
        // rasterized separately, producing visible seams and a soft trailing
        // echo when values change quickly. One frozen path per continuous run
        // gives WPF a single antialiased stroke for both layers.
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            for (int index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index], true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, glow, geometry);
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawBars(DrawingContext dc, Rect graph, double barsLeft)
    {
        double firstX = barsLeft + BarInset;
        double secondX = firstX + BarWidth + BarGap;
        DrawBar(dc, new Rect(firstX, graph.Top, BarWidth, graph.Height), _shownThrottle, ColorFromHex("#42e37c"));
        // Lock-up colors belong to the brake history line. The live brake bar
        // remains the stable pink/red pedal indicator.
        DrawBar(dc, new Rect(secondX, graph.Top, BarWidth, graph.Height), _shownBrake, ColorFromHex(BrakeColor));
    }

    private static void DrawBar(DrawingContext dc, Rect rect, double value, Color fillColor)
    {
        dc.DrawRoundedRectangle(Brush(Color.FromArgb(97,
                BarBackgroundColor.R, BarBackgroundColor.G, BarBackgroundColor.B)),
            new Pen(Brush(Color.FromArgb(31, 255, 255, 255)), 1), rect, 3, 3);
        double innerHeight = Math.Max(0, rect.Height - 4);
        double fillHeight = Math.Clamp(value, 0, 1) * innerHeight;
        if (fillHeight <= 0) return;
        Rect fill = new(rect.Left + 2, rect.Bottom - 2 - fillHeight, rect.Width - 4, fillHeight);
        dc.DrawRoundedRectangle(Brush(fillColor), null, fill, 1, 1);
    }

    private void DrawSteering(DrawingContext dc, Rect area)
    {
        double diameter = Math.Min(area.Width, area.Height);
        // The dial is left-aligned and uses the full content-height diameter.
        // This removes the left inset while the window background below keeps
        // its half-height rounded left edge.
        Point center = new(area.Left + (diameter / 2), area.Top + (area.Height / 2));
        double radius = diameter / 2;
        dc.DrawEllipse(Brush(Color.FromArgb(46,
                OverlayBackgroundColor.R, OverlayBackgroundColor.G, OverlayBackgroundColor.B)),
            new Pen(Brush(Color.FromArgb(51, 255, 255, 255)), 1), center, radius, radius);
        double innerRadius = radius - 13;
        dc.DrawEllipse(null, new Pen(Brush(Color.FromArgb(41, 255, 255, 255)), 1), center, innerRadius, innerRadius);

        double normalized = Math.Clamp(_shownSteering, -1, 1);
        double centerAngle = -90 + (normalized * SteeringMarkerArcDegrees);
        Geometry marker = CreateSteeringMarker(center, radius - 2, radius - 11, centerAngle, 24);
        dc.DrawGeometry(Brush(Color.FromArgb(245, 232, 237, 242)),
            new Pen(Brush(Color.FromArgb(80, 232, 237, 242)), 1), marker);

        int degrees = (int)Math.Round(normalized * MaxSteeringDegrees);
        FormattedText text = new($"{degrees}°", CultureInfo.InvariantCulture, WpfFlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            15, Brush(Color.FromArgb(245, 244, 247, 250)), 1);
        dc.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
    }

    private static Geometry CreateSteeringMarker(Point center, double outerRadius, double innerRadius,
        double centerAngle, double tangentWidth)
    {
        double halfAngle = Math.Asin(Math.Min(0.45, (tangentWidth / 2) / outerRadius)) * 180 / Math.PI;
        Point outerStart = CirclePoint(center, outerRadius, centerAngle - halfAngle);
        Point outerEnd = CirclePoint(center, outerRadius, centerAngle + halfAngle);
        Point innerEnd = CirclePoint(center, innerRadius, centerAngle + halfAngle);
        Point innerStart = CirclePoint(center, innerRadius, centerAngle - halfAngle);
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(outerStart, true, true);
            context.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(innerEnd, true, false);
            context.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point CirclePoint(Point center, double radius, double angle)
    {
        double radians = angle * Math.PI / 180;
        return new Point(center.X + (Math.Cos(radians) * radius), center.Y + (Math.Sin(radians) * radius));
    }

    private Color BrakeColorFor(BrakeLockup lockup)
    {
        return ColorFromHex(lockup == BrakeLockup.None ? BrakeColor : _settings.LockupColors.Single);
    }

    private static Geometry CreateRoundedRectangle(Rect rect, double leftRadius, double rightRadius)
    {
        leftRadius = Math.Min(leftRadius, rect.Height / 2);
        rightRadius = Math.Min(rightRadius, rect.Height / 2);
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(rect.Left + leftRadius, rect.Top), true, true);
            context.LineTo(new Point(rect.Right - rightRadius, rect.Top), true, false);
            context.ArcTo(new Point(rect.Right, rect.Top + rightRadius), new Size(rightRadius, rightRadius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(rect.Right, rect.Bottom - rightRadius), true, false);
            context.ArcTo(new Point(rect.Right - rightRadius, rect.Bottom), new Size(rightRadius, rightRadius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(rect.Left + leftRadius, rect.Bottom), true, false);
            context.ArcTo(new Point(rect.Left, rect.Bottom - leftRadius), new Size(leftRadius, leftRadius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(rect.Left, rect.Top + leftRadius), true, false);
            context.ArcTo(new Point(rect.Left + leftRadius, rect.Top), new Size(leftRadius, leftRadius), 0, false, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static byte ClampAlpha(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static Color ColorFromHex(string value)
    {
        try
        {
            return (Color)WpfColorConverter.ConvertFromString(value)!;
        }
        catch (FormatException)
        {
            return Colors.White;
        }
    }

    private static SolidColorBrush Brush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private readonly record struct HistorySample(double Time, double Throttle, double Brake, BrakeLockup BrakeLockup);
}
