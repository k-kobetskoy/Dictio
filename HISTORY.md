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

New issues discovered during MVP:
- [ ] Hotkey system only supports Ctrl/Shift; Alt removed due to Windows menu-bar activation side effect — needs a better hotkey design
- [ ] Ctrl+Shift can trigger keyboard layout switch — default is Ctrl+Space, but user should be warned
- [ ] No visual feedback during transcription (spinner / status in overlay)
- [ ] No way to retry/undo last paste
- [ ] Overlay has no animation
- [ ] Hook-based workaround for Space key suppression is fragile — should be redesigned
- [ ] Shift-up injection before paste is a workaround — proper fix: event-driven modifier-release tracking in HotkeyService
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
