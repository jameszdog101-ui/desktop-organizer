using System.IO;
using System.Text.Json;
using PerformanceGuardian.Models;

namespace PerformanceGuardian.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DataDirectory { get; }
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public SettingsService(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartDesktopGuardian");
    }

    public OrganizerSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<OrganizerSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new();
        }
        catch { }
        return new OrganizerSettings();
    }

    public void Save(OrganizerSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
