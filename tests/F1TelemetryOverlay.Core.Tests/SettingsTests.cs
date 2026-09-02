using System.Text.Json;
using F1TelemetryOverlay.Core;

namespace F1TelemetryOverlay.Core.Tests;

public sealed class SettingsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void InvalidSettingsUseSafeDefaults(string json) =>
        Assert.Equal(AppSettings.Default, SettingsSanitizer.Sanitize(json));

    [Fact]
    public void WrongTypesAndInvalidColorsUseDefaults()
    {
        AppSettings result = SettingsSanitizer.Sanitize("""
            { "udpPort": "20777", "lockupColors": { "front": "yellow" } }
            """);

        Assert.Equal(AppSettings.Default, result);
    }

    [Fact]
    public void NormalizesRangesColorsAndShortcutStrings()
    {
        AppSettings result = SettingsSanitizer.Sanitize("""
            {
              "steeringEnabledByDefault": true,
              "steeringPosition": "right",
              "overlayTransparency": 0.05,
              "udpPort": 20778,
              "lockupSensitivity": 2,
              "graphDurationSeconds": 8.5,
              "shortcuts": { "toggleVisibility": "  Alt+H  ", "quit": "" },
              "lockupColorMode": "single",
              "lockupColors": { "front": "#AABBCC", "single": "#123456" }
            }
            """);

        Assert.True(result.SteeringEnabledByDefault);
        Assert.Equal(SteeringPosition.Right, result.SteeringPosition);
        Assert.Equal(0.2, result.OverlayTransparency);
        Assert.Equal(20778, result.UdpPort);
        Assert.Equal(0.9, result.LockupSensitivity);
        Assert.Equal(8.5, result.GraphDurationSeconds);
        Assert.Equal("Alt+H", result.Shortcuts.ToggleVisibility);
        Assert.Equal(AppSettings.Default.Shortcuts.Quit, result.Shortcuts.Quit);
        Assert.Equal(LockupColorMode.Single, result.LockupColorMode);
        Assert.Equal("#aabbcc", result.LockupColors.Front);
        Assert.Equal("#123456", result.LockupColors.Single);
        Assert.Equal(0.2, result.PedalsOverlay.Opacity);
        Assert.Equal(AppSettings.Default.TyreWearOverlay, result.TyreWearOverlay);
    }

    [Fact]
    public void LegacyTransparencyMigratesToPedalsOverlay()
    {
        AppSettings result = SettingsSanitizer.Sanitize("""{ "overlayTransparency": 0.64 }""");

        Assert.Equal(0.64, result.OverlayTransparency);
        Assert.Equal(0.64, result.PedalsOverlay.Opacity);
        Assert.True(result.PedalsOverlay.Enabled);
        Assert.False(result.TyreWearOverlay.Enabled);
    }

    [Fact]
    public void NestedOverlayValuesAreClampedAndMalformedValuesRecover()
    {
        AppSettings result = SettingsSanitizer.Sanitize("""
            {
              "overlayTransparency": 0.91,
              "overlays": {
                "pedals": { "enabled": false, "locked": true, "opacity": 0.1, "scale": 3, "left": 120.5, "top": null },
                "tyreWear": { "enabled": true, "locked": "yes", "opacity": "opaque", "scale": 0.1, "left": "off-screen", "top": 42.25 }
              }
            }
            """);

        Assert.False(result.PedalsOverlay.Enabled);
        Assert.True(result.PedalsOverlay.Locked);
        Assert.Equal(0.2, result.PedalsOverlay.Opacity);
        Assert.Equal(2, result.PedalsOverlay.Scale);
        Assert.Equal(120.5, result.PedalsOverlay.Left);
        Assert.Null(result.PedalsOverlay.Top);
        Assert.Equal(AppSettings.Default.TyreWearOverlay.Opacity, result.TyreWearOverlay.Opacity);
        Assert.True(result.TyreWearOverlay.Enabled);
        Assert.False(result.TyreWearOverlay.Locked);
        Assert.Equal(0.5, result.TyreWearOverlay.Scale);
        Assert.Null(result.TyreWearOverlay.Left);
        Assert.Equal(42.25, result.TyreWearOverlay.Top);
        // The nested pedal opacity is authoritative when both representations
        // are supplied, and the legacy field follows it.
        Assert.Equal(result.PedalsOverlay.Opacity, result.OverlayTransparency);
    }

    [Fact]
    public void OverlaySettingsRoundTripThroughStore()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "settings.json");
        SettingsStore store = new(path);
        AppSettings input = AppSettings.Default with
        {
            PedalsOverlay = AppSettings.Default.PedalsOverlay with { Opacity = 0.72, Scale = 1.25, Left = 18, Top = 24 },
            TyreWearOverlay = AppSettings.Default.TyreWearOverlay with { Enabled = true, Locked = true, Opacity = 0.88, Scale = 1.4, Left = 500, Top = 300 },
        };

        AppSettings saved = store.Save(input);
        AppSettings loaded = store.Load();

        Assert.Equal(input.PedalsOverlay, saved.PedalsOverlay);
        Assert.Equal(input.TyreWearOverlay, saved.TyreWearOverlay);
        Assert.Equal(saved.PedalsOverlay, loaded.PedalsOverlay);
        Assert.Equal(saved.TyreWearOverlay, loaded.TyreWearOverlay);
        string persisted = File.ReadAllText(path);
        Assert.Contains("\"pedals\"", persisted, StringComparison.Ordinal);
        Assert.Contains("\"tyreWear\"", persisted, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(persisted);
        JsonElement root = document.RootElement;
        JsonElement overlays = root.GetProperty("overlays");
        Assert.True(overlays.TryGetProperty("pedals", out JsonElement pedals));
        Assert.True(overlays.TryGetProperty("tyreWear", out JsonElement tyreWear));
        Assert.False(overlays.TryGetProperty("tyrewear", out _));
        Assert.False(root.TryGetProperty("pedalsOverlay", out _));
        Assert.False(root.TryGetProperty("tyreWearOverlay", out _));
        Assert.Equal(0.72, pedals.GetProperty("opacity").GetDouble());
        Assert.Equal(0.88, tyreWear.GetProperty("opacity").GetDouble());
        Assert.Equal(0.72, root.GetProperty("overlayTransparency").GetDouble());
    }

    [Theory]
    [InlineData("1e100", 65535)]
    [InlineData("-1e100", 1)]
    [InlineData("20777.5", 20777)]
    public void UdpPortClampsNumericInputToValidRange(string jsonNumber, int expected)
    {
        AppSettings result = SettingsSanitizer.Sanitize($$"""{ "udpPort": {{jsonNumber}} }""");

        Assert.Equal(expected, result.UdpPort);
    }

    [Fact]
    public void LoadsExistingCamelCaseSettingsAndPersistsJson()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, """
            {
              "steeringEnabledByDefault": true,
              "steeringPosition": "right",
              "udpPort": 20778,
              "graphDurationSeconds": 7,
              "shortcuts": { "toggleSteering": "Alt+Shift+S" },
              "lockupColors": { "rear": "#ABCDEF" }
            }
            """);
        SettingsStore store = new(path);

        AppSettings loaded = store.Load();
        AppSettings saved = store.Save(loaded);
        string persisted = File.ReadAllText(path);

        Assert.True(saved.SteeringEnabledByDefault);
        Assert.Equal(SteeringPosition.Right, saved.SteeringPosition);
        Assert.Equal(20778, saved.UdpPort);
        Assert.Equal(7, saved.GraphDurationSeconds);
        Assert.Equal("Alt+Shift+S", saved.Shortcuts.ToggleSteering);
        Assert.Equal("#abcdef", saved.LockupColors.Rear);
        Assert.Contains("\"steeringEnabledByDefault\"", persisted, StringComparison.Ordinal);
        Assert.Contains("\"steeringPosition\": \"right\"", persisted, StringComparison.Ordinal);
        Assert.Contains("\"lockupColorMode\": \"axle\"", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAndMalformedFilesLoadDefaults()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "settings.json");
        SettingsStore store = new(path);
        Assert.Equal(AppSettings.Default, store.Load());

        File.WriteAllText(path, "{");
        Assert.Equal(AppSettings.Default, store.Load());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"f1-overlay-core-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
