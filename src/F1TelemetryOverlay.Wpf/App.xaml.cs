using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using F1TelemetryOverlay.Core;

namespace F1TelemetryOverlay.Wpf;

public partial class App : System.Windows.Application
{
    internal const string OverlayTitle = "F1 25 Telemetry Overlay";
    internal const int OverlayWidth = 460;
    internal const int SteeringWidth = 141;
    internal const int OverlayHeight = 150;

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private SettingsStore? _settingsStore;
    private TelemetryReceiver? _receiver;
    private ShortcutManager? _shortcutManager;
    private TrayController? _tray;
    private MainWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _demoTimer;
    private AppSettings _settings = AppSettings.Default;
    private PedalTelemetry _lastTelemetry = EmptyTelemetry();
    private OverlayStatus _lastStatus = new(ConnectionState.Listening, "", 20777);
    private bool _locked;
    private bool _steeringEnabled;
    private bool _demoEnabled;
    private int _udpPort;

    internal AppSettings Settings => _settings;
    internal bool IsLocked => _locked;
    internal bool IsSteeringEnabled => _steeringEnabled;
    internal bool IsDemoEnabled => _demoEnabled;
    internal bool IsOverlayVisible => _overlay?.IsVisible == true;
    internal PedalTelemetry LastTelemetry => _lastTelemetry;
    internal OverlayStatus LastStatus => _demoEnabled
        ? new OverlayStatus(ConnectionState.Connected, "Demo signal", _udpPort)
        : _lastStatus;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The overlay is a tiny 2D HUD. Software composition avoids reserving a
        // dedicated hardware surface for a transparent window and keeps its
        // memory footprint predictable while a 3D game is running.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        bool created;
        _singleInstanceMutex = new Mutex(true, "Local\\F1TelemetryOverlay.SingleInstance", out created);
        _ownsSingleInstanceMutex = created;
        if (!created)
        {
            NativeMethods.ShowExistingOverlay();
            Shutdown();
            return;
        }

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _udpPort = ResolveUdpPort(e.Args, _settings.UdpPort);
        _steeringEnabled = HasArgument(e.Args, "--steering")
            || string.Equals(Environment.GetEnvironmentVariable("F1_OVERLAY_STEERING"), "1", StringComparison.Ordinal)
            || _settings.SteeringEnabledByDefault;

        _lastStatus = WaitingStatus(_udpPort);
        _overlay = new MainWindow(this);
        _overlay.SourceInitialized += OverlaySourceInitialized;
        _overlay.Closed += (_, _) => Shutdown();
        PositionOverlay(_overlay);
        _overlay.ShowInactive();

        _tray = new TrayController(this);
        _receiver = new TelemetryReceiver(_udpPort)
        {
            LockupSensitivity = _settings.LockupSensitivity,
        };
        _receiver.TelemetryReceived += ReceiveTelemetry;
        _receiver.StatusChanged += ReceiveStatus;
        _receiver.Start();

        if (HasArgument(e.Args, "--demo")
            || string.Equals(Environment.GetEnvironmentVariable("F1_OVERLAY_DEMO"), "1", StringComparison.Ordinal))
        {
            SetDemoEnabled(true);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _demoTimer?.Stop();
        _receiver?.Dispose();
        _shortcutManager?.Dispose();
        _tray?.Dispose();
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    internal void InitializeNativeWindow(MainWindow window)
    {
        if (_shortcutManager is not null) return;

        _shortcutManager = new ShortcutManager(window);
        _shortcutManager.ShortcutPressed += HandleShortcut;
        if (!_shortcutManager.TryReplace(_settings.Shortcuts, HandleShortcut, out _))
        {
            _settings = _settingsStore?.Save(_settings with { Shortcuts = AppSettings.Default.Shortcuts }) ?? _settings;
            _shortcutManager.TryReplace(_settings.Shortcuts, HandleShortcut, out _);
        }
    }

    internal void ShowOverlay()
    {
        if (_overlay is null) return;
        _overlay.ShowInactive();
        _tray?.Refresh();
    }

    internal void HideOverlay()
    {
        _overlay?.Hide();
        _tray?.Refresh();
    }

    internal void ToggleOverlayVisibility()
    {
        if (_overlay?.IsVisible == true) HideOverlay();
        else ShowOverlay();
    }

    internal void SetLocked(bool locked)
    {
        _locked = locked;
        _overlay?.SetLocked(locked);
        _tray?.Refresh();
    }

    internal void SetSteeringEnabled(bool enabled)
    {
        if (_steeringEnabled == enabled) return;
        _steeringEnabled = enabled;
        _overlay?.SetSteeringEnabled(enabled);
        _tray?.Refresh();
    }

    internal void SetDemoEnabled(bool enabled)
    {
        _demoEnabled = enabled;
        _demoTimer?.Stop();
        _demoTimer = null;

        if (enabled)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            _demoTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            _demoTimer.Tick += (_, _) =>
            {
                double seconds = stopwatch.Elapsed.TotalSeconds;
                double throttle = Math.Clamp(0.64 + (Math.Sin(seconds * 1.7) * 0.36), 0, 1);
                double steering = Math.Sin(seconds * 0.9) * 0.85;
                double brakePulse = Math.Sin(seconds * 0.82);
                double brake = brakePulse > 0.63 ? Math.Min(1, (brakePulse - 0.63) * 2.8) : 0;
                double lockupPulse = Math.Sin(seconds * 5.5);
                BrakeLockup lockup = brake <= 0.72
                    ? BrakeLockup.None
                    : lockupPulse > 0.35
                        ? BrakeLockup.Front
                        : lockupPulse < -0.35 ? BrakeLockup.Rear : BrakeLockup.Both;
                int speed = (int)Math.Round(Math.Max(0, 110 + (throttle * 210) - (brake * 95)));
                PedalTelemetry demoTelemetry = new(speed, throttle, steering, brake, lockup,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                _lastTelemetry = demoTelemetry;
                _overlay?.UpdateTelemetry(demoTelemetry);
            };
            _demoTimer.Start();
        }

        _overlay?.SetDemoEnabled(enabled);
        _tray?.Refresh();
    }

    internal void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, candidate =>
        {
            bool ok = TrySaveSettings(candidate, out string error);
            return (ok, error);
        });
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    internal void ShowControlMenu()
    {
        _tray?.ShowAtCursor();
    }

