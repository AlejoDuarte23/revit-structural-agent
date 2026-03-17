using System.Text.Json;

namespace AnalyticalExport;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AnalyticalExport");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static string? LoadLastJsonPath()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        try
        {
            SettingsDto? settings = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(SettingsPath));
            return string.IsNullOrWhiteSpace(settings?.LastJsonPath) ? null : settings.LastJsonPath;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLastJsonPath(string path)
    {
        Directory.CreateDirectory(SettingsDirectory);

        SettingsDto payload = new()
        {
            LastJsonPath = path,
        };

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private sealed class SettingsDto
    {
        public string? LastJsonPath { get; set; }
    }
}
