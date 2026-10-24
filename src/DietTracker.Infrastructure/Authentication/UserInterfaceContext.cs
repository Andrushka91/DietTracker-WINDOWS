namespace DietTracker.Infrastructure.Authentication;

/// <summary>
/// Provides the window handle that dialogs (system browser prompts, file pickers) must be parented
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