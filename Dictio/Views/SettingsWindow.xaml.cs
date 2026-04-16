using NAudio.Wave;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dictio.Models;

namespace Dictio.Views;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Result = current;
        Load(current);
    }

    private void Load(AppSettings s)
    {
        ApiKeyBox.Password = s.OpenAiApiKey;
        PopulateAudioDevices(s.AudioDeviceIndex);
        ModelCombo.SelectedIndex = s.ModelId == "gpt-4o-transcribe" ? 1 : 0;
        HotkeyModeCombo.SelectedIndex = s.HotkeyMode == HotkeyMode.PushToTalk ? 1 : 0;
        CtrlCheck.IsChecked = s.HotkeyCtrl;
        ShiftCheck.IsChecked = s.HotkeyShift;
        KeyBox.Text = s.HotkeyKey.ToString();
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = new AppSettings
        {
            OpenAiApiKey = ApiKeyBox.Password,
            AudioDeviceIndex = (AudioDeviceCombo.SelectedItem as ComboBoxItem)?.Tag is int idx ? idx : 0,
            ModelId = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "whisper-1",
            HotkeyMode = HotkeyModeCombo.SelectedIndex == 1 ? HotkeyMode.PushToTalk : HotkeyMode.Toggle,
            HotkeyCtrl = CtrlCheck.IsChecked == true,
            HotkeyShift = ShiftCheck.IsChecked == true,
            HotkeyKey = Enum.TryParse<Key>(KeyBox.Text, true, out var k) ? k : Key.Space,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}