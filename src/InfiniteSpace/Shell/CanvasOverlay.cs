using System.Windows;
using System.Windows.Input;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Color4 = Vortice.Mathematics.Color4;
using Int2 = Vortice.Mathematics.Int2;
using Ellipse = Vortice.Direct2D1.Ellipse;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Shell;

internal enum OverlayMode
{
    Draw,
    Erase,
}

/// <summary>
/// In Draw mode one freehand stroke becomes a space; in Erase mode a round brush cuts away whatever
/// part of a space it passes over.
/// </summary>
/// <remarks>
/// A stroke is drawn a piece at a time: each frame redraws only the rectangle its newest pieces cover
/// (with whatever else crosses it). The shape's fill, which can change anywhere the stroke has been, is
/// redrawn at most 60 times a second under it. The eraser's circle is a picture that moves.
/// </remarks>
internal sealed class CanvasOverlay : GpuOverlay
{
    private const double EraserRadius = 22;
    private const double MinPointSpacing = 1.0;
    private const double LineWidth = 2;

    /// <summary>How often the shape's fill follows the stroke; the line itself keeps up with every refresh.</summary>
    private const double FillRate = 60;

    private static readonly Color4 PreviewFill = new(0, 0, 0, 0x99 / 255f);
    private static readonly Color4 EraserInk = new(0xFF / 255f, 0x6D / 255f, 0xA8 / 255f, 1);
    private static readonly Color4 OutlineInk = new(1, 1, 1, 0xCC / 255f);
    private static readonly Color4 OutlineFill = new(1, 1, 1, 0x22 / 255f);

    private readonly SpaceManager _spaces;
    private readonly IntPtr _host;
    private readonly List<Point> _points = [];
    private readonly OverlayLayer? _existing;
    private readonly OverlayLayer? _fill;
    private readonly OverlayLayer _stroke;
    private readonly IDCompositionEffectGroup? _trailOpacity;
    private readonly OverlaySprite? _brush;
    private readonly OverlayPicture? _brushPicture;
    private ID2D1StrokeStyle? _round;
    private ID2D1StrokeStyle? _dashed;
    private int _drawn;               // points whose pieces are on screen
    private Rect _filled = Rect.Empty; // where the fill was last drawn
    private bool _fillStale;
    private double _lastFill = double.NegativeInfinity;
    private Rect _existingArea = Rect.Empty;
    private bool _drawing;

    /// <param name="host">In Draw mode, the app window the new space will sit on (zero: desktop level).</param>
    /// <param name="bounds">Where it covers (screen DIPs); the whole virtual screen by default.</param>
    public CanvasOverlay(OverlayMode mode, SpaceManager spaces, Universe universe, IntPtr host, Rect? bounds = null)
        : base(universe, mode == OverlayMode.Draw
            ? "Draw any shape to open a space   ·   Esc to cancel"
            : "Drag over spaces to erase them   ·   Esc or right-click when done", bounds)
    {
        Mode = mode;
        _spaces = spaces;
        _host = host;
        Cursor = mode == OverlayMode.Draw ? SystemCursors.Cross : IntPtr.Zero;

        if (mode == OverlayMode.Draw)
        {
            _fill = new OverlayLayer(Gpu, Layers, PixelWidth, PixelHeight);
            _stroke = new OverlayLayer(Gpu, Layers, PixelWidth, PixelHeight);
        }
        else
        {
            _existing = new OverlayLayer(Gpu, Layers, PixelWidth, PixelHeight);
            // The trail is painted solid and shown see-through as a whole, so where it crosses itself it
            // isn't any darker (as one stroke of a brush).
            _stroke = new OverlayLayer(Gpu, Layers, PixelWidth, PixelHeight);
            Gpu.Composition.CreateEffectGroup(out _trailOpacity).CheckError();
            _trailOpacity!.SetOpacity(0x66 / 255f);
            _stroke.Visual.SetEffect(_trailOpacity);
            double size = EraserRadius * 2 + 2;
            _brushPicture = OverlayPicture.Paint(Gpu, size, size, context =>
            {
                using var white = context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
                context.DrawEllipse(new Ellipse(new Vector2((float)(size / 2), (float)(size / 2)), (float)EraserRadius - 0.75f, (float)EraserRadius - 0.75f), white, 1.5f);
            });
            _brush = new OverlaySprite(Gpu, Layers, _brushPicture);
            RefreshExisting();
        }
    }

    public OverlayMode Mode { get; }

    public override bool WantsFrame => _drawn < _points.Count || _fillStale;

    /// <summary>Points of the stroke so far (DIPs from the overlay's top-left), for tests.</summary>
    internal IReadOnlyList<Point> Points => _points;

    protected override void OnMouseDown(Point at, int clickCount)
    {
        _drawing = true;
        ResetStroke();
        AddPoint(at);
        CaptureMouse();
    }

    protected override void OnMouseMove(Point at)
    {
        if (_brush is not null)
        {
            _brush.CentreAt(at);
            _brush.Shown = true;
        }
        if (_drawing)
            AddPoint(at);
    }

    protected override void OnMouseUp(Point at)
    {
        if (!_drawing)
            return;
        _drawing = false;
        ReleaseMouseCapture();
        Commit();
    }

    protected override void OnCaptureLost()
    {
        if (!_drawing)
            return;
        _drawing = false;
        ResetStroke();
    }

