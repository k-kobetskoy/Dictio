using Dictio.Models;
using Dictio.Services;
using Dictio.Views;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Dictio;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private HotkeyService? _hotkey;
    private AudioRecorderService? _audio;
    private TranscriptionService? _transcription;
    private OverlayWindow? _overlay;
    private AppSettings _settings = AppSettings.Load();
    private bool _recording;
    private DateTime _recordingStarted;
    private IntPtr _targetWindow;
    private static readonly TimeSpan MinRecordingDuration = TimeSpan.FromSeconds(1);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Logger.Log("=== Dictio started ===");
        Logger.Log($"Settings loaded: model={_settings.ModelId}, device={_settings.AudioDeviceIndex}, hotkey={(_settings.HotkeyCtrl?"Ctrl+":"")}{(_settings.HotkeyShift?"Shift+":"")}{_settings.HotkeyKey}, mode={_settings.HotkeyMode}");

        _audio = new AudioRecorderService();
        _transcription = new TranscriptionService(() => _settings.OpenAiApiKey, () => _settings.ModelId);
        _overlay = new OverlayWindow();

        SetupTray();
        SetupHotkey();
    }

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Text = "Dictio",
            Icon = CreateTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
    }

    private void SetupHotkey()
    {
        _hotkey = new HotkeyService(() => _settings);
        _hotkey.HotkeyPressed += OnToggle;
        _hotkey.RecordStartPressed += StartRecording;
        _hotkey.RecordStopPressed += () => _ = StopRecordingAsync();
        _hotkey.Install();
    }

    private void OnToggle()
    {
        if (_recording) _ = StopRecordingAsync();
        else StartRecording();
    }

    private void StartRecording()
    {
        if (_recording) return;
        if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey)) { OpenSettings(); return; }
        _targetWindow = GetForegroundWindow();
        _recording = true;
        _recordingStarted = DateTime.UtcNow;
        Logger.Log($"Recording started (device={_settings.AudioDeviceIndex}, target=0x{_targetWindow:X8})");
        _audio!.Start(_settings.AudioDeviceIndex);
        _overlay!.Show();
    }

    private async Task StopRecordingAsync()
    {
        if (!_recording) return;
        _recording = false;
        _overlay!.Hide();
        var duration = DateTime.UtcNow - _recordingStarted;
        Logger.Log($"Recording stopped after {duration.TotalSeconds:F1}s, waiting for audio flush...");
        var stream = await Task.Run(() => _audio!.Stop());
        if (duration < MinRecordingDuration)
        {
            Logger.Log($"Recording too short ({duration.TotalSeconds:F1}s < {MinRecordingDuration.TotalSeconds}s), discarding.");
            return;
        }
        long expectedPcmBytes = (long)(duration.TotalSeconds * 16000 * 2);
        Logger.Log($"Audio ready: {stream.Length} bytes (expected ~{expectedPcmBytes + 44} for {duration.TotalSeconds:F1}s), starting transcription (model={_settings.ModelId})");
        try
        {
            var text = await _transcription!.TranscribeAsync(stream);
            Logger.Log($"Transcription result: \"{text}\"");
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (_targetWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(_targetWindow);
                    await Task.Delay(80);
                }
                ClipboardService.PasteText(text);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Transcription ERROR: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show($"Transcription failed:\n{ex.Message}", "Dictio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings);
        if (win.ShowDialog() == true)
        {
            _settings = win.Result;
            _settings.Save();
        }
    }

    private static Icon CreateTrayIcon()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawEllipse(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 120, 230)),
                null,
                new System.Windows.Point(8, 8), 7, 7);

        var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        var bmp = new Bitmap(ms);
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _audio?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}