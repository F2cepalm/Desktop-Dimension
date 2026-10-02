using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Color4 = Vortice.Mathematics.Color4;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Rendering;

/// <summary>
/// The Claudish theme: a flat clay-orange shape with a bold black ink outline that swells and thins
/// like a brush stroke and "boils" (re-draws itself slightly) a few times a second, as hand-drawn
/// animation does. Clicks splash ink. Drawn as anti-aliased vectors with Direct2D at device resolution.
/// </summary>
internal sealed class InkyLook : ISpaceLook
{
    /// <summary>Outline sample spacing in DIPs (rendering resolution only).</summary>
    private const double SampleSpacing = 3;
    private const double SplashLife = 0.9;
    private const int SplashDrops = 12;

    /// <summary>
    /// How many drawings the line boils through before it comes round again (as a hand-drawn "boil"
    /// cycles through a few drawings). Each is tessellated once per shape and then only redrawn.
    /// </summary>
    private const int BoilDrawings = 8;

    private readonly BoilDrawing?[] _drawings = new BoilDrawing?[BoilDrawings];
    private ID2D1DeviceContext1? _context1;
    private IntPtr _context1Of;

    private readonly List<Splash> _splashes = [];
    private Geometry _outline = Geometry.Empty;
    private Rect _world;
    private Rect _bounds;
    private FillMode _fillMode = FillMode.Winding;
    private Contour[] _contours = [];
    private long _boil = long.MinValue;
    private double _time;
    private bool _dirty = true;
    private ID2D1SolidColorBrush? _clayBrush;
    private ID2D1SolidColorBrush? _inkBrush;

    public bool HasBursts => _splashes.Count > 0;

    public void SetShape(Geometry outline, Rect world)
    {
        _outline = outline;
        _world = world;
        _bounds = outline.Bounds;
        var flattened = SpaceGeometry.Flat(outline);
        _fillMode = flattened.FillRule == FillRule.EvenOdd ? FillMode.Alternate : FillMode.Winding;
        var offset = new Vector(-world.X, -world.Y);
        _contours = [.. flattened.Figures.Select(f => Contour.Build(f, offset)).OfType<Contour>()];
        ForgetDrawings();
        _dirty = true;
    }

    public void Move(Geometry outline, Rect world)
    {
        // The contours are relative to the window, so only what refers to the screen changes.
        _outline = outline;
        _world = world;
        _bounds = outline.Bounds;
    }

    public bool Render(FrameState frame)
    {
        _time = frame.Time;
        for (int i = _splashes.Count - 1; i >= 0; i--)
        {
            if (_time - _splashes[i].Start > SplashLife)
                _splashes.RemoveAt(i);
        }
        long boil = (long)Math.Floor(frame.Time * ClaudishStyle.BoilRate);
        bool changed = _dirty || boil != _boil || _splashes.Count > 0;
        _boil = boil;
        _dirty = false;
        return changed;
    }

    public void Present(ISpaceCanvas canvas)
    {
        double scale = Dpi.Scale;
        int width = Math.Max(1, (int)Math.Round(_world.Width * scale));
        int height = Math.Max(1, (int)Math.Round(_world.Height * scale));
        canvas.DrawVector(width, height, (float)scale, Draw);
    }

    public bool IsInside(Point screen) => _bounds.Contains(screen) && _outline.FillContains(screen);

    public void AddBurst(Point screen)
    {
        var rng = Random.Shared;
        var drops = new Drop[SplashDrops];
        for (int i = 0; i < drops.Length; i++)
        {
            double angle = rng.NextDouble() * Math.Tau;
            drops[i] = new Drop(
                new Vector(Math.Cos(angle), Math.Sin(angle)),
                Speed: 60 + rng.NextDouble() * 110,
                Radius: 1.8 + rng.NextDouble() * 2.6);
        }
        _splashes.Add(new Splash(new Point(screen.X - _world.X, screen.Y - _world.Y), _time, drops));
    }

    /// <summary>Draws in DIPs relative to the window's top-left (the canvas places it and runs at the device DPI).</summary>
    private void Draw(ID2D1DeviceContext context, ID2D1Factory1 factory)
    {
        _clayBrush ??= context.CreateSolidColorBrush(ToColor4(ClaudishStyle.Clay));
        _inkBrush ??= context.CreateSolidColorBrush(ToColor4(ClaudishStyle.Ink));
        int drawing = (int)(((_boil % BoilDrawings) + BoilDrawings) % BoilDrawings);
        var boil = new BoilFrame(drawing);

        if (_splashes.Count == 0 && Context1(context) is { } context1)
        {
            // At rest the line only boils: its drawings are tessellated once and redrawn from then on.
            var cached = _drawings[drawing] ??= Realize(context1, factory, boil);
            context1.DrawGeometryRealization(cached.Body, _clayBrush);
            context1.DrawGeometryRealization(cached.Rim, _inkBrush);
            return;
        }

        var (body, rim) = Build(factory, boil, _splashes);
        using (body)
        using (rim)
        {
            context.FillGeometry(body, _clayBrush);
            context.FillGeometry(rim, _inkBrush);
        }

        foreach (var splash in _splashes)
        {
            double age = _time - splash.Start;
            double travel = (1 - Math.Exp(-age * 3)) / 3;
            double shrink = Math.Max(0, 1 - age / SplashLife);
            foreach (var drop in splash.Drops)
            {
                var p = splash.Origin + drop.Direction * (drop.Speed * travel);
                float r = (float)(drop.Radius * shrink);
                if (r > 0.2f)
                    context.FillEllipse(new Ellipse(new Vector2((float)p.X, (float)p.Y), r, r), _inkBrush);
            }
        }
    }

