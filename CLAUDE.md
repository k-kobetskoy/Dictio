# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Dictio** — a WPF tray application targeting .NET 9 (`net9.0-windows`), namespace `Dictio`. Records audio via hotkey, transcribes via OpenAI gpt-4o-transcribe, and pastes the result into the focused window.

## Build & Run

```bash
# Build
dotnet build Dictio/Dictio.csproj

# Run
dotnet run --project Dictio/Dictio.csproj

# Publish (self-contained Windows exe)
dotnet publish Dictio/Dictio.csproj -c Release -r win-x64 --self-contained
```

## Architecture

There is no main window. The app runs as a tray icon with `ShutdownMode = OnExplicitShutdown`.

```
App.xaml.cs              — startup, tray icon, hotkey wiring, record/stop/paste flow
                           OnStartup shows WelcomeWindow on first launch
                           Tray: left-click → Settings, right-click → Settings / Debug / Exit
Models/
  AppSettings.cs         — JSON at %AppData%\Dictio\settings.json
                           API key stored DPAPI-encrypted (ProtectedData)
                           Fields: OpenAiApiKey, HotkeyMode, HotkeyModifiers, HotkeyVirtualKey,
                                   AudioDeviceIndex, TranscriptionLanguage, SelectedModel,
                                   Theme, OverlayPosition, OverlayOpacity, EqScrollIntervalMs,
                                   EnableHistory, HistoryMaxRecords, SaveTranscriptionText,
                                   SkipSilentRecordings, ForceWavDebug, FirstLaunchDone
Services/
  AudioRecorderService.cs  — NAudio WaveInEvent → MemoryStream WAV; LevelChanged ~10 Hz
  TranscriptionService.cs  — gpt-4o-transcribe; logprobs enabled; rejects transcriptions
                             with avg logprob < -2.5 (silence/noise guard); returns null
  HotkeyService.cs         — WH_KEYBOARD_LL hook; Toggle and PushToTalk modes;
                             configurable modifiers (Ctrl/Shift/Alt) + virtual key;
                             filters LLKHF_INJECTED events; swallows the trigger key
  ClipboardService.cs      — SetClipboard + SendInput Ctrl+V (INPUT struct correctly sized)
  AudioArchive.cs          — saves each recording to %AppData%\Dictio\audio\;
                             rolling buffer, keeps last N WAV files (configurable)
  AudioCompressor.cs       — Ogg/Opus 24 kbps via Concentus; 10× size reduction
  ThemeService.cs          — Wpf.Ui theme management; ThemeChanged event
  Logger.cs                — append-only log at %AppData%\Dictio\debug.log, 2 MB rotation
Views/
  SettingsWindow.xaml/.cs  — FluentWindow; sidebar nav: Main / Models / API Keys / History / Overlay
                             Main: microphone, hotkey capture, hotkey mode, language
                             Models: card-based model picker with accuracy/speed bars
                             API Keys: OpenAI key with show/hide
                             History: recording list with transcript preview
                             Overlay: position, opacity, visibility
  OverlayWindow.xaml/.cs   — WS_EX_NOACTIVATE floating indicator; states: Collapsed / Idle /
                             RecordingSilent / Processing; 4-slot equalizer FIFO; dot animation
  WelcomeWindow.xaml/.cs   — 2-step onboarding wizard (hotkey+mic+lang → model); FluentWindow
  ErrorPopup.xaml/.cs      — user-friendly API error messages (401/429/5xx/network)
Themes/
  Tokens.xaml              — design tokens (colours, corner radii, font sizes)
  ContextMenu.xaml         — styled WPF ContextMenu for tray
```

## Key behaviours

- **Hotkey**: configurable combo (default Ctrl+Space); stored as `HotkeyModifiers` bitmask + `HotkeyVirtualKey`; Toggle = press to start/stop, PushToTalk = hold to record
- **Min recording duration**: 1 s — shorter recordings are silently discarded
- **Logprob rejection threshold**: −2.5 average — discards silent/noisy audio before paste
- **Audio compression**: WAV → Ogg/Opus 24 kbps before API call (~10× size reduction); fallback to WAV on encode error
- **Target window**: `GetForegroundWindow()` saved at recording start; `SetForegroundWindow` + 80 ms delay before paste
- **Audio archive**: `%AppData%\Dictio\audio\recording_YYYYMMDD_HHmmss.wav`, max N files (AppSettings.HistoryMaxRecords)

## Patterns

- All views follow `*.xaml` + `*.xaml.cs` partial-class pattern (code-behind, no MVVM)
- `TranscriptionService.TranscribeAsync` returns `string?` — null means rejected (already logged)
- `AppSettings.Load()` / `.Save()` handle missing fields gracefully (defaults apply)
- `SettingsWindow.SettingChanged` event fires on every change — `App.xaml.cs` applies immediately without restart
- `#pragma warning disable OPENAI001` required when using `AudioTranscriptionOptions.Includes` (experimental API)
- Icons: use `Wpf.Ui` `SymbolIcon` (inherits Foreground, adapts to theme) — do not use PNG or bitmap icons
