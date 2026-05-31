using Dictio.Models;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Media;
using WpfApp = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace Dictio.Services;

public static class ThemeService
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE     = 38;
    private const int DWMSBT_MAINWINDOW             = 2; // Mica

    private static AppColorTheme _currentTheme = AppColorTheme.System;

    public static bool CurrentIsDark { get; private set; }
    public static AppColorTheme CurrentTheme => _currentTheme;

    // Fires on the UI thread after resources have been updated
    public static event Action? ThemeChanged;

    public static void Apply(AppColorTheme theme)
    {
        _currentTheme = theme;

        bool dark = theme switch
        {
            AppColorTheme.Dark  => true,
            AppColorTheme.Light => false,
            _                   => IsSystemDark()
        };

        CurrentIsDark = dark;
        UpdateAppResources(dark);
        ThemeChanged?.Invoke();

        // Re-register system watcher only in Auto mode
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (theme == AppColorTheme.System)
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Call after the window HWND is available (e.g. in the Window.Loaded handler).
    /// Sets Mica backdrop + dark/light title bar to match the current theme.
    /// </summary>
    public static void ApplyMicaToWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        int dark = CurrentIsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        int mica = DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        WpfApp.Current?.Dispatcher.BeginInvoke(() => Apply(_currentTheme));
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static void UpdateAppResources(bool dark)
    {
        var res = WpfApp.Current.Resources;

        // ── Tray context menu (ContextMenu.xaml switches to DynamicResource) ───
        res["Menu.Background"]       = B(dark ? 0x1e2330 : 0xFFFFFF);
        res["Menu.Border"]           = B(dark ? 0x2d3748 : 0xE5E7EB);
        res["Menu.Foreground"]       = B(dark ? 0xe2e8f0 : 0x111827);
        res["Menu.Foreground.Muted"] = B(dark ? 0x94a3b8 : 0x9CA3AF);
        res["Menu.Item.Hover"]       = B(dark ? 0x2d3748 : 0xF3F4F6);
        res["Menu.Item.Checked"]     = B(0x3b82f6);

        // ── Settings window palette ────────────────────────────────────────────
        res["Settings.Sidebar.Bg"]      = B(dark ? 0x1A1A1A : 0xF3F4F6);
        res["Settings.Content.Bg"]      = B(dark ? 0x202020 : 0xFFFFFF);
        res["Settings.Footer.Bg"]       = B(dark ? 0x181818 : 0xFFFFFF);
        res["Settings.Line"]            = B(dark ? 0x2D2D2D : 0xE5E7EB);
        res["Settings.Fg.Primary"]      = B(dark ? 0xF0F0F0 : 0x111827);
        res["Settings.Fg.Secondary"]    = B(dark ? 0x9E9E9E : 0x6B7280);
        res["Settings.Fg.Hint"]         = B(dark ? 0x6B6B6B : 0x9CA3AF);
        res["Settings.Nav.Selected.Bg"] = B(dark ? 0x1E3A5F : 0xEFF6FF);
        res["Settings.Nav.Hover.Bg"]    = B(dark ? 0x2A2A2A : 0xE8F0FE);
        res["Settings.Card.Bg"]         = B(dark ? 0x2A2A2A : 0xFFFFFF);
        res["Settings.Card.Border"]     = B(dark ? 0x383838 : 0xE5E7EB);
        res["Settings.InfoBox.Bg"]      = B(dark ? 0x1A2C3F : 0xF0F9FF);
        res["Settings.InfoBox.Border"]  = B(dark ? 0x1E4A6E : 0xBAE6FD);
        res["Settings.Input.Bg"]        = B(dark ? 0x2A2A2A : 0xFFFFFF);
        res["Settings.Input.Border"]    = B(dark ? 0x404040 : 0xD1D5DB);
        res["Settings.Scrollbar.Thumb"] = B(dark ? 0x555555 : 0xC4C8D0);
        res["Toggle.Track.Off"]         = B(dark ? 0x4A4A4A : 0xD1D5DB);
        res["Settings.Kbd.Bg"]          = B(dark ? 0x333333 : 0xF3F4F6);
        res["Settings.Kbd.Fg"]          = B(dark ? 0xD1D5DB : 0x374151);
    }

    private static SolidColorBrush B(int rgb) =>
        new(WpfColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}
