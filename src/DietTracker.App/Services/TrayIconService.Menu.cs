namespace DietTracker.App.Services;

/// <summary>Window message handling and tray context menu of <see cref="TrayIconService"/>.</summary>
public sealed partial class TrayIconService
{
    private const int CommandRestore = 1001;
    private const int CommandExit = 1002;

    private IntPtr OnWindowMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case CallbackMessage:
                var notification = lParam.ToInt32();
                if (notification == TrayNativeMethods.WM_LBUTTONUP)
                {
                    RestoreRequested?.Invoke(this, EventArgs.Empty);
                }
                else if (notification == TrayNativeMethods.WM_RBUTTONUP)
                {
                    ShowContextMenu(hWnd);
                }

                return IntPtr.Zero;

            case TrayNativeMethods.WM_COMMAND:
                HandleCommand(wParam.ToInt32());
                return IntPtr.Zero;

            default:
                return TrayNativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
        }
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        var menu = TrayNativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            TrayNativeMethods.AppendMenu(menu, TrayNativeMethods.MF_STRING, new IntPtr(CommandRestore), _localization.Get("Tray_Open"));
            TrayNativeMethods.AppendMenu(menu, TrayNativeMethods.MF_STRING, new IntPtr(CommandExit), _localization.Get("Tray_Exit"));

            TrayNativeMethods.GetCursorPos(out var point);

            // Without SetForegroundWindow the menu would not close when the user clicks elsewhere.
            TrayNativeMethods.SetForegroundWindow(hWnd);

            var command = TrayNativeMethods.TrackPopupMenu(
                menu,
                TrayNativeMethods.TPM_RETURNCMD | TrayNativeMethods.TPM_NONOTIFY,
                point.X,
                point.Y,
                0,
                hWnd,
                IntPtr.Zero);

            HandleCommand(command);
        }
        finally
        {
            TrayNativeMethods.DestroyMenu(menu);
        }
    }

    private void HandleCommand(int command)
    {
        switch (command)
        {
            case CommandRestore:
                RestoreRequested?.Invoke(this, EventArgs.Empty);
                break;

            case CommandExit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
