using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dictio.Models;

public enum HotkeyMode { Toggle, PushToTalk }
public enum TranscriptionLanguage { English, Russian, Ukrainian, Auto }
public enum AppColorTheme { System, Light, Dark }
public enum OverlayPosition { BottomCenter, BottomLeft, BottomRight, TopCenter, TopLeft, TopRight }

public enum TranscriptionModelId
{
    GPT4oTranscribe,
    WhisperTurbo, WhisperLarge, WhisperMedium, WhisperSmall, WhisperBase, WhisperTiny,
    ParakeetV3, ParakeetV2,
    SenseVoice,
    CanaryV2,
    GigaAMv3,
}

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dictio", "settings.json");


    public AppColorTheme Theme { get; set; } = AppColorTheme.System;
    public string? FontFamily { get; set; } = null;
    public bool RestoreClipboard { get; set; } = true;
    public double SettingsWindowWidth  { get; set; } = 760;
    public double SettingsWindowHeight { get; set; } = 780;

    public TranscriptionModelId SelectedModel      { get; set; } = TranscriptionModelId.GPT4oTranscribe;
    public bool   ShowTrayIcon          { get; set; } = true;
    public bool   HideOnStart           { get; set; } = false;
    public bool   StartWithWindows      { get; set; } = false;
    public bool   EnableHistory         { get; set; } = true;
    public int    HistoryMaxRecords     { get; set; } = 10;
    public bool   SaveTranscriptionText { get; set; } = true;
    public bool   OverlayVisible        { get; set; } = true;
    public double OverlayOpacity        { get; set; } = 1.0;

    // When true, recordings where the peak RMS never exceeds the silence threshold
    // are skipped — no API call, no paste. The WAV file is still archived.
    public bool SkipSilentRecordings { get; set; } = true;

    // How often (ms) the equalizer scrolls one slot to the left. Range 50–800.
    // Lower = faster scroll; higher = more history visible per slot.
    public int EqScrollIntervalMs { get; set; } = 150;

    // Overlay anchor and offsets in px.
    // VerticalOffsetPx   — distance from top or bottom screen edge.
    // HorizontalOffsetPx — distance from left or right screen edge (ignored for Center positions).
    public OverlayPosition OverlayPosition        { get; set; } = OverlayPosition.BottomCenter;
    public int             OverlayVerticalOffsetPx   { get; set; } = 20;
    public int             OverlayHorizontalOffsetPx { get; set; } = 20;

    // In-memory plain text — never written to disk directly
    public string OpenAiApiKey { get; set; } = "";
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Toggle;
    public int AudioDeviceIndex { get; set; } = 0;
    public bool FirstLaunchDone { get; set; } = false;
    public TranscriptionLanguage TranscriptionLanguage { get; set; } = TranscriptionLanguage.Auto;
    // When true, skips Ogg/Opus compression and sends raw WAV to the API.
    // Useful for comparing transcription quality with and without compression.
    public bool ForceWavDebug { get; set; } = false;

    // Returns ISO-639-1 code to pass to the API, or null to let the model auto-detect.
    public string? LanguageCode => TranscriptionLanguage switch
    {
        TranscriptionLanguage.English   => "en",
        TranscriptionLanguage.Russian   => "ru",
        TranscriptionLanguage.Ukrainian => "uk",
        _                               => null
    };


    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath))
                         ?? new StoredSettings();
            var settings = new AppSettings
            {
                HotkeyMode                = stored.HotkeyMode,
                AudioDeviceIndex          = stored.AudioDeviceIndex,
                FirstLaunchDone           = stored.FirstLaunchDone,
                TranscriptionLanguage     = stored.TranscriptionLanguage,
                Theme                     = stored.Theme,
                FontFamily                = stored.FontFamily,
                RestoreClipboard          = stored.RestoreClipboard,
                SkipSilentRecordings      = stored.SkipSilentRecordings,
                EqScrollIntervalMs        = stored.EqScrollIntervalMs,
                OverlayPosition           = stored.OverlayPosition,
                OverlayVerticalOffsetPx   = stored.OverlayVerticalOffsetPx,
                OverlayHorizontalOffsetPx = stored.OverlayHorizontalOffsetPx,
                ForceWavDebug             = stored.ForceWavDebug,
                SelectedModel             = stored.SelectedModel,
                ShowTrayIcon              = stored.ShowTrayIcon,
                HideOnStart               = stored.HideOnStart,
                StartWithWindows          = stored.StartWithWindows,
                EnableHistory             = stored.EnableHistory,
                HistoryMaxRecords         = stored.HistoryMaxRecords,
                SaveTranscriptionText     = stored.SaveTranscriptionText,
                OverlayVisible            = stored.OverlayVisible,
                OverlayOpacity            = stored.OverlayOpacity,
                SettingsWindowWidth       = stored.SettingsWindowWidth,
                SettingsWindowHeight      = stored.SettingsWindowHeight,
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
                OpenAiApiKeyEncrypted     = Encrypt(OpenAiApiKey),
                HotkeyMode                = HotkeyMode,
                AudioDeviceIndex          = AudioDeviceIndex,
                FirstLaunchDone           = FirstLaunchDone,
                TranscriptionLanguage     = TranscriptionLanguage,
                Theme                     = Theme,
                FontFamily                = FontFamily,
                RestoreClipboard          = RestoreClipboard,
                SkipSilentRecordings      = SkipSilentRecordings,
                EqScrollIntervalMs        = EqScrollIntervalMs,
                OverlayPosition           = OverlayPosition,
                OverlayVerticalOffsetPx   = OverlayVerticalOffsetPx,
                OverlayHorizontalOffsetPx = OverlayHorizontalOffsetPx,
                ForceWavDebug             = ForceWavDebug,
                SelectedModel             = SelectedModel,
                ShowTrayIcon              = ShowTrayIcon,
                HideOnStart               = HideOnStart,
                StartWithWindows          = StartWithWindows,
                EnableHistory             = EnableHistory,
                HistoryMaxRecords         = HistoryMaxRecords,
                SaveTranscriptionText     = SaveTranscriptionText,
                OverlayVisible            = OverlayVisible,
                OverlayOpacity            = OverlayOpacity,
                SettingsWindowWidth       = SettingsWindowWidth,
                SettingsWindowHeight      = SettingsWindowHeight,
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
        public AppColorTheme Theme { get; set; } = AppColorTheme.System;
        public string? FontFamily { get; set; } = null;
        public bool RestoreClipboard { get; set; } = true;
        public bool SkipSilentRecordings { get; set; } = true;
        public int EqScrollIntervalMs { get; set; } = 150;
        public OverlayPosition OverlayPosition        { get; set; } = OverlayPosition.BottomCenter;
        public int             OverlayVerticalOffsetPx   { get; set; } = 20;
        public int             OverlayHorizontalOffsetPx { get; set; } = 20;
        public bool            ForceWavDebug             { get; set; } = false;
        public TranscriptionModelId SelectedModel        { get; set; } = TranscriptionModelId.GPT4oTranscribe;
        public bool   ShowTrayIcon          { get; set; } = true;
        public bool   HideOnStart           { get; set; } = false;
        public bool   StartWithWindows      { get; set; } = false;
        public bool   EnableHistory         { get; set; } = true;
        public int    HistoryMaxRecords     { get; set; } = 10;
        public bool   SaveTranscriptionText { get; set; } = true;
        public bool   OverlayVisible        { get; set; } = true;
        public double OverlayOpacity        { get; set; } = 1.0;
        public double SettingsWindowWidth   { get; set; } = 760;
        public double SettingsWindowHeight  { get; set; } = 780;
    }
}


