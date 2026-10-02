using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Color4 = Vortice.Mathematics.Color4;
using GradientStop = Vortice.Direct2D1.GradientStop;
using Matrix3x2 = System.Numerics.Matrix3x2;
using RawRectF = Vortice.RawRectF;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace InfiniteSpace.Rendering;

/// <summary>How a painted theme finishes the edge of a space (see <see cref="SceneLook"/>).</summary>
internal sealed record FrameStyle
{
    /// <summary>The outline itself.</summary>
    public required Color Line { get; init; }

    /// <summary>Width of the outline, in DIPs.</summary>
    public double LineWidth { get; init; } = 1.5;

    /// <summary>Soft light along the outline, inside and out (its alpha is its strength); none when null.</summary>
    public Color? Glow { get; init; }

    public double GlowWidth { get; init; } = 2.5;

    public double GlowBlur { get; init; } = 2.5;

    /// <summary>Shadow thrown inwards from the edge (its alpha is its strength); none when null.</summary>
    public Color? Shade { get; init; }

    public double ShadeBlur { get; init; } = 12;

    /// <summary>Shadow the space casts outside itself (its alpha is its strength); none when null.</summary>
    public Color? Cast { get; init; }

    public Vector CastOffset { get; init; }

    public double CastBlur { get; init; } = 4;
}

/// <summary>
/// Base of the painted themes (Glory, Burn, Sunday, Parallax, Ocean, Void): Direct2D vectors at device
/// resolution in three layers. The backdrop (under) and the frame (over: shadows, glow, outline) are
/// painted once per shape; only the moving middle is drawn per frame, and only when it changed.
/// Everything inside is cut to the space's shape.
/// </summary>
/// <remarks>
/// Moving things are mostly sprites (small pictures painted once) and live in screen space, like the
/// Dreamy stars: every space is a window onto the same scene.
/// </remarks>
internal abstract class SceneLook : ISpaceLook
{
    private readonly List<Burst> _bursts = [];
    private readonly List<Mote> _motes = [];
    private Geometry _outline = Geometry.Empty;
    private Rect _bounds;
    private FillMode _fillMode = FillMode.Winding;
    private Vector2[][] _figures = [];
    private readonly List<CachedGradient> _gradients = [];
    private ID2D1PathGeometry? _shape;
    private ID2D1Bitmap1? _mask;
    private ID2D1Bitmap1? _outside; // the frame's pictures: kept, and painted over for each new shape
    private ID2D1Bitmap1? _cast;
    private ID2D1Bitmap1? _rim;
    private Shadow? _shadeEffect;
    private Shadow? _castEffect;
    private Shadow? _glowEffect;
    private ID2D1SolidColorBrush? _white;
    private ID2D1StrokeStyle? _roundStroke;
    private bool _stillsDirty = true;
    private bool _dirty = true;
    private bool _artShown = true; // whatever the previous theme left there
    private long _step = long.MinValue;
    private Vector _drawnLook = new(double.NaN, double.NaN);

    public bool HasBursts => _bursts.Count > 0;

    /// <summary>Screen rectangle (DIPs) the window covers; drawing coordinates start at its top-left.</summary>
    protected Rect World { get; private set; }

    /// <summary>The outline's bounds in window DIPs.</summary>
    protected Rect Area { get; private set; }

    /// <summary>Seconds since start.</summary>
    protected double Time { get; private set; }

    /// <summary>Smoothed cursor offset from the centre of the screen, in DIPs.</summary>
    protected Vector Look { get; private set; }

    /// <summary>Clicks still playing, oldest first.</summary>
    protected IReadOnlyList<Burst> Bursts => _bursts;

    /// <summary>How long a click reaction plays, in seconds.</summary>
    protected virtual double BurstLife => 1;

    /// <summary>Whether the theme has a moving layer at all (without one, it is only drawn for clicks).</summary>
    protected virtual bool HasMotionLayer => true;

    protected abstract FrameStyle Frame { get; }

    /// <summary>Whether the shape is a draft (it is changing every frame, see <see cref="ISpaceLook.SetShape(Geometry, Rect, bool)"/>).</summary>
    protected bool Draft { get; private set; }

    public void SetShape(Geometry outline, Rect world) => SetShape(outline, world, draft: false);

