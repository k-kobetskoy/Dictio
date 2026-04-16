using System.Runtime.InteropServices;

namespace Dictio.Services;

public static class ClipboardService
{
    private const int INPUT_KEYBOARD = 1;
    private const int KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

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
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static void PasteText(string text)
    {
        var hwnd = GetForegroundWindow();
        Logger.Log($"PasteText: foreground=0x{hwnd:X8}, text length={text.Length}");
        System.Windows.Clipboard.SetText(text);

        // If Shift is physically held (e.g. from hotkey), release it first —
        // otherwise target window receives Ctrl+Shift+V instead of Ctrl+V.
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
        inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } } });
        inputs.Add(new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } });

        var arr = inputs.ToArray();
        var sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        Logger.Log($"PasteText: SendInput sent {sent}/{arr.Length} events, shiftHeld={shiftHeld}");
    }
}
