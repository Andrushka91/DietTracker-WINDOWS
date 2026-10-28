using System.Diagnostics;

namespace DietTracker.App.Services;

/// <summary>
/// Keeps Diet Tracker to a single instance per Windows session. A second launch does not open a second
/// window: it brings the running window to the foreground (also restoring it from the notification
/// area) and then ends, so two copies can never write to the same SQLite database side by side.
/// </summary>
/// <remarks>
/// A named mutex is used instead of the Windows App SDK single-instancing because it behaves exactly
/// the same for the packaged (MSIX) and the unpackaged build, and because the decision is taken before
/// any service, database or window exists. The name lives in the <c>Local\</c> namespace, so every
/// Windows user signed in to the machine still gets their own instance.
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>Mutex that is held for the whole lifetime of the first instance.</summary>
    internal const string MutexName = @"Local\DietTracker.SingleInstance";

    /// <summary>How long the second launch waits for the first window to appear.</summary>
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan ActivationPollInterval = TimeSpan.FromMilliseconds(250);

    private Mutex? _mutex;
    private bool _ownsMutex;

    /// <summary>
    /// Takes the single-instance mutex. Returns <see langword="false"/> when another instance already
    /// holds it; the caller must then activate that instance and exit.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            _ownsMutex = true;
            return true;
        }

        // The previous instance may have been killed (Task Manager, a crash) instead of exiting, which
        // leaves the mutex abandoned. It can be taken over instead of blocking the application for good.
        try
        {
            if (_mutex.WaitOne(0))
            {
                _ownsMutex = true;
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
            return true;
        }

        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>
    /// Brings the window of the running instance to the foreground and returns <see langword="true"/>
    /// when it was found. The first instance may still be creating its window, so the lookup is
    /// repeated for a few seconds.
    /// </summary>
    public static bool ActivateRunningInstance()
    {
        var deadline = DateTime.UtcNow + ActivationTimeout;

        do
        {
            var handle = FindRunningWindow();
            if (handle != IntPtr.Zero)
            {
                // A window hidden in the notification area has to be shown again; a minimized one is
                // restored so the user sees exactly what they left.
                var command = SingleInstanceNativeMethods.IsIconic(handle)
                    ? SingleInstanceNativeMethods.SW_RESTORE
                    : SingleInstanceNativeMethods.SW_SHOW;

                SingleInstanceNativeMethods.ShowWindowAsync(handle, command);
                BringToForeground(handle);
                return true;
            }

            Thread.Sleep(ActivationPollInterval);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    /// <summary>
    /// Puts the window in front of the user. Windows refuses the change when the launching process did
    /// not receive the last input event (a start from Explorer, a background shortcut), so the input
    /// queue of the window that currently owns the foreground is borrowed for the call, and the shell
    /// is asked as a last resort.
    /// </summary>
    private static void BringToForeground(IntPtr handle)
    {
        if (SingleInstanceNativeMethods.SetForegroundWindow(handle))
        {
            return;
        }

        var currentThread = SingleInstanceNativeMethods.GetCurrentThreadId();
        var foregroundThread = SingleInstanceNativeMethods.GetWindowThreadProcessId(
            SingleInstanceNativeMethods.GetForegroundWindow(), out _);

        var attached = foregroundThread != 0
                       && foregroundThread != currentThread
                       && SingleInstanceNativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        SingleInstanceNativeMethods.SetForegroundWindow(handle);

        if (attached)
        {
            SingleInstanceNativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }

        if (SingleInstanceNativeMethods.GetForegroundWindow() != handle)
        {
            SingleInstanceNativeMethods.SwitchToThisWindow(handle, true);
        }
    }

    /// <summary>Top-level window of another Diet Tracker process, or <see cref="IntPtr.Zero"/>.</summary>
    private static IntPtr FindRunningWindow()
    {
        var peers = FindOtherInstanceProcessIds();
        if (peers.Count == 0)
        {
            return IntPtr.Zero;
        }

        var largest = IntPtr.Zero;
        long largestArea = 0;
        var first = IntPtr.Zero;

        SingleInstanceNativeMethods.EnumWindows((window, _) =>
        {
            SingleInstanceNativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (!peers.Contains(processId))
            {
                return true;
            }

            if (first == IntPtr.Zero)
            {
                first = window;
            }

            // A WinUI process owns several top-level windows: the shell window, plus 0x0 helper windows
            // (the OLE DDE server, the input-method editor). Only the shell window has a real size, and
            // only it can take the foreground - so the largest window is the one to activate.
            if (SingleInstanceNativeMethods.GetWindowRect(window, out var rectangle) && rectangle.Area > largestArea)
            {
                largestArea = rectangle.Area;
                largest = window;
            }

            return true;
        }, IntPtr.Zero);

        return largestArea > 0 ? largest : first;
    }

    private static HashSet<uint> FindOtherInstanceProcessIds()
    {
        var current = (uint)Environment.ProcessId;
        var peers = new HashSet<uint>();

        try
        {
            // The process name is the same for the packaged and the unpackaged build ("DietTracker").
            foreach (var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                using (process)
                {
                    if ((uint)process.Id != current)
                    {
                        peers.Add((uint)process.Id);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Enumerating processes can fail on a locked-down machine; the mutex has already decided
            // that this is a second instance, and it still ends without opening a window.
        }

        return peers;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned any more (abandoned takeover); nothing to release.
            }

            _ownsMutex = false;
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
