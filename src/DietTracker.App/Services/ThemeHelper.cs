using Microsoft.Win32;

namespace DietTracker.App.Services;

/// <summary>
/// Reads the Windows setting that decides whether applications run in dark or in light mode - the
/// same setting the XAML theme of the application follows, so the interface and the window title
/// bar always agree with each other and with the system.
/// </summary>
internal static class ThemeHelper
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// True when Windows runs its applications in dark mode. The value is read fresh on every call,
    /// so a change of the system setting is picked up immediately; when the setting cannot be read
    /// the light mode is assumed, which is the Windows default.
    /// </summary>
    public static bool IsDarkAppMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0;
            }
        }
        catch (Exception)
        {
            // Fall through to the Windows default.
        }

        return false;
    }
}