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
/// Client identifiers are public information for desktop (public client) applications and no secret
/// is ever embedded in the repository. The file is read from
/// <c>%LOCALAPPDATA%\DietTracker\appsettings.json</c> so that a developer or an enterprise can point
/// the build at its own registration without recompiling; the defaults are placeholders that must be
/// replaced before a real sign-in is attempted (see docs/authentication).
/// </remarks>
public sealed class ProviderSettings
{
    // The documented format (docs/authentication/setup.md) uses camelCase keys and allows "//"
    // comment lines, so matching must be case-insensitive and comments must be skipped: a correctly
    // written file must never be silently ignored and replaced by the placeholders.
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The built-in placeholder for the Google client identifier.</summary>
    public const string GooglePlaceholderClientId = "000000000000-diettracker.apps.googleusercontent.com";

    public string GoogleClientId { get; set; } = GooglePlaceholderClientId;

    /// <summary>
    /// Client secret of the Google Desktop app registration. Google's token endpoint expects it even
    /// though the secret of an installed application is not treated as confidential; like the client
    /// identifier it comes from the local settings file, never from the repository.
    /// </summary>
    public string GoogleClientSecret { get; set; } = string.Empty;

    /// <summary>True when the Google client is still configured with a placeholder identifier.</summary>
    [JsonIgnore]
    public bool IsConfigured => !GoogleClientId.StartsWith("000000000000", StringComparison.Ordinal);

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
            return JsonSerializer.Deserialize<ProviderSettings>(json, ReadOptions) ?? new ProviderSettings();
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
