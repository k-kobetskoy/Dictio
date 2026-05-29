# Dictio — Project History

Context file for AI sessions. Updated after each significant change.

---

## Project goal

Dictio is a WPF tray app (.NET 9) that records audio via hotkey, transcribes it via OpenAI Whisper/GPT-4o, and pastes the result into the focused window.

---

## Session log

### 2026-04-17 — Initial scaffolding + package migration + bug fixes

**What was done:**

1. **Replaced `H.NotifyIcon.Wpf` with `System.Windows.Forms.NotifyIcon`**
   - `H.NotifyIcon.Wpf 2.4.1` triggered NU1701 (no .NET 9 target). No newer WPF-native version exists.
   - Switched to `UseWindowsForms=true` + `System.Windows.Forms.NotifyIcon`.
   - Required disambiguating `Clipboard`, `MessageBox`, `Application` across several files.

2. **Fixed transcription not working — disposed MemoryStream bug**
   - `WaveFileWriter.Dispose()` closed the underlying `MemoryStream`.
   - Subsequent `stream.Position = 0` threw `ObjectDisposedException`.
   - Fix: call `_buffer.ToArray()` (works on closed MemoryStream) after `_writer.Dispose()`, return `new MemoryStream(data)`.

3. **Fixed overlay appearing intermittently after failed transcription**
   - `_audio.Stop()` called `ManualResetEvent.Wait(3s)` on the UI thread, freezing WPF for up to 3 seconds.
   - Fix: wrapped in `await Task.Run(() => _audio!.Stop())`.

4. **Fixed injected keystrokes triggering hotkey hook**
   - `ClipboardService.PasteText` sends Ctrl+V via `SendInput`.
   - WH_KEYBOARD_LL hook intercepted these injected events, corrupting `_ctrl`/`_alt` state.
   - Fix: skip events where `LLKHF_INJECTED` flag is set in `kbd.flags`.

5. **Added audio device selection**
   - `AppSettings.AudioDeviceIndex` (int, default 0).
   - `AudioRecorderService.Start(int deviceIndex)` passes it to `WaveInEvent.DeviceNumber`.
   - Settings window lists all `WaveIn` devices; first device labeled "(default)".

6. **Added file logger**
   - `Logger.Log(string)` appends to `%AppData%\Dictio\debug.log`.
   - Rotates at 2 MB (copies to `.old`, starts fresh).
   - Logs: app start, settings summary, recording start/stop, audio byte count, transcription result or error.

---

### 2026-04-17 — Paste fix, hotkey cleanup, workarounds noted

1. **Target window tracking** — `GetForegroundWindow()` called in `StartRecording` before overlay appears, saved as `_targetWindow`. Before paste, `SetForegroundWindow(_targetWindow)` + 80ms delay ensures Ctrl+V goes to the right app.

2. **Default hotkey changed** to `Ctrl+Space` — Ctrl+Shift and Alt+Shift are Windows built-in language switchers, bad for hotkey use.

3. **Shift-release injection in PasteText** — ⚠️ WORKAROUND. If Shift is physically held at paste time, we inject Shift-up before Ctrl+V. Root cause: hotkey modifiers may still be physically pressed during transcription. Proper fix would be event-driven (hook-based release notification).

4. **Space consumption in hook** — ⚠️ WORKAROUND. `return (IntPtr)1` suppresses Space from reaching the active window during hotkey. Works but the general approach of blocking individual keys is fragile.

5. **Minimum recording duration (1s)** — acceptable threshold, easily configurable via `MinRecordingDuration`.

6. **Logging in ClipboardService** — `PasteText` now logs foreground window handle, text length, and SendInput return value for diagnostics.

### 2026-04-17 — Root cause of paste failure found and fixed ✅ MVP working

**The real bug:** `SendInput` was returning 0 for all events — not a focus or modifier issue, but a struct size mismatch.

