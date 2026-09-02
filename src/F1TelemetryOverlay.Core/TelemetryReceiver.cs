using System.Net;
using System.Net.Sockets;

namespace F1TelemetryOverlay.Core;

public sealed class TelemetryReceiver : IDisposable, IAsyncDisposable
{
    public static readonly TimeSpan DisconnectAfter = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ConnectionCheckInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly BrakeLockupDetector _lockupDetector = new();
    private Socket? _socket;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveTask;
    private ITimer? _connectionTimer;
    private long _lastPacketTimestamp;
    private bool _disposed;

    public TelemetryReceiver(int port = 20777, TimeProvider? timeProvider = null)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        Port = port;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int Port { get; }

    public double LockupSensitivity
    {
        get => _lockupDetector.Sensitivity;
        set => _lockupDetector.Sensitivity = value;
    }

    public event Action<PedalTelemetry>? TelemetryReceived;

    public event Action<TyreWearTelemetry>? TyreWearReceived;

    public event Action<OverlayStatus>? StatusChanged;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_socket is not null)
            {
                return;
            }

            Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ExclusiveAddressUse = true,
            };
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);

            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, Port));
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                EmitStatus(ConnectionState.Error, exception.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"UDP {Port} is already in use"
                    : exception.Message);
                return;
            }

            _socket = socket;
            _cancellation = new CancellationTokenSource();
            _lastPacketTimestamp = 0;
            _receiveTask = ReceiveLoopAsync(socket, _cancellation.Token);
            _connectionTimer = _timeProvider.CreateTimer(
                _ => CheckConnectionStatus(),
                null,
                ConnectionCheckInterval,
                ConnectionCheckInterval);
            EmitStatus(ConnectionState.Listening, $"Waiting on UDP {Port}");
        }
    }

    public async ValueTask StopAsync()
    {
        Task? receiveTask;
        lock (_gate)
        {
            _connectionTimer?.Dispose();
            _connectionTimer = null;
            _cancellation?.Cancel();
            _socket?.Dispose();
            _socket = null;
            receiveTask = _receiveTask;
            _receiveTask = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _lastPacketTimestamp = 0;
            _lockupDetector.Reset();
        }

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public void CheckConnectionStatus()
    {
        bool disconnected = false;
        lock (_gate)
        {
            if (_lastPacketTimestamp != 0 &&
                _timeProvider.GetElapsedTime(_lastPacketTimestamp) > DisconnectAfter)
            {
                _lastPacketTimestamp = 0;
                disconnected = true;
            }
        }

        if (disconnected)
        {
            EmitStatus(ConnectionState.Listening, $"Waiting on UDP {Port}");
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveLoopAsync(Socket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[2048];
        EndPoint sender = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            int received;
            try
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, sender, cancellationToken)
                    .ConfigureAwait(false);
                received = result.ReceivedBytes;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    EmitStatus(ConnectionState.Error, exception.Message);
                }

                break;
            }

            long timestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            ReadOnlySpan<byte> packet = buffer.AsSpan(0, received);
            WheelMotionTelemetry? motion = F125PacketParser.ParseWheelMotion(packet, timestamp);
            if (motion is not null)
            {
                lock (_gate)
                {
                    _lockupDetector.UpdateMotion(motion);
                }

                continue;
            }

            TyreWearTelemetry? tyreWear = F125PacketParser.ParseTyreWear(packet, timestamp);
            if (tyreWear is not null)
            {
                if (MarkPacketReceived())
                {
                    EmitStatus(ConnectionState.Connected, "F1 25 connected");
                }

                TyreWearReceived?.Invoke(tyreWear);
                continue;
            }

            PedalTelemetry? telemetry = F125PacketParser.ParsePedals(packet, timestamp);
            if (telemetry is null)
            {
                continue;
            }

            bool wasDisconnected = MarkPacketReceived();
            BrakeLockup lockup;
            lock (_gate)
            {
                lockup = _lockupDetector.Detect(telemetry);
            }

            if (wasDisconnected)
            {
                EmitStatus(ConnectionState.Connected, "F1 25 connected");
            }

            TelemetryReceived?.Invoke(telemetry with { BrakeLockup = lockup });
        }
    }

    private bool MarkPacketReceived()
    {
        lock (_gate)
        {
            long nowTimestamp = _timeProvider.GetTimestamp();
            bool wasDisconnected = _lastPacketTimestamp == 0 ||
                _timeProvider.GetElapsedTime(_lastPacketTimestamp, nowTimestamp) > DisconnectAfter;
            _lastPacketTimestamp = nowTimestamp;
            return wasDisconnected;
        }
    }

    private void EmitStatus(ConnectionState state, string message) =>
        StatusChanged?.Invoke(new OverlayStatus(state, message, Port));
}
