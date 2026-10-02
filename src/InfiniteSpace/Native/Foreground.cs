using System.Text;
using System.Windows.Threading;

namespace InfiniteSpace.Native;

/// <summary>Which app the user was in, and handing focus back to it.</summary>
internal static class Foreground
{
    /// <summary>
    /// The window a new space should sit on: the app in front right now, or zero when that is the
    /// desktop itself (the space then lives at desktop level). The taskbar and its tray flyouts, and
    /// this app's own menus, are not apps: the front-most app window gets the space then.
    /// </summary>
    public static IntPtr CaptureHost()
    {
        IntPtr hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var className = new StringBuilder(64);
        Win32.GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW")
            return IntPtr.Zero; // the desktop itself: desktop level
        if (AppWindows.IsCandidate(hwnd))
            return hwnd;
        // The taskbar, its tray and overflow flyouts, this app's own menu and popups: not an app.
        // The space goes on the front-most app window instead, even though it isn't active.
        var apps = AppWindows.OpenWindows();
        return apps.Count > 0 ? apps[0] : IntPtr.Zero;
    }

    /// <summary>
    /// Gives focus back to <paramref name="window"/> after one of our popups closes, unless the user
    /// has meanwhile switched to another app (that app keeps focus).
    /// </summary>
    public static void RestoreTo(IntPtr window)
    {
        if (window == IntPtr.Zero || !Win32.IsWindow(window))
            return;
        IntPtr current = Win32.GetForegroundWindow();
        if (current == IntPtr.Zero || IsOwn(current))
            Win32.SetForegroundWindow(window);
    }

    /// <summary>
    /// Runs <paramref name="action"/> once no mouse button is held (at once if none is). Handing focus
    /// around or closing a window mid-stroke could take the mouse from the stroke being painted.
    /// </summary>
    public static void WhenMouseUp(Action action)
    {
        if (!Win32.IsMouseButtonDown())
        {
            action();
            return;
        }
        var wait = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
        wait.Tick += (_, _) =>
        {
            if (Win32.IsMouseButtonDown())
                return;
            wait.Stop();
            action();
        };
        wait.Start();
    }

    public static bool IsOwn(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == Environment.ProcessId;
    }
}
