using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Color4 = Vortice.Mathematics.Color4;
using Int2 = Vortice.Mathematics.Int2;

namespace InfiniteSpace.Shell;

/// <summary>
/// Ctrl+Alt+B: a rectangular dream block spawned under the mouse. It follows the mouse until a click
/// drops it. Before or after that, arrows push a side out (Shift+arrow pulls it in, Ctrl for fine
/// steps) and the wheel scales it. Once dropped, edges and corners drag to resize, the body drags to
/// move, and a click elsewhere moves it there. Enter or a double-click makes it a space.
/// </summary>
/// <remarks>
/// The block on screen is a live space (a preview <see cref="SpaceWindow"/> just under this overlay),
/// so it looks exactly as it will once created; this overlay only adds the handles and takes the input.
/// The handles are pictures that move with the block, in the same frame as it. In the Dreamy theme,
/// resizing it wobbles like jelly for a moment (see <see cref="Jelly"/>); the handles show where it
/// comes to rest.
/// </remarks>
internal sealed class BlockOverlay : GpuOverlay
{
    private const double DefaultWidth = 240;
    private const double DefaultHeight = 160;
    private const double MinSize = 24;
    private const double Step = 10;
    private const double FineStep = DreamStyle.ArtPixel;
    private const double GripMargin = 8;
    private const double HandleSize = 8;

    private readonly SpaceManager _spaces;
    private readonly IntPtr _host;
    private readonly OverlayPicture _handle;
    private readonly OverlaySprite[] _handles;
    private readonly OverlaySprite _size;
    private OverlayPicture? _sizePicture;
    private string _sizeText = "";
    private SpaceWindow? _preview;
    private Rect _rect;
    private bool _dropped;
    private BlockGrip _drag;
    private Point _last;

    /// <param name="host">The app window the new space will sit on (zero: desktop level).</param>
    /// <param name="bounds">Where it covers (screen DIPs); the whole virtual screen by default.</param>
    public BlockOverlay(SpaceManager spaces, Universe universe, IntPtr host, Rect? bounds = null)
        : base(universe, "Move to place, click to drop   ·   arrows grow a side (Shift shrinks, Ctrl fine), wheel scales   ·   drag edges   ·   Enter to create, Esc to cancel", bounds)
    {
        _spaces = spaces;
        _host = host;
        Cursor = SystemCursors.SizeAll;
        _handle = OverlayPicture.Paint(Gpu, HandleSize, HandleSize, context =>
        {
            using var white = context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            context.FillRectangle(new Vortice.RawRectF(0, 0, (float)HandleSize, (float)HandleSize), white);
        });
        _handles = [.. Enumerable.Range(0, 8).Select(_ => new OverlaySprite(Gpu, Layers, _handle))];
        _size = new OverlaySprite(Gpu, Layers);

        _last = CursorOnSurface();
        _rect = new Rect(_last.X - DefaultWidth / 2, _last.Y - DefaultHeight / 2, DefaultWidth, DefaultHeight);
        Layout();
    }

    /// <summary>The block in screen DIPs (for tests).</summary>
    internal Rect Block => new(_rect.X + Origin.X, _rect.Y + Origin.Y, _rect.Width, _rect.Height);

    protected override void OnShown()
    {
        // The preview goes just under this overlay, so it needs the overlay on screen first.
        _preview = new SpaceWindow(new SpaceRecord(), Outline(), IntPtr.Zero, Gpu, _spaces.Theme, previewUnder: Handle);
        Universe.Attach(_preview);
    }

    protected override void OnClosing()
    {
        if (_preview is null)
            return;
        // Leaves a few frames later, once the created space (if any) is showing: no blink.
        Universe.Retire(_preview);
        _preview = null;
    }

    protected override void DisposeParts()
    {
        foreach (var handle in _handles)
            handle.Dispose();
        _handle.Dispose();
        _size.Dispose();
        _sizePicture?.Dispose();
    }

    protected override void OnMouseMove(Point p)
    {
        if (!_dropped)
        {
            _rect.Offset(p - _last);
            Layout();
        }
        else if (_drag != BlockGrip.None)
        {
            _rect = Contained(_rect, BlockEdit.Drag(_rect, _drag, p - _last, MinSize), onSurface: true);
            Layout();
        }
        else
        {
            Cursor = SystemCursors.For(BlockEdit.GripAt(_rect, p, GripMargin));
        }
        _last = p;
    }

    protected override void OnMouseDown(Point p, int clickCount)
    {
        _last = p;
        if (clickCount == 2 && _rect.Contains(p))
        {
            Create();
            return;
        }
        if (!_dropped)
        {
            _dropped = true;
            Layout();
            Cursor = SystemCursors.For(BlockEdit.GripAt(_rect, p, GripMargin));
            return;
        }

        _drag = BlockEdit.GripAt(_rect, p, GripMargin);
        if (_drag == BlockGrip.None)
        {
            // A click away from the block moves it there.
            _rect.Offset(p - new Point(_rect.X + _rect.Width / 2, _rect.Y + _rect.Height / 2));
            Layout();
            return;
        }
        CaptureMouse();
    }

    protected override void OnMouseUp(Point p)
    {
        if (_drag == BlockGrip.None)
            return;
        _drag = BlockGrip.None;
        ReleaseMouseCapture();
    }

    protected override void OnCaptureLost() => _drag = BlockGrip.None;

    protected override void OnWheel(int delta)
    {
        _rect = Contained(_rect, BlockEdit.Scale(_rect, delta > 0 ? 1.1 : 1 / 1.1, MinSize), onSurface: true);
        Layout();
    }

    protected override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        var side = key switch
        {
            Key.Left => BlockGrip.Left,
            Key.Right => BlockGrip.Right,
            Key.Up => BlockGrip.Top,
            Key.Down => BlockGrip.Bottom,
            _ => BlockGrip.None,
        };
        if (side != BlockGrip.None)
        {
            double step = modifiers.HasFlag(ModifierKeys.Control) ? FineStep : Step;
            if (modifiers.HasFlag(ModifierKeys.Shift))
                step = -step;
            _rect = Contained(_rect, BlockEdit.Grow(_rect, side, step, MinSize), onSurface: true);
            Layout();
            return true;
        }
        if (key == Key.Enter)
        {
            Create();
            return true;
        }
        return base.OnKeyDown(key, modifiers);
    }

    private void Create()
    {
        // At rest first: the preview stays up a few frames over the new space, and must match it.
        _preview?.FinishReshape();
        if (_spaces.Create(Outline(), _host))
            Dismiss();
    }

    /// <summary>The block in screen DIPs, edges on the art-pixel grid so its rim is straight.</summary>
    private PathGeometry Outline()
    {
        var screen = BlockEdit.Snap(Block, DreamStyle.ArtPixel);
        return SpaceGeometry.FromStroke([screen.TopLeft, screen.TopRight, screen.BottomRight, screen.BottomLeft], new Vector(0, 0));
    }

    private void Layout()
    {
        _preview?.ReshapeLive(Outline(), Matrix.Identity); // drawn on the next frame; in Dreamy a resize wobbles like jelly

        // Handles on the corners and edge midpoints, once the block is dropped and can be resized.
        double[] xs = [_rect.Left, _rect.Left + _rect.Width / 2, _rect.Right];
        double[] ys = [_rect.Top, _rect.Top + _rect.Height / 2, _rect.Bottom];
        int i = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                if (row == 1 && column == 1)
                    continue;
                var handle = _handles[i++];
                handle.CentreAt(new Point(xs[column], ys[row]));
                handle.Shown = _dropped;
            }
        }

        string text = $"{Math.Round(_rect.Width)} × {Math.Round(_rect.Height)}";
        if (text != _sizeText)
        {
            _sizeText = text;
            var old = _sizePicture;
            _size.Picture = _sizePicture = OverlayText.Paint(Gpu, text, 12);
            old?.Dispose();
        }
        _size.MoveTo(new Point(_rect.X, _rect.Bottom + 12)); // clear of the rim's wobble
        _size.Shown = true;
    }
}
