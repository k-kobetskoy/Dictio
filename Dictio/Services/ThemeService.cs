using Dictio.Models;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace Dictio.Services;

public static class ThemeService
{
    private static System.Windows.Window? _watchedWindow;

    public static void Apply(AppColorTheme theme, System.Windows.Window? watchWindow = null)
    {
        if (_watchedWindow != null)
        {
            SystemThemeWatcher.UnWatch(_watchedWindow);
            _watchedWindow = null;
        }

        var appTheme = theme switch
        {
            AppColorTheme.Light => ApplicationTheme.Light,
            AppColorTheme.Dark  => ApplicationTheme.Dark,
            _                   => IsSystemDark() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(appTheme);

        if (theme == AppColorTheme.System && watchWindow != null)
        {
            _watchedWindow = watchWindow;
            SystemThemeWatcher.Watch(watchWindow);
        }
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
