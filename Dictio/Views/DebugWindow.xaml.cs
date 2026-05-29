using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dictio.Models;
using Dictio.Services;

namespace Dictio.Views;

public partial class DebugWindow : Window
{
    private readonly TranscriptionService _transcription;
    private readonly AppSettings _settings;

    public DebugWindow(TranscriptionService transcription, AppSettings settings)
    {
        InitializeComponent();
        _transcription = transcription;
        _settings = settings;
        LoadAudioList();
    }

    private void LoadAudioList()
    {
        AudioList.Items.Clear();
        foreach (var file in AudioArchive.GetAll())
            AudioList.Items.Add(new AudioEntry(file));

        if (AudioList.Items.Count == 0)
            StatusText.Text = "No recordings yet.";
        else
            StatusText.Text = $"{AudioList.Items.Count} recording(s)";
    }

    private void AudioList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RetranscribeBtn.IsEnabled = AudioList.SelectedItem != null;
    }

    private async void Retranscribe_Click(object sender, RoutedEventArgs e)
    {
        if (AudioList.SelectedItem is not AudioEntry entry) return;

        RetranscribeBtn.IsEnabled = false;
        StatusText.Text = "Transcribing…";
        ResultBox.Text = "";

        int? prefixMs = int.TryParse(PrefixPaddingBox.Text, out var p) ? p : null;
        int? silenceMs = int.TryParse(SilenceDurationBox.Text, out var s) ? s : null;
        float? temperature = float.TryParse(TemperatureBox.Text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var t)
            ? t
            : _settings.TranscriptionTemperature;

        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(entry.File.FullName));
            using var stream = new MemoryStream(bytes);
            var result = await _transcription.TranscribeAsync(stream, prompt: null, prefixMs, silenceMs, temperature);

            if (result == null)
            {
                ResultBox.Text = "(Rejected — low confidence. Check debug.log for logprob details.)";
                StatusText.Text = "Rejected";
            }
            else if (string.IsNullOrWhiteSpace(result))
            {
                ResultBox.Text = "(Empty transcription)";
                StatusText.Text = "Empty";
            }
            else
            {
                ResultBox.Text = result;
                StatusText.Text = "Done";
            }
        }
        catch (Exception ex)
        {
            ResultBox.Text = $"Error: {ex.Message}";
            StatusText.Text = "Error";
        }
        finally
        {
            RetranscribeBtn.IsEnabled = AudioList.SelectedItem != null;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadAudioList();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Dictio", "audio");
        if (Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed class AudioEntry(System.IO.FileInfo file)
    {
        public System.IO.FileInfo File { get; } = file;
        public string DisplayName =>
            $"{File.LastWriteTime:yyyy-MM-dd  HH:mm:ss}   {File.Length / 1024,4} KB   {File.Name}";
    }
}
