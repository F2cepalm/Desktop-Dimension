using InfiniteSpace.Native;

namespace InfiniteSpace.Spaces;

/// <summary>The one place that decides the mouse cursor for a tool, and shows it again when the tool changes.</summary>
internal static class ToolCursors
{
    private const int HTCLIENT = 1;
    private static readonly Dictionary<int, IntPtr> Loaded = [];

    /// <summary>The system cursor id (IDC_*) for a tool as it acts on a space.</summary>
    /// <param name="overInk">The mouse is on ink, or on a text card, which the hand would move.</param>
    internal static int IdFor(Tool effectiveTool, bool overInk) => effectiveTool switch
    {
        Tool.Pen or Tool.Eraser => Win32.IDC_CROSS,
        Tool.Text => Win32.IDC_IBEAM,
        _ => overInk ? Win32.IDC_SIZEALL : Win32.IDC_ARROW,
    };

    /// <summary>The cursor for a tool as it acts on a space (see <see cref="IdFor"/>).</summary>
    public static IntPtr For(Tool effectiveTool, bool overInk)
    {
        int id = IdFor(effectiveTool, overInk);
        if (!Loaded.TryGetValue(id, out var cursor))
            Loaded[id] = cursor = Win32.LoadCursor(IntPtr.Zero, id);
        return cursor;
    }

    /// <summary>
    /// Has whichever of this app's windows is under the mouse show its cursor now: Windows asks only on
    /// mouse input, so a tool picked by key, or a window closing, would otherwise leave the old one.
    /// </summary>
    public static void RefreshUnderMouse()
    {
        if (!Win32.GetCursorPos(out var p))
            return;
        IntPtr window = Win32.WindowFromPoint(p);
        if (window == IntPtr.Zero || Win32.GetWindowThreadProcessId(window, out uint process) == 0 || process != Environment.ProcessId)
            return;
        Win32.SendMessage(window, Win32.WM_SETCURSOR, window, new IntPtr(HTCLIENT | (Win32.WM_MOUSEMOVE << 16)));
    }
}
