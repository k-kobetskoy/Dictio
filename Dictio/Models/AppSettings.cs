using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace Dictio.Models;

public enum HotkeyMode { Toggle, PushToTalk }

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dictio", "settings.json");

    public string OpenAiApiKey { get; set; } = "";
    public string ModelId { get; set; } = "whisper-1";
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Toggle;
    public Key HotkeyKey { get; set; } = Key.Space;
    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyShift { get; set; } = false;
    public int AudioDeviceIndex { get; set; } = 0;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}