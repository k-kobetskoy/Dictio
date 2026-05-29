using Dictio.Models;
using Dictio.Services;
using Dictio.Views;
using NAudio.Wave;
using System.ClientModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application   = System.Windows.Application;
using MouseButtons  = System.Windows.Forms.MouseButtons;
using WpfColor      = System.Windows.Media.Color;
using WpfPoint      = System.Windows.Point;

namespace Dictio;

public partial class App : Application
{
    private NotifyIcon?           _tray;
    private ContextMenu?          _trayMenu;
    private HotkeyService?        _hotkey;
    private AudioRecorderService? _audio;
    private TranscriptionService? _transcription;
    private OverlayWindow?        _overlay;
    private AppSettings           _settings = AppSettings.Load();
    private bool                  _recording;
    private DateTime              _recordingStarted;
    private IntPtr                _targetWindow;

    // Theme submenu items — kept as fields so IsChecked can be updated.
    private MenuItem? _themeAuto, _themeLight, _themeDark;
    private MenuItem? _micMenu;

    private static readonly TimeSpan MinRecordingDuration = TimeSpan.FromSeconds(1);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool   SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool   GetCursorPos(out POINT pt);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Logger.Log("=== Dictio started ===");
        Logger.Log($"Settings: device={_settings.AudioDeviceIndex}, mode={_settings.HotkeyMode}, theme={_settings.Theme}");

        _audio         = new AudioRecorderService();
        _transcription = new TranscriptionService(() => _settings.OpenAiApiKey);

        // Apply theme before creating the overlay so Wpf.Ui resources are
        // initialised once and don't retroactively affect the transparent window.
        ThemeService.Apply(_settings.Theme);

        _overlay = new OverlayWindow();
        _audio.LevelChanged      += level => _overlay.SetLevel(level);
        _overlay.RecordRequested += StartRecording;
        _overlay.SendRequested   += () => _ = StopRecordingAsync();
        _overlay.Show();

        SetupTray();
        SetupHotkey();

