using System.Runtime.InteropServices;

namespace DietTracker.App.Services;

/// <summary>
/// Enforces a minimum and a maximum size on a window. WinUI has no built-in API for this, so the
/// window procedure is extended (WM_GETMINMAXINFO), which is what Windows itself uses while the user
/// drags a border and when the window is maximized. All values are device-independent pixels (DIP);
/// they are converted to pixels with the window's DPI.
/// </summary>
public sealed class WindowSizeLimiter : IDisposable
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int GWLP_WNDPROC = -4;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private readonly IntPtr _hwnd;
    private readonly WndProcDelegate _subclassProc; // kept alive so the GC cannot collect the delegate
    private readonly IntPtr _previousWndProc;

    public WindowSizeLimiter(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _subclassProc = SubclassProc;
        _previousWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_subclassProc));
    }

    /// <summary>Smallest allowed window size (DIP).</summary>
    public double MinWidth { get; set; }

    /// <summary>Smallest allowed window height (DIP).</summary>
    public double MinHeight { get; set; }

    /// <summary>Largest allowed window width (DIP); infinity means unbounded.</summary>
    public double MaxWidth { get; set; } = double.PositiveInfinity;

    /// <summary>Largest allowed window height (DIP); infinity means unbounded.</summary>
    public double MaxHeight { get; set; } = double.PositiveInfinity;

    /// <summary>Pixels per DIP for the given window (1.0 at 96 DPI).</summary>
    public static double GetScaleForWindow(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1d : dpi / 96d;
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var scale = GetScaleForWindow(hWnd);

            info.ptMinTrackSize.X = (int)Math.Round(MinWidth * scale);
            info.ptMinTrackSize.Y = (int)Math.Round(MinHeight * scale);

            if (!double.IsInfinity(MaxWidth))
            {
                var maxWidth = (int)Math.Round(Math.Max(MinWidth, MaxWidth) * scale);
                info.ptMaxTrackSize.X = maxWidth;
                info.ptMaxSize.X = maxWidth;
            }

            if (!double.IsInfinity(MaxHeight))
            {
                var maxHeight = (int)Math.Round(Math.Max(MinHeight, MaxHeight) * scale);
                info.ptMaxTrackSize.Y = maxHeight;
                info.ptMaxSize.Y = maxHeight;
            }

            Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }

        return CallWindowProc(_previousWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>Restores the original window procedure.</summary>
    public void Dispose()
    {
        if (_previousWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _previousWndProc);
        }
    }
}
