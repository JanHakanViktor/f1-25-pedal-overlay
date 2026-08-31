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

public sealed record OverlayStatus(ConnectionState State, string Message, int Port);
