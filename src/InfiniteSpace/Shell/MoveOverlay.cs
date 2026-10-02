using System.Windows;
using System.Windows.Input;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Vortice.DirectComposition;
using Color4 = Vortice.Mathematics.Color4;
using Int2 = Vortice.Mathematics.Int2;
using RawRectF = Vortice.RawRectF;

namespace InfiniteSpace.Shell;

/// <summary>
/// Ctrl+Alt+M: moves and reshapes a space. The space under the mouse is picked up at once (otherwise
/// a click picks one up) and shows reshaping arrows on its sides and corners. It follows the mouse
/// until a click sets it down; then the arrows drag to stretch it, the body drags to move it, and a
/// click elsewhere moves it there. Throughout, arrow keys push a side out (Shift pulls it in, Ctrl for
/// fine steps), Alt+arrows nudge it and the wheel scales it, as for a block. A double-click or Enter
/// drops it on the app window under the mouse, or on the desktop. Esc, a right-click or switching
/// away puts it back as it was.
/// </summary>
/// <remarks>
/// Paint on the space stretches with it. Text goes along when the space moves, but stays where it is
/// while the space is reshaped, unless an edge coming in pushes it. Each new shape is drawn properly
/// on the next frame (see <see cref="SpaceManager.Fit"/>), never stretched. The arrows and the dashed
/// frame are pictures drawn once, which only move (the frame's sides are long dashed lines cut to
/// length), in the same frame as the space.
/// </remarks>
internal sealed class MoveOverlay : GpuOverlay
{
    private const double Step = 10;
    private const double FineStep = DreamStyle.ArtPixel;
    private const double MinSize = 24;

    /// <summary>How far from an edge a press still grips it: the whole of the arrow drawn on it.</summary>
    private const double GripMargin = ArrowHandle.Size / 2 + 1;

    private static readonly BlockGrip[] Grips =
    [
        BlockGrip.Left | BlockGrip.Top, BlockGrip.Top, BlockGrip.Right | BlockGrip.Top,
        BlockGrip.Left, BlockGrip.Right,
        BlockGrip.Left | BlockGrip.Bottom, BlockGrip.Bottom, BlockGrip.Right | BlockGrip.Bottom,
    ];

    private static readonly Color4 FrameInk = new(1, 1, 1, 0xB3 / 255f);

    private readonly SpaceManager _spaces;
    private readonly Dictionary<double, OverlayPicture> _arrowPictures = [];
    private readonly OverlaySprite[] _arrows;
    private readonly IDCompositionVisual _arrowLayer;
    private readonly IDCompositionEffectGroup _arrowOpacity;
    private readonly OverlayPicture _across;
    private readonly OverlayPicture _down;
    private readonly OverlaySprite[] _sides; // top, bottom, left, right
    private readonly OverlaySprite _size;
    private OverlayPicture? _sizePicture;
    private string _sizeText = "";
    private SpaceManager.MovingSpace? _moving;
    private Rect _rect;   // the space's bounds on screen (DIPs)
    private bool _placed; // set down: no longer follows the mouse, and the arrows take drags
    private BlockGrip _drag;
    private Point _last;  // screen DIPs
    private Vector _carried; // how far moves (not reshapes) have taken it: its text goes along

    /// <param name="bounds">Where it covers (screen DIPs); the whole virtual screen by default.</param>
    public MoveOverlay(SpaceManager spaces, Universe universe, Rect? bounds = null)
        : base(universe, PickHint, bounds)
    {
        _spaces = spaces;
        _last = CursorOnScreen();
        Cursor = SystemCursors.Hand;

        // The frame's sides: dashed lines as long as the overlay, cut to the frame's size by a clip.
        double longest = Math.Max(PixelWidth, PixelHeight) / Native.Dpi.Scale + 8;
        _across = OverlayPicture.Paint(Gpu, longest, 1, context => DashedLine(context, longest, horizontal: true));
        _down = OverlayPicture.Paint(Gpu, 1, longest, context => DashedLine(context, longest, horizontal: false));
        _sides = [new(Gpu, Layers, _across), new(Gpu, Layers, _across), new(Gpu, Layers, _down), new(Gpu, Layers, _down)];

        _arrowLayer = Gpu.Composition.CreateVisual();
        Layers.AddVisual(_arrowLayer, true, null);
        Gpu.Composition.CreateEffectGroup(out _arrowOpacity).CheckError();
        _arrowLayer.SetEffect(_arrowOpacity);
        _arrows = [.. Grips.Select(grip => new OverlaySprite(Gpu, _arrowLayer, Arrow(ArrowHandle.AngleFor(grip))))];
        _size = new OverlaySprite(Gpu, Layers);
    }

