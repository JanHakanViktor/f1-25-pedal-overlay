using F1TelemetryOverlay.Core;

namespace F1TelemetryOverlay.Core.Tests;

public sealed class BrakeLockupDetectorTests
{
    [Theory]
    [InlineData(-0.5, 0, 0, 0, BrakeLockup.Rear)]
    [InlineData(0, 0, -0.5, 0, BrakeLockup.Front)]
    [InlineData(-0.5, 0, -0.5, 0, BrakeLockup.Both)]
    [InlineData(0, 0, 0, 0, BrakeLockup.None)]
    public void IdentifiesLockedAxleInOfficialWheelOrder(
        double rearLeft,
        double rearRight,
        double frontLeft,
        double frontRight,
        BrakeLockup expected)
    {
        BrakeLockupDetector detector = new();
        detector.UpdateMotion(new WheelMotionTelemetry(rearLeft, rearRight, frontLeft, frontRight, 1000));

        Assert.Equal(expected, detector.Detect(BrakingTelemetry(1100)));
    }

    [Theory]
    [InlineData(1150, BrakeLockup.Front)]
    [InlineData(1151, BrakeLockup.None)]
    [InlineData(999, BrakeLockup.Front)]
    public void MotionFreshnessMatchesTimestampComparison(long telemetryTimestamp, BrakeLockup expected)
    {
        BrakeLockupDetector detector = new();
        detector.UpdateMotion(new WheelMotionTelemetry(0, 0, -0.5, 0, 1000));
        Assert.Equal(expected, detector.Detect(BrakingTelemetry(telemetryTimestamp)));
    }

    [Theory]
    [InlineData(19, 0.9)]
    [InlineData(100, 0.09)]
    public void RequiresSpeedAndBrakeThreshold(int speed, double brake)
    {
        BrakeLockupDetector detector = new();
        detector.UpdateMotion(new WheelMotionTelemetry(0, 0, -0.9, 0, 1000));
        Assert.Equal(BrakeLockup.None, detector.Detect(BrakingTelemetry(1000) with { SpeedKph = speed, Brake = brake }));
    }

    [Fact]
    public void UsesSensitivityAndHysteresisThresholds()
    {
        BrakeLockupDetector detector = new() { Sensitivity = 0.5 };
        detector.UpdateMotion(new WheelMotionTelemetry(0, 0, -0.49, 0, 1000));
        Assert.Equal(BrakeLockup.None, detector.Detect(BrakingTelemetry(1000)));

        detector.UpdateMotion(new WheelMotionTelemetry(0, 0, -0.5, 0, 1010));
        Assert.Equal(BrakeLockup.Front, detector.Detect(BrakingTelemetry(1010)));

        detector.UpdateMotion(new WheelMotionTelemetry(0, 0, -0.27, 0, 1020));
        Assert.Equal(BrakeLockup.Front, detector.Detect(BrakingTelemetry(1020)));

        detector.UpdateMotion(new WheelMotionTelemetry(0, 0, -0.25, 0, 1030));
        Assert.Equal(BrakeLockup.None, detector.Detect(BrakingTelemetry(1030)));
    }

    [Fact]
    public void ClampsSensitivityAndIgnoresNonFiniteValues()
    {
        BrakeLockupDetector detector = new();
        detector.Sensitivity = 4;
        Assert.Equal(0.9, detector.Sensitivity);
        detector.Sensitivity = 0;
        Assert.Equal(0.15, detector.Sensitivity);
        detector.Sensitivity = double.NaN;
        Assert.Equal(0.15, detector.Sensitivity);
    }

    private static PedalTelemetry BrakingTelemetry(long timestamp) =>
        new(100, 0, 0, 0.8, BrakeLockup.None, timestamp);
}
