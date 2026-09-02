using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class TyreWearVisualsTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(12.5, 45)]
    [InlineData(100, 360)]
    [InlineData(101, 360)]
    public void SweepIsClampedToPercentRange(double percentage, double expectedDegrees) =>
        Assert.Equal(expectedDegrees, TyreWearVisuals.SweepDegrees(percentage));

    [Theory]
    [InlineData(0, TyreWearVisuals.Green)]
    [InlineData(39.99, TyreWearVisuals.Green)]
    [InlineData(40, TyreWearVisuals.Yellow)]
    [InlineData(54.99, TyreWearVisuals.Yellow)]
    [InlineData(55, TyreWearVisuals.Orange)]
    [InlineData(69.99, TyreWearVisuals.Orange)]
    [InlineData(70, TyreWearVisuals.Red)]
    public void PaletteUsesExclusiveUpperBoundaries(double percentage, string expected) =>
        Assert.Equal(expected, TyreWearVisuals.ColorFor(percentage));

    [Theory]
    [InlineData(null, "--%")]
    [InlineData(0d, "0%")]
    [InlineData(12.49, "12%")]
    [InlineData(12.5, "13%")]
    [InlineData(100d, "100%")]
    public void DisplayTextUsesWholeRoundedPercent(double? percentage, string expected) =>
        Assert.Equal(expected, TyreWearVisuals.DisplayText(percentage));

    [Fact]
    public void DisplayOrderMatchesTwoByTwoVisualOrder()
    {
        TyreWearTelemetry telemetry = new(10, 20, 30, 40, 1);

        Assert.Equal(new double?[] { 30, 40, 10, 20 }, TyreWearVisuals.DisplayOrder(telemetry));
        Assert.Equal(new double?[] { null, null, null, null }, TyreWearVisuals.DisplayOrder(null));
    }
}