    private static string PickHint => "Click a space to pick it up   ·   Esc to cancel";

    private static string FollowHint =>
        "Click to set it down   ·   arrow keys reshape (Shift shrinks, Ctrl fine), Alt+arrows nudge, wheel scales   ·   double-click or Enter drops it   ·   Esc puts it back";

    private static string PlacedHint =>
        "Drag the arrows to reshape, the body to move   ·   arrow keys reshape, Alt+arrows nudge   ·   double-click or Enter drops it   ·   Esc puts it back";

    /// <summary>The space being moved, if one was picked up (for tests).</summary>
    internal SpaceManager.MovingSpace? Moving => _moving;

    protected override void OnShown() => PickUp(_last); // picking up floats the space just under this overlay

    protected override void OnClosing()
    {
        if (_moving is null)
            return;
        _spaces.PutBack(_moving);
        _moving = null;
    }

    protected override void DisposeParts()
    {
        foreach (var arrow in _arrows)
            arrow.Dispose();
        foreach (var picture in _arrowPictures.Values)
            picture.Dispose();
        foreach (var side in _sides)
            side.Dispose();
        _across.Dispose();
        _down.Dispose();
        _arrowOpacity.Dispose();
        _arrowLayer.Dispose();
        _size.Dispose();
        _sizePicture?.Dispose();
    }

    protected override void OnMouseMove(Point at)
    {
        var p = ToScreen(at);
        if (_moving is not null)
        {
            if (!_placed)
                MoveBy(p - _last);
            else if (_drag == BlockGrip.Move)
                MoveBy(p - _last);
            else if (_drag != BlockGrip.None)
                Fit(Contained(_rect, BlockEdit.Drag(_rect, _drag, p - _last, MinSize)));
            else
                Cursor = SystemCursors.For(BlockEdit.GripAt(_rect, p, GripMargin));
        }
        _last = p;
    }

    protected override void OnMouseDown(Point at, int clickCount)
    {
        var p = ToScreen(at);
        _last = p;
        if (_moving is null)
        {
            PickUp(p);
            return;
        }

        if (clickCount == 2 && BlockEdit.GripAt(_rect, p, GripMargin) != BlockGrip.None)
        {
            DropAt(p);
            return;
        }
        if (!_placed)
        {
            _placed = true;
            Hint = PlacedHint;
            Layout();
            Cursor = SystemCursors.For(BlockEdit.GripAt(_rect, p, GripMargin));
            return;
        }

        _drag = BlockEdit.GripAt(_rect, p, GripMargin);
        if (_drag == BlockGrip.None)
        {
            // A click away from the space moves it there.
            MoveBy(p - new Point(_rect.X + _rect.Width / 2, _rect.Y + _rect.Height / 2));
            return;
        }
        CaptureMouse();
    }

    protected override void OnMouseUp(Point at)
    {
        if (_drag == BlockGrip.None)
            return;
        _drag = BlockGrip.None;
        ReleaseMouseCapture();
    }

    protected override void OnCaptureLost() => _drag = BlockGrip.None;

    protected override void OnWheel(int delta)
    {
        if (_moving is null)
            return;
        Fit(Contained(_rect, BlockEdit.Scale(_rect, delta > 0 ? 1.1 : 1 / 1.1, MinSize)));
    }

