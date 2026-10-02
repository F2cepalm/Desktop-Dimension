using System.Runtime.InteropServices;

namespace InfiniteSpace.Native;

internal static class Win32
{
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_HOTKEY = 0x0312;
    public const int MA_NOACTIVATE = 3;

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // Layered popup windows drawn with UpdateLayeredWindow.

    public const int WM_SETCURSOR = 0x0020;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CAPTURECHANGED = 0x0215;
    public const int IDC_CROSS = 32515;
    public const int SW_HIDE = 0;
    public const long WS_EX_TOPMOST = 0x00000008;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    // Window regions: the clickable shape of a (non-layered) space window.

    public const int WINDING = 2;

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreatePolyPolygonRgn(POINT[] points, int[] counts, int polygonCount, int fillMode);

    /// <summary>On success the system owns the region; delete it only when this fails.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    // WinEvents: hearing about other apps' windows (activation, minimising) as it happens.

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    /// <summary>Blocks until the desktop compositor's next frame.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();
    public const uint GW_HWNDPREV = 3;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    /// <summary>Whether a mouse button is held down right now (physical buttons, so either way round).</summary>
    public static bool IsMouseButtonDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;

    /// <summary>
    /// Windows 11: waits for the desktop compositor's clock to tick (or for one of the handles).
    /// Missing on Windows 10 (EntryPointNotFoundException).
    /// </summary>
    [DllImport("dcomp.dll", ExactSpelling = true)]
    public static extern uint DCompositionWaitForCompositorClock(uint count, IntPtr handles, uint timeoutInMs);

    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();

    /// <summary>A key's state as of the message being handled (high bit: down).</summary>
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int virtualKey);

    public const int SW_SHOW = 5;
    public const int IDC_SIZENWSE = 32642;
    public const int IDC_SIZENESW = 32643;
    public const int IDC_SIZEWE = 32644;
    public const int IDC_SIZENS = 32645;
    public const int IDC_SIZEALL = 32646;
    public const int IDC_HAND = 32649;

    public const uint LWA_ALPHA = 0x2;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    private const int WM_STYLECHANGING = 0x007C;
    private const int WM_NCDESTROY = 0x0082;

    private delegate IntPtr SubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, IntPtr data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // Held for good: windows call it for as long as they live.
    private static readonly SubclassProc KeepLayeredProc = KeepLayered;

    /// <summary>
    /// Goes in front of a window's own procedure (WPF's, for an HwndSource) while it lets clicks
    /// through: WPF takes WS_EX_LAYERED off any window whose content it doesn't draw as a layer
    /// itself, and does so again each time the window is resized, so the style is put back into
    /// every change made to it. <paramref name="keep"/> is nonzero while it must stay layered.
    /// </summary>
    private static IntPtr KeepLayered(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr keep)
    {
        if (msg == WM_NCDESTROY)
            RemoveWindowSubclass(hWnd, KeepLayeredProc, id);
        var result = DefSubclassProc(hWnd, msg, wParam, lParam);
        if (msg == WM_STYLECHANGING && wParam.ToInt64() == GWL_EXSTYLE && keep != IntPtr.Zero)
        {
            // STYLESTRUCT { styleOld, styleNew }
            int changed = Marshal.ReadInt32(lParam, 4);
            Marshal.WriteInt32(lParam, 4, changed | (int)WS_EX_LAYERED);
        }
        return result;
    }

    /// <summary>
    /// Makes a window let every click through to whatever is under it, or take clicks again. Only a
    /// layered window is skipped by hit testing (WS_EX_TRANSPARENT alone isn't); fully opaque as a
    /// layer, it keeps showing its DirectComposition content as it was.
    /// </summary>
    public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
    {
        SetWindowSubclass(hWnd, KeepLayeredProc, 1, clickThrough ? 1 : 0); // put in once, then only told what to keep
        long style = GetWindowLong(hWnd, GWL_EXSTYLE);
        long changed = clickThrough ? style | WS_EX_LAYERED | WS_EX_TRANSPARENT : style & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT);
        if (changed == style)
            return;
        SetWindowLong(hWnd, GWL_EXSTYLE, changed);
        if (clickThrough)
            SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
    }

    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    /// <summary>The window under a screen point; transparent pixels of layered windows don't count.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();
    public const uint WS_POPUP = 0x80000000;
    public const long WS_EX_LAYERED = 0x00080000;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;
    public const int IDC_ARROW = 32512;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr cursorName);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bitmapInfo, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GdiFlush();

    public static long GetWindowLong(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index).ToInt64() : GetWindowLong32(hWnd, index);

    public static void SetWindowLong(IntPtr hWnd, int index, long value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, index, new IntPtr(value));
        else
            SetWindowLong32(hWnd, index, unchecked((int)value));
    }

    // Other apps' windows: where they are, what they are, and finding them again.

    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const int RGN_COPY = 5;

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOCOPYBITS = 0x0100;
    public const uint SWP_DEFERERASE = 0x2000;
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint GA_ROOT = 2;
    public const int DWMWA_CLOAKED = 14;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);
}
