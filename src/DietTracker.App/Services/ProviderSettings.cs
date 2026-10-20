using System.Text.Json;
using System.Text.Json.Serialization;
using DietTracker.Infrastructure.Authentication;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace DietTracker.App.Services;

/// <summary>
/// Identity provider configuration read from an optional local settings file.
/// </summary>
/// <remarks>
/// Client identifiers are public information for desktop (public client) applications and no client
/// secret is ever embedded. The file is read from
/// <c>%LOCALAPPDATA%\DietTracker\appsettings.json</c> so that a developer or an enterprise can point
/// the build at its own registration without recompiling; the defaults are placeholders that must be
/// replaced before a real sign-in is attempted (see docs/authentication).
/// </remarks>
public sealed class ProviderSettings
{
    private const string PlaceholderClientId = "00000000-0000-0000-0000-000000000000";

    public string MicrosoftClientId { get; set; } = PlaceholderClientId;

    public string MicrosoftAuthority { get; set; } = "https://login.microsoftonline.com/consumers";

    public string GoogleClientId { get; set; } = "000000000000-diettracker.apps.googleusercontent.com";

    /// <summary>True when both providers are still configured with placeholder identifiers.</summary>
    [JsonIgnore]
    public bool IsConfigured => !MicrosoftClientId.Contains("00000000-0000-0000", StringComparison.Ordinal)
                                || !GoogleClientId.StartsWith("000000000000", StringComparison.Ordinal);

    /// <summary>Loads the optional settings file, falling back to the built-in defaults.</summary>
    public static ProviderSettings Load()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DietTracker",
                "appsettings.json");

            if (!File.Exists(path))
            {
                return new ProviderSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProviderSettings>(json) ?? new ProviderSettings();
        }
        catch (Exception)
        {
            // A broken settings file must never prevent startup.
            return new ProviderSettings();
        }
    }
}

/// <summary>
/// Supplies the main window handle to authentication providers that need to parent their dialog
/// (the Windows Account Manager broker and MSAL's interactive prompt).
/// </summary>
public sealed class WindowHandleProvider : IUserInterfaceContext
{
    /// <inheritdoc />
    public IntPtr GetWindowHandle()
    {
        var window = App.MainAppWindow;
        if (window?.Content is not FrameworkElement element)
        {
            return IntPtr.Zero;
        }

        try
        {
            return WindowNative.GetWindowHandle(window);
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }
}