    protected override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Enter && Mode == OverlayMode.Erase)
        {
            Dismiss();
            return true;
        }
        return base.OnKeyDown(key, modifiers);
    }

    public override void RenderFrame(FrameState frame)
    {
        if (_drawn < _points.Count)
            DrawNewPieces();
        if (_fillStale && _fill is not null && (frame.Time - _lastFill >= 0.75 / FillRate || !_drawing))
        {
            _lastFill = frame.Time;
            DrawFill();
        }
    }

    protected override void DisposeParts()
    {
        _round?.Dispose();
        _dashed?.Dispose();
        _existing?.Dispose();
        _fill?.Dispose();
        _stroke.Dispose();
        _trailOpacity?.Dispose();
        _brush?.Dispose();
        _brushPicture?.Dispose();
    }

    private void AddPoint(Point p)
    {
        if (_points.Count > 0 && (p - _points[^1]).Length < MinPointSpacing)
            return;
        _points.Add(p);
        _fillStale = Mode == OverlayMode.Draw;
    }

    private double Width => Mode == OverlayMode.Draw ? LineWidth : EraserRadius * 2;

    /// <summary>
    /// The pieces added since the last frame, with everything else of the stroke that crosses the
    /// rectangle they cover (that rectangle is drawn afresh).
    /// </summary>
    private void DrawNewPieces()
    {
        int from = Math.Max(0, _drawn - 1);
        var area = Rect.Empty;
        for (int i = from; i < _points.Count; i++)
            area.Union(_points[i]);
        double reach = Width / 2 + 1;
        area.Inflate(reach, reach);
        _drawn = _points.Count;

        _stroke.Draw(area, context =>
        {
            using var brush = context.CreateSolidColorBrush(Mode == OverlayMode.Draw ? new Color4(1, 1, 1, 1) : EraserInk);
            var round = Round();
            float width = (float)Width;
            if (_points.Count == 1)
            {
                context.FillEllipse(new Ellipse(V(_points[0]), width / 2, width / 2), brush);
                return;
            }
            for (int i = 1; i < _points.Count; i++)
            {
                Point a = _points[i - 1], b = _points[i];
                if (Math.Max(a.X, b.X) + reach < area.Left || Math.Min(a.X, b.X) - reach > area.Right ||
                    Math.Max(a.Y, b.Y) + reach < area.Top || Math.Min(a.Y, b.Y) - reach > area.Bottom)
                    continue;
                context.DrawLine(V(a), V(b), brush, width, round);
            }
        });
    }

    /// <summary>The shape as it would close now, filled: it can change anywhere the stroke has been.</summary>
    private void DrawFill()
    {
        _fillStale = false;
        var area = Rect.Empty;
        foreach (var p in _points)
            area.Union(p);
        area.Inflate(1, 1);
        var redraw = _filled;
        redraw.Union(area);
        _filled = area;
        if (_points.Count < 3)
        {
            _fill!.Draw(redraw, _ => { });
            return;
        }
        _fill!.Draw(redraw, context =>
        {
            using var path = Gpu.D2DFactory.CreatePathGeometry();
            using (var sink = path.Open())
            {
                sink.SetFillMode(FillMode.Winding);
                sink.BeginFigure(V(_points[0]), FigureBegin.Filled);
                for (int i = 1; i < _points.Count; i++)
                    sink.AddLine(V(_points[i]));
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }
            using var brush = context.CreateSolidColorBrush(PreviewFill);
            context.FillGeometry(path, brush);
        });
    }

    private void Commit()
    {
        if (Mode == OverlayMode.Draw)
        {
            if (_points.Count >= 3 && _spaces.Create(SpaceGeometry.FromStroke(_points, Origin), _host))
            {
                Dismiss();
                return;
            }
            Hint = "Too small to hold a space, draw a bigger shape   ·   Esc to cancel";
            ResetStroke();
            return;
        }

        if (_points.Count > 0)
            _spaces.Erase(SpaceGeometry.EraserStroke(_points, Origin, EraserRadius));
        ResetStroke();
        RefreshExisting();
    }

    private void ResetStroke()
    {
        _points.Clear();
        _drawn = 0;
        _stroke.Clear();
        if (_fill is not null && !_filled.IsEmpty)
        {
            _fill.Clear();
            _filled = Rect.Empty;
        }
        _fillStale = false;
    }

    /// <summary>Dashed outlines of the saved spaces, which may be hidden behind other windows.</summary>
    private void RefreshExisting()
    {
        if (_existing is null)
            return;
        var outlines = _spaces.Outlines.Select(o => (Geometry: o, Bounds: o.Bounds)).ToList();
        var area = Rect.Empty;
        foreach (var (_, bounds) in outlines)
            area.Union(new Rect(bounds.X - Origin.X, bounds.Y - Origin.Y, bounds.Width, bounds.Height));
        area.Inflate(2, 2);
        var redraw = _existingArea;
        redraw.Union(area);
        _existingArea = area;
        _existing.Draw(redraw, context =>
        {
            using var ink = context.CreateSolidColorBrush(OutlineInk);
            using var fill = context.CreateSolidColorBrush(OutlineFill);
            var factory = Gpu.D2DFactory;
            _dashed ??= factory.CreateStrokeStyle(new StrokeStyleProperties { DashStyle = DashStyle.Custom, LineJoin = LineJoin.Round }, [4, 3]);
            foreach (var (outline, _) in outlines)
            {
                var figures = Outline2D.From(outline, (Point)Origin);
                using var path = figures.ToPath(factory);
                context.FillGeometry(path, fill);
                context.DrawGeometry(path, ink, 1.5f, _dashed);
            }
        });
    }

    private ID2D1StrokeStyle Round() =>
        _round ??= Gpu.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });

    private static Vector2 V(Point p) => new((float)p.X, (float)p.Y);
}
