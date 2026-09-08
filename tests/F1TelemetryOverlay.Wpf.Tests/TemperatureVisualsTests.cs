using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class TemperatureVisualsTests
{
    [Theory]
    [InlineData(69, TemperatureVisuals.Blue)]
    [InlineData(70, TemperatureVisuals.Green)]
    [InlineData(99, TemperatureVisuals.Green)]
    [InlineData(100, TemperatureVisuals.Yellow)]
    [InlineData(109, TemperatureVisuals.Yellow)]
    [InlineData(110, TemperatureVisuals.Orange)]
    [InlineData(119, TemperatureVisuals.Orange)]
    [InlineData(120, TemperatureVisuals.Red)]
    public void SurfacePaletteUsesExclusiveUpperBoundaries(int celsius, string expected) =>
        Assert.Equal(expected, TemperatureVisuals.ColorFor(TemperatureMetric.Surface, celsius));

    [Theory]
    [InlineData(199, TemperatureVisuals.Blue)]
    [InlineData(200, TemperatureVisuals.Green)]
    [InlineData(799, TemperatureVisuals.Green)]
    [InlineData(800, TemperatureVisuals.Yellow)]
    [InlineData(999, TemperatureVisuals.Yellow)]
    [InlineData(1000, TemperatureVisuals.Orange)]
    [InlineData(1199, TemperatureVisuals.Orange)]
    [InlineData(1200, TemperatureVisuals.Red)]
    public void BrakePaletteUsesExclusiveUpperBoundaries(int celsius, string expected) =>
        Assert.Equal(expected, TemperatureVisuals.ColorFor(TemperatureMetric.Brake, celsius));

    [Theory]
    [InlineData(null, "--°")]
    [InlineData(0, "0°")]
    [InlineData(1200, "1200°")]
    public void DisplayTextUsesCelsiusWithoutMixingMetrics(int? celsius, string expected) =>
        Assert.Equal(expected, TemperatureVisuals.DisplayText(celsius));

    [Fact]
    public void DisplayOrderMatchesPhysicalWheelLayoutAndPreservesInnerTemperature()
    {
        WheelTemperatures rearLeft = new(201, 71, 81);
        WheelTemperatures rearRight = new(802, 101, 102);
        WheelTemperatures frontLeft = new(1003, 111, 112);
        WheelTemperatures frontRight = new(1204, 121, 122);
        TemperatureTelemetry telemetry = new(rearLeft, rearRight, frontLeft, frontRight, 10_000);

        IReadOnlyList<WheelTemperatures?> order = TemperatureVisuals.DisplayOrder(telemetry, 10_000);

        Assert.Equal(new WheelTemperatures?[] { frontLeft, frontRight, rearLeft, rearRight }, order);
        Assert.Equal(112, order[0]!.InnerCelsius);
        Assert.Equal(111, order[0]!.SurfaceCelsius);
    }

    [Theory]
    [InlineData(8_499, false)]
    [InlineData(8_500, true)]
    [InlineData(10_000, true)]
    [InlineData(11_500, true)]
    [InlineData(11_501, false)]
    public void FreshnessHasDeterministicSymmetricClockBoundary(long telemetryTimestamp, bool expectedFresh)
    {
        TemperatureTelemetry telemetry = Sample(telemetryTimestamp);

        Assert.Equal(expectedFresh, TemperatureVisuals.IsFresh(telemetry, 10_000));
        Assert.Equal(expectedFresh, TemperatureVisuals.DisplayOrder(telemetry, 10_000)[0] is not null);
    }

    [Fact]
    public void MissingTelemetryProducesFourMutedCells()
    {
        Assert.False(TemperatureVisuals.IsFresh(null, 10_000));
        Assert.Equal(new WheelTemperatures?[] { null, null, null, null },
            TemperatureVisuals.DisplayOrder(null, 10_000));
    }

    private static TemperatureTelemetry Sample(long timestamp)
    {
        WheelTemperatures wheel = new(500, 90, 95);
        return new TemperatureTelemetry(wheel, wheel, wheel, wheel, timestamp);
    }
}