- `INPUT` union contained only `KEYBDINPUT` → `Marshal.SizeOf<INPUT>()` = 32 bytes on 64-bit.
- Windows expects 40 bytes (union must be sized to `MOUSEINPUT`, which has `ULONG_PTR dwExtraInfo` = 8 bytes, bringing it to 32, + 4-byte type + 4-byte padding = 40).
- `SendInput` validates `cbSize` and returns 0 if it doesn't match. No events were ever sent.
- Fix: added `MOUSEINPUT` as a dummy `[FieldOffset(0)]` member to `INPUTUNION` — union automatically sizes to the larger member, making `Marshal.SizeOf<INPUT>()` = 40 on 64-bit, 28 on 32-bit. Both correct.

**Status after fix:** MVP fully working. Transcription + paste confirmed working end-to-end.

---

## Current state (2026-04-17)

**Working:**
- Tray icon (System.Windows.Forms.NotifyIcon)
- WH_KEYBOARD_LL hotkey hook (default: Ctrl+Space, configurable)
- Toggle and Push-to-Talk modes
- Audio recording via NAudio (configurable device)
- Transcription via OpenAI (whisper-1 or gpt-4o-transcribe)
- Paste into focused window via SendInput Ctrl+V
- Overlay indicator (red circle, WS_EX_NOACTIVATE)
- Settings UI (API key, model, mic device, hotkey)
- File logger at `%AppData%\Dictio\debug.log` with 2 MB rotation

**Known workarounds (want to revisit):**
- `return (IntPtr)1` in hook to swallow Space key reaching active window — fragile if hotkey key changes
- Shift-up injection before Ctrl+V if Shift is physically held — better than polling, but still a workaround; proper fix is hook-based modifier-release notification
- `SetForegroundWindow` + 80ms delay before paste — works reliably but the delay is arbitrary

**Known limitations / future improvements:**

From original plan (gleaming-stirring-tulip.md):
- [ ] Deepgram API support
- [ ] Azure Speech / ElevenLabs support
- [ ] Language selection for transcription
- [ ] Transcription history
- [ ] Windows autostart (registry / Startup folder)
- [ ] Encrypt API key via DPAPI (`ProtectedData.Protect`) — currently stored plain text in settings.json

OpenAI gpt-4o-transcribe — лимиты (для будущей обработки длинных файлов):
- Максимальный размер файла: **25 MB**
- Максимальная длительность: **1500 секунд (25 минут)**
- Для аудио > 30 секунд рекомендуется `chunking_strategy: "auto"` (VAD на стороне сервера)
- [ ] Добавить проверку: если файл > 25 MB или запись > 25 мин — нарезать локально на чанки
- [ ] Рассмотреть передачу `chunking_strategy: "auto"` для улучшения точности транскрибации

New issues discovered during MVP:
- [ ] Hotkey system only supports Ctrl/Shift; Alt removed due to Windows menu-bar activation side effect — needs a better hotkey design
- [ ] Ctrl+Shift can trigger keyboard layout switch — default is Ctrl+Space, but user should be warned
- [ ] No visual feedback during transcription (spinner / status in overlay)
- [ ] No way to retry/undo last paste
- [ ] Overlay has no animation
- [ ] Hook-based workaround for Space key suppression is fragile — should be redesigned
- [ ] Shift-up injection before paste is a workaround — proper fix: event-driven modifier-release tracking in HotkeyService
- [ ] **PushToTalk stop bug**: если отпустить Ctrl раньше Space — запись не останавливается. В HotkeyService стоп-триггер требует оба модификатора, но при раздельном отпускании состояние `_ctrl` сбрасывается раньше, чем приходит key-up Space. Нужно отслеживать "все клавиши хоткея были зажаты" и триггерить стоп при отпускании любой из них.
- [ ] **Cold start lag**: первая запись после запуска приложения стартует с задержкой ~1 сек (индикатор появляется позже). Последующие записи моментальные. Вероятно, NAudio инициализирует WaveIn device при первом вызове `Start()`.
- [ ] **Overlay recording animation**: анимировать индикатор записи в зависимости от громкости — пульсирующий кружок с амплитудой, пропорциональной уровню входящего сигнала с микрофона
- [ ] **Overlay modes (user choice in Settings):**
  - *Icon mode* (current) — small animated indicator while recording, disappears after paste
  - *Text strip mode* — overlay shows a live text bar where transcribed words appear as the user speaks, using OpenAI Realtime API (WebSocket, `gpt-4o-realtime-preview`). After recording stops, the final text is pasted as usual. Gives real-time visual feedback without the complexity of incrementally updating the target app's text field.
