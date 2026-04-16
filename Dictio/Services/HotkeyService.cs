using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Input;
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

            if (vk is VK_LCONTROL or VK_RCONTROL) _ctrl = isDown;
            else if (vk is VK_LSHIFT or VK_RSHIFT) _shift = isDown;
            else
            {
                var s = _getSettings();
                int expected = KeyInterop.VirtualKeyFromKey(s.HotkeyKey);

                if (vk == expected && _ctrl == s.HotkeyCtrl && _shift == s.HotkeyShift)
                {
                    if (s.HotkeyMode == HotkeyMode.PushToTalk)
                    {
                        if (isDown && !_hotkeyDown)
                        {
                            _hotkeyDown = true;
                            Application.Current.Dispatcher.BeginInvoke(RecordStartPressed);
                        }
                        else if (isUp && _hotkeyDown)
                        {
                            _hotkeyDown = false;
                            Application.Current.Dispatcher.BeginInvoke(RecordStopPressed);
                        }
                    }
                    else
                    {
                        if (isDown && !_hotkeyDown)
                        {
                            _hotkeyDown = true;
                            Application.Current.Dispatcher.BeginInvoke(HotkeyPressed);
                        }
                        else if (isUp)
                        {
                            _hotkeyDown = false;
                        }
                    }
                    return (IntPtr)1; // consume — prevent Space (or other key) reaching active window
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