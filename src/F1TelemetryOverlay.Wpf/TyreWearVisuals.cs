namespace F1TelemetryOverlay.Wpf;

internal enum TyreWearWheel
{
    FrontLeft,
    FrontRight,
    RearLeft,
    RearRight,
}

/// <summary>
/// Deterministic display policy for the compact tyre-wear widget.
/// Values are percentages in the same units as <see cref="Core.TyreWearTelemetry"/>.
/// </summary>
internal static class TyreWearVisuals
{
    internal const double GreenEnd = 40;
    internal const double YellowEnd = 55;
    internal const double OrangeEnd = 70;
    internal const string Green = "#42e37c";
    internal const string Yellow = "#ffd84a";
    internal const string Orange = "#ff8a2a";
    internal const string Red = "#ff4261";
    internal const string MissingText = "--%";

    internal static double ClampPercentage(double percentage) =>
        double.IsFinite(percentage) ? Math.Clamp(percentage, 0, 100) : 0;

    internal static double SweepDegrees(double percentage) => ClampPercentage(percentage) * 3.6;

    internal static string ColorFor(double percentage)
    {
        double value = ClampPercentage(percentage);
        return value < GreenEnd ? Green
            : value < YellowEnd ? Yellow
            : value < OrangeEnd ? Orange
            : Red;
    }

    internal static string DisplayText(double? percentage)
    {
        if (!percentage.HasValue || !double.IsFinite(percentage.Value)) return MissingText;
        int rounded = (int)Math.Round(ClampPercentage(percentage.Value), MidpointRounding.AwayFromZero);
        return $"{rounded}%";
    }

    /// <summary>
    /// Maps the packet's rear-left, rear-right, front-left, front-right order
    /// to the visual order: front-left, front-right, rear-left, rear-right.
    /// </summary>
    internal static IReadOnlyList<double?> DisplayOrder(Core.TyreWearTelemetry? telemetry) => telemetry is null
        ? [null, null, null, null]
        : [telemetry.FrontLeftPercentage, telemetry.FrontRightPercentage,
            telemetry.RearLeftPercentage, telemetry.RearRightPercentage];
}