- [ ] **UI modernization** — Settings window and tray context menu look like Windows 98. Options: WPF custom styles + modern color scheme, or adopt a UI library (e.g. `Wpf.Ui` / ModernWpf) for fluent/Windows 11 look. Context menu should be replaced with a custom WPF popup instead of WinForms ContextMenuStrip.

---

## Architecture snapshot

```
App.xaml.cs          — startup, tray, hotkey wiring, record/stop/paste flow
                       fields: _targetWindow (saved before recording for paste focus restore)
Services/
  AudioRecorderService.cs   — NAudio WaveInEvent → MemoryStream WAV (device index param)
  TranscriptionService.cs   — OpenAI SDK audio transcription
  HotkeyService.cs          — WH_KEYBOARD_LL hook; filters LLKHF_INJECTED; swallows hotkey key
  ClipboardService.cs       — SetClipboard + SendInput Ctrl+V (INPUT struct correctly sized)
  Logger.cs                 — append-only file log, 2 MB rotation
Models/
  AppSettings.cs            — JSON in %AppData%\Dictio\settings.json
                              fields: OpenAiApiKey, ModelId, AudioDeviceIndex,
                                      HotkeyMode, HotkeyKey, HotkeyCtrl, HotkeyShift
Views/
  SettingsWindow.xaml/.cs   — API key, model, mic device, hotkey (Ctrl/Shift + key)
  OverlayWindow.xaml/.cs    — WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW floating red circle
```

---

### 2026-05-30 — A2: Scrolling equalizer in OverlayWindow

**A2 — Dynamic 4-slot FIFO equalizer**

- `OverlayWindow.xaml`: removed `StateRecordingVoice` (S3). In `StateRecordingSilent` (S2) replaced 5 static silent dots with 4 named `EqBar0..EqBar3` at x=24,32,40,48 (4 px wide, 8 px step).
- `OverlayWindow.xaml.cs`:
  - Removed `RecordingVoice` from `OverlayState` enum — S2 and S3 are now a single state.
  - Added `Queue<float> _rmsHistory` (FIFO, 4 slots) and `WpfRect[] _eqBars` fields.
  - `ResetEqHistory()` fills the queue with 4 zeros; called in the constructor and every time `SetState(RecordingSilent)` is entered — bars start flat on each new recording.
  - `SetLevel(float rms)`: dequeues the oldest value, enqueues the new RMS, then calls `UpdateEqBar` for each slot.
  - `UpdateEqBar`: if `rms < 0.02` → height=4, opacity=0.43 (silent dot); otherwise → `height = min(18, 4 + rms*14)`, opacity=0.72, `Canvas.Top=(28−h)/2` (centered bar).
  - `SetState()` no longer references `RecordingVoice`.
  - `using WpfRect = System.Windows.Shapes.Rectangle` alias resolves ambiguity with `System.Drawing.Rectangle` (project uses `UseWindowsForms=true`).

**Files changed:**
- `Views/OverlayWindow.xaml`
- `Views/OverlayWindow.xaml.cs`

Build: 0 errors, 0 warnings ✅

---

### 2026-05-30 — A1: RMS level event + A4: smooth processing spinner

**A1 — Volume level passthrough from `AudioRecorderService`**

- `AudioRecorderService`: added `LevelChanged: Action<float>?` event. Fires on every `DataAvailable` callback (~10 Hz at default `BufferMilliseconds=100`). Computes RMS of 16-bit signed PCM, normalised to [0, 1] via `ComputeRms`.
- `App.xaml.cs`: one-line subscription after overlay creation: `_audio.LevelChanged += level => _overlay.SetLevel(level)`.
- `OverlayWindow.xaml.cs`: `SetLevel(float rms)` — if state is `RecordingSilent` or `RecordingVoice`, switches between the two at threshold `0.02` (≈ −34 dBFS). Directly toggles only the two relevant `Visibility` values instead of going through the full `SetState` path — avoids unnecessary dot-animation resets at 10 Hz.

**A4 — Smooth record → transcribe transition**

