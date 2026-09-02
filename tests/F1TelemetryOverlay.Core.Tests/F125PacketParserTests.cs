using System.Buffers.Binary;
using F1TelemetryOverlay.Core;

namespace F1TelemetryOverlay.Core.Tests;

public sealed class F125PacketParserTests
{
    [Fact]
    public void ParsesSelectedPlayerCarPedals()
    {
        byte[] packet = PacketBuilder.Pedals(3, 287, 0.72f, -0.45f, 0.31f);

        PedalTelemetry? result = F125PacketParser.ParsePedals(packet, 1234);

        Assert.NotNull(result);
        Assert.Equal(1234, result.Timestamp);
        Assert.Equal(287, result.SpeedKph);
        Assert.Equal(0.72, result.Throttle, 4);
        Assert.Equal(-0.45, result.Steering, 4);
        Assert.Equal(0.31, result.Brake, 4);
        Assert.Equal(BrakeLockup.None, result.BrakeLockup);
    }

    [Fact]
    public void ParsesWheelSlipInRearLeftRearRightFrontLeftFrontRightOrder()
    {
        WheelMotionTelemetry? result = F125PacketParser.ParseWheelMotion(
            PacketBuilder.Motion(-0.42f, -0.08f, 0.03f, -0.51f),
            4321);

        Assert.NotNull(result);
        Assert.Equal(4321, result.Timestamp);
        Assert.Equal([-0.42, -0.08, 0.03, -0.51], result.WheelSlipRatio, new ApproximateComparer());
    }

    [Fact]
    public void ParsesSelectedPlayerCarTyreWearInRearLeftRearRightFrontLeftFrontRightOrder()
    {
        TyreWearTelemetry? result = F125PacketParser.ParseTyreWear(
            PacketBuilder.TyreWear(3, 12.5f, 34.25f, 56.75f, 78.125f),
            5678);

        Assert.NotNull(result);
        Assert.Equal(5678, result.Timestamp);
        Assert.Equal(12.5, result.RearLeftPercentage, 4);
        Assert.Equal(34.25, result.RearRightPercentage, 4);
        Assert.Equal(56.75, result.FrontLeftPercentage, 4);
        Assert.Equal(78.125, result.FrontRightPercentage, 4);
        Assert.Equal([12.5, 34.25, 56.75, 78.125], result.WheelWearPercentage, new ApproximateComparer());
    }

    [Fact]
    public void ParsesOfficialCarDamageWireLayoutAtLastPlayerRecord()
    {
        const int headerSize = 29;
        const int packetSize = 1041;
        const int recordSize = 46;
        const int playerIndex = 21;
        const int recordOffset = headerSize + (playerIndex * recordSize);
        byte[] packet = new byte[packetSize];

        BinaryPrimitives.WriteUInt16LittleEndian(packet, 2025);
        packet[6] = 10;
        packet[27] = (byte)playerIndex;
        WriteWireSingle(packet, recordOffset, 1.25f);
        WriteWireSingle(packet, recordOffset + 4, 2.5f);
        WriteWireSingle(packet, recordOffset + 8, 3.75f);
        WriteWireSingle(packet, recordOffset + 12, 5f);

        TyreWearTelemetry? result = F125PacketParser.ParseTyreWear(packet, 5679);

        Assert.NotNull(result);
        Assert.Equal(5679, result.Timestamp);
        Assert.Equal(1.25, result.RearLeftPercentage, 4);
        Assert.Equal(2.5, result.RearRightPercentage, 4);
        Assert.Equal(3.75, result.FrontLeftPercentage, 4);
        Assert.Equal(5, result.FrontRightPercentage, 4);
        Assert.Equal((byte)10, F125PacketParser.CarDamagePacketId);
        Assert.Equal(packetSize, F125PacketParser.CarDamagePacketSize);
        Assert.Equal(recordSize, F125PacketParser.CarDamageRecordSize);
    }

    [Fact]
    public void RejectsWrongFormatPacketTypeAndPlayerIndex()
    {
        byte[] wrongFormat = PacketBuilder.Pedals();
        BinaryPrimitives.WriteUInt16LittleEndian(wrongFormat, 2024);
        byte[] wrongType = PacketBuilder.Pedals();
        wrongType[6] = 2;

        Assert.Null(F125PacketParser.ParsePedals(wrongFormat, 1));
        Assert.Null(F125PacketParser.ParsePedals(wrongType, 1));
        Assert.Null(F125PacketParser.ParsePedals(PacketBuilder.Pedals(22), 1));

        byte[] wrongTyreFormat = PacketBuilder.TyreWear();
        BinaryPrimitives.WriteUInt16LittleEndian(wrongTyreFormat, 2024);
        byte[] wrongTyreType = PacketBuilder.TyreWear();
        wrongTyreType[6] = F125PacketParser.CarTelemetryPacketId;

        Assert.Null(F125PacketParser.ParseTyreWear(wrongTyreFormat, 1));
        Assert.Null(F125PacketParser.ParseTyreWear(wrongTyreType, 1));
        Assert.Null(F125PacketParser.ParseTyreWear(PacketBuilder.TyreWear(22), 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(28)]
    public void RejectsTruncatedHeader(int length) =>
        Assert.Null(F125PacketParser.ParsePedals(new byte[length], 1));

    [Fact]
    public void RequiresEntireSelectedRecordFieldsButNotUnusedTrailingCars()
    {
        int exactLength = F125PacketParser.PacketHeaderSize + 14;
        byte[] exact = PacketBuilder.Pedals(length: exactLength, throttle: 0.5f);
        byte[] shortPacket = PacketBuilder.Pedals(length: exactLength - 1);

        Assert.NotNull(F125PacketParser.ParsePedals(exact, 1));
        Assert.Null(F125PacketParser.ParsePedals(shortPacket, 1));
    }

    [Fact]
    public void TyreWearRequiresSelectedRecordFieldsButNotUnusedTrailingCars()
    {
        int exactLength = F125PacketParser.PacketHeaderSize +
            (3 * F125PacketParser.CarDamageRecordSize) + F125PacketParser.TyreWearFieldsSize;
        byte[] exact = PacketBuilder.TyreWear(3, frontRight: 42.5f, length: exactLength);
        byte[] shortPacket = PacketBuilder.TyreWear(3, length: exactLength - 1);

        TyreWearTelemetry? result = F125PacketParser.ParseTyreWear(exact, 1);
        Assert.NotNull(result);
        Assert.Equal(42.5, result.FrontRightPercentage, 4);
        Assert.Null(F125PacketParser.ParseTyreWear(shortPacket, 1));
    }

    [Fact]
    public void ClampsOutOfRangeAndNonFiniteInputs()
    {
        byte[] packet = PacketBuilder.Pedals(throttle: 1.5f, steering: 2, brake: -0.4f);
        PedalTelemetry result = Assert.IsType<PedalTelemetry>(F125PacketParser.ParsePedals(packet, 1));
        Assert.Equal(1, result.Throttle);
        Assert.Equal(1, result.Steering);
        Assert.Equal(0, result.Brake);

        packet = PacketBuilder.Pedals(throttle: float.NaN, steering: float.PositiveInfinity, brake: float.NaN);
        result = Assert.IsType<PedalTelemetry>(F125PacketParser.ParsePedals(packet, 1));
        Assert.Equal(0, result.Throttle);
        Assert.Equal(0, result.Steering);
        Assert.Equal(0, result.Brake);
    }

    [Fact]
    public void ClampsFiniteTyreWearToPercentageRange()
    {
        TyreWearTelemetry result = Assert.IsType<TyreWearTelemetry>(F125PacketParser.ParseTyreWear(
            PacketBuilder.TyreWear(rearLeft: -5, rearRight: 12.5f, frontLeft: 100.5f, frontRight: 100),
            1));

        Assert.Equal(0, result.RearLeftPercentage);
        Assert.Equal(12.5, result.RearRightPercentage);
        Assert.Equal(100, result.FrontLeftPercentage);
        Assert.Equal(100, result.FrontRightPercentage);
    }

    [Fact]
    public void RejectsNonFiniteSelectedTyreWearValues()
    {
        Assert.Null(F125PacketParser.ParseTyreWear(PacketBuilder.TyreWear(rearLeft: float.NaN), 1));
        Assert.Null(F125PacketParser.ParseTyreWear(PacketBuilder.TyreWear(rearRight: float.PositiveInfinity), 1));
        Assert.Null(F125PacketParser.ParseTyreWear(PacketBuilder.TyreWear(frontLeft: float.NegativeInfinity), 1));
        Assert.Null(F125PacketParser.ParseTyreWear(PacketBuilder.TyreWear(frontRight: float.NaN), 1));
    }

    [Fact]
    public void MotionRequiresFullPacketAndNormalizesNonFiniteValues()
    {
        byte[] shortPacket = new byte[F125PacketParser.MotionExPacketSize - 1];
        Assert.Null(F125PacketParser.ParseWheelMotion(shortPacket, 1));

        WheelMotionTelemetry result = Assert.IsType<WheelMotionTelemetry>(
            F125PacketParser.ParseWheelMotion(PacketBuilder.Motion(float.NaN), 1));
        Assert.Equal(0, result.RearLeftSlipRatio);
    }

    private static void WriteWireSingle(byte[] packet, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset), BitConverter.SingleToInt32Bits(value));

    private sealed class ApproximateComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 0.0001;

        public int GetHashCode(double obj) => obj.GetHashCode();
    }
}
