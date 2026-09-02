using System.Buffers.Binary;
using F1TelemetryOverlay.Core;

namespace F1TelemetryOverlay.Core.Tests;

internal static class PacketBuilder
{
    public static byte[] Pedals(
        int playerIndex = 0,
        ushort speed = 0,
        float throttle = 0,
        float steering = 0,
        float brake = 0,
        int? length = null)
    {
        byte[] packet = new byte[length ?? F125PacketParser.CarTelemetryPacketSize];
        if (packet.Length >= F125PacketParser.PacketHeaderSize)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(packet, F125PacketParser.PacketFormat);
            packet[6] = F125PacketParser.CarTelemetryPacketId;
            packet[27] = (byte)playerIndex;
        }

        int offset = F125PacketParser.PacketHeaderSize + playerIndex * F125PacketParser.CarTelemetryRecordSize;
        if (packet.Length >= offset + 14)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset), speed);
            WriteSingle(packet, offset + 2, throttle);
            WriteSingle(packet, offset + 6, steering);
            WriteSingle(packet, offset + 10, brake);
        }

        return packet;
    }

    public static byte[] Motion(float rearLeft = 0, float rearRight = 0, float frontLeft = 0, float frontRight = 0)
    {
        byte[] packet = new byte[F125PacketParser.MotionExPacketSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet, F125PacketParser.PacketFormat);
        packet[6] = F125PacketParser.MotionExPacketId;
        WriteSingle(packet, F125PacketParser.WheelSlipRatioOffset, rearLeft);
        WriteSingle(packet, F125PacketParser.WheelSlipRatioOffset + 4, rearRight);
        WriteSingle(packet, F125PacketParser.WheelSlipRatioOffset + 8, frontLeft);
        WriteSingle(packet, F125PacketParser.WheelSlipRatioOffset + 12, frontRight);
        return packet;
    }

    public static byte[] TyreWear(
        int playerIndex = 0,
        float rearLeft = 0,
        float rearRight = 0,
        float frontLeft = 0,
        float frontRight = 0,
        int? length = null)
    {
        byte[] packet = new byte[length ?? F125PacketParser.CarDamagePacketSize];
        if (packet.Length >= F125PacketParser.PacketHeaderSize)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(packet, F125PacketParser.PacketFormat);
            packet[6] = F125PacketParser.CarDamagePacketId;
            packet[27] = (byte)playerIndex;
        }

        int offset = F125PacketParser.PacketHeaderSize + playerIndex * F125PacketParser.CarDamageRecordSize;
        if ((uint)playerIndex < F125PacketParser.MaximumCars &&
            packet.Length >= offset + F125PacketParser.TyreWearFieldsSize)
        {
            WriteSingle(packet, offset, rearLeft);
            WriteSingle(packet, offset + F125PacketParser.TyreWearFieldSize, rearRight);
            WriteSingle(packet, offset + (2 * F125PacketParser.TyreWearFieldSize), frontLeft);
            WriteSingle(packet, offset + (3 * F125PacketParser.TyreWearFieldSize), frontRight);
        }

        return packet;
    }

    private static void WriteSingle(byte[] packet, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset), BitConverter.SingleToInt32Bits(value));
}
