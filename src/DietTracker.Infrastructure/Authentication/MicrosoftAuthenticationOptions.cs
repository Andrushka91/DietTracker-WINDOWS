namespace DietTracker.Infrastructure.Authentication;

/// <summary>
/// Provides the window handle that provider dialogs (WAM / system browser prompts) must be parented
/// to. The WinUI application registers the implementation; tests and headless hosts use the default.
/// </summary>
public interface IUserInterfaceContext
{
    /// <summary>Handle of the main window, or <see cref="IntPtr.Zero"/> when there is none yet.</summary>
    IntPtr GetWindowHandle();
}

/// <summary>Default context used when no window exists (unit tests, background activation).</summary>
public sealed class HeadlessUserInterfaceContext : IUserInterfaceContext
{
    public IntPtr GetWindowHandle() => IntPtr.Zero;
}

/// <summary>Configuration of the Microsoft identity integration.</summary>
public sealed class MicrosoftAuthenticationOptions
{
    /// <summary>
    /// Application (client) identifier of the DietTracker registration in Microsoft Entra ID.
    /// A public client: no secret is ever embedded in the application.
    /// </summary>
    public string ClientId { get; set; } = "00000000-0000-0000-0000-000000000000";

    /// <summary>
    /// Authority used for sign-in. The consumers tenant keeps the flow on personal Microsoft
    /// accounts (Outlook.com, Hotmail.com) which is the primary target; work and school accounts
    /// work through the same public client registration when the organization allows it.
    /// </summary>
    public string Authority { get; set; } = "https://login.microsoftonline.com/consumers";

    /// <summary>
    /// Least privileged delegated scope that allows writing inside the application folder of the
    /// user's OneDrive. The rest of the drive stays inaccessible.
    /// </summary>
    public string[] Scopes { get; set; } = ["Files.ReadWrite.AppFolder", "User.Read"];

    /// <summary>Use the Windows Account Manager (WAM) broker when the platform supports it.</summary>
    public bool UseWindowsAccountManager { get; set; } = true;

    /// <summary>Key under which the serialized MSAL token cache is stored in the protected token store.</summary>
    public string TokenCacheKey { get; set; } = "msal-token-cache";
}
