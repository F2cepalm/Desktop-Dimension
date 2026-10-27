using System.Text;
using System.Windows;

namespace InfiniteSpace.Native;

/// <summary>What an app window is, where it is, and finding windows again after they close and reopen.</summary>
internal static class AppWindows
{
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>Smallest window (DIPs) worth putting spaces back on; smaller ones are menus, tooltips, splashes.</summary>
    private const double MinSide = 80;

    /// <summary>A top-level window: its program, class and title.</summary>
    internal readonly record struct Identity(string App, string WindowClass, string Title);

    /// <summary>The window's rectangle in screen DIPs; false for a minimised, hidden or vanished window.</summary>
    public static bool TryGetFrame(IntPtr hwnd, out Rect frame)
    {
        frame = Rect.Empty;
        if (!Win32.IsWindow(hwnd) || Win32.IsIconic(hwnd) || !Win32.GetWindowRect(hwnd, out var r))
            return false;
        double scale = Dpi.Scale;
        frame = new Rect(r.Left / scale, r.Top / scale, Math.Max(0, r.Right - r.Left) / scale, Math.Max(0, r.Bottom - r.Top) / scale);
        return frame.Width >= 1 && frame.Height >= 1;
    }

    /// <summary>The program, class and title of a window; null when it is gone or its program can't be read.</summary>
    public static Identity? Describe(IntPtr hwnd)
    {
        if (!Win32.IsWindow(hwnd))
            return null;
        string app = ProgramOf(hwnd);
        return app.Length == 0 ? null : new Identity(app, ClassOf(hwnd), TitleOf(hwnd));
    }

    public static string TitleOf(IntPtr hwnd)
    {
        int length = Win32.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";
        var text = new StringBuilder(length + 1);
        Win32.GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    [ThreadStatic]
    private static StringBuilder? t_class;

    public static string ClassOf(IntPtr hwnd)
    {
        var name = t_class ??= new StringBuilder(256);
        name.Clear();
        Win32.GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    /// <summary>Hidden from view: on another virtual desktop, or a suspended store app.</summary>
    public static bool IsCloaked(IntPtr hwnd) =>
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>The desktop or the taskbar: spaces there live at desktop level.</summary>
    public static bool IsShell(IntPtr hwnd) => ShellClasses.Contains(ClassOf(hwnd));

    /// <summary>
    /// Other apps' windows a space could sit on, front to back: visible, on this desktop, not
    /// minimised, not tool windows, and not tiny.
    /// </summary>
    public static List<IntPtr> OpenWindows()
    {
        var windows = new List<IntPtr>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (IsCandidate(hwnd))
                windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>Whether a window could have spaces put back on it (see <see cref="OpenWindows"/>).</summary>
    public static bool IsCandidate(IntPtr hwnd)
    {
        if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd) || Foreground.IsOwn(hwnd))
            return false;
        if (Win32.GetAncestor(hwnd, Win32.GA_ROOT) != hwnd)
            return false;
        if ((Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0)
            return false;
        if (IsCloaked(hwnd) || IsShell(hwnd))
            return false;
        return TryGetFrame(hwnd, out var frame) && frame.Width >= MinSide && frame.Height >= MinSide;
    }

    /// <summary>
    /// The front-most other app's window under a screen DIP point, or zero for the desktop (nothing
    /// there but the wallpaper or the taskbar).
    /// </summary>
    public static IntPtr WindowAt(Point screen)
    {
        IntPtr found = IntPtr.Zero;
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd) || Foreground.IsOwn(hwnd) || IsCloaked(hwnd))
                return true;
            if (!TryGetFrame(hwnd, out var frame) || !frame.Contains(screen))
                return true;
            // Invisible overlays that let clicks through don't count.
            if ((Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TRANSPARENT) != 0)
                return true;
            found = IsShell(hwnd) ? IntPtr.Zero : hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Full path of the program that owns a window; empty when it can't be read.</summary>
    private static string ProgramOf(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint processId);
        IntPtr process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
            return "";
        try
        {
            var path = new StringBuilder(1024);
            uint size = (uint)path.Capacity;
            return Win32.QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString(0, (int)size) : "";
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }
}