    public void SetShape(Geometry outline, Rect world, bool draft)
    {
        Draft = draft;
        _outline = outline;
        World = world;
        _bounds = outline.Bounds;
        Area = new Rect(_bounds.X - world.X, _bounds.Y - world.Y, _bounds.Width, _bounds.Height);
        var flattened = SpaceGeometry.Flat(outline);
        _fillMode = flattened.FillRule == FillRule.EvenOdd ? FillMode.Alternate : FillMode.Winding;
        _figures = [.. flattened.Figures.Select(f => Figure(f, world.TopLeft)).Where(f => f.Length >= 3)];
        _shape?.Dispose();
        _shape = null;
        _stillsDirty = true;
        _dirty = true;
    }

    public void Move(Geometry outline, Rect world)
    {
        // The stills are relative to the window and stay; the moving layer is in screen space.
        _outline = outline;
        World = world;
        _bounds = outline.Bounds;
        _dirty = true;
    }

    public bool Render(FrameState frame)
    {
        Time = frame.Time;
        Look = frame.Look;
        bool hadBursts = _bursts.Count > 0;
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            if (Time - _bursts[i].Start > BurstLife)
                _bursts.RemoveAt(i);
        }
        bool changed = Advance() | _dirty | hadBursts; // a burst that just ended still has to be wiped
        _dirty = false;
        return changed;
    }

    public void Present(ISpaceCanvas canvas)
    {
        float scale = (float)Dpi.Scale;
        int width = Math.Max(1, (int)Math.Round(World.Width * scale));
        int height = Math.Max(1, (int)Math.Round(World.Height * scale));

        if (_stillsDirty)
        {
            _stillsDirty = false;
            canvas.Repaint(ref _mask, width, height, scale, (context, factory) => context.FillGeometry(Shape(factory), White(context)));
            PrepareStills(canvas, scale);
            canvas.DrawBackdrop(width, height, scale, (context, factory) =>
            {
                DrawBackdrop(context, factory);
                Cut(context);
            });
            PaintFrame(canvas, width, height, scale);
        }

        if (HasMotionLayer || _bursts.Count > 0)
        {
            canvas.DrawVector(width, height, scale, (context, factory) =>
            {
                var origin = context.Transform;
                DrawMotion(context, factory, origin);
                context.Transform = origin;
                Cut(context);
            });
            _artShown = true;
        }
        else if (_artShown)
        {
            canvas.HideArt();
            _artShown = false;
        }
    }

    public bool IsInside(Point screen) => _bounds.Contains(screen) && _outline.FillContains(screen);

    public void AddBurst(Point screen) =>
        _bursts.Add(new Burst(new Point(screen.X - World.X, screen.Y - World.Y), Time, (uint)Random.Shared.Next()));

    public void Dispose()
    {
        DisposeResources();
        _shape?.Dispose();
        _mask?.Dispose();
        _outside?.Dispose();
        _cast?.Dispose();
        _rim?.Dispose();
        _shadeEffect?.Dispose();
        _castEffect?.Dispose();
        _glowEffect?.Dispose();
        _white?.Dispose();
        _roundStroke?.Dispose();
        foreach (var gradient in _gradients)
            gradient.Collection.Dispose();
        _gradients.Clear();
    }

    /// <summary>A white brush, made once.</summary>
    protected ID2D1SolidColorBrush White(ID2D1DeviceContext context) => _white ??= Solid(context, Colors.White);

    /// <summary>Moves on to this frame (<see cref="Time"/> and <see cref="Look"/> are set).</summary>
    /// <returns>Whether the moving layer looks different now.</returns>
    protected abstract bool Advance();

    /// <summary>Paints what the stills and the motion need for the current shape (sprites, textures).</summary>
    /// <remarks>Called before the stills are drawn, outside any draw callback, so it may use <see cref="ISpaceCanvas.Paint"/>.</remarks>
    protected virtual void PrepareStills(ISpaceCanvas canvas, float scale)
    {
    }

    /// <summary>The still picture under the motion, in window DIPs (cut to the shape afterwards).</summary>
    protected abstract void DrawBackdrop(ID2D1DeviceContext context, ID2D1Factory1 factory);

    /// <summary>The moving picture, in window DIPs (cut to the shape afterwards).</summary>
    /// <param name="origin">The window's transform: sprite transforms are applied on top of it.</param>
    protected abstract void DrawMotion(ID2D1DeviceContext context, ID2D1Factory1 factory, Matrix3x2 origin);

    protected virtual void DisposeResources()
    {
    }

    /// <summary>True once per step of an animation running at <paramref name="rate"/> frames per second.</summary>
    protected bool Stepped(double rate)
    {
        long step = (long)Math.Floor(Time * rate);
        if (step == _step)
            return false;
        _step = step;
        return true;
    }

    /// <summary>
    /// True when the cursor moved enough to shift the nearest parallax layer (<paramref name="parallax"/>
    /// DIPs per DIP) by a visible amount since the last frame this returned true.
    /// </summary>
    protected bool LookMoved(double parallax)
    {
        if (!double.IsNaN(_drawnLook.X) && (Look - _drawnLook).Length * parallax < 0.04)
            return false;
        _drawnLook = Look;
        return true;
    }

    /// <summary>The space's outline as a Direct2D geometry in window DIPs.</summary>
    protected ID2D1PathGeometry Shape(ID2D1Factory1 factory)
    {
        if (_shape is not null)
            return _shape;
        _shape = factory.CreatePathGeometry();
        using (var sink = _shape.Open())
        {
            sink.SetFillMode(_fillMode);
            foreach (var figure in _figures)
            {
                sink.BeginFigure(figure[0], FigureBegin.Filled);
                sink.AddLines(figure);
                sink.EndFigure(FigureEnd.Closed);
            }
            sink.Close();
        }
        return _shape;
    }

    /// <summary>Strokes with round joins and caps (a freehand outline has sharp turns).</summary>
    protected ID2D1StrokeStyle RoundStroke(ID2D1Factory1 factory) =>
        _roundStroke ??= factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            DashCap = CapStyle.Round,
            LineJoin = LineJoin.Round,
            MiterLimit = 1,
        });

    /// <summary>
    /// The particles of one layer of a screen-space field that are near this window, in window DIPs.
    /// The list is reused by the next call.
    /// </summary>
    /// <param name="layer">Tells layers of the same field apart.</param>
    /// <param name="margin">Also includes particles this far (DIPs) outside the window, for big sprites.</param>
    protected List<Mote> Scatter(Field field, int layer, double margin)
    {
        _motes.Clear();
        var offset = field.Drift * Time - Look * field.Parallax;
        double cell = field.Cell;
        int x0 = (int)Math.Floor((World.Left - margin - offset.X) / cell);
        int x1 = (int)Math.Floor((World.Right + margin - offset.X) / cell);
        int y0 = (int)Math.Floor((World.Top - margin - offset.Y) / cell);
        int y1 = (int)Math.Floor((World.Bottom + margin - offset.Y) / cell);
        uint threshold = (uint)(field.Density * 0x10000);
        for (int cy = y0; cy <= y1; cy++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                uint h = Hash(cx, cy, layer);
                if ((h & 0xFFFF) >= threshold)
                    continue;
                uint h2 = Mix(h);
                double x = (cx + ((h >> 16) & 0xFF) / 256.0) * cell + offset.X - World.Left;
                double y = (cy + (h2 & 0xFF) / 256.0) * cell + offset.Y - World.Top;
                _motes.Add(new Mote(new Point(x, y), Mix(h2 ^ 0x5bd1e995u)));
            }
        }
        return _motes;
    }

    /// <summary>A number in [0, 1] from eight bits of a seed.</summary>
    protected static double Unit(uint seed, int shift) => ((seed >> shift) & 0xFF) / 255.0;

    protected static uint Hash(int x, int y, int z)
    {
        unchecked
        {
            return Mix((uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu);
        }
    }

    protected static uint Mix(uint h)
    {
        unchecked
        {
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    protected static double Frac(double x) => x - Math.Floor(x);

    /// <summary>Turns a vector by an angle in radians (clockwise on screen, like Direct2D rotations).</summary>
    protected static Vector Rotate(Vector v, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vector(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    protected static double Smooth(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    protected static Color Rgb(uint rgb, double alpha = 1) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    protected static Color4 ToColor4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    protected static Vector2 V(Point p) => new((float)p.X, (float)p.Y);

    protected static RawRectF R(Rect r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);

    protected static ID2D1SolidColorBrush Solid(ID2D1DeviceContext context, Color color) => context.CreateSolidColorBrush(ToColor4(color));

    protected ID2D1LinearGradientBrush Linear(ID2D1DeviceContext context, Point from, Point to, params (float At, Color Color)[] stops) =>
        Linear(context, from, to, ExtendMode.Clamp, stops);

    protected ID2D1LinearGradientBrush Linear(ID2D1DeviceContext context, Point from, Point to, ExtendMode extend, params (float At, Color Color)[] stops) =>
        context.CreateLinearGradientBrush(new LinearGradientBrushProperties(V(from), V(to)), Gradient(context, extend, stops));

    protected ID2D1RadialGradientBrush Radial(ID2D1DeviceContext context, Point centre, double radiusX, double radiusY, params (float At, Color Color)[] stops) =>
        context.CreateRadialGradientBrush(new RadialGradientBrushProperties(V(centre), Vector2.Zero, (float)radiusX, (float)radiusY), Gradient(context, ExtendMode.Clamp, stops));

    /// <summary>
    /// The colours of a gradient on the GPU, made once per look: making them is the costly part of a
    /// gradient brush, and the stills are painted again for every new shape.
    /// </summary>
    private ID2D1GradientStopCollection Gradient(ID2D1DeviceContext context, ExtendMode extend, (float At, Color Color)[] stops)
    {
        foreach (var cached in _gradients)
        {
            if (cached.Matches(extend, stops))
                return cached.Collection;
        }
        var gradient = new GradientStop[stops.Length];
        for (int i = 0; i < stops.Length; i++)
            gradient[i] = new GradientStop(stops[i].At, ToColor4(stops[i].Color));
        var collection = context.CreateGradientStopCollection(gradient, extend);
        _gradients.Add(new CachedGradient(extend, [.. stops], collection));
        return collection;
    }

    /// <summary>A soft round light (a radial gradient filling a square this many DIPs wide), painted once.</summary>
    protected ID2D1Bitmap1 PaintGlow(ISpaceCanvas canvas, float scale, double diameter, params (float At, Color Color)[] stops)
    {
        int pixels = (int)Math.Ceiling(diameter * scale);
        double d = pixels / scale;
        return canvas.Paint(pixels, pixels, scale, (context, _) =>
        {
            using var brush = Radial(context, new Point(d / 2, d / 2), d / 2, d / 2, stops);
            context.FillRectangle(new RawRectF(0, 0, (float)d, (float)d), brush);
        });
    }

    /// <summary>Draws a sprite centred on a window DIP point, stretched to a size and turned by an angle (radians).</summary>
    protected static void DrawSprite(ID2D1DeviceContext context, Matrix3x2 origin, ID2D1Bitmap1 sprite, Point centre, double width, double height, double angle, double opacity)
    {
        if (opacity < 0.004 || width <= 0 || height <= 0)
            return;
        context.Transform = Matrix3x2.CreateRotation((float)angle) * Matrix3x2.CreateTranslation((float)centre.X, (float)centre.Y) * origin;
        float w = (float)(width / 2), h = (float)(height / 2);
        context.DrawBitmap(sprite, new RawRectF(-w, -h, w, h), (float)Math.Min(1, opacity), InterpolationMode.Linear, null, null);
    }

    /// <summary>Draws an effect's result, offset in DIPs.</summary>
    protected static void DrawEffect(ID2D1DeviceContext context, ID2D1Effect effect, Vector2 offset)
    {
        using var output = effect.Output;
        context.DrawImage(output, offset, null, InterpolationMode.Linear, CompositeMode.SourceOver);
    }

    /// <summary>A colour for effect properties (straight alpha).</summary>
    protected static Vector4 ToVector4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    /// <summary>A polyline (or a polygon, when closed) as a Direct2D geometry.</summary>
    protected static ID2D1PathGeometry Path(ID2D1Factory1 factory, IReadOnlyList<Vector2> points, bool closed)
    {
        var path = factory.CreatePathGeometry();
        using var sink = path.Open();
        sink.BeginFigure(points[0], closed ? FigureBegin.Filled : FigureBegin.Hollow);
        sink.AddLines([.. points.Skip(1)]);
        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return path;
    }

    private static Vector2[] Figure(PathFigure figure, Point origin)
    {
        var points = new List<Vector2> { V(figure.StartPoint - (Vector)origin) };
        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment line)
                points.Add(V(line.Point - (Vector)origin));
            else if (segment is PolyLineSegment poly)
                foreach (var p in poly.Points)
                    points.Add(V(p - (Vector)origin));
        }
        return [.. points];
    }

    /// <summary>Keeps only what lies on the space (the context must be at the window's transform).</summary>
    private void Cut(ID2D1DeviceContext context) =>
        context.DrawImage(_mask!, InterpolationMode.NearestNeighbor, CompositeMode.DestinationIn);

    /// <summary>Paints the still frame: inner shade, cast shadow, glow, then the outline on top.</summary>
    private void PaintFrame(ISpaceCanvas canvas, int width, int height, float scale)
    {
        // The pictures and effects are kept from shape to shape (a space being reshaped gets a new
        // shape every frame): they are painted over, not made anew.
        var style = Frame;
        float pad = 0;
        if (style.Shade is not null)
        {
            // Everything but the space, with room around it so the blur doesn't fade at the window's edges.
            int padPixels = (int)Math.Ceiling(style.ShadeBlur * 3 * scale);
            pad = padPixels / scale;
            float w = (width + 2 * padPixels) / scale, h = (height + 2 * padPixels) / scale;
            canvas.Repaint(ref _outside, width + 2 * padPixels, height + 2 * padPixels, scale, (context, _) =>
            {
                context.FillRectangle(new RawRectF(0, 0, w, h), White(context));
                context.DrawImage(_mask!, new Vector2(pad, pad), null, InterpolationMode.NearestNeighbor, CompositeMode.DestinationOut);
            });
        }
        if (style.Cast is { } castColor)
        {
            canvas.Repaint(ref _cast, width, height, scale, (context, _) =>
            {
                _castEffect ??= new Shadow(context);
                _castEffect.BlurStandardDeviation = (float)style.CastBlur;
                _castEffect.Color = ToVector4(castColor);
                _castEffect.SetInput(0, _mask!, true);
                DrawEffect(context, _castEffect, new Vector2((float)style.CastOffset.X, (float)style.CastOffset.Y));
                context.DrawImage(_mask!, InterpolationMode.NearestNeighbor, CompositeMode.DestinationOut);
            });
        }
        if (style.Glow is not null)
        {
            canvas.Repaint(ref _rim, width, height, scale, (context, factory) =>
                context.DrawGeometry(Shape(factory), White(context), (float)style.GlowWidth, RoundStroke(factory)));
        }

        canvas.DrawFrame(width, height, scale, (context, factory) =>
        {
            if (style.Shade is { } shadeColor)
            {
                _shadeEffect ??= new Shadow(context);
                _shadeEffect.BlurStandardDeviation = (float)style.ShadeBlur;
                _shadeEffect.Color = ToVector4(shadeColor);
                _shadeEffect.SetInput(0, _outside!, true);
                DrawEffect(context, _shadeEffect, new Vector2(-pad, -pad));
                Cut(context); // the shade only falls inside
            }
            if (style.Cast is not null)
                context.DrawImage(_cast!, InterpolationMode.NearestNeighbor, CompositeMode.SourceOver);
            if (style.Glow is { } glowColor)
            {
                _glowEffect ??= new Shadow(context);
                _glowEffect.BlurStandardDeviation = (float)style.GlowBlur;
                _glowEffect.Color = ToVector4(glowColor);
                _glowEffect.SetInput(0, _rim!, true);
                DrawEffect(context, _glowEffect, Vector2.Zero);
            }
            using var line = Solid(context, style.Line);
            context.DrawGeometry(Shape(factory), line, (float)style.LineWidth, RoundStroke(factory));
        });
    }

    /// <summary>A gradient's colours on the GPU, and what they were made from.</summary>
    private sealed record CachedGradient(ExtendMode Extend, (float At, Color Color)[] Stops, ID2D1GradientStopCollection Collection)
    {
        public bool Matches(ExtendMode extend, (float At, Color Color)[] stops) =>
            extend == Extend && stops.AsSpan().SequenceEqual(Stops);
    }

    /// <summary>One layer of particles in screen space, shared by every space.</summary>
    /// <param name="Cell">Particles sit on a jittered grid of this pitch (DIPs).</param>
    /// <param name="Density">Chance that a grid cell holds one.</param>
    /// <param name="Drift">How the layer moves, in DIPs per second.</param>
    /// <param name="Parallax">How far it shifts against the cursor, in DIPs per DIP of cursor offset.</param>
    protected readonly record struct Field(double Cell, double Density, Vector Drift, double Parallax);

    /// <summary>A particle of a <see cref="Field"/>: where it is this frame (window DIPs), and a random seed of its own.</summary>
    protected readonly record struct Mote(Point At, uint Seed);

    /// <summary>A click: where (window DIPs), when, and a random seed for its shape.</summary>
    protected sealed record Burst(Point Origin, double Start, uint Seed);
}
