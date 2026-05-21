using Dictio.Models;
using Dictio.Services;
using Dictio.Views;
using System.ClientModel;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Application = System.Windows.Application;

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
        Logger.Log($"Settings loaded: device={_settings.AudioDeviceIndex}, mode={_settings.HotkeyMode}");

        _audio = new AudioRecorderService();
        _transcription = new TranscriptionService(() => _settings.OpenAiApiKey);
        _overlay = new OverlayWindow();

        SetupTray();
        SetupHotkey();

        if (!_settings.FirstLaunchDone)
        {
            new WelcomeWindow().ShowDialog();
            _settings.FirstLaunchDone = true;
            _settings.Save();
        }
    }

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Debug", null, (_, _) => OpenDebug());
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
        _overlay!.ShowRecording();
    }

    private async Task StopRecordingAsync()
    {
        if (!_recording) return;
        _recording = false;
        var duration = DateTime.UtcNow - _recordingStarted;
        Logger.Log($"Recording stopped after {duration.TotalSeconds:F1}s, waiting for audio flush...");
        var stream = await Task.Run(() => _audio!.Stop());
        if (duration < MinRecordingDuration)
        {
            Logger.Log($"Recording too short ({duration.TotalSeconds:F1}s < {MinRecordingDuration.TotalSeconds}s), discarding.");
            _overlay!.Hide();
            return;
        }
        long expectedPcmBytes = (long)(duration.TotalSeconds * 16000 * 2);
        Logger.Log($"Audio ready: {stream.Length} bytes (expected ~{expectedPcmBytes + 44} for {duration.TotalSeconds:F1}s), starting transcription");
        AudioArchive.Save(stream);
        _overlay!.ShowTranscribing();
        try
        {
            var text = await _transcription!.TranscribeAsync(stream, _settings.EffectivePrompt);
            Logger.Log($"Transcription result: \"{text}\"");
            if (text == null)
            {
                // Rejected by logprob threshold — already logged inside TranscriptionService
            }
            else if (!string.IsNullOrWhiteSpace(text))
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
            new ErrorPopup(GetUserFriendlyError(ex)).ShowDialog();
        }
        finally
        {
            _overlay.Hide();
        }
    }

    private void OpenDebug()
    {
        new DebugWindow(_transcription!, _settings).Show();
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

    private static string GetUserFriendlyError(Exception ex)
    {
        if (ex is HttpRequestException || ex is TaskCanceledException)
            return "No internet connection. Check your network and try again.";

        if (ex is ClientResultException apiEx)
        {
            return apiEx.Status switch
            {
                401 => "Invalid API key — open Settings to update it.",
                429 => "OpenAI rate limit reached, try again later.",
                >= 500 => $"OpenAI service error (HTTP {apiEx.Status}).",
                _ => $"API error (HTTP {apiEx.Status})."
            };
        }

        return ex.Message;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _audio?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}