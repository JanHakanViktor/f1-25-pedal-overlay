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
    }

    [Theory]
    [InlineData("1e100", 65535)]
    [InlineData("-1e100", 1)]
    [InlineData("20777.5", 20777)]
    public void UdpPortMatchesJavaScriptIntegerAndClampSemantics(string jsonNumber, int expected)
    {
        AppSettings result = SettingsSanitizer.Sanitize($$"""{ "udpPort": {{jsonNumber}} }""");

        Assert.Equal(expected, result.UdpPort);
    }

    [Fact]
    public void LoadsExistingElectronCamelCaseSettingsAndPersistsCompatibleJson()
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