        if (!_settings.FirstLaunchDone)
        {
            new WelcomeWindow().ShowDialog();
            _settings.FirstLaunchDone = true;
            _settings.Save();
        }
    }

    // ── Tray ─────────────────────────────────────────────────────────────────

    private void SetupTray()
    {
        _trayMenu = BuildTrayMenu();

        _tray = new NotifyIcon
        {
            Text    = "Dictio",
            Icon    = CreateTrayIcon(),
            Visible = true
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Dispatcher.Invoke(OpenSettings);
            else if (e.Button == MouseButtons.Right)
                Dispatcher.Invoke(ShowTrayMenu);
        };
    }

    private ContextMenu BuildTrayMenu()
    {
        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => OpenSettings();

        _micMenu = new MenuItem { Header = "Microphone" };

        var themeItem = new MenuItem { Header = "Theme" };
        _themeAuto  = new MenuItem { Header = "Auto",  IsCheckable = true };
        _themeLight = new MenuItem { Header = "Light", IsCheckable = true };
        _themeDark  = new MenuItem { Header = "Dark",  IsCheckable = true };
        _themeAuto.Click  += (_, _) => ApplyTheme(AppColorTheme.System);
        _themeLight.Click += (_, _) => ApplyTheme(AppColorTheme.Light);
        _themeDark.Click  += (_, _) => ApplyTheme(AppColorTheme.Dark);
        themeItem.Items.Add(_themeAuto);
        themeItem.Items.Add(_themeLight);
        themeItem.Items.Add(_themeDark);

        var debugItem = new MenuItem { Header = "Debug" };
        debugItem.Click += (_, _) => OpenDebug();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();

        var menu = new ContextMenu { StaysOpen = false };
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_micMenu);
        menu.Items.Add(themeItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(debugItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        menu.Opened += (_, _) =>
        {
            RefreshMicMenu();
            UpdateThemeChecks();
        };

        return menu;
    }

    private void ShowTrayMenu()
    {
        GetCursorPos(out var pt);
        _trayMenu!.PlacementTarget = _overlay;
        _trayMenu.Placement        = PlacementMode.AbsolutePoint;
        _trayMenu.HorizontalOffset = pt.X;
        _trayMenu.VerticalOffset   = pt.Y;
        _trayMenu.IsOpen           = true;
    }

    private void RefreshMicMenu()
    {
        _micMenu!.Items.Clear();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps  = WaveIn.GetCapabilities(i);
            var label = i == 0 ? $"{caps.ProductName} (default)" : caps.ProductName;
            var item  = new MenuItem { Header = label, IsCheckable = true, IsChecked = _settings.AudioDeviceIndex == i };
            var idx   = i;
            item.Click += (_, _) =>
            {
                _settings.AudioDeviceIndex = idx;
                _settings.Save();
            };
            _micMenu.Items.Add(item);
        }
    }

    private void UpdateThemeChecks()
    {
        _themeAuto!.IsChecked  = _settings.Theme == AppColorTheme.System;
        _themeLight!.IsChecked = _settings.Theme == AppColorTheme.Light;
        _themeDark!.IsChecked  = _settings.Theme == AppColorTheme.Dark;
    }

    private void ApplyTheme(AppColorTheme mode)
    {
        _settings.Theme = mode;
        _settings.Save();
        ThemeService.Apply(mode, _overlay);
    }

    // ── Hotkey ────────────────────────────────────────────────────────────────

    private void SetupHotkey()
    {
        _hotkey = new HotkeyService(() => _settings);
        _hotkey.HotkeyPressed      += OnToggle;
        _hotkey.RecordStartPressed += StartRecording;
        _hotkey.RecordStopPressed  += () => _ = StopRecordingAsync();
        _hotkey.Install();
    }

    private void OnToggle()
    {
        if (_recording) _ = StopRecordingAsync();
        else StartRecording();
    }

    // ── Recording flow ────────────────────────────────────────────────────────

    private void StartRecording()
    {
        if (_recording) return;
        if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey)) { OpenSettings(); return; }
        _targetWindow     = GetForegroundWindow();
        _recording        = true;
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
        Logger.Log($"Recording stopped after {duration.TotalSeconds:F1}s");
        var stream = await Task.Run(() => _audio!.Stop());
        if (duration < MinRecordingDuration)
        {
            Logger.Log($"Too short ({duration.TotalSeconds:F1}s), discarding.");
            _overlay!.Collapse();
            return;
        }
        Logger.Log($"Audio ready: {stream.Length} bytes, transcribing…");
        AudioArchive.Save(stream);
        _overlay!.ShowProcessing();
        try
        {
            var text = await _transcription!.TranscribeAsync(stream, prompt: null,
                temperature: _settings.TranscriptionTemperature,
                language:    _settings.LanguageCode);
            Logger.Log($"Transcription: \"{text}\"");
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (_targetWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(_targetWindow);
                    await Task.Delay(80);
                }
                await ClipboardService.PasteTextAsync(text, _settings.RestoreClipboard);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Transcription ERROR: {ex.GetType().Name}: {ex.Message}");
            new ErrorPopup(GetUserFriendlyError(ex)).ShowDialog();
        }
        finally
        {
            _overlay!.Collapse();
        }
    }

    // ── Windows ───────────────────────────────────────────────────────────────

    private void OpenDebug()    => new DebugWindow(_transcription!, _settings).Show();

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings);
        if (win.ShowDialog() == true)
        {
            _settings = win.Result;
            _settings.Save();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawEllipse(
                new SolidColorBrush(WpfColor.FromRgb(30, 120, 230)),
                null,
                new WpfPoint(8, 8), 7, 7);
        var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        using var bmp = new System.Drawing.Bitmap(ms);
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static string GetUserFriendlyError(Exception ex)
    {
        if (ex is HttpRequestException || ex is TaskCanceledException)
            return "No internet connection. Check your network and try again.";
        if (ex is ClientResultException apiEx)
            return apiEx.Status switch
            {
                401    => "Invalid API key — open Settings to update it.",
                429    => "OpenAI rate limit reached, try again later.",
                >= 500 => $"OpenAI service error (HTTP {apiEx.Status}).",
                _      => $"API error (HTTP {apiEx.Status})."
            };
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

