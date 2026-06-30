using System.Text.Json;

namespace TimetableScheduler.ViewModel.Pages;

internal sealed class ManualEditUserSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool ShowRoomAdditionalInfo { get; set; }

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimetableScheduler",
        "manual-edit-settings.json");

    public static ManualEditUserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new ManualEditUserSettings();

            return JsonSerializer.Deserialize<ManualEditUserSettings>(File.ReadAllText(SettingsPath))
                ?? new ManualEditUserSettings();
        }
        catch
        {
            return new ManualEditUserSettings();
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // User settings must not block timetable editing.
        }
    }
}
