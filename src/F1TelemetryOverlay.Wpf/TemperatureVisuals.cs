namespace F1TelemetryOverlay.Wpf;

internal enum TemperatureMetric
{
    Surface,
    Brake,
}

/// <summary>
/// Deterministic display and colour policy for the temperature widget.
/// The thresholds are approximate game-style bands and require calibration
/// against live F1 25 telemetry; they are not documented EA parity values.
/// </summary>
internal static class TemperatureVisuals
{
    internal const long StaleAfterMilliseconds = 1500;

    internal const int SurfaceBlueEnd = 70;
    internal const int SurfaceGreenEnd = 100;
    internal const int SurfaceYellowEnd = 110;
    internal const int SurfaceOrangeEnd = 120;

    internal const int BrakeBlueEnd = 200;
    internal const int BrakeGreenEnd = 800;
    internal const int BrakeYellowEnd = 1000;
    internal const int BrakeOrangeEnd = 1200;

    internal const string Blue = "#48a9ff";
    internal const string Green = "#42e37c";
    internal const string Yellow = "#ffd84a";
    internal const string Orange = "#ff8a2a";
    internal const string Red = "#ff4261";
    internal const string MissingText = "--°";

    internal static string ColorFor(TemperatureMetric metric, int celsius) => metric switch
    {
        TemperatureMetric.Surface => ColorForBand(celsius, SurfaceBlueEnd, SurfaceGreenEnd,
            SurfaceYellowEnd, SurfaceOrangeEnd),
        TemperatureMetric.Brake => ColorForBand(celsius, BrakeBlueEnd, BrakeGreenEnd,
            BrakeYellowEnd, BrakeOrangeEnd),
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
    };

    internal static string DisplayText(int? celsius) => celsius.HasValue
        ? $"{celsius.Value}°"
        : MissingText;

    internal static bool IsFresh(Core.TemperatureTelemetry? telemetry, long nowMilliseconds)
    {
        if (telemetry is null) return false;
        long age;
        try
        {
            age = checked(nowMilliseconds - telemetry.Timestamp);
        }
        catch (OverflowException)
        {
            return false;
        }

        // Treat a small backwards clock adjustment as fresh. Both producer and
        // consumer use Unix milliseconds, so this also avoids a transient blank.
        return age >= -StaleAfterMilliseconds && age <= StaleAfterMilliseconds;
    }

    /// <summary>
    /// Maps packet order (RL, RR, FL, FR) to physical visual order
    /// (FL, FR, RL, RR). Inner temperature remains available in each wheel
    /// value but is deliberately not substituted for the displayed surface.
    /// </summary>
    internal static IReadOnlyList<Core.WheelTemperatures?> DisplayOrder(
        Core.TemperatureTelemetry? telemetry,
        long nowMilliseconds) => !IsFresh(telemetry, nowMilliseconds)
            ? [null, null, null, null]
            : [telemetry!.FrontLeft, telemetry.FrontRight, telemetry.RearLeft, telemetry.RearRight];

    private static string ColorForBand(int value, int blueEnd, int greenEnd, int yellowEnd, int orangeEnd) =>
        value < blueEnd ? Blue
        : value < greenEnd ? Green
        : value < yellowEnd ? Yellow
        : value < orangeEnd ? Orange
        : Red;
}
