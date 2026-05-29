using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;
using Dictio.Models;

namespace Dictio.Services;

public class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_SPACE = 0x20;

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
    private bool _spaceEngaged; // Space was consumed on key-down; must consume the matching key-up too
    private bool _ctrl, _shift;

    public event Action? HotkeyPressed;
    public event Action? RecordStartPressed;
    public event Action? RecordStopPressed;

    public HotkeyService(Func<AppSettings> getSettings)
    {
        _getSettings = getSettings;
    }

    public void Install()
    {
        _proc = HookCallback;
        using var proc = Process.GetCurrentProcess();
        using var module = proc.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName!), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((kbd.flags & LLKHF_INJECTED) != 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            int vk = (int)kbd.vkCode;
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            if (vk is VK_LCONTROL or VK_RCONTROL)
            {
                _ctrl = isDown;
                // PushToTalk: Ctrl released before Space — still stop recording
                if (isUp && _hotkeyDown && _getSettings().HotkeyMode == HotkeyMode.PushToTalk)
                {
                    _hotkeyDown = false;
                    Application.Current.Dispatcher.BeginInvoke(RecordStopPressed);
                }
            }
            else if (vk is VK_LSHIFT or VK_RSHIFT) _shift = isDown;
            else if (vk == VK_SPACE)
            {
                if (isDown && _ctrl && !_hotkeyDown)
                {
                    _hotkeyDown = true;
                    _spaceEngaged = true;
                    var s = _getSettings();
                    Application.Current.Dispatcher.BeginInvoke(
                        s.HotkeyMode == HotkeyMode.PushToTalk ? RecordStartPressed : HotkeyPressed);
                    return (IntPtr)1;
                }
                if (_spaceEngaged)
                {
                    if (isUp)
                    {
                        _spaceEngaged = false;
                        if (_hotkeyDown) // not already stopped by Ctrl-up
                        {
                            _hotkeyDown = false;
                            if (_getSettings().HotkeyMode == HotkeyMode.PushToTalk)
                                Application.Current.Dispatcher.BeginInvoke(RecordStopPressed);
                        }
                    }
                    return (IntPtr)1; // consume Space key-up even if Ctrl was released first
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
}