namespace F1TelemetryOverlay.Core;

public sealed record ShortcutSettings(
    string ToggleVisibility,
    string ToggleLock,
    string ToggleDemo,
    string ToggleSteering,
    string Quit);

public sealed record LockupColorSettings(
    string Front,
    string Rear,
    string Both,
    string Single);

public sealed record AppSettings(
    bool SteeringEnabledByDefault,
    double OverlayTransparency,
    int UdpPort,
    double LockupSensitivity,
    double GraphDurationSeconds,
    ShortcutSettings Shortcuts,
    LockupColorMode LockupColorMode,
    LockupColorSettings LockupColors)
{
    // Added as an init-only property so existing settings files and callers
    // using the original positional constructor remain source-compatible.
    public SteeringPosition SteeringPosition { get; init; } = SteeringPosition.Left;

    public static AppSettings Default { get; } = new(
        SteeringEnabledByDefault: false,
        OverlayTransparency: 0.3,
        UdpPort: 20777,
        LockupSensitivity: 0.35,
        GraphDurationSeconds: 5,
        Shortcuts: new ShortcutSettings(
            ToggleVisibility: "Control+Shift+H",
            ToggleLock: "Control+Shift+O",
            ToggleDemo: "Control+Shift+D",
            ToggleSteering: "Control+Shift+S",
            Quit: "Control+Shift+Q"),
        LockupColorMode: LockupColorMode.Axle,
        LockupColors: new LockupColorSettings(
            Front: "#ffd84a",
            Rear: "#ff8a2a",
            Both: "#8f1525",
            Single: "#ffd84a"));
}
