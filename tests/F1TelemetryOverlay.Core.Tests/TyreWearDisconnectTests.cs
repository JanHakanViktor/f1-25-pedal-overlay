using System.Net;
using System.Net.Sockets;
using F1TelemetryOverlay.Core;
using Xunit;

namespace F1TelemetryOverlay.Core.Tests;

public sealed class TyreWearDisconnectTests
{
    [Fact]
    public async Task TyreOnlyStreamReturnsToListeningAfterSilence()
    {
        ManualTimeProvider time = new();
        int port = GetFreeUdpPort();
        await using TelemetryReceiver receiver = new(port, time);
        List<OverlayStatus> statuses = [];
        TaskCompletionSource<TyreWearTelemetry> received = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.StatusChanged += statuses.Add;
        receiver.TyreWearReceived += value => received.TrySetResult(value);
        receiver.Start();

        using UdpClient sender = new();
        await sender.SendAsync(
            PacketBuilder.TyreWear(2, 12, 45, 62, 78),
            new IPEndPoint(IPAddress.Loopback, port));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        int statusesBeforeSilence = statuses.Count;

        time.Advance(TimeSpan.FromMilliseconds(1501));
        receiver.CheckConnectionStatus();

        Assert.True(statuses.Count > statusesBeforeSilence,
            "A valid tyre-only stream must participate in disconnect liveness.");
        Assert.Contains(statuses, status => status.State == ConnectionState.Listening);
        Assert.Equal(ConnectionState.Listening, statuses[^1].State);
    }

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
