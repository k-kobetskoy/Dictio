using System.Runtime.InteropServices;
using System.Threading;

namespace Dictio.Services;

public static class ClipboardService
{
    private const int    INPUT_KEYBOARD  = 1;
    private const int    KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL      = 0x11;
    private const ushort VK_V            = 0x56;
    private const ushort VK_LSHIFT       = 0xA0;
    private const ushort VK_RSHIFT       = 0xA1;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    // Union must be as large as the biggest member (MOUSEINPUT) so that
    // Marshal.SizeOf<INPUT>() matches what Windows expects (40 bytes on 64-bit).
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi; // keeps union size correct
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int    dx;
        public int    dy;
        public uint   mouseData;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // Retries clipboard COM calls up to 3 times with 50 ms gaps.
    // Throws the last COMException if all attempts fail.
    private static void ClipboardRetry(Action action)
    {
        COMException? last = null;
        for (int i = 0; i < 3; i++)
        {
            try { action(); return; }
            catch (COMException ex) { last = ex; Thread.Sleep(50); }
        }
        throw last!;
    }

    private static string? TryGetClipboardSnapshot()
    {
        string? result = null;
        ClipboardRetry(() =>
            result = System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : null);
        return result;
    }

    private static void TrySetClipboardText(string text) =>
        ClipboardRetry(() => System.Windows.Clipboard.SetText(text));

    private static void TryClearClipboard() =>
        ClipboardRetry(() => System.Windows.Clipboard.Clear());

    // Pastes text into the focused window via clipboard + Ctrl+V.
    // B1: saves current clipboard text before overwriting.
    // B2: restores it 200 ms after SendInput (gives target app time to read).
    // B3: errors at every step are logged; paste aborts only if SetText itself fails.
    public static async Task PasteTextAsync(string text, bool restoreClipboard = true)
    {
        var hwnd = GetForegroundWindow();
        Logger.Log($"PasteText: foreground=0x{hwnd:X8}, textLen={text.Length}, restore={restoreClipboard}");

        // B1 — snapshot
        string? savedText = null;
        bool doRestore = restoreClipboard;
        if (doRestore)
        {
            try
            {
                savedText = TryGetClipboardSnapshot();
                var preview = savedText is null ? "empty/non-text"
                    : $"\"{savedText[..Math.Min(40, savedText.Length)]}\"";
                Logger.Log($"PasteText: snapshot={preview}");
            }
            catch (COMException ex)
            {
                // B3 — snapshot failed; proceed with paste but skip restore
                Logger.Log($"PasteText: snapshot failed ({ex.Message}), restore skipped");
                doRestore = false;
            }
        }

        // Place transcription text in clipboard
        try
        {
            TrySetClipboardText(text);
        }
        catch (COMException ex)
        {
            // B3 — cannot set clipboard, nothing to paste
            Logger.Log($"PasteText: SetText failed after retries ({ex.Message}), aborting paste");
            return;
        }

        // Send Ctrl+V (inject Shift-up first if Shift is physically held)
        bool shiftHeld = (GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 ||
                         (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;

        var inputs = new List<INPUT>();
        if (shiftHeld)
        {
            inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_LSHIFT, dwFlags = KEYEVENTF_KEYUP } } });
            inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_RSHIFT, dwFlags = KEYEVENTF_KEYUP } } });
        }
        inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_CONTROL } } });
        inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_V } } });
        inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_V,       dwFlags = KEYEVENTF_KEYUP } } });
        inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } });

        var arr  = inputs.ToArray();
        var sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        Logger.Log($"PasteText: SendInput sent {sent}/{arr.Length} events, shiftHeld={shiftHeld}");

        // B2 — restore after target app has had time to read clipboard
        if (doRestore)
        {
            await Task.Delay(200);
            try
            {
                if (savedText != null)
                    TrySetClipboardText(savedText);
                else
                    TryClearClipboard();
                Logger.Log("PasteText: clipboard restored");
            }
            catch (COMException ex)
            {
                // B3 — restore failed; transcription text remains in clipboard
                Logger.Log($"PasteText: restore failed ({ex.Message}), transcription left in clipboard");
            }
        }
    }
}
