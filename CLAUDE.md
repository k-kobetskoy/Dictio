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
                           Fields: OpenAiApiKey, HotkeyMode, AudioDeviceIndex,
                                   TranscriptionLanguage, CustomTranscriptionPrompt,
                                   FirstLaunchDone
                           EffectivePrompt — returns custom prompt or per-language default
Services/
  AudioRecorderService.cs  — NAudio WaveInEvent → MemoryStream WAV (configurable device)
  TranscriptionService.cs  — gpt-4o-transcribe; logprobs enabled; rejects transcriptions
                             with avg logprob < -2.5 (silence/noise guard); returns null
  HotkeyService.cs         — WH_KEYBOARD_LL hook; Toggle and PushToTalk modes;
                             filters LLKHF_INJECTED events; swallows hotkey key (Space)
  ClipboardService.cs      — SetClipboard + SendInput Ctrl+V (INPUT struct correctly sized)
  AudioArchive.cs          — saves each recording to %AppData%\Dictio\audio\;
                             rolling buffer, keeps last 10 WAV files
  Logger.cs                — append-only log at %AppData%\Dictio\debug.log, 2 MB rotation
Views/
  SettingsWindow.xaml/.cs  — API key, microphone, hotkey mode, transcription language,
                             custom prompt override
  OverlayWindow.xaml/.cs   — WS_EX_NOACTIVATE floating indicator:
                             recording = red circle, transcribing = spinning arc
  WelcomeWindow.xaml/.cs   — first-launch onboarding, sets FirstLaunchDone = true
  ErrorPopup.xaml/.cs      — user-friendly API error messages (401/429/5xx/network)
  DebugWindow.xaml/.cs     — lists saved recordings, supports re-transcription with
                             current settings
```

## Key behaviours

- **Hotkey**: Ctrl+Space (hardcoded); Toggle = press to start/stop, PushToTalk = hold to record
- **Min recording duration**: 1 s — shorter recordings are silently discarded
- **Logprob rejection threshold**: −2.5 average — discards silent/noisy audio before paste
- **Target window**: `GetForegroundWindow()` saved at recording start; `SetForegroundWindow` + 80 ms delay before paste
- **Audio archive**: `%AppData%\Dictio\audio\recording_YYYYMMDD_HHmmss.wav`, max 10 files

## Patterns

- All views follow `*.xaml` + `*.xaml.cs` partial-class pattern (code-behind, no MVVM)
- `TranscriptionService.TranscribeAsync` returns `string?` — null means rejected (already logged)
- `AppSettings.Load()` / `.Save()` handle missing fields gracefully (defaults apply)
- `#pragma warning disable OPENAI001` required when using `AudioTranscriptionOptions.Includes` (experimental API)
