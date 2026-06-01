using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;
using Dictio.Models;

namespace Dictio.Services;

public class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_KEYUP       = 0x0101;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_SYSKEYUP    = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT   = 0xA0;
    private const int VK_RSHIFT   = 0xA1;
    private const int VK_LALT     = 0xA4;
    private const int VK_RALT     = 0xA5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private readonly Func<AppSettings> _getSettings;

    private bool _hotkeyDown;
    private bool _triggerEngaged;   // trigger key was consumed on key-down; must consume matching key-up
    private int  _engagedVk;        // VK code that was consumed (matches the key-up we must swallow)
    private bool _ctrl, _shift, _alt;
    private volatile bool _paused;

    public event Action? HotkeyPressed;
    public event Action? RecordStartPressed;
    public event Action? RecordStopPressed;

    public HotkeyService(Func<AppSettings> getSettings)
    {
        _getSettings = getSettings;
    }

    public void Pause()
    {
        _paused         = true;
        _hotkeyDown     = false;
        _triggerEngaged = false;
        _ctrl = _shift = _alt = false;
    }

    public void Resume() => _paused = false;

    public void Install()
    {
        _proc = HookCallback;
        using var proc   = Process.GetCurrentProcess();
        using var module = proc.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName!), 0);
    }

    private bool ModifiersMatch()
    {
        int mods = _getSettings().HotkeyModifiers;
        bool ctrlOk  = ((mods & 1) != 0) == _ctrl;
        bool shiftOk = ((mods & 2) != 0) == _shift;
        bool altOk   = ((mods & 4) != 0) == _alt;
        return ctrlOk && shiftOk && altOk;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if (_paused)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((kbd.flags & LLKHF_INJECTED) != 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            int  vk     = (int)kbd.vkCode;
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp   = wParam == WM_KEYUP   || wParam == WM_SYSKEYUP;

            // ── Modifier tracking + PushToTalk release ────────────────────────
            if (vk is VK_LCONTROL or VK_RCONTROL)
            {
                _ctrl = isDown;
                if (isUp && _hotkeyDown && _getSettings().HotkeyMode == HotkeyMode.PushToTalk
                    && (_getSettings().HotkeyModifiers & 1) != 0)
                {
                    _hotkeyDown = false;
                    Application.Current.Dispatcher.BeginInvoke(RecordStopPressed);
                }
            }
            else if (vk is VK_LSHIFT or VK_RSHIFT)
            {
                _shift = isDown;
                if (isUp && _hotkeyDown && _getSettings().HotkeyMode == HotkeyMode.PushToTalk
                    && (_getSettings().HotkeyModifiers & 2) != 0)
                {
                    _hotkeyDown = false;
                    Application.Current.Dispatcher.BeginInvoke(RecordStopPressed);
                }
            }
            else if (vk is VK_LALT or VK_RALT)
            {
                _alt = isDown;
                if (isUp && _hotkeyDown && _getSettings().HotkeyMode == HotkeyMode.PushToTalk
                    && (_getSettings().HotkeyModifiers & 4) != 0)
                {
                    _hotkeyDown = false;
                    Application.Current.Dispatcher.BeginInvoke(RecordStopPressed);
                }
            }
            // ── Trigger key ───────────────────────────────────────────────────
            else
            {
                int triggerVk = _getSettings().HotkeyVirtualKey;

                // If this is the engaged key being released, always consume it
                if (_triggerEngaged && vk == _engagedVk)
                {
                    if (isUp)
                    {
                        _triggerEngaged = false;
                        if (_hotkeyDown)
                        {
                            _hotkeyDown = false;
                            if (_getSettings().HotkeyMode == HotkeyMode.PushToTalk)
                                Application.Current.Dispatcher.BeginInvoke(RecordStopPressed);
                        }
                    }
                    return (IntPtr)1; // consume both key-down repeat and key-up
                }

                if (vk == triggerVk && isDown && ModifiersMatch() && !_hotkeyDown)
                {
                    _hotkeyDown     = true;
                    _triggerEngaged = true;
                    _engagedVk      = vk;
                    var s = _getSettings();
                    Application.Current.Dispatcher.BeginInvoke(
                        s.HotkeyMode == HotkeyMode.PushToTalk ? RecordStartPressed : HotkeyPressed);
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    // ── Formatting helpers (used by Settings and Welcome windows) ─────────────

    public static string FormatHotkey(int mods, int vk)
    {
        var parts = new List<string>();
        if ((mods & 1) != 0) parts.Add("Ctrl");
        if ((mods & 2) != 0) parts.Add("Shift");
        if ((mods & 4) != 0) parts.Add("Alt");
        parts.Add(VkToFriendlyName(vk));
        return string.Join(" + ", parts);
    }

    public static string VkToFriendlyName(int vk) => vk switch
    {
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0xBB => "=",
        0xBD => "-",
        0xDB => "[",
        0xDD => "]",
        0xBA => ";",
        0xC0 => "`",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        _ => $"0x{vk:X2}"
    };
}
