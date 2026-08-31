namespace F1TelemetryOverlay.Core;

public sealed class BrakeLockupDetector
{
    public const long MotionMaximumAgeMilliseconds = 150;
    public const double DefaultSensitivity = 0.35;

    private WheelMotionTelemetry? _motion;
    private bool _frontLocked;
    private bool _rearLocked;
    private double _sensitivity = DefaultSensitivity;

    public double Sensitivity
    {
        get => _sensitivity;
        set
        {
            if (double.IsFinite(value))
            {
                _sensitivity = Math.Clamp(value, 0.15, 0.9);
            }
        }
    }

    public void UpdateMotion(WheelMotionTelemetry motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        _motion = motion;
    }

    public BrakeLockup Detect(PedalTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        long motionAge = _motion is null ? long.MaxValue : telemetry.Timestamp - _motion.Timestamp;
        bool motionIsFresh = motionAge <= MotionMaximumAgeMilliseconds;
        bool brakingAtSpeed = telemetry.Brake >= 0.1 && telemetry.SpeedKph >= 20;
        if (!motionIsFresh || !brakingAtSpeed)
        {
            ResetLocks();
            return BrakeLockup.None;
        }

        double rearSlipRatio = Math.Min(_motion!.RearLeftSlipRatio, _motion.RearRightSlipRatio);
        double frontSlipRatio = Math.Min(_motion.FrontLeftSlipRatio, _motion.FrontRightSlipRatio);
        _frontLocked = AxleIsLocked(frontSlipRatio, _frontLocked);
        _rearLocked = AxleIsLocked(rearSlipRatio, _rearLocked);

        return (_frontLocked, _rearLocked) switch
        {
            (true, true) => BrakeLockup.Both,
            (true, false) => BrakeLockup.Front,
            (false, true) => BrakeLockup.Rear,
            _ => BrakeLockup.None,
        };
    }

    public void Reset()
    {
        _motion = null;
        ResetLocks();
    }

    private bool AxleIsLocked(double slipRatio, bool wasLocked)
    {
        double enterThreshold = -Sensitivity;
        double exitThreshold = -Math.Max(0.08, Sensitivity * 0.52);
        return slipRatio <= (wasLocked ? exitThreshold : enterThreshold);
    }

    private void ResetLocks()
    {
        _frontLocked = false;
        _rearLocked = false;
    }
}
