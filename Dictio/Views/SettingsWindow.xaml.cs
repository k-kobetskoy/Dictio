using NAudio.Wave;
using System.Windows;
using System.Windows.Controls;
using Dictio.Models;

namespace Dictio.Views;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }
    private readonly AppSettings _current;

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
            TranscriptionLanguage.Russian => 1,
            TranscriptionLanguage.Ukrainian => 2,
            _ => 0
        };

        CustomPromptBox.Text = s.CustomTranscriptionPrompt;
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

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDefaultPromptPreview();
    }

    private void UpdateDefaultPromptPreview()
    {
        if (DefaultPromptPreview == null) return;
        DefaultPromptPreview.Text = AppSettings.GetDefaultPrompt(SelectedLanguage());
    }

    private TranscriptionLanguage SelectedLanguage() => LanguageCombo.SelectedIndex switch
    {
        1 => TranscriptionLanguage.Russian,
        2 => TranscriptionLanguage.Ukrainian,
        _ => TranscriptionLanguage.English
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
            FirstLaunchDone = _current.FirstLaunchDone,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
