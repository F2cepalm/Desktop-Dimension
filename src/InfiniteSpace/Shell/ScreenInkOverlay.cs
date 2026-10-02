using System.Windows;
using System.Windows.Input;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>What the screen-wide overlay does: draw (the pen) or place a text box.</summary>
internal enum ScreenInkMode
{
    Pen,
    Text,
    Eraser,
}

/// <summary>
/// The pen, text or eraser over the whole screen. A stroke paints the space it starts on, or else becomes
/// screen notes on the app under it; the text tool places one text box where it is clicked; the eraser
/// takes away the ink it passes over, on spaces and screen notes. Esc, a right-click or switching away ends it.
/// </summary>
internal sealed class ScreenInkOverlay : GpuOverlay
{
    private const string PenHint = "Draw on spaces or over apps   ·   Ctrl+Z undo   ·   H hand, T text, E eraser, C cube   ·   Esc or right-click when done";
    private const string TextHint = "Click where the text goes   ·   Esc to cancel";
    private const string EraserHint = "Drag over ink to erase it, on spaces or over apps   ·   Ctrl+Z undo   ·   H hand, P pen, T text, C cube   ·   Esc or right-click when done";

    /// <summary>How far apart (DIPs) the eraser looks along a sweep, so a fast one misses nothing.</summary>
    private const double EraseStep = 3;

    private readonly SpaceManager _spaces;
    private readonly Action<Tool> _selectTool;
    private readonly OverlayStroke? _stroke;
    private readonly Stack<IntPtr[]> _changed = new(); // windows each stroke or sweep changed, newest on top (Ctrl+Z)
    private readonly HashSet<IntPtr> _sweep = [];      // windows the eraser changed in this sweep
    private bool _drawing;
    private long _eraseOrder;
    private Point _erasedTo;

    /// <param name="selectTool">Picks another tool (H, P, T or C pressed here); called after this message is handled.</param>
    /// <param name="bounds">Where it covers (screen DIPs); the whole virtual screen by default. Tests keep it off screen.</param>
    public ScreenInkOverlay(ScreenInkMode mode, SpaceManager spaces, Universe universe, Action<Tool> selectTool, Rect? bounds = null)
        : base(universe, mode switch { ScreenInkMode.Pen => PenHint, ScreenInkMode.Text => TextHint, _ => EraserHint }, bounds)
    {
        Mode = mode;
        _spaces = spaces;
        _selectTool = selectTool;
        Cursor = mode == ScreenInkMode.Text ? SystemCursors.IBeam : SystemCursors.Cross;
        if (mode == ScreenInkMode.Pen)
            _stroke = new OverlayStroke(Gpu, Layers, PixelWidth, PixelHeight);
    }

    /// <summary>Drawing, placing text or erasing.</summary>
    public ScreenInkMode Mode { get; }

    /// <inheritdoc/>
    public override bool WantsFrame => _stroke?.HasNewPieces == true;

    /// <inheritdoc/>
    protected override void OnMouseDown(Point at, int clickCount)
    {
        if (Mode == ScreenInkMode.Eraser)
        {
            _drawing = true;
            _eraseOrder = InkOrder.Next(); // one undo brings the whole sweep back
            _sweep.Clear();
            _erasedTo = at;
            EraseAt(at);
            CaptureMouse();
            return;
        }
        if (_stroke is null)
            return;
        var pen = _spaces.Pen;
        _stroke.Start(at, InkColors.Resolve(pen.Color, _spaces.InkColorAt(at + Origin)), pen.Thickness);
        _drawing = true;
        CaptureMouse();
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(Point at)
    {
        if (!_drawing)
            return;
        if (Mode != ScreenInkMode.Eraser)
        {
            _stroke!.Add(at);
            return;
        }
        var from = _erasedTo;
        _erasedTo = at;
        int steps = Math.Max(1, (int)Math.Ceiling((at - from).Length / EraseStep));
        for (int i = 1; i <= steps; i++)
            EraseAt(from + (at - from) * i / steps);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(Point at)
    {
        if (Mode == ScreenInkMode.Text)
        {
            var screen = at + Origin;
            Dismiss(); // the app gets the keyboard back first; then the text box takes it
            Later(() => _spaces.TypeAt(screen));
            return;
        }
        if (!_drawing)
            return;
        _drawing = false;
        ReleaseMouseCapture();
        if (Mode == ScreenInkMode.Eraser)
        {
            EndSweep();
            return;
        }
        var points = _stroke!.Points.Select(p => p + Origin).ToList();
        _stroke.Reset(); // the kept stroke shows on its space or screen notes in the same frame
        IntPtr window = _spaces.AddInk(points, _spaces.Pen);
        if (window != IntPtr.Zero)
            _changed.Push([window]);
    }

    /// <inheritdoc/>
    protected override void OnCaptureLost()
    {
        if (!_drawing)
            return;
        _drawing = false;
        if (Mode == ScreenInkMode.Eraser)
            EndSweep(); // what was erased stays erased
        else
            _stroke!.Reset();
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key == Key.Z && Mode != ScreenInkMode.Text)
        {
            while (_changed.TryPop(out var windows))
            {
                bool undone = false;
                foreach (var window in windows)
                    undone |= _spaces.Undo(window);
                if (undone)
                    break;
            }
            return true;
        }
        if (modifiers == ModifierKeys.None && ToolCatalog.ForKey(key) is { } tool && tool != ToolOfMode)
        {
            Later(() => _selectTool(tool)); // not from inside this overlay's own message: picking may close it
            return true;
        }
        return base.OnKeyDown(key, modifiers); // Esc
    }

    /// <inheritdoc/>
    public override void RenderFrame(FrameState frame) => _stroke?.DrawNewPieces();

    /// <inheritdoc/>
    protected override void DisposeParts() => _stroke?.Dispose();

    private Tool ToolOfMode => Mode switch { ScreenInkMode.Pen => Tool.Pen, ScreenInkMode.Text => Tool.Text, _ => Tool.Eraser };

    private void EraseAt(Point at)
    {
        IntPtr window = _spaces.EraseAt(at + Origin, _eraseOrder);
        if (window != IntPtr.Zero)
            _sweep.Add(window);
    }

    /// <summary>The sweep is over: Ctrl+Z takes it back as one.</summary>
    private void EndSweep()
    {
        if (_sweep.Count > 0)
            _changed.Push([.. _sweep]);
        _sweep.Clear();
    }
}
