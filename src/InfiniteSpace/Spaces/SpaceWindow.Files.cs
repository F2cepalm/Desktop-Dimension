using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>What was done to a note with the mouse, beyond painting (see <see cref="SpaceWindow.NoteClicked"/>).</summary>
internal enum NoteClick
{
    /// <summary>Ctrl+click: a link in an opened file opens.</summary>
    CtrlClick,

    /// <summary>Double-click: an opened file's name opens the file itself.</summary>
    DoubleClick,
}

/// <summary>Opened files on a space: dropping them on it, scrolling them, and their arrival.</summary>
internal sealed partial class SpaceWindow
{
    /// <summary>How long a scroll takes to glide to where the wheel sent it, in seconds (a time constant).</summary>
    private const double ScrollGlide = 0.07;

    /// <summary>How long a newly opened file takes to fade in, in seconds.</summary>
    private const double ArrivalFade = 0.3;

    private const int WM_DROPFILES = 0x0233;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_COPYDATA = 0x004A;
    private const int WM_COPYGLOBALDATA = 0x0049;
    private const int CS_DBLCLKS = 0x0008;

    private readonly Dictionary<TextNote, double> _scrolls = [];     // where each file is gliding to
    private readonly Dictionary<TextNote, double> _arrivals = [];    // when each new file started fading in
    private double _filesTime = double.NaN;

    /// <summary>Files were dropped on the space (from Explorer), at this screen DIP point.</summary>
    public event Action<SpaceWindow, string[], Point>? FilesDropped;

    /// <summary>The mouse wheel turned over the space at this screen DIP point (120 a notch, positive away from the user).</summary>
    public event Action<SpaceWindow, Point, int>? Wheel;

    /// <summary>A scroll glided to rest (worth saving where it ended).</summary>
    public event Action<SpaceWindow>? ScrollSettled;

    /// <summary>
    /// Asked for a Ctrl+click or a double-click at a screen DIP point; true when it meant something (a
    /// link, a file's name), and a click's splash is left out then.
    /// </summary>
    public Func<SpaceWindow, Point, NoteClick, bool>? NoteClicked { get; set; }

    /// <summary>Text was dropped on the space (from another app), at this screen DIP point.</summary>
    public event Action<SpaceWindow, string, Point>? TextDropped;

    /// <summary>
    /// <paramref name="to"/> takes <paramref name="from"/>'s place with the same text (moved): it keeps
    /// what was laid out for it, and any scroll under way.
    /// </summary>
    public void Adopt(TextNote from, TextNote to)
    {
        NoteText.Adopt(from, to);
        _ink?.Adopt(from, to);
        if (_scrolls.Remove(from, out double target))
            _scrolls[to] = target;
    }

    /// <summary>Where a file is gliding to, or where it is if it isn't (for the next notch of the wheel).</summary>
    public double ScrollTarget(TextNote note) => _scrolls.TryGetValue(note, out double target) ? target : note.File?.Scroll ?? 0;

    /// <summary>Glides an opened file's text to <paramref name="target"/> (DIPs from its top), a little every frame.</summary>
    public void ScrollTo(TextNote note, double target)
    {
        if (note.File is null || _disposed)
            return;
        _scrolls[note] = target;
    }

    /// <summary>A splash at a screen DIP point, to show which space something happened to.</summary>
    public void Splash(Point at)
    {
        if (!_disposed)
            _look.AddBurst(at);
    }

    /// <summary>
    /// A file just opened here: a splash where it landed, its text fading in, and in the Dreamy
    /// theme a little wobble of the space.
    /// </summary>
    public void Arrive(TextNote note, Point at)
    {
        if (_disposed)
            return;
        _look.AddBurst(at);
        _arrivals[note] = double.NaN; // starts with the next frame
        Wobble();
    }

