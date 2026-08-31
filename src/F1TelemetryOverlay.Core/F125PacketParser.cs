using System.Buffers.Binary;

namespace F1TelemetryOverlay.Core;

public static class F125PacketParser
{
    public const ushort PacketFormat = 2025;
    public const int PacketHeaderSize = 29;
    public const byte CarTelemetryPacketId = 6;
    public const int CarTelemetryRecordSize = 60;
    public const int MaximumCars = 22;
    public const int CarTelemetryPacketSize = 1352;
    public const byte MotionExPacketId = 13;
    public const int MotionExPacketSize = 237;
    public const int WheelSlipRatioOffset = PacketHeaderSize + 64;

    public static PedalTelemetry? ParsePedals(ReadOnlySpan<byte> packet, long timestamp)
    {
        if (packet.Length < PacketHeaderSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(packet) != PacketFormat ||
            packet[6] != CarTelemetryPacketId)
        {
            return null;
        }

        int playerCarIndex = packet[27];
        if (playerCarIndex >= MaximumCars)
        {
            return null;
        }

        int recordOffset = PacketHeaderSize + (playerCarIndex * CarTelemetryRecordSize);
        int brakeOffset = recordOffset + 10;
        if (packet.Length < brakeOffset + sizeof(float))
        {
            return null;
        }

        int speedKph = BinaryPrimitives.ReadUInt16LittleEndian(packet[recordOffset..]);
        double throttle = ClampInput(ReadSingle(packet, recordOffset + 2));
        double steering = ClampSteering(ReadSingle(packet, recordOffset + 6));
        double brake = ClampInput(ReadSingle(packet, brakeOffset));
        return new PedalTelemetry(speedKph, throttle, steering, brake, BrakeLockup.None, timestamp);
    }

    public static WheelMotionTelemetry? ParseWheelMotion(ReadOnlySpan<byte> packet, long timestamp)
    {
        if (packet.Length < MotionExPacketSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(packet) != PacketFormat ||
            packet[6] != MotionExPacketId)
        {
            return null;
        }

        return new WheelMotionTelemetry(
            FiniteOrZero(ReadSingle(packet, WheelSlipRatioOffset)),
            FiniteOrZero(ReadSingle(packet, WheelSlipRatioOffset + 4)),
            FiniteOrZero(ReadSingle(packet, WheelSlipRatioOffset + 8)),
            FiniteOrZero(ReadSingle(packet, WheelSlipRatioOffset + 12)),
            timestamp);
    }

    private static float ReadSingle(ReadOnlySpan<byte> packet, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(packet[offset..]));

    private static double ClampInput(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static double ClampSteering(float value) => float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;

    private static double FiniteOrZero(float value) => float.IsFinite(value) ? value : 0;
}
