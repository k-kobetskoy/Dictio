using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dictio.Models;

public enum HotkeyMode { Toggle, PushToTalk }
public enum TranscriptionLanguage { English, Russian, Ukrainian, Auto }
public enum AppColorTheme { System, Light, Dark }

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dictio", "settings.json");

    private static readonly Dictionary<TranscriptionLanguage, string> DefaultPrompts = new()
    {
        [TranscriptionLanguage.English] =
            "Hello. Today I want to go over a few important things. " +
            "First, let's look at the main idea — it's quite straightforward. " +
            "We have several options available, and each one has its own advantages. " +
            "The key point here is clarity of expression.",

        [TranscriptionLanguage.Russian] =
            "Привет. Сегодня я хочу рассказать о нескольких важных вещах. " +
            "Во-первых, давайте разберём основную идею — она достаточно проста. " +
            "У нас есть несколько вариантов, и у каждого свои преимущества. " +
            "Главное здесь — чёткость изложения.",

        [TranscriptionLanguage.Ukrainian] =
            "Привіт. Сьогодні я хочу розповісти про кілька важливих речей. " +
            "По-перше, давайте розберемо основну ідею — вона досить проста. " +
            "У нас є кілька варіантів, і кожен має свої переваги. " +
            "Головне тут — чіткість викладу.",
    };

    public AppColorTheme Theme { get; set; } = AppColorTheme.System;
    public string? FontFamily { get; set; } = null;
    public bool RestoreClipboard { get; set; } = true;

    // In-memory plain text — never written to disk directly
    public string OpenAiApiKey { get; set; } = "";
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Toggle;
    public int AudioDeviceIndex { get; set; } = 0;
    public bool FirstLaunchDone { get; set; } = false;
    public TranscriptionLanguage TranscriptionLanguage { get; set; } = TranscriptionLanguage.Auto;
    public string CustomTranscriptionPrompt { get; set; } = "";

    // null = not specified (API uses its default); valid range 0–1
    public float? TranscriptionTemperature { get; set; } = null;

    // Returns ISO-639-1 code to pass to the API, or null to let the model auto-detect.
    public string? LanguageCode => TranscriptionLanguage switch
    {
        TranscriptionLanguage.English   => "en",
        TranscriptionLanguage.Russian   => "ru",
        TranscriptionLanguage.Ukrainian => "uk",
        _                               => null
    };

    // Returns custom prompt if set, otherwise the default for the selected language
    public string EffectivePrompt =>
        string.IsNullOrWhiteSpace(CustomTranscriptionPrompt)
            ? DefaultPrompts[TranscriptionLanguage]
            : CustomTranscriptionPrompt;

    public static string GetDefaultPrompt(TranscriptionLanguage lang) => DefaultPrompts[lang];

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath))
                         ?? new StoredSettings();
            var settings = new AppSettings
            {
                HotkeyMode               = stored.HotkeyMode,
                AudioDeviceIndex         = stored.AudioDeviceIndex,
                FirstLaunchDone         = stored.FirstLaunchDone,
                TranscriptionLanguage    = stored.TranscriptionLanguage,
                CustomTranscriptionPrompt = stored.CustomTranscriptionPrompt,
                TranscriptionTemperature  = stored.TranscriptionTemperature,
                Theme = stored.Theme,
                FontFamily               = stored.FontFamily,
                RestoreClipboard         = stored.RestoreClipboard,
            };
            if (!string.IsNullOrEmpty(stored.OpenAiApiKeyEncrypted))
                settings.OpenAiApiKey = Decrypt(stored.OpenAiApiKeyEncrypted);
            return settings;
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var stored = new StoredSettings
            {
                OpenAiApiKeyEncrypted    = Encrypt(OpenAiApiKey),
                HotkeyMode               = HotkeyMode,
                AudioDeviceIndex         = AudioDeviceIndex,
                FirstLaunchDone         = FirstLaunchDone,
                TranscriptionLanguage    = TranscriptionLanguage,
                CustomTranscriptionPrompt = CustomTranscriptionPrompt,
                TranscriptionTemperature  = TranscriptionTemperature,
                Theme = Theme,
                FontFamily               = FontFamily,
                RestoreClipboard         = RestoreClipboard,
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Decrypt(string base64)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(base64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; }
    }

    // DTO used only for JSON serialization
    private class StoredSettings
    {
        public string OpenAiApiKeyEncrypted { get; set; } = "";
        public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Toggle;
        public int AudioDeviceIndex { get; set; } = 0;
        public bool FirstLaunchDone { get; set; } = false;
        public TranscriptionLanguage TranscriptionLanguage { get; set; } = TranscriptionLanguage.Auto;
        public string CustomTranscriptionPrompt { get; set; } = "";
        public float? TranscriptionTemperature { get; set; } = null;
        public AppColorTheme Theme { get; set; } = AppColorTheme.System;
        public string? FontFamily { get; set; } = null;
        public bool RestoreClipboard { get; set; } = true;
    }
}