    /// <summary>How opaque a note is drawn: newly opened files fade in.</summary>
    private float NoteOpacity(TextNote note)
    {
        if (!_arrivals.TryGetValue(note, out double start) || double.IsNaN(start) || double.IsNaN(_filesTime))
            return _arrivals.ContainsKey(note) ? 0 : 1;
        double t = Math.Clamp((_filesTime - start) / ArrivalFade, 0, 1);
        return (float)(t * t * (3 - 2 * t));
    }

    private bool FilesMoving => _scrolls.Count > 0 || _arrivals.Count > 0;

    /// <summary>Moves scrolls and fade-ins on to this frame.</summary>
    /// <returns>Whether the ink needs drawing again.</returns>
    private bool AdvanceFiles(FrameState frame)
    {
        if (!FilesMoving)
        {
            _filesTime = double.NaN;
            return false;
        }
        double dt = double.IsNaN(_filesTime) ? 0 : Math.Clamp(frame.Time - _filesTime, 0, 0.1);
        _filesTime = frame.Time;
        bool moved = false, settled = false;

        foreach (var (note, target) in _scrolls.ToArray())
        {
            var file = note.File!;
            double gap = target - file.Scroll;
            if (Math.Abs(gap) < 0.25)
            {
                file.Scroll = target;
                _scrolls.Remove(note);
                settled = true;
            }
            else
            {
                file.Scroll += gap * (1 - Math.Exp(-dt / ScrollGlide));
            }
            moved = true;
        }

        foreach (var (note, start) in _arrivals.ToArray())
        {
            if (double.IsNaN(start))
                _arrivals[note] = frame.Time;
            else if (frame.Time - start >= ArrivalFade)
                _arrivals.Remove(note);
            moved = true;
        }

        if (settled && _scrolls.Count == 0)
            ScrollSettled?.Invoke(this);
        return moved;
    }

    /// <summary>A little wobble, as if the space took the file in: Dreamy only, and not while Windows' animations are off.</summary>
    private void Wobble()
    {
        if (IsSheet || _theme != Themes.Dreamy || !SystemParameters.ClientAreaAnimation || _gpu.IsSoftware || !_shown || _hidden || _floating || _live is not null || _liftedUnder != IntPtr.Zero)
            return;
        _live = (_outline, _toScreen);
        _restWhenStill = true;
        _jelly.Retarget(_outline.Bounds, wobble: false); // where it rests
        _jelly.Poke(DropWobble);
        _needsPresent = true;
    }

    /// <summary>How far (DIPs) a drop pushes the edges out before they spring back.</summary>
    private const double DropWobble = 7;

    private DropTarget? _dropTarget;

    /// <summary>What OLE calls while something is dragged over the space (null if drag and drop isn't available): for tests.</summary>
    internal DropTarget? Drops => _dropTarget;