    internal bool TrySaveSettings(AppSettings candidate, out string error)
    {
        error = string.Empty;
        if (_settingsStore is null || _shortcutManager is null)
        {
            error = "Settings are not ready yet.";
            return false;
        }

        AppSettings sanitized = SanitizeCandidate(candidate);
        if (!_shortcutManager.TryReplace(sanitized.Shortcuts, HandleShortcut, out error)) return false;

        AppSettings previous = _settings;
        try
        {
            _settings = _settingsStore.Save(sanitized);
        }
        catch (IOException)
        {
            _shortcutManager.TryReplace(previous.Shortcuts, HandleShortcut, out _);
            error = "Windows could not write the settings file.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            _shortcutManager.TryReplace(previous.Shortcuts, HandleShortcut, out _);
            error = "Windows could not write the settings file.";
            return false;
        }

        _receiver?.LockupSensitivity = _settings.LockupSensitivity;
        if (_settings.UdpPort != _udpPort) RestartReceiver(_settings.UdpPort);
        _overlay?.ApplySettings(_settings);
        _tray?.Refresh();
        return true;
    }

    private void HandleShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.ToggleVisibility: ToggleOverlayVisibility(); break;
            case ShortcutAction.ToggleLock: SetLocked(!_locked); break;
            case ShortcutAction.ToggleDemo: SetDemoEnabled(!_demoEnabled); break;
            case ShortcutAction.ToggleSteering: SetSteeringEnabled(!_steeringEnabled); break;
            case ShortcutAction.Quit: Shutdown(); break;
        }
    }

    private void OverlaySourceInitialized(object? sender, EventArgs e)
    {
        if (_overlay is not null) InitializeNativeWindow(_overlay);
    }

    private void ReceiveTelemetry(PedalTelemetry telemetry)
    {
        if (_demoEnabled) return;
        _lastTelemetry = telemetry;
        _overlay?.UpdateTelemetry(telemetry);
    }

    private void ReceiveStatus(OverlayStatus status)
    {
        _lastStatus = status;
    }

    private void RestartReceiver(int port)
    {
        _receiver?.Dispose();
        _udpPort = port;
        _lastStatus = WaitingStatus(port);
        _receiver = new TelemetryReceiver(port)
        {
            LockupSensitivity = _settings.LockupSensitivity,
        };
        _receiver.TelemetryReceived += ReceiveTelemetry;
        _receiver.StatusChanged += ReceiveStatus;
        _receiver.Start();
    }

    private void PositionOverlay(MainWindow window)
    {
        // WPF window coordinates are DIPs. WinForms Screen.WorkingArea is in
        // physical pixels, so mixing the two places the window off-screen on
        // a scaled display (for example, at 150% DPI).
        Rect area = SystemParameters.WorkArea;
        int width = OverlayWidth + (_steeringEnabled ? SteeringWidth : 0);
        window.Left = area.Left + area.Width - width - 40d;
        window.Top = area.Top + (area.Height - OverlayHeight) / 2d;
    }

    private static AppSettings SanitizeCandidate(AppSettings candidate)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        string json = JsonSerializer.Serialize(candidate, options);
        return SettingsSanitizer.Sanitize(json);
    }

    private static bool HasArgument(IReadOnlyList<string> args, string argument) =>
        args.Any(value => string.Equals(value, argument, StringComparison.OrdinalIgnoreCase));

    private static int ResolveUdpPort(IReadOnlyList<string> args, int fallback)
    {
        string? argument = args.FirstOrDefault(value => value.StartsWith("--udp-port=", StringComparison.OrdinalIgnoreCase));
        string? value = argument?.Split('=', 2).ElementAtOrDefault(1)
            ?? Environment.GetEnvironmentVariable("F1_UDP_PORT");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
            && port is >= 1 and <= 65535 ? port : fallback;
    }

    private static OverlayStatus WaitingStatus(int port) =>
        new(ConnectionState.Listening, $"Waiting on UDP {port}", port);

    private static PedalTelemetry EmptyTelemetry() =>
        new(0, 0, 0, 0, BrakeLockup.None, 0);
}
