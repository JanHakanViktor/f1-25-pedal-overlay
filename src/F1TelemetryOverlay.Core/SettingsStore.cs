using System.Text.Json;
using System.Text.Json.Serialization;

namespace F1TelemetryOverlay.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? GetDefaultFilePath();
    }

    public string FilePath { get; }

    public static string GetDefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "F1 25 Pedal Overlay",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            return SettingsSanitizer.Sanitize(File.ReadAllText(FilePath));
        }
        catch (IOException)
        {
            return AppSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    public AppSettings Save(AppSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        AppSettings sanitized = SettingsSanitizer.Sanitize(json);
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(sanitized, SerializerOptions) + Environment.NewLine);
        return sanitized;
    }
}
