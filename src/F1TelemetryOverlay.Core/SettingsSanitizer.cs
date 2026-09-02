using System.Text.Json;
using System.Text.RegularExpressions;

namespace F1TelemetryOverlay.Core;

public static partial class SettingsSanitizer
{
    public static AppSettings Sanitize(JsonElement value)
    {
        JsonElement input = value.ValueKind == JsonValueKind.Object ? value : default;
        JsonElement shortcuts = GetObject(input, "shortcuts");
        JsonElement colors = GetObject(input, "lockupColors");
        JsonElement overlays = GetObject(input, "overlays");
        JsonElement pedalsOverlay = GetObject(overlays, "pedals");
        JsonElement tyreWearOverlay = GetObject(overlays, "tyreWear");
        AppSettings defaults = AppSettings.Default;
        double legacyTransparency = Number(input, "overlayTransparency", 0.2, 1, defaults.OverlayTransparency);
        bool hasPedalsOpacity = TryGet(pedalsOverlay, "opacity", out _);
        double pedalsOpacity = hasPedalsOpacity
            ? Number(pedalsOverlay, "opacity", 0.2, 1, defaults.PedalsOverlay.Opacity)
            : legacyTransparency;

        AppSettings sanitized = new AppSettings(
            Boolean(input, "steeringEnabledByDefault", defaults.SteeringEnabledByDefault),
            // Keep the old field synchronized with the nested pedals opacity.
            // This makes old consumers and newly migrated consumers render the
            // same widget while preserving legacy files on first load.
            pedalsOpacity,
            Integer(input, "udpPort", 1, 65535, defaults.UdpPort),
            Number(input, "lockupSensitivity", 0.15, 0.9, defaults.LockupSensitivity),
            Number(input, "graphDurationSeconds", 2, 15, defaults.GraphDurationSeconds),
            new ShortcutSettings(
                Text(shortcuts, "toggleVisibility", defaults.Shortcuts.ToggleVisibility),
                Text(shortcuts, "toggleLock", defaults.Shortcuts.ToggleLock),
                Text(shortcuts, "toggleDemo", defaults.Shortcuts.ToggleDemo),
                Text(shortcuts, "toggleSteering", defaults.Shortcuts.ToggleSteering),
                Text(shortcuts, "quit", defaults.Shortcuts.Quit)),
            String(input, "lockupColorMode") == "single" ? LockupColorMode.Single : LockupColorMode.Axle,
            new LockupColorSettings(
                Color(colors, "front", defaults.LockupColors.Front),
                Color(colors, "rear", defaults.LockupColors.Rear),
                Color(colors, "both", defaults.LockupColors.Both),
                Color(colors, "single", defaults.LockupColors.Single)))
        {
            SteeringPosition = String(input, "steeringPosition") == "right"
                ? SteeringPosition.Right
                : SteeringPosition.Left,
            PedalsOverlay = OverlayWidget(
                pedalsOverlay,
                defaults.PedalsOverlay,
                pedalsOpacity),
            TyreWearOverlay = OverlayWidget(
                tyreWearOverlay,
                defaults.TyreWearOverlay,
                defaults.TyreWearOverlay.Opacity),
        };

        return sanitized;
    }

    public static AppSettings Sanitize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AppSettings.Default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return Sanitize(document.RootElement);
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
    }

    private static JsonElement GetObject(JsonElement parent, string name) =>
        TryGet(parent, name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static OverlayWidgetSettings OverlayWidget(
        JsonElement input,
        OverlayWidgetSettings defaults,
        double fallbackOpacity) =>
        new(
            Boolean(input, "enabled", defaults.Enabled),
            Boolean(input, "locked", defaults.Locked),
            Number(input, "opacity", 0.2, 1, fallbackOpacity),
            Number(input, "scale", 0.5, 2, defaults.Scale),
            Position(input, "left", defaults.Left),
            Position(input, "top", defaults.Top));

    private static double? Position(JsonElement parent, string name, double? fallback)
    {
        if (!TryGet(parent, name, out JsonElement value)) return fallback;
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out double position) && double.IsFinite(position)
            ? position
            : fallback;
    }

    private static bool Boolean(JsonElement parent, string name, bool fallback) =>
        TryGet(parent, name, out JsonElement value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;

    private static double Number(JsonElement parent, string name, double minimum, double maximum, double fallback) =>
        TryGet(parent, name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number) && double.IsFinite(number)
            ? Math.Clamp(number, minimum, maximum)
            : fallback;

    private static int Integer(JsonElement parent, string name, int minimum, int maximum, int fallback)
    {
        if (!TryGet(parent, name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out double number) ||
            !double.IsFinite(number) ||
            number != Math.Truncate(number))
        {
            return fallback;
        }

        return (int)Math.Clamp(number, minimum, maximum);
    }

    private static string Text(JsonElement parent, string name, string fallback)
    {
        string? value = String(parent, name)?.Trim();
        return value is { Length: > 0 and <= 80 } ? value : fallback;
    }

    private static string Color(JsonElement parent, string name, string fallback)
    {
        string? value = String(parent, name);
        return value is not null && HexColorRegex().IsMatch(value) ? value.ToLowerInvariant() : fallback;
    }

    private static string? String(JsonElement parent, string name) =>
        TryGet(parent, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGet(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    [GeneratedRegex("^#[0-9a-f]{6}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}
