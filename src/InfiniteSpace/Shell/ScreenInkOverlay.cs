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
}

/// <summary>
/// The pen or text tool over the whole screen. A stroke paints the space it starts on, or else becomes
/// screen notes on the app under it; the text tool places one text box where it is clicked. Esc, a
/// right-click or switching away ends it.
/// </summary>
internal sealed class ScreenInkOverlay : GpuOverlay
{
    private const string PenHint = "Draw on spaces or over apps   ·   Ctrl+Z undo   ·   H hand, T text, C cube   ·   Esc or right-click when done";
    private const string TextHint = "Click where the text goes   ·   Esc to cancel";

    private readonly SpaceManager _spaces;
    private readonly Action<Tool> _selectTool;
    private readonly OverlayStroke? _stroke;
    private readonly Stack<IntPtr> _drawnOn = new(); // windows strokes went to, newest on top (Ctrl+Z)
    private bool _drawing;

    /// <param name="selectTool">Picks another tool (H, P, T or C pressed here); called after this message is handled.</param>
    /// <param name="bounds">Where it covers (screen DIPs); the whole virtual screen by default. Tests keep it off screen.</param>
    public ScreenInkOverlay(ScreenInkMode mode, SpaceManager spaces, Universe universe, Action<Tool> selectTool, Rect? bounds = null)
        : base(universe, mode == ScreenInkMode.Pen ? PenHint : TextHint, bounds)
    {
        Mode = mode;
        _spaces = spaces;
        _selectTool = selectTool;
        Cursor = mode == ScreenInkMode.Pen ? SystemCursors.Cross : SystemCursors.IBeam;
        if (mode == ScreenInkMode.Pen)
            _stroke = new OverlayStroke(Gpu, Layers, PixelWidth, PixelHeight);
    }

    /// <summary>Drawing or placing text.</summary>
    public ScreenInkMode Mode { get; }

    /// <inheritdoc/>
    public override bool WantsFrame => _stroke?.HasNewPieces == true;

    /// <inheritdoc/>
    protected override void OnMouseDown(Point at, int clickCount)
    {
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
        if (_drawing)
            _stroke!.Add(at);
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
        var points = _stroke!.Points.Select(p => p + Origin).ToList();
        _stroke.Reset(); // the kept stroke shows on its space or screen notes in the same frame
        IntPtr window = _spaces.AddInk(points, _spaces.Pen);
        if (window != IntPtr.Zero)
            _drawnOn.Push(window);
    }

    /// <inheritdoc/>
    protected override void OnCaptureLost()
    {
        if (!_drawing)
            return;
        _drawing = false;
        _stroke!.Reset();
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key == Key.Z && Mode == ScreenInkMode.Pen)
        {
            while (_drawnOn.TryPop(out var window))
            {
                if (_spaces.Undo(window))
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

    private Tool ToolOfMode => Mode == ScreenInkMode.Pen ? Tool.Pen : Tool.Text;
}
