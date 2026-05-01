using System.IO;
using System.Text.Json;

namespace EqApoTray.Services;

public class Settings
{
    public string ConfigPath { get; set; } = @"C:\Program Files\EqualizerAPO\config\config.txt";
}

public static class SettingsStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EqApoTray");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<Settings>(json);
                if (s is not null) return s;
            }
        }
        catch
        {
            // fall through to defaults
        }
        return new Settings();
    }

    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