    /// <summary>
    /// Takes files dropped from Explorer and text dragged from other apps (only the space's shape takes
    /// them: its window region). Files come as WM_DROPFILES too, should drag and drop not be available.
    /// </summary>
    private void AcceptFiles()
    {
        if (OleInitialize(IntPtr.Zero) >= 0)
        {
            // WPF registers a drop target of its own for its elements (a space has none): ours takes
            // its place. Should that fail, Explorer falls back on WM_DROPFILES.
            RevokeDragDrop(Handle);
            _dropTarget = new DropTarget(this);
            if (RegisterDragDrop(Handle, _dropTarget) < 0)
                _dropTarget = null;
        }
        DragAcceptFiles(Handle, true);
        // Let drops through from a less privileged Explorer if this app runs elevated.
        ChangeWindowMessageFilterEx(Handle, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(Handle, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(Handle, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
    }

    private void OnDropFiles(IntPtr drop)
    {
        try
        {
            uint count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
            var paths = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(drop, i, null, 0);
                var name = new StringBuilder((int)length + 1);
                if (DragQueryFile(drop, i, name, (uint)name.Capacity) > 0)
                    paths.Add(name.ToString());
            }
            DragQueryPoint(drop, out var point);
            ClientToScreen(Handle, ref point); // right whether or not the window is spread out to float
            var at = new Point(point.X / Dpi.Scale, point.Y / Dpi.Scale);
            if (paths.Count > 0)
                FilesDropped?.Invoke(this, [.. paths], at);
        }
        finally
        {
            DragFinish(drop);
        }
    }

    /// <summary>Stops taking drops (the window is going).</summary>
    private void RefuseDrops()
    {
        if (_dropTarget is null)
            return;
        RevokeDragDrop(Handle);
        _dropTarget = null;
    }

    /// <summary>Files or text let go over the space, at a screen point (device pixels).</summary>
    private void OnDrop(string[]? paths, string? text, Win32.POINT point)
    {
        var at = new Point(point.X / Dpi.Scale, point.Y / Dpi.Scale);
        if (_disposed || !Contains(at))
            return;
        if (paths is { Length: > 0 })
            FilesDropped?.Invoke(this, paths, at);
        else if (!string.IsNullOrEmpty(text))
            TextDropped?.Invoke(this, text, at);
    }

    /// <summary>What was dropped: files (Explorer offers those first), or else text.</summary>
    private static (string[]? Paths, string? Text) Read(System.Windows.IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
            ? (paths, null)
            : (null, data.GetData(DataFormats.UnicodeText, autoConvert: true) as string);

    private static bool CanTake(System.Windows.IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true);

    /// <summary>The space as OLE sees it while something is dragged over it: it copies files and text in.</summary>
    [ComVisible(true)]
    internal sealed class DropTarget(SpaceWindow window) : IOleDropTarget
    {
        private const int DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1;
        private bool _takes;

        public int DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, int keyState, Win32.POINT point, ref int effect)
        {
            try
            {
                _takes = CanTake(new DataObject(data));
            }
            catch (COMException)
            {
                _takes = false;
            }
            effect = Allowed(effect);
            return 0;
        }

        public int DragOver(int keyState, Win32.POINT point, ref int effect)
        {
            effect = Allowed(effect);
            return 0;
        }

        public int DragLeave()
        {
            _takes = false;
            return 0;
        }

        public int Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, int keyState, Win32.POINT point, ref int effect)
        {
            effect = Allowed(effect);
            _takes = false;
            if (effect == DROPEFFECT_NONE)
                return 0;
            try
            {
                var (paths, text) = Read(new DataObject(data)); // now, while the source still has it
                // After OLE is done with the drop: the source isn't kept waiting while files open.
                window._source.Dispatcher.BeginInvoke(() => window.OnDrop(paths, text, point));
            }
            catch (COMException)
            {
                effect = DROPEFFECT_NONE;
            }
            return 0;
        }

        /// <summary>Copies, never moves (the text stays where it was dragged from).</summary>
        private int Allowed(int effects) => _takes && (effects & DROPEFFECT_COPY) != 0 ? DROPEFFECT_COPY : DROPEFFECT_NONE;
    }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleDropTarget
    {
        [PreserveSig]
        int DragEnter([MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject data, int keyState, Win32.POINT point, ref int effect);

        [PreserveSig]
        int DragOver(int keyState, Win32.POINT point, ref int effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject data, int keyState, Win32.POINT point, ref int effect);
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr window, IOleDropTarget target);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr window);

    private static bool IsCtrlDown() => (Win32.GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL, as of this message

    /// <summary>Screen coordinates (device pixels, as in wheel messages) to screen DIPs.</summary>
    private static Point ScreenAt(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        int x = (short)(value & 0xFFFF), y = (short)((value >> 16) & 0xFFFF);
        return new Point(x / Dpi.Scale, y / Dpi.Scale);
    }

    private const uint MSGFLT_ALLOW = 1;

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, uint length);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DragQueryPoint(IntPtr drop, out Win32.POINT point);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr drop);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref Win32.POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, int message, uint action, IntPtr changeFilterStruct);
}