- `StartDotsAnimation()`: first plays a 200 ms `DoubleAnimation(0→1, CubicEase.EaseOut)` on `StateProcessing.Opacity` (the whole spinner container). Dot wave animations get `BeginTime = 200 ms + their own offset` so they start pulsing only after the fade-in completes — the spinner "materialises" as a whole, then starts waving.
- `SetState(Processing)`: sets `StateProcessing.Opacity = 0` before `Visibility = Visible` to prevent a one-frame flash at full opacity.
- `StopDotsAnimation()`: added `StateProcessing.BeginAnimation(OpacityProperty, null)` to clear the fade animation and restore opacity when leaving the Processing state.

**Files changed:**
- `Services/AudioRecorderService.cs` — `LevelChanged` event, `ComputeRms` static method
- `App.xaml.cs` — one-line subscription
- `Views/OverlayWindow.xaml.cs` — `VoiceThreshold` const, `SetLevel()`, `SetState` opacity pre-zero, updated `StartDotsAnimation`/`StopDotsAnimation`

Build: 0 errors, 0 warnings ✅

---

### 2026-05-30 — Research sprint 0: R3 (VAD) + R5 (Clipboard)

Planning session only — no code changes.

**R3 — VAD decision:**

- Evaluated: RMS threshold, WebRtcVad.NET, WebRtcVadSharp, Silero ONNX, Picovoice Cobra.
- **A3 (overlay voice indicator):** RMS threshold in `AudioRecorderService.DataAvailable` — already wired via `LevelChanged`. No additional library needed.
- **WebRtcVad.NET** (`dotnet add package WebRtcVad.NET`) kept as fallback if RMS produces too many false positives on specific microphones.
- **Silero VAD excluded** — adds ~20 MB of OnnxRuntime dependencies, not justified.
- **Epik C (silence trimming, C1–C5) cancelled** — OpenAI Whisper runs server-side VAD; pre-trimming silence is premature optimisation. File size is addressed by compression (R4/epik D). C1–C5 marked `cancelled` in task_plan.md. D1 dependency on C3 removed.

**R5 — Clipboard decision:**

- Windows clipboard requires STA thread — WPF main thread is already STA, no extra threading needed.
- **v1 restore scope: text only.** Save `Clipboard.GetText()` before `SetText(transcription)`; restore after `SendInput Ctrl+V` + 200 ms delay. Delay is mandatory to avoid race where target app reads restored (old) text instead of transcription.
- Non-text content (bitmap, file drop, GDI handles, delayed rendering) cannot be reliably restored — fallback is `Clipboard.Clear()`.
- All `Clipboard.*` calls need retry wrapper (3× × 50 ms) for `COMException` — clipboard is a shared OS resource.
- Full IDataObject multi-format snapshot deferred to B2+ if users report specific format loss.
- Alternatives evaluated (SendInput unicode chars, WM_CHAR, UIAutomation) — all inferior to clipboard for dictation use case. Clipboard is the industry standard (Wispr, Talon, Windows Speech Recognition all use it).

**Plan state after this session:**

- Spint 0: all done (R1 ✅ R2 ✅ R3 ✅ R5 ✅).
- Next: Sprint 1 — A1–A4 (overlay equaliser/spinner) + B1–B3 (clipboard save/restore).

---

### 2026-05-24 — chunking_strategy VAD controls in Debug window

Added `prefix_padding_ms` and `silence_duration_ms` fields to `DebugWindow` for experimenting with VAD parameters during re-transcription.

**Problem:** OpenAI SDK 2.10.0 doesn't expose `chunking_strategy` in `AudioTranscriptionOptions`.

**Solution:** When either VAD field is non-empty, bypass the SDK and send a raw multipart HTTP POST to `/v1/audio/transcriptions` with `chunking_strategy={"type":"server_vad",...}`. Normal (no VAD overrides) transcription still uses the SDK path unchanged.

**Files changed:**
- `Services/TranscriptionService.cs` — added `TranscribeRawAsync`, refactored `LogAndFilterLogprobs` to accept `string json`, static `HttpClient`
- `Views/DebugWindow.xaml` — new VAD params row (PrefixPaddingBox, SilenceDurationBox)
- `Views/DebugWindow.xaml.cs` — reads VAD fields, passes to `TranscribeAsync`
