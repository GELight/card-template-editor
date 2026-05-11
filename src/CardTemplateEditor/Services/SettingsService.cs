using System.IO;
using System.Text.Json;
using CardTemplateEditor.Models;

namespace CardTemplateEditor.Services;

public class SettingsService
{
    private readonly string _dataDir;

    public SettingsService(string dataDir)
    {
        _dataDir = dataDir;
    }

    public string SettingsFile => Path.Combine(_dataDir, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFile))
            return new AppSettings();

        try
        {
            using var stream = File.OpenRead(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(stream, JsonStorage.Options)
                   ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_dataDir);
        var tmp = SettingsFile + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, settings, JsonStorage.Options);
        }
        File.Move(tmp, SettingsFile, overwrite: true);
    }
}