    /// <summary>The fill (along the line's centre) and the ribbon of ink, for one boil drawing and the splashes rippling it.</summary>
    private (ID2D1PathGeometry Body, ID2D1PathGeometry Rim) Build(ID2D1Factory1 factory, BoilFrame boil, List<Splash> splashes)
    {
        var body = factory.CreatePathGeometry();
        var rim = factory.CreatePathGeometry();
        using (var bodySink = body.Open())
        using (var rimSink = rim.Open())
        {
            bodySink.SetFillMode(_fillMode);
            rimSink.SetFillMode(FillMode.Winding); // overlaps at tight corners stay inked
            foreach (var contour in _contours)
                contour.Emit(bodySink, rimSink, boil, _time, splashes);
            bodySink.Close();
            rimSink.Close();
        }
        return (body, rim);
    }

    /// <summary>One boil drawing, tessellated at the device's resolution.</summary>
    private BoilDrawing Realize(ID2D1DeviceContext1 context, ID2D1Factory1 factory, BoilFrame boil)
    {
        var (body, rim) = Build(factory, boil, []);
        using (body)
        using (rim)
        {
            float tolerance = 0.25f / (float)Dpi.Scale; // Direct2D's own: a quarter of a device pixel
            return new BoilDrawing(context.CreateFilledGeometryRealization(body, tolerance), context.CreateFilledGeometryRealization(rim, tolerance));
        }
    }

    private ID2D1DeviceContext1? Context1(ID2D1DeviceContext context)
    {
        if (_context1Of != context.NativePointer)
        {
            _context1?.Dispose();
            _context1 = context.QueryInterfaceOrNull<ID2D1DeviceContext1>();
            _context1Of = context.NativePointer;
            ForgetDrawings(); // made for another device
        }
        return _context1;
    }

    private void ForgetDrawings()
    {
        for (int i = 0; i < _drawings.Length; i++)
        {
            _drawings[i]?.Body.Dispose();
            _drawings[i]?.Rim.Dispose();
            _drawings[i] = null;
        }
    }

    public void Dispose()
    {
        ForgetDrawings();
        _context1?.Dispose();
        _clayBrush?.Dispose();
        _inkBrush?.Dispose();
    }

    /// <summary>A boil drawing, ready to draw again and again.</summary>
    private sealed record BoilDrawing(ID2D1GeometryRealization Body, ID2D1GeometryRealization Rim);

    private static Color4 ToColor4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    /// <summary>
    /// Ripple from a click, in DIPs along the normal (same wave as the Dreamy rim).
    /// </summary>
    private static double Ripple(Point p, double time, List<Splash> splashes)
    {
        double w = 0;
        foreach (var splash in splashes)
        {
            double age = time - splash.Start;
            double d = (p - splash.Origin).Length;
            w += 5 * Math.Exp(-age * 3) * Math.Exp(-d / 140) * Math.Sin(d * 0.09 - age * 16);
        }
        return w;
    }

    private readonly record struct Drop(Vector Direction, double Speed, double Radius);

    private sealed record Splash(Point Origin, double Start, Drop[] Drops);

    /// <summary>The random phases of one boil frame: the same for the whole frame, new on the next.</summary>
    private readonly struct BoilFrame
    {
        public BoilFrame(long index)
        {
            ulong h = unchecked((ulong)index * 0x9E3779B97F4A7C15UL);
            h ^= h >> 29;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 32;
            Shift1 = (h & 0xFFFF) / 65536.0 * Math.Tau;
            Shift2 = ((h >> 16) & 0xFFFF) / 65536.0 * Math.Tau;
            Width = ((h >> 32) & 0xFFFF) / 65536.0 * Math.Tau;
        }

        public double Shift1 { get; }

        public double Shift2 { get; }

        public double Width { get; }
    }

    /// <summary>A closed outline in window DIPs, resampled evenly, with normals and a fixed brush-width pattern.</summary>
    private sealed class Contour
    {
        private readonly Vector2[] _center;
        private readonly Vector2[] _outer;
        private readonly Vector2[] _inner;

