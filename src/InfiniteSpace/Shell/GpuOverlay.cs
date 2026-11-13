using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using Vortice.DirectComposition;
using Color4 = Vortice.Mathematics.Color4;
using Int2 = Vortice.Mathematics.Int2;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Shell;

/// <summary>
/// An invisible, topmost layer over every monitor, so the screen looks unchanged while the user draws,
/// erases, places or moves a space. A hint sits at the top of the primary screen. Esc, a right-click or
/// switching away cancels.
/// </summary>
/// <remarks>
/// It shows only what DirectComposition puts in it, drawn on the GPU in small pieces (a stroke's
/// newest part, a handle moved by its offset) and sent to the compositor with the spaces' frame (see
/// <see cref="Universe.Attach(IFramePart)"/>): nothing is redrawn in software, and the window is
/// never repainted as a whole. Input comes straight from its window messages.
/// </remarks>
internal abstract class GpuOverlay : IFramePart
{
    private const int CS_DBLCLKS = 0x0008;

    private static readonly Color4 HintBacking = new(0, 0, 0, 0xCC / 255f);

    private readonly HwndSource _source;
    private readonly IDCompositionTarget _target;
    private readonly OverlaySprite _hint;
    private OverlayPicture? _hintPicture;
    private string _hintText = "";
    private IntPtr _cursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW);
    private bool _shown;
    private bool _closed;

    /// <param name="hint">What the hint at the top says.</param>
    /// <param name="bounds">
    /// Where it covers (screen DIPs); the whole virtual screen by default. Tests keep it off screen.
    /// </param>
    protected GpuOverlay(Universe universe, string hint, Rect? bounds = null)
    {
        Universe = universe;
        Gpu = universe.Gpu;
        var area = bounds ?? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Area = area;
        double scale = Dpi.Scale;
        int left = (int)Math.Floor(area.Left * scale), top = (int)Math.Floor(area.Top * scale);
        PixelWidth = Math.Max(1, (int)Math.Ceiling(area.Right * scale) - left);
        PixelHeight = Math.Max(1, (int)Math.Ceiling(area.Bottom * scale) - top);
        Origin = new Vector(left / scale, top / scale);

        // No redirection bitmap: the window shows only what DirectComposition puts in it.
        _source = new HwndSource(new HwndSourceParameters("Desktop_Dimension")
        {
            WindowStyle = unchecked((int)Win32.WS_POPUP),
            ExtendedWindowStyle = (int)(Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOREDIRECTIONBITMAP),
            WindowClassStyle = CS_DBLCLKS,
            PositionX = left,
            PositionY = top,
            Width = PixelWidth,
            Height = PixelHeight,
        });
        _source.AddHook(WndProc);
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, left, top, PixelWidth, PixelHeight, Win32.SWP_NOACTIVATE);

        Gpu.Composition.CreateTargetForHwnd(Handle, true, out _target!).CheckError();
        Root = Gpu.Composition.CreateVisual();
        _target.SetRoot(Root);
        Layers = Gpu.Composition.CreateVisual();
        Root.AddVisual(Layers, false, null);
        _hint = new OverlaySprite(Gpu, Root); // above every layer
        Hint = hint;
    }

    /// <summary>Raised once when it has closed (dismissed, cancelled or done).</summary>
    public event EventHandler? Closed;

    public IntPtr Handle => _source.Handle;

    /// <summary>Screen DIP position of its top-left: surface coordinates start there.</summary>
    protected Vector Origin { get; }

    /// <summary>What it covers, in screen DIPs (the whole virtual screen, unless a test says otherwise).</summary>
    protected Rect Area { get; }

    /// <summary>
    /// A block or space reshaped from <paramref name="before"/> to <paramref name="after"/>, kept from
    /// being stretched far past the screen's edges (see <see cref="BlockEdit.Contain"/>).
    /// </summary>
    /// <param name="onSurface">The rectangles are in surface coordinates (from <see cref="Origin"/>), not the screen's.</param>
    protected Rect Contained(Rect before, Rect after, bool onSurface = false)
    {
        var area = onSurface ? new Rect(Area.X - Origin.X, Area.Y - Origin.Y, Area.Width, Area.Height) : Area;
        area.Inflate(BlockEdit.Spill, BlockEdit.Spill);
        return BlockEdit.Contain(before, after, area);
    }

    protected Universe Universe { get; }

    protected GpuCompositor Gpu { get; }

    /// <summary>The whole overlay's visual; it shows <see cref="Layers"/> and the hint over them.</summary>
    protected IDCompositionVisual Root { get; }

    /// <summary>Where subclasses add what they show, bottom to top in the order added.</summary>
    protected IDCompositionVisual Layers { get; }

    protected int PixelWidth { get; }

    protected int PixelHeight { get; }

    protected bool IsClosed => _closed;

    /// <summary>The hint at the top of the primary screen.</summary>
    protected string Hint
    {
        get => _hintText;
        set
        {
            if (value == _hintText && _hintPicture is not null)
                return;
            _hintText = value;
            var old = _hintPicture;
            _hintPicture = OverlayText.Paint(Gpu, value, 14, HintBacking, new Vector2(16, 8), 8);
            _hint.Picture = _hintPicture;
            _hint.Shown = true;
            // Centred on the primary screen, whose top-left is (0,0) on screen.
            _hint.MoveTo(new Point(SystemParameters.PrimaryScreenWidth / 2 - _hintPicture.Size.Width / 2 - Origin.X, 28 - Origin.Y));
            old?.Dispose();
        }
    }

    /// <summary>The mouse cursor over it (a system cursor such as <see cref="Win32.IDC_CROSS"/>; zero hides it).</summary>
    protected IntPtr Cursor
    {
        get => _cursor;
        set
        {
            if (_cursor == value)
                return;
            _cursor = value;
            if (Win32.GetCursorPos(out var p) && Win32.WindowFromPoint(p) == Handle)
                Win32.SetCursor(value);
        }
    }

    public virtual bool WantsFrame => false;

    /// <summary>Shows it over everything and gives it the keyboard; <see cref="OnShown"/> follows.</summary>
    public void Show() => Show(activate: true);

    /// <param name="activate">False leaves the keyboard where it is (tests, which run beside the user's apps).</param>
    internal void Show(bool activate)
    {
        if (_closed || _shown)
            return;
        _shown = true;
        Universe.Attach(this);
        Gpu.Commit();
        Win32.ShowWindow(Handle, activate ? Win32.SW_SHOW : Win32.SW_SHOWNOACTIVATE);
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | (activate ? 0 : Win32.SWP_NOACTIVATE));
        if (activate)
        {
            Win32.SetForegroundWindow(Handle);
            SetFocus(Handle);
        }
        OnShown();
        Gpu.Commit();
    }

    /// <summary>Closes it; safe to call again, even while it is already closing.</summary>
    public void Dismiss()
    {
        if (_closed)
            return;
        _closed = true;
        if (Win32.GetCapture() == Handle)
            Win32.ReleaseCapture();
        try
        {
            OnClosing();
        }
        finally
        {
            Universe.Detach(this);
            _target.SetRoot(null);
            Gpu.Invalidate();
            Gpu.Commit(); // off screen before the window goes
            DisposeParts();
            _hint.Dispose();
            _hintPicture?.Dispose();
            Layers.Dispose();
            Root.Dispose();
            _target.Dispose();
            _source.RemoveHook(WndProc);
            _source.Dispose();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public virtual void RenderFrame(FrameState frame)
    {
    }

    /// <summary>The GPU went away: what it showed is gone, so it closes (cancelled, as with Esc).</summary>
    public void DeviceLost() => Later(Dismiss);

    /// <summary>It is on screen, topmost and has the keyboard: windows can now be placed just under it.</summary>
    protected virtual void OnShown()
    {
    }

    /// <summary>It is closing: undo whatever is left undone (it is still on screen).</summary>
    protected virtual void OnClosing()
    {
    }

    /// <summary>Lets go of what subclasses made (visuals, pictures, layers).</summary>
    protected virtual void DisposeParts()
    {
    }

    /// <param name="at">DIPs from the overlay's top-left.</param>
    protected virtual void OnMouseMove(Point at)
    {
    }

    /// <param name="clickCount">2 for the second press of a double-click.</param>
    protected virtual void OnMouseDown(Point at, int clickCount)
    {
    }

    protected virtual void OnMouseUp(Point at)
    {
    }

    /// <param name="delta">Positive away from the user, 120 a notch.</param>
    protected virtual void OnWheel(int delta)
    {
    }

    /// <summary>The mouse was taken away mid-drag (another window, a system gesture).</summary>
    protected virtual void OnCaptureLost()
    {
    }

    /// <returns>Whether the key was used.</returns>
    protected virtual bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Escape)
            return false;
        Dismiss();
        return true;
    }

    protected void CaptureMouse() => Win32.SetCapture(Handle);

    protected void ReleaseMouseCapture()
    {
        if (Win32.GetCapture() == Handle)
            Win32.ReleaseCapture();
    }

    /// <summary>Where the mouse is, in DIPs from the overlay's top-left (its middle, if the mouse is elsewhere).</summary>
    protected Point CursorOnSurface()
    {
        var size = new Size(PixelWidth / Dpi.Scale, PixelHeight / Dpi.Scale);
        if (Win32.GetCursorPos(out var p))
        {
            var at = new Point(p.X / Dpi.Scale - Origin.X, p.Y / Dpi.Scale - Origin.Y);
            if (new Rect(size).Contains(at))
                return at;
        }
        return new Point(size.Width / 2, size.Height / 2);
    }

    protected static ModifierKeys Modifiers =>
        (Down(VK_SHIFT) ? ModifierKeys.Shift : 0) | (Down(VK_CONTROL) ? ModifierKeys.Control : 0) | (Down(VK_MENU) ? ModifierKeys.Alt : 0);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_closed)
            return IntPtr.Zero;
        if (msg is Win32.WM_MOUSEMOVE or Win32.WM_LBUTTONDOWN or WM_MOUSEWHEEL or WM_KEYDOWN or WM_SYSKEYDOWN)
            Universe.Hurry(); // what the user does shows on the very next refresh, not the next ambient frame
        switch (msg)
        {
            case Win32.WM_SETCURSOR when (lParam.ToInt64() & 0xFFFF) == HTCLIENT:
                Win32.SetCursor(_cursor);
                handled = true;
                return new IntPtr(1);

            case Win32.WM_MOUSEMOVE:
                OnMouseMove(At(lParam));
                handled = true;
                break;

            case Win32.WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                OnMouseDown(At(lParam), msg == WM_LBUTTONDBLCLK ? 2 : 1);
                handled = true;
                break;

            case Win32.WM_LBUTTONUP:
                OnMouseUp(At(lParam));
                handled = true;
                break;

            case Win32.WM_RBUTTONDOWN:
                handled = true;
                break;

            case Win32.WM_RBUTTONUP:
                handled = true;
                Later(Dismiss); // like a context menu: on release
                break;

            case WM_MOUSEWHEEL:
                OnWheel((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                handled = true;
                break;

            case Win32.WM_CAPTURECHANGED when lParam != Handle:
                OnCaptureLost();
                break;

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
            {
                var key = KeyInterop.KeyFromVirtualKey((int)wParam.ToInt64());
                if (key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl)
                {
                    handled = msg == WM_SYSKEYDOWN; // Alt alone would open the (missing) window menu
                    break;
                }
                handled = OnKeyDown(key, Modifiers) || msg == WM_SYSKEYDOWN;
                break;
            }

            case WM_SYSCHAR:
                handled = true; // no beep for Alt+letters
                break;

            case WM_ACTIVATE when (wParam.ToInt64() & 0xFFFF) == WA_INACTIVE:
                Later(Dismiss); // switched away: cancelled
                break;

            case WM_CLOSE:
                handled = true;
                Later(Dismiss);
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Client coordinates (device pixels, signed 16-bit each) to DIPs from the overlay's top-left.</summary>
    private static Point At(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        int x = (short)(value & 0xFFFF), y = (short)((value >> 16) & 0xFFFF);
        return new Point(x / Dpi.Scale, y / Dpi.Scale);
    }

    /// <summary>Runs once the current message is handled (closing from inside one of its own messages is not safe).</summary>
    protected void Later(Action action) => Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, action);

    private static bool Down(int key) => (Win32.GetKeyState(key) & 0x8000) != 0;

    private const int HTCLIENT = 1;
    private const int WM_ACTIVATE = 0x0006;
    private const int WA_INACTIVE = 0;
    private const int WM_CLOSE = 0x0010;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSCHAR = 0x0106;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);
}

