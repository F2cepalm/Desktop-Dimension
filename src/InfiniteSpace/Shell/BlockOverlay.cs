using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Color4 = Vortice.Mathematics.Color4;
using Int2 = Vortice.Mathematics.Int2;
using RawRectF = Vortice.RawRectF;

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

    /// <summary>How thick the outline round the block is (DIPs): a dark halo with white dashes along its middle.</summary>
    private const double FrameWidth = 3;

    /// <summary>How far outside the block (DIPs) its outline runs.</summary>
    private const double FrameGap = 2;

    private static readonly Color4 FrameInk = new(1, 1, 1, 0xB3 / 255f);
    private static readonly Color4 FrameHalo = new(0, 0, 0, 0.55f);

    private readonly SpaceManager _spaces;
    private readonly IntPtr _host;
    private readonly OverlayPicture _across;
    private readonly OverlayPicture _down;
    private readonly OverlaySprite[] _sides; // top, bottom, left, right
    private readonly OverlayPicture _handle;
    private readonly OverlaySprite[] _handles;
    private readonly OverlaySprite _size;
    private OverlayPicture? _sizePicture;
    private string _sizeText = "";
    private bool _sizeDirty;
    private SpaceWindow? _preview;
    private Rect _previewScreen;
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

        // The outline shows over spaces until the block is created (over a space in the same theme its
        // rim can vanish): dashed lines as long as the overlay, cut to the block's size by a clip.
        double longest = Math.Max(PixelWidth, PixelHeight) / Native.Dpi.Scale + 8;
        _across = OverlayPicture.Paint(Gpu, longest, FrameWidth, context => DashedLine(context, longest, horizontal: true));
        _down = OverlayPicture.Paint(Gpu, FrameWidth, longest, context => DashedLine(context, longest, horizontal: false));
        _sides = [new(Gpu, Layers, _across), new(Gpu, Layers, _across), new(Gpu, Layers, _down), new(Gpu, Layers, _down)]; // before the handles, which stay on top
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

    public override bool WantsFrame => _sizeDirty;

    public override void RenderFrame(FrameState frame)
    {
        if (!_sizeDirty)
            return;
        _sizeDirty = false;
        _size.Picture = OverlayText.Repaint(Gpu, ref _sizePicture, _sizeText, 12);
    }

    protected override void OnShown()
    {
        // The preview goes just under this overlay, so it needs the overlay on screen first.
        _previewScreen = BlockEdit.Snap(Block, DreamStyle.ArtPixel);
        _preview = new SpaceWindow(new SpaceRecord(), Outline(_previewScreen), IntPtr.Zero, Gpu, _spaces.Theme, previewUnder: Handle);
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
        foreach (var side in _sides)
            side.Dispose();
        _across.Dispose();
        _down.Dispose();
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

    private PathGeometry Outline() => Outline(BlockEdit.Snap(Block, DreamStyle.ArtPixel));

    private static PathGeometry Outline(Rect screen) =>
        SpaceGeometry.FromStroke([screen.TopLeft, screen.TopRight, screen.BottomRight, screen.BottomLeft], new Vector(0, 0));

    private void Layout()
    {
        var screen = BlockEdit.Snap(Block, DreamStyle.ArtPixel);
        if (_preview is not null && screen != _previewScreen)
        {
            var outline = Outline(screen);
            if (Math.Abs(screen.Width - _previewScreen.Width) < 1e-4 && Math.Abs(screen.Height - _previewScreen.Height) < 1e-4 && !_preview.IsReshaping)
                _preview.Move(outline, screen.TopLeft - _previewScreen.TopLeft, Matrix.Identity);
            else
                _preview.ReshapeLive(outline, Matrix.Identity); // drawn on the next frame; in Dreamy a resize wobbles like jelly
            _previewScreen = screen;
        }

        // The outline, just outside the block, whether or not it has been dropped.
        var box = Rect.Inflate(_rect, FrameGap, FrameGap);
        double scale = Native.Dpi.Scale;
        float across = (float)Math.Round((box.Width + 2 * FrameWidth) * scale), down = (float)Math.Round((box.Height + 2 * FrameWidth) * scale);
        float thick = (float)Math.Round(FrameWidth * scale);
        Place(_sides[0], new Point(box.Left - FrameWidth, box.Top - FrameWidth), new RawRectF(0, 0, across, thick));
        Place(_sides[1], new Point(box.Left - FrameWidth, box.Bottom), new RawRectF(0, 0, across, thick));
        Place(_sides[2], new Point(box.Left - FrameWidth, box.Top - FrameWidth), new RawRectF(0, 0, thick, down));
        Place(_sides[3], new Point(box.Right, box.Top - FrameWidth), new RawRectF(0, 0, thick, down));

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
            if (_sizePicture is null)
                _size.Picture = OverlayText.Repaint(Gpu, ref _sizePicture, text, 12);
            else
                _sizeDirty = true;
        }
        _size.MoveTo(new Point(_rect.X, _rect.Bottom + 12)); // clear of the rim's wobble
        _size.Shown = true;
    }

    private static void Place(OverlaySprite side, Point at, RawRectF clip)
    {
        side.MoveTo(at);
        side.Visual.SetClip(clip);
        side.Shown = true;
    }

    /// <summary>A dark line <see cref="FrameWidth"/> thick with one-DIP white dashes of 4 (gaps of 3) along its middle: it reads on light themes and dark ones alike.</summary>
    private static void DashedLine(Vortice.Direct2D1.ID2D1DeviceContext context, double length, bool horizontal)
    {
        using var halo = context.CreateSolidColorBrush(FrameHalo);
        using var ink = context.CreateSolidColorBrush(FrameInk);
        float far = (float)length, thick = (float)FrameWidth, mid = (float)((FrameWidth - 1) / 2);
        context.FillRectangle(horizontal ? new RawRectF(0, 0, far, thick) : new RawRectF(0, 0, thick, far), halo);
        for (double at = 0; at < length; at += 7)
        {
            float from = (float)at, to = (float)Math.Min(length, at + 4);
            context.FillRectangle(horizontal ? new RawRectF(from, mid, to, mid + 1) : new RawRectF(mid, from, mid + 1, to), ink);
        }
    }
}
