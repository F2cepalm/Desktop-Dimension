namespace InfiniteSpace.Native;

/// <summary>Which of this app's windows other apps' windows hide completely, so that drawing them is wasted.</summary>
/// <remarks>
/// One pass over the windows, front to back, adds up what opaque windows cover; each of this app's windows
/// is covered when nothing of it is left outside that. Windows that may be see-through (layered,
/// click-through), hidden ones, the shell's and this app's own don't count, so a window is only ever
/// called covered when it truly can't be seen.
/// </remarks>
internal static class Occlusion
{
    [ThreadStatic]
    private static Dictionary<IntPtr, int>? t_index;

    [ThreadStatic]
    private static bool[]? t_covered;

    /// <param name="windows">This app's top-level windows to look at.</param>
    /// <returns>For each, whether other windows above it cover all of it.</returns>
    public static bool[] Covered(IReadOnlyList<IntPtr> windows)
    {
        if (windows.Count == 0)
            return [];
        var covered = t_covered is { } c && c.Length >= windows.Count ? c : (t_covered = new bool[windows.Count]);
        Array.Clear(covered, 0, windows.Count);
        var index = t_index ??= new Dictionary<IntPtr, int>(windows.Count);
        index.Clear();
        for (int i = 0; i < windows.Count; i++)
            index[windows[i]] = i;

        int left = index.Count;
        IntPtr cover = Win32.CreateRectRgn(0, 0, 0, 0), piece = Win32.CreateRectRgn(0, 0, 0, 0), rest = Win32.CreateRectRgn(0, 0, 0, 0);
        try
        {
            Win32.EnumWindows((hwnd, _) =>
            {
                if (index.TryGetValue(hwnd, out int i))
                {
                    if (Win32.GetWindowRect(hwnd, out var own))
                    {
                        Win32.SetRectRgn(piece, own.Left, own.Top, own.Right, own.Bottom);
                        covered[i] = Win32.CombineRgn(rest, piece, cover, Win32.RGN_DIFF) == Win32.NULLREGION;
                    }
                    return --left > 0; // done once every window was looked at
                }
                if (Hides(hwnd, out var r))
                {
                    Win32.SetRectRgn(piece, r.Left, r.Top, r.Right, r.Bottom);
                    Win32.CombineRgn(cover, cover, piece, Win32.RGN_OR);
                }
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            Win32.DeleteObject(cover);
            Win32.DeleteObject(piece);
            Win32.DeleteObject(rest);
        }
        return covered;
    }

    /// <summary>Whether a window hides what is under it, and where (screen pixels, without shadow and invisible borders).</summary>
    private static bool Hides(IntPtr hwnd, out Win32.RECT bounds)
    {
        bounds = default;
        if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd))
            return false;
        long style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        if ((style & (Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT)) != 0)
            return false; // may be see-through
        if ((style & Win32.WS_EX_TOPMOST) != 0 && (style & Win32.WS_EX_NOREDIRECTIONBITMAP) != 0)
            return false; // an overlay drawn only by DirectComposition, as this app's are: likely see-through
        if (Foreground.IsOwn(hwnd) || AppWindows.IsCloaked(hwnd) || AppWindows.IsShell(hwnd))
            return false;
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>()) != 0 &&
            !Win32.GetWindowRect(hwnd, out bounds))
            return false;
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }
}