/// <summary>The system's mouse cursors, for overlays (zero hides the cursor).</summary>
internal static class SystemCursors
{
    public static readonly IntPtr Arrow = Load(Win32.IDC_ARROW);
    public static readonly IntPtr Cross = Load(Win32.IDC_CROSS);
    public static readonly IntPtr IBeam = Load(Win32.IDC_IBEAM);
    public static readonly IntPtr Hand = Load(Win32.IDC_HAND);
    public static readonly IntPtr SizeAll = Load(Win32.IDC_SIZEALL);
    public static readonly IntPtr SizeNWSE = Load(Win32.IDC_SIZENWSE);
    public static readonly IntPtr SizeNESW = Load(Win32.IDC_SIZENESW);
    public static readonly IntPtr SizeWE = Load(Win32.IDC_SIZEWE);
    public static readonly IntPtr SizeNS = Load(Win32.IDC_SIZENS);

    /// <summary>The cursor for dragging a side or corner (or the body) of a block.</summary>
    public static IntPtr For(BlockGrip grip) => grip switch
    {
        BlockGrip.Left | BlockGrip.Top or BlockGrip.Right | BlockGrip.Bottom => SizeNWSE,
        BlockGrip.Right | BlockGrip.Top or BlockGrip.Left | BlockGrip.Bottom => SizeNESW,
        BlockGrip.Left or BlockGrip.Right => SizeWE,
        BlockGrip.Top or BlockGrip.Bottom => SizeNS,
        BlockGrip.Move => SizeAll,
        _ => Arrow,
    };

    private static IntPtr Load(int id) => Win32.LoadCursor(IntPtr.Zero, id);
}
