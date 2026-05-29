using NAudio.Wave;
using System.Windows;
using System.Windows.Controls;
using Dictio.Models;

namespace Dictio.Views;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }
    private readonly AppSettings _current;
    private float? _temperature;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _current = current;
        Result = current;
        Load(current);
    }

    private void Load(AppSettings s)
    {
        ApiKeyBox.Password = s.OpenAiApiKey;
        PopulateAudioDevices(s.AudioDeviceIndex);
        HotkeyModeCombo.SelectedIndex = s.HotkeyMode == HotkeyMode.PushToTalk ? 1 : 0;

        LanguageCombo.SelectedIndex = s.TranscriptionLanguage switch
        {
            TranscriptionLanguage.English => 1,
            TranscriptionLanguage.Russian => 2,
            TranscriptionLanguage.Ukrainian => 3,
            _ => 0  // Auto
        };

        CustomPromptBox.Text = s.CustomTranscriptionPrompt;
        _temperature = s.TranscriptionTemperature;
        UpdateTemperatureDisplay();
        UpdateDefaultPromptPreview();
    }

    private void PopulateAudioDevices(int selectedIndex)
    {
        AudioDeviceCombo.Items.Clear();
        int count = WaveIn.DeviceCount;
        if (count == 0)
        {
            AudioDeviceCombo.Items.Add(new ComboBoxItem { Content = "(No microphones found)", IsEnabled = false });
            AudioDeviceCombo.SelectedIndex = 0;
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            var label = i == 0 ? $"{caps.ProductName} (default)" : caps.ProductName;
            AudioDeviceCombo.Items.Add(new ComboBoxItem { Content = label, Tag = i });
        }
        AudioDeviceCombo.SelectedIndex = selectedIndex < count ? selectedIndex : 0;
    }

    private void UpdateTemperatureDisplay()
    {
        TemperatureDisplay.Text = _temperature.HasValue
            ? _temperature.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : "–";
    }

    private void TempUp_Click(object sender, RoutedEventArgs e)
    {
        _temperature = _temperature.HasValue
            ? (float)Math.Round(Math.Min(1.0f, _temperature.Value + 0.1f), 1)
            : 0.1f;
        UpdateTemperatureDisplay();
    }

    private void TempDown_Click(object sender, RoutedEventArgs e)
    {
        if (!_temperature.HasValue) return;
        var next = (float)Math.Round(_temperature.Value - 0.1f, 1);
        _temperature = next <= 0f ? null : next;
        UpdateTemperatureDisplay();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDefaultPromptPreview();
    }

    private void UpdateDefaultPromptPreview()
    {
        if (DefaultPromptPreview == null) return;
        var lang = SelectedLanguage();
        DefaultPromptPreview.Text = lang == TranscriptionLanguage.Auto
            ? "(language will be auto-detected)"
            : AppSettings.GetDefaultPrompt(lang);
    }

    private TranscriptionLanguage SelectedLanguage() => LanguageCombo.SelectedIndex switch
    {
        1 => TranscriptionLanguage.English,
        2 => TranscriptionLanguage.Russian,
        3 => TranscriptionLanguage.Ukrainian,
        _ => TranscriptionLanguage.Auto
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = new AppSettings
        {
            OpenAiApiKey = ApiKeyBox.Password,
            AudioDeviceIndex = (AudioDeviceCombo.SelectedItem as ComboBoxItem)?.Tag is int idx ? idx : 0,
            HotkeyMode = HotkeyModeCombo.SelectedIndex == 1 ? HotkeyMode.PushToTalk : HotkeyMode.Toggle,
            TranscriptionLanguage = SelectedLanguage(),
            CustomTranscriptionPrompt = CustomPromptBox.Text.Trim(),
            TranscriptionTemperature = _temperature,
            FirstLaunchDone   = _current.FirstLaunchDone,
            Theme             = _current.Theme,
            FontFamily        = _current.FontFamily,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
