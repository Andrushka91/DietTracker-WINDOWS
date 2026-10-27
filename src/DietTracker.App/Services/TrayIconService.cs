using System.Runtime.InteropServices;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.Services;

/// <summary>
/// Keeps Diet Tracker available in the notification area so reminders keep working while the window is
/// closed, and offers Restore / Exit actions from the tray menu.
/// </summary>
/// <remarks>
/// The icon is hosted by a message-only window created for this purpose. Every call is best effort: an
/// environment without a notification area (for example a service session) simply results in no icon
/// instead of a failure.
/// </remarks>
public sealed partial class TrayIconService : ITrayIconService, IDisposable
{
    private const int IconId = 1;
    private const int CallbackMessage = TrayNativeMethods.WM_APP + 1;

    private readonly ILogger<TrayIconService> _logger;
    private readonly ILocalizationService _localization;
    private readonly TrayNativeMethods.WndProc _windowProc;

    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool _iconOwnsHandle;
    private bool _iconAdded;
    private bool _disposed;

    public TrayIconService(ILogger<TrayIconService> logger, ILocalizationService localization)
    {
        _logger = logger;
        _localization = localization;

        // The delegate is stored in a field so the garbage collector cannot collect it while Windows
        // still calls it.
        _windowProc = OnWindowMessage;
    }

    /// <inheritdoc />
    public event EventHandler? RestoreRequested;

    /// <inheritdoc />
    public event EventHandler? ExitRequested;

    /// <inheritdoc />
    public Task ShowAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureWindow())
        {
            return Task.CompletedTask;
        }

        var data = CreateIconData();
        data.uFlags = TrayNativeMethods.NIF_MESSAGE | TrayNativeMethods.NIF_ICON | TrayNativeMethods.NIF_TIP;

        _iconAdded = TrayNativeMethods.Shell_NotifyIcon(
            _iconAdded ? TrayNativeMethods.NIM_MODIFY : TrayNativeMethods.NIM_ADD,
            ref data);

        if (!_iconAdded)
        {
            _logger.LogInformation("The notification area icon could not be created in this session.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HideAsync(CancellationToken cancellationToken = default)
    {
        if (!_iconAdded)
        {
            return Task.CompletedTask;
        }

        var data = CreateIconData();
        TrayNativeMethods.Shell_NotifyIcon(TrayNativeMethods.NIM_DELETE, ref data);
        _iconAdded = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowBalloonAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (!_iconAdded)
        {
            return Task.CompletedTask;
        }

        var data = CreateIconData();
        data.uFlags = TrayNativeMethods.NIF_INFO;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        data.dwInfoFlags = TrayNativeMethods.NIIF_INFO;

        TrayNativeMethods.Shell_NotifyIcon(TrayNativeMethods.NIM_MODIFY, ref data);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_iconAdded)
        {
            var data = CreateIconData();
            TrayNativeMethods.Shell_NotifyIcon(TrayNativeMethods.NIM_DELETE, ref data);
            _iconAdded = false;
        }

        if (_iconOwnsHandle && _iconHandle != IntPtr.Zero)
        {
            TrayNativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
            _iconOwnsHandle = false;
        }

        if (_windowHandle != IntPtr.Zero)
        {
            TrayNativeMethods.DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }
    }

    private bool EnsureWindow()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            return true;
        }

        try
        {
            var instance = TrayNativeMethods.GetModuleHandle(null);
            var windowClass = new TrayNativeMethods.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<TrayNativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
                hInstance = instance,
                lpszClassName = "DietTracker.TrayWindow",
            };

            if (TrayNativeMethods.RegisterClassEx(ref windowClass) == 0)
            {
                _logger.LogDebug("The tray window class could not be registered (it may already exist).");
            }

            _windowHandle = TrayNativeMethods.CreateWindowEx(
                0, windowClass.lpszClassName, "Diet Tracker", 0, 0, 0, 0, 0,
                TrayNativeMethods.HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);

            _iconHandle = LoadTrayIcon();
            return _windowHandle != IntPtr.Zero;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The notification area icon is unavailable.");
            _windowHandle = IntPtr.Zero;
            return false;
        }
    }

    /// <summary>
    /// Loads the application icon from <c>Assets\DietTracker.ico</c> (shipped next to the executable,
    /// see <c>tools/Generate-StoreAssets.ps1</c>) so the notification area shows the same mark as the
    /// Start menu entry. Falls back to the generic Windows application icon when the file is missing
    /// or cannot be decoded, so a broken installation never loses the tray icon.
    /// </summary>
    private IntPtr LoadTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DietTracker.ico");
            if (File.Exists(iconPath))
            {
                var handle = TrayNativeMethods.LoadImage(
                    IntPtr.Zero,
                    iconPath,
                    TrayNativeMethods.IMAGE_ICON,
                    0,
                    0,
                    TrayNativeMethods.LR_LOADFROMFILE | TrayNativeMethods.LR_DEFAULTSIZE);

                if (handle != IntPtr.Zero)
                {
                    // Unlike the shared LoadIcon handle, a file-loaded icon belongs to us
                    // and has to be destroyed in Dispose.
                    _iconOwnsHandle = true;
                    return handle;
                }

                _logger.LogWarning(
                    "The application icon at {IconPath} could not be loaded; using the default icon.",
                    iconPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The application icon could not be loaded; using the default icon.");
        }

        return TrayNativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(TrayNativeMethods.IDI_APPLICATION));
    }

    private TrayNativeMethods.NOTIFYICONDATA CreateIconData() => new()
    {
        cbSize = Marshal.SizeOf<TrayNativeMethods.NOTIFYICONDATA>(),
        hWnd = _windowHandle,
        uID = IconId,
        uCallbackMessage = CallbackMessage,
        hIcon = _iconHandle,
        szTip = "Diet Tracker",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
}
