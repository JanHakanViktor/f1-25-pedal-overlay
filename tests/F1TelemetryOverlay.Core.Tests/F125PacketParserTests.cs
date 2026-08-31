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
    public void RejectsWrongFormatPacketTypeAndPlayerIndex()
    {
        byte[] wrongFormat = PacketBuilder.Pedals();
        BinaryPrimitives.WriteUInt16LittleEndian(wrongFormat, 2024);
        byte[] wrongType = PacketBuilder.Pedals();
        wrongType[6] = 2;

        Assert.Null(F125PacketParser.ParsePedals(wrongFormat, 1));
        Assert.Null(F125PacketParser.ParsePedals(wrongType, 1));
        Assert.Null(F125PacketParser.ParsePedals(PacketBuilder.Pedals(22), 1));
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
    public void MotionRequiresFullPacketAndNormalizesNonFiniteValues()
    {
        byte[] shortPacket = new byte[F125PacketParser.MotionExPacketSize - 1];
        Assert.Null(F125PacketParser.ParseWheelMotion(shortPacket, 1));

        WheelMotionTelemetry result = Assert.IsType<WheelMotionTelemetry>(
            F125PacketParser.ParseWheelMotion(PacketBuilder.Motion(float.NaN), 1));
        Assert.Equal(0, result.RearLeftSlipRatio);
    }

    private sealed class ApproximateComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 0.0001;

        public int GetHashCode(double obj) => obj.GetHashCode();
    }
}