    protected override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (_moving is not null)
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
                if (modifiers.HasFlag(ModifierKeys.Alt))
                {
                    MoveBy(side switch
                    {
                        BlockGrip.Left => new Vector(-step, 0),
                        BlockGrip.Right => new Vector(step, 0),
                        BlockGrip.Top => new Vector(0, -step),
                        _ => new Vector(0, step),
                    });
                }
                else
                {
                    Fit(Contained(_rect, BlockEdit.Grow(_rect, side, modifiers.HasFlag(ModifierKeys.Shift) ? -step : step, MinSize)));
                }
                return true;
            }
            if (key == Key.Enter)
            {
                DropAt(DropPoint());
                return true;
            }
        }
        return base.OnKeyDown(key, modifiers);
    }

    private void PickUp(Point at)
    {
        if (_moving is not null)
            return;
        _moving = _spaces.PickUp(at, Handle);
        if (_moving is null)
        {
            Hint = PickHint;
            Cursor = SystemCursors.Hand;
            return;
        }
        _rect = _moving.Bounds;
        Hint = FollowHint;
        Cursor = SystemCursors.SizeAll;
        Layout();
    }

    /// <summary>Moves the space, with its text.</summary>
    private void MoveBy(Vector offset)
    {
        if (_moving is null)
            return;
        _carried += offset;
        Fit(Offset(_rect, offset));
    }

    /// <summary>The space's bounds become <paramref name="rect"/>: it stretches to fill them, its text stays put.</summary>
    private void Fit(Rect rect)
    {
        if (_moving is null)
            return;
        _rect = rect;
        _spaces.Fit(_moving, rect, _carried);
        Layout();
    }

    /// <summary>A dashed frame around the space, with an arrow on each side and corner and its size below.</summary>
    private void Layout()
    {
        bool shown = _moving is not null;
        var box = new Rect(_rect.X - Origin.X, _rect.Y - Origin.Y, _rect.Width, _rect.Height);
        double scale = Native.Dpi.Scale;
        float across = (float)Math.Round(box.Width * scale), down = (float)Math.Round(box.Height * scale);
        // The frame's sides, as the old WPF frame had them: a one-DIP line centred on each edge.
        Place(_sides[0], new Point(box.Left - 0.5, box.Top - 0.5), new RawRectF(0, 0, across + (float)scale, (float)scale));
        Place(_sides[1], new Point(box.Left - 0.5, box.Bottom - 0.5), new RawRectF(0, 0, across + (float)scale, (float)scale));
        Place(_sides[2], new Point(box.Left - 0.5, box.Top - 0.5), new RawRectF(0, 0, (float)scale, down + (float)scale));
        Place(_sides[3], new Point(box.Right - 0.5, box.Top - 0.5), new RawRectF(0, 0, (float)scale, down + (float)scale));
        foreach (var side in _sides)
            side.Shown = shown;

        for (int i = 0; i < Grips.Length; i++)
        {
            var grip = Grips[i];
            double x = grip.HasFlag(BlockGrip.Left) ? box.Left : grip.HasFlag(BlockGrip.Right) ? box.Right : box.Left + box.Width / 2;
            double y = grip.HasFlag(BlockGrip.Top) ? box.Top : grip.HasFlag(BlockGrip.Bottom) ? box.Bottom : box.Top + box.Height / 2;
            _arrows[i].CentreAt(new Point(x, y));
            _arrows[i].Shown = shown;
        }
        _arrowOpacity.SetOpacity(_placed ? 1 : 0.75f); // dimmer while the space follows the mouse
        Gpu.Invalidate();

        string text = $"{Math.Round(_rect.Width)} × {Math.Round(_rect.Height)}";
        if (text != _sizeText)
        {
            _sizeText = text;
            var old = _sizePicture;
            _size.Picture = _sizePicture = OverlayText.Paint(Gpu, text, 12);
            old?.Dispose();
        }
        _size.MoveTo(new Point(box.X, box.Bottom + ArrowHandle.Size / 2 + 4)); // clear of the arrows
        _size.Shown = shown;
    }

    private void Place(OverlaySprite side, Point at, RawRectF clip)
    {
        side.MoveTo(at);
        side.Visual.SetClip(clip);
    }

    /// <summary>The arrow handle for an angle, painted once.</summary>
    private OverlayPicture Arrow(double angle)
    {
        if (!_arrowPictures.TryGetValue(angle, out var picture))
            _arrowPictures[angle] = picture = ArrowHandle.Picture(Gpu, angle);
        return picture;
    }

    /// <summary>A one-DIP line in dashes of 4 with gaps of 3, along the picture.</summary>
    private static void DashedLine(Vortice.Direct2D1.ID2D1DeviceContext context, double length, bool horizontal)
    {
        using var ink = context.CreateSolidColorBrush(FrameInk);
        for (double at = 0; at < length; at += 7)
        {
            float from = (float)at, to = (float)Math.Min(length, at + 4);
            context.FillRectangle(horizontal ? new RawRectF(from, 0, to, 1) : new RawRectF(0, from, 1, to), ink);
        }
    }

    /// <summary>Where Enter drops the space: at the mouse if it is on the space, else at the space's middle.</summary>
    private Point DropPoint()
    {
        var cursor = CursorOnScreen();
        return _rect.Contains(cursor) ? cursor : new Point(_rect.X + _rect.Width / 2, _rect.Y + _rect.Height / 2);
    }

    private void DropAt(Point at)
    {
        var moving = _moving!;
        _moving = null; // dropped: closing no longer puts it back
        _spaces.Drop(moving, at);
        Dismiss();
    }

    private static Rect Offset(Rect rect, Vector by)
    {
        rect.Offset(by);
        return rect;
    }

    private Point ToScreen(Point onSurface) => onSurface + Origin;

    private Point CursorOnScreen() => CursorOnSurface() + Origin;
}
