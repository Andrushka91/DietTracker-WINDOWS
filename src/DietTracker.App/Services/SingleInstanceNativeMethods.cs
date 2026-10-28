using System.Runtime.InteropServices;

namespace DietTracker.App.Services;

/// <summary>
/// Native interop used to hand a second launch to the window that is already open. The Windows App
/// SDK exposes no "activate the other instance" call, so the shell window is located by process and
/// brought to the foreground with the documented <c>user32</c> functions.
/// </summary>
internal static class SingleInstanceNativeMethods
{
    /// <summary>Shows a hidden window without changing its size or position.</summary>
    internal const int SW_SHOW = 5;

    /// <summary>Restores a minimized or maximized window to its previous size and position.</summary>
    internal const int SW_RESTORE = 9;

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Shares the input state of another thread with this one. Windows only lets a process take the
    /// foreground when it received the last input event; attaching to the thread that owns the
    /// foreground lifts that restriction for the duration of the call.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    /// <summary>
    /// Fallback used when <see cref="SetForegroundWindow"/> is refused: the shell then brings the
    /// window forward itself. The second launch was started by the user, so this never steals focus
    /// from an unrelated application.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern void SwitchToThisWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rectangle);

    /// <summary>Window rectangle in screen coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        /// <summary>Area in pixels; the shell window is the only large window of the process.</summary>
        public readonly long Area => (long)(Right - Left) * (Bottom - Top);
    }
}
