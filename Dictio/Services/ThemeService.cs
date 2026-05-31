using Dictio.Models;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Wpf.Ui.Appearance;
using WpfApp = System.Windows.Application;

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

    // Fires after WPF-UI has applied the new theme
    public static event Action? ThemeChanged;

    static ThemeService()
    {
        ApplicationThemeManager.Changed += (theme, _) =>
        {
            CurrentIsDark = theme == ApplicationTheme.Dark;
            ThemeChanged?.Invoke();
        };
    }

    public static void Apply(AppColorTheme theme)
    {
        _currentTheme = theme;

        // Remove previous system watcher before re-evaluating
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        if (theme == AppColorTheme.System)
        {
            // Apply current system dark/light setting and watch for changes
            ApplyFromSystemSetting();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        else
        {
            var wpfUiTheme = theme == AppColorTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(wpfUiTheme);
        }
    }

    /// <summary>
    /// Sets Mica backdrop and dark/light title bar via DWM.
    /// Not needed for FluentWindow — only for plain Window subclasses.
    /// </summary>
    public static void ApplyMicaToWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        int dark = CurrentIsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        int mica = DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
    }

    private static void ApplyFromSystemSetting()
    {
        var wpfUiTheme = IsSystemDark() ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(wpfUiTheme);
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        WpfApp.Current?.Dispatcher.BeginInvoke(ApplyFromSystemSetting);
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
}
