namespace F1TelemetryOverlay.Core;

public enum ConnectionState
{
    Listening,
    Connected,
    Error,
}

public enum BrakeLockup
{
    None,
    Front,
    Rear,
    Both,
}

public enum LockupColorMode
{
    Axle,
    Single,
}

public enum SteeringPosition
{
    Left,
    Right,
}

public sealed record PedalTelemetry(
    int SpeedKph,
    double Throttle,
    double Steering,
    double Brake,
    BrakeLockup BrakeLockup,
    long Timestamp);

public sealed record WheelMotionTelemetry(
    double RearLeftSlipRatio,
    double RearRightSlipRatio,
    double FrontLeftSlipRatio,
    double FrontRightSlipRatio,
    long Timestamp)
{
    public IReadOnlyList<double> WheelSlipRatio =>
        [RearLeftSlipRatio, RearRightSlipRatio, FrontLeftSlipRatio, FrontRightSlipRatio];
}

public sealed record TyreWearTelemetry(
    double RearLeftPercentage,
    double RearRightPercentage,
    double FrontLeftPercentage,
    double FrontRightPercentage,
    long Timestamp)
{
    // The packet order is rear-left, rear-right, front-left, front-right.
    // Keep this mapping explicit for consumers that need all four values.
    public IReadOnlyList<double> WheelWearPercentage =>
        [RearLeftPercentage, RearRightPercentage, FrontLeftPercentage, FrontRightPercentage];
}

public sealed record OverlayStatus(ConnectionState State, string Message, int Port);
