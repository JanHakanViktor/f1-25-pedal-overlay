using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace F1TelemetryOverlay.Wpf;

/// <summary>Small retained preview of the existing pedals/input widget.</summary>
internal sealed class HubPedalPreview : FrameworkElement
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double graphLeft = 7;
        double graphTop = 12;
        double graphRight = width - 35;
        double graphBottom = height - 12;
        drawingContext.DrawRoundedRectangle(Brush("#0D141C"), Pen("#2B3744", 1),
            new Rect(graphLeft, graphTop, Math.Max(1, graphRight - graphLeft), Math.Max(1, graphBottom - graphTop)), 4, 4);

        Point[] throttle =
        [
            new(graphLeft + 4, graphBottom - 13),
            new(graphLeft + 20, graphTop + 33),
            new(graphLeft + 37, graphBottom - 25),
            new(graphLeft + 54, graphTop + 20),
            new(graphRight - 4, graphTop + 28),
        ];
        Point[] brake =
        [
            new(graphLeft + 4, graphBottom - 31),
            new(graphLeft + 20, graphBottom - 24),
            new(graphLeft + 37, graphBottom - 36),
            new(graphLeft + 54, graphBottom - 18),
            new(graphRight - 4, graphBottom - 27),
        ];
        DrawLine(drawingContext, throttle, Color("#42E37C"));
        DrawLine(drawingContext, brake, Color("#FF4261"));

        double barWidth = 10;
        DrawBar(drawingContext, new Rect(width - 29, graphTop, barWidth, graphBottom - graphTop), 0.76, Color("#42E37C"));
        DrawBar(drawingContext, new Rect(width - 14, graphTop, barWidth, graphBottom - graphTop), 0.4, Color("#FF4261"));
        drawingContext.DrawEllipse(null, Pen("#AAB5C2", 1),
            new Point(width - 9, height / 2), 19, 19);
        drawingContext.DrawEllipse(null, Pen("#3E4D5D", 1),
            new Point(width - 9, height / 2), 12, 12);
    }

    private static void DrawLine(DrawingContext drawingContext, IReadOnlyList<Point> points, Color color)
    {
        if (points.Count < 2) return;
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            for (int index = 1; index < points.Count; index++) context.LineTo(points[index], true, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, Pen(System.Windows.Media.Color.FromArgb(55, color.R, color.G, color.B), 4), geometry);
        drawingContext.DrawGeometry(null, Pen(color, 1.5), geometry);
    }

    private static void DrawBar(DrawingContext drawingContext, Rect rect, double value, Color color)
    {
        drawingContext.DrawRoundedRectangle(Brush("#18212B"), Pen("#3E4D5D", 1), rect, 2, 2);
        double fillHeight = Math.Clamp(value, 0, 1) * Math.Max(0, rect.Height - 3);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(color), null,
            new Rect(rect.Left + 2, rect.Bottom - fillHeight - 1, Math.Max(0, rect.Width - 4), fillHeight), 1, 1);
    }

    private static Color Color(string value) => (Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!;

    private static SolidColorBrush Brush(string value)
    {
        SolidColorBrush brush = new(Color(value));
        brush.Freeze();
        return brush;
    }

    private static Pen Pen(string value, double thickness) => Pen(Color(value), thickness);

    private static Pen Pen(Color color, double thickness)
    {
        Pen pen = new(Brush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush Brush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
