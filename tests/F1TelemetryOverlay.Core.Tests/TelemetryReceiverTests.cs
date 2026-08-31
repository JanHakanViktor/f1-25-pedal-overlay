using System.Net;
using System.Net.Sockets;
using F1TelemetryOverlay.Core;

namespace F1TelemetryOverlay.Core.Tests;

public sealed class TelemetryReceiverTests
{
    [Fact]
    public async Task ReceivesPedalsAndMotionAndEmitsConnectionStatuses()
    {
        int port = GetFreeUdpPort();
        await using TelemetryReceiver receiver = new(port);
        List<OverlayStatus> statuses = [];
        TaskCompletionSource<PedalTelemetry> firstTelemetry = NewCompletionSource<PedalTelemetry>();
        TaskCompletionSource<PedalTelemetry> lockupTelemetry = NewCompletionSource<PedalTelemetry>();
        int telemetryCount = 0;
        receiver.StatusChanged += statuses.Add;
        receiver.TelemetryReceived += telemetry =>
        {
            if (Interlocked.Increment(ref telemetryCount) == 1)
            {
                firstTelemetry.TrySetResult(telemetry);
            }
            else
            {
                lockupTelemetry.TrySetResult(telemetry);
            }
        };
        receiver.Start();

        using UdpClient sender = new();
        await sender.SendAsync(PacketBuilder.Pedals(speed: 301, throttle: 0.72f, steering: -0.25f, brake: 0.31f),
            new IPEndPoint(IPAddress.Loopback, port));
        PedalTelemetry first = await firstTelemetry.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await sender.SendAsync(PacketBuilder.Motion(frontLeft: -0.62f), new IPEndPoint(IPAddress.Loopback, port));
        await sender.SendAsync(PacketBuilder.Pedals(speed: 185, brake: 0.82f), new IPEndPoint(IPAddress.Loopback, port));
        PedalTelemetry locked = await lockupTelemetry.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(301, first.SpeedKph);
        Assert.Equal(0.72, first.Throttle, 4);
        Assert.Equal(-0.25, first.Steering, 4);
        Assert.Equal(BrakeLockup.Front, locked.BrakeLockup);
        Assert.Contains(statuses, status => status.State == ConnectionState.Listening);
        Assert.Contains(statuses, status => status.State == ConnectionState.Connected);
    }

    [Fact]
    public async Task ReportsExclusivePortConflict()
    {
        int port = GetFreeUdpPort();
        await using TelemetryReceiver first = new(port);
        await using TelemetryReceiver second = new(port);
        OverlayStatus? conflict = null;
        second.StatusChanged += status => conflict = status;
        first.Start();
        second.Start();

        Assert.NotNull(conflict);
        Assert.Equal(ConnectionState.Error, conflict.State);
        Assert.Equal($"UDP {port} is already in use", conflict.Message);
    }

    [Fact]
    public async Task ReturnsToListeningAfterExactDisconnectWindow()
    {
        ManualTimeProvider time = new();
        int port = GetFreeUdpPort();
        await using TelemetryReceiver receiver = new(port, time);
        List<OverlayStatus> statuses = [];
        TaskCompletionSource<PedalTelemetry> telemetry = NewCompletionSource<PedalTelemetry>();
        receiver.StatusChanged += statuses.Add;
        receiver.TelemetryReceived += value => telemetry.TrySetResult(value);
        receiver.Start();
        using UdpClient sender = new();
        await sender.SendAsync(PacketBuilder.Pedals(), new IPEndPoint(IPAddress.Loopback, port));
        await telemetry.Task.WaitAsync(TimeSpan.FromSeconds(3));

        time.Advance(TimeSpan.FromMilliseconds(1500));
        receiver.CheckConnectionStatus();
        Assert.Equal(ConnectionState.Connected, statuses[^1].State);

        time.Advance(TimeSpan.FromMilliseconds(1));
        receiver.CheckConnectionStatus();
        Assert.Equal(ConnectionState.Listening, statuses[^1].State);
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int GetFreeUdpPort()
    {
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp = 1;
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch.AddSeconds(1);

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount)
        {
            _timestamp += (long)amount.TotalMilliseconds;
            _utcNow += amount;
        }
    }
}
