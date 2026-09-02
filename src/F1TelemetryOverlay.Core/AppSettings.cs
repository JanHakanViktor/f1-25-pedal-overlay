using System.Text.Json.Serialization;

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

/// <summary>
/// Persisted state for one independently movable overlay widget.
/// Positions use WPF logical DIPs rather than physical screen pixels.
/// </summary>
public sealed record OverlayWidgetSettings(
    bool Enabled,
    bool Locked,
    double Opacity,
    double Scale,
    double? Left,
    double? Top)
{
    public static OverlayWidgetSettings DefaultPedals { get; } = new(
        Enabled: true,
        Locked: false,
        Opacity: 0.3,
        Scale: 1,
        Left: null,
        Top: null);

    public static OverlayWidgetSettings DefaultTyreWear { get; } = new(
        Enabled: false,
        Locked: false,
        Opacity: 0.3,
        Scale: 1,
        Left: null,
        Top: null);
}

public sealed record OverlaySettings(
    OverlayWidgetSettings Pedals,
    OverlayWidgetSettings TyreWear);

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

    // Init-only additions keep the original positional constructor source
    // compatible while allowing settings.json to gain independently persisted
    // overlay state.
    [JsonIgnore]
    public OverlayWidgetSettings PedalsOverlay { get; init; } = OverlayWidgetSettings.DefaultPedals;

    [JsonIgnore]
    public OverlayWidgetSettings TyreWearOverlay { get; init; } = OverlayWidgetSettings.DefaultTyreWear;

    // The wire shape is intentionally stable: overlays.pedals and
    // overlays.tyreWear. The convenience properties above keep call sites
    // strongly typed and source-compatible with the original settings record.
    [JsonPropertyName("overlays")]
    public OverlaySettings Overlays => new(PedalsOverlay, TyreWearOverlay);

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