        private Contour(Point[] points, Vector[] normals, double perimeter)
        {
            Points = points;
            Normals = normals;
            int n = points.Length;
            _center = new Vector2[n];
            _outer = new Vector2[n];
            _inner = new Vector2[n];

            // Whole numbers of waves around the loop, so nothing jumps where it closes.
            double Rate(double wavelength) => Math.Tau * Math.Max(1, Math.Round(perimeter / wavelength)) / n;
            ShiftRate1 = Rate(60);
            ShiftRate2 = Rate(23);
            WidthRate1 = Rate(140);
            WidthRate2 = Rate(45);
            WidthPhase1 = perimeter * 0.618 % Math.Tau;
            WidthPhase2 = perimeter * 0.377 % Math.Tau;
        }

        private Point[] Points { get; }

        private Vector[] Normals { get; }

        private double ShiftRate1 { get; }

        private double ShiftRate2 { get; }

        private double WidthRate1 { get; }

        private double WidthRate2 { get; }

        private double WidthPhase1 { get; }

        private double WidthPhase2 { get; }

        public static Contour? Build(PathFigure figure, Vector offset)
        {
            var raw = new List<Point> { figure.StartPoint + offset };
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                    raw.Add(line.Point + offset);
                else if (segment is PolyLineSegment poly)
                    foreach (var p in poly.Points)
                        raw.Add(p + offset);
            }
            if (raw.Count > 1 && (raw[^1] - raw[0]).Length < 1e-3)
                raw.RemoveAt(raw.Count - 1);
            if (raw.Count < 3)
                return null;

            int n = raw.Count;
            double perimeter = 0;
            for (int i = 0; i < n; i++)
                perimeter += (raw[(i + 1) % n] - raw[i]).Length;
            if (perimeter < 1)
                return null;

            int count = Math.Max(12, (int)Math.Round(perimeter / SampleSpacing));
            double step = perimeter / count;
            var points = new Point[count];
            int seg = 0;
            double segStart = 0;
            double segLength = (raw[1] - raw[0]).Length;
            for (int k = 0; k < count; k++)
            {
                double target = k * step;
                while (segStart + segLength < target && seg < n - 1)
                {
                    segStart += segLength;
                    seg++;
                    segLength = (raw[(seg + 1) % n] - raw[seg]).Length;
                }
                var a = raw[seg];
                var b = raw[(seg + 1) % n];
                double u = segLength > 1e-9 ? Math.Clamp((target - segStart) / segLength, 0, 1) : 0;
                points[k] = a + (b - a) * u;
            }

            var normals = new Vector[count];
            const int span = 2;
            for (int k = 0; k < count; k++)
            {
                var tangent = points[(k + span) % count] - points[(k - span + count) % count];
                if (tangent.LengthSquared < 1e-12)
                    tangent = new Vector(1, 0);
                tangent.Normalize();
                normals[k] = new Vector(-tangent.Y, tangent.X);
            }
            return new Contour(points, normals, perimeter);
        }

        /// <summary>Adds this outline to the fill (its centre line) and to the ink (a ribbon around it).</summary>
        public void Emit(ID2D1GeometrySink body, ID2D1GeometrySink rim, BoilFrame boil, double time, List<Splash> splashes)
        {
            int n = Points.Length;
            for (int i = 0; i < n; i++)
            {
                var p = Points[i];
                var normal = Normals[i];
                double shift = ClaudishStyle.BoilAmplitude * (
                        0.6 * Math.Sin(i * ShiftRate1 + boil.Shift1) +
                        0.4 * Math.Sin(i * ShiftRate2 + boil.Shift2))
                    + Ripple(p, time, splashes);
                double width = ClaudishStyle.StrokeWidth
                    * (1 + 0.35 * Math.Sin(i * WidthRate1 + WidthPhase1) + 0.15 * Math.Sin(i * WidthRate2 + WidthPhase2))
                    * (1 + 0.08 * Math.Sin(i * ShiftRate1 + boil.Width));

                var c = p + normal * shift;
                var half = normal * (width / 2);
                _center[i] = new Vector2((float)c.X, (float)c.Y);
                _outer[i] = new Vector2((float)(c.X + half.X), (float)(c.Y + half.Y));
                _inner[n - 1 - i] = new Vector2((float)(c.X - half.X), (float)(c.Y - half.Y));
            }

            AddFigure(body, _center);
            // Outer edge one way, inner edge the other: a closed ribbon of ink.
            AddFigure(rim, _outer);
            AddFigure(rim, _inner);
        }

        private static void AddFigure(ID2D1GeometrySink sink, Vector2[] points)
        {
            sink.BeginFigure(points[0], FigureBegin.Filled);
            sink.AddLines(points);
            sink.EndFigure(FigureEnd.Closed);
        }
    }
}
