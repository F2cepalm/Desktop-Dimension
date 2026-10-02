using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Rendering;

/// <summary>
/// Software renderer of one space as pixel art: one buffer element per art pixel
/// (<see cref="DreamStyle.ArtPixel"/> DIPs), premultiplied BGRA, transparent outside the space.
/// </summary>
/// <remarks>
/// Drawing the effect ourselves at art resolution is far cheaper than asking WPF to re-tessellate a
/// wobbling vector outline every frame, and it gives the crisp look of the original.
/// </remarks>
internal sealed class DreamRaster
{
    private const double BurstLife = 1.2;
    private const int BurstSparks = 14;

    private static readonly int VoidPixel = Pack(DreamStyle.Void);
    private static readonly int RimPixel = Pack(DreamStyle.Rim);
    private static readonly int[][] LayerPixels = [.. DreamStyle.Layers.Select(l => l.Colors.Select(Pack).ToArray())];
    private static readonly int[] SparkPixels = [.. LayerPixels.SelectMany(p => p)];

    private Rect _world;
    private readonly List<Contour> _contours;
    private readonly Edge[] _edges;
    private readonly Crossing[] _crossings;
    private readonly List<int> _active = [];
    private readonly List<Burst> _bursts = [];
    private int[] _rowStart = [];   // per row, where its edges start in _sorted (see FillVoid)
    private int[] _placed = [];
    private Edge[] _sorted = [];
    private int[] _pixels;
    private int[] _next;
    private double _time;

    /// <param name="outline">Outline in screen DIPs.</param>
    /// <param name="world">Screen rectangle (DIPs) covered by the raster, aligned to the art-pixel grid.</param>
    /// <param name="recycle">
    /// The raster this one replaces (a space being reshaped or a block being placed gets a new one
    /// every frame): its buffers and outlines are taken over, and reused where big enough. It must not
    /// be used afterwards.
    /// </param>
    public DreamRaster(Geometry outline, Rect world, DreamRaster? recycle = null)
    {
        _world = world;
        Width = Math.Max(1, (int)Math.Round(world.Width / DreamStyle.ArtPixel));
        Height = Math.Max(1, (int)Math.Round(world.Height / DreamStyle.ArtPixel));
        _pixels = Buffer(recycle?._pixels, Width * Height);
        _next = Buffer(recycle?._next, Width * Height);
        if (recycle is not null)
            (_rowStart, _placed, _sorted) = (recycle._rowStart, recycle._placed, recycle._sorted);

        double scale = 1 / DreamStyle.ArtPixel;
        var toArt = new Matrix(scale, 0, 0, scale, -world.X * scale, -world.Y * scale);
        var flattened = SpaceGeometry.Flat(outline);
        _contours = recycle?._contours ?? [];
        int built = 0, edgeCount = 0;
        foreach (var figure in flattened.Figures)
        {
            if (Contour.Build(figure, toArt, built < _contours.Count ? _contours[built] : null) is not { } contour)
                continue;
            if (built < _contours.Count)
                _contours[built] = contour;
            else
                _contours.Add(contour);
            built++;
            edgeCount += contour.Count;
        }
        _contours.RemoveRange(built, _contours.Count - built);

        _edges = recycle?._edges is { } edges && edges.Length >= edgeCount ? edges : new Edge[edgeCount + edgeCount / 2];
        _crossings = recycle?._crossings is { } crossings && crossings.Length >= edgeCount ? crossings : new Crossing[edgeCount + edgeCount / 2];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The last rendered frame: premultiplied BGRA, row-major, <see cref="Width"/> per row (it may run on past the last row).</summary>
    public int[] Pixels => _pixels;

    /// <summary>An old buffer, cleared, if it holds <paramref name="count"/> pixels; else a new one (with room to grow, when replacing a smaller one).</summary>
    private static int[] Buffer(int[]? old, int count)
    {
        if (old is null)
            return new int[count];
        if (old.Length < count)
            return new int[count + count / 2];
        Array.Clear(old, 0, count);
        return old;
    }

    /// <summary>Whether a click burst is still playing.</summary>
    public bool HasBursts => _bursts.Count > 0;

    /// <summary>The same shape, now over another screen rectangle of the same size (the space was moved).</summary>
    public void Move(Rect world) => _world = world;

    /// <summary>Whether an art-pixel position is part of the space in the last frame.</summary>
    public bool IsInside(Point art)
    {
        int x = (int)Math.Floor(art.X), y = (int)Math.Floor(art.Y);
        return (uint)x < (uint)Width && (uint)y < (uint)Height && _pixels[y * Width + x] != 0;
    }

    /// <summary>A splash of sparks and a ripple through the rim, at an art-pixel position.</summary>
    public void AddBurst(Point art)
    {
        var rng = Random.Shared;
        var sparks = new Spark[BurstSparks];
        for (int i = 0; i < sparks.Length; i++)
        {
            double angle = rng.NextDouble() * Math.Tau;
            sparks[i] = new Spark(new Vector(Math.Cos(angle), Math.Sin(angle)), 30 + rng.NextDouble() * 70, SparkPixels[rng.Next(SparkPixels.Length)]);
        }
        _bursts.Add(new Burst(art, _time, sparks));
    }

    /// <returns>Whether any pixel changed since the previous frame.</returns>
    public bool Render(FrameState frame)
    {
        _time = frame.Time;
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            if (_time - _bursts[i].Start > BurstLife)
                _bursts.RemoveAt(i);
        }

        foreach (var contour in _contours)
            Wobble(contour);

        int count = Width * Height; // the buffers may be longer (see the constructor)
        Array.Clear(_next, 0, count);
        FillVoid();
        DrawStars(frame);
        DrawSparks();
        DrawRim();

        if (_next.AsSpan(0, count).SequenceEqual(_pixels.AsSpan(0, count)))
            return false;
        (_pixels, _next) = (_next, _pixels);
        return true;
    }

    /// <summary>Displaces each rim sample along its normal: two travelling waves plus ripples from clicks.</summary>
    private void Wobble(Contour contour)
    {
        double t = _time;
        for (int i = 0; i < contour.Count; i++)
        {
            var p = contour.Points[i];
            double w = DreamStyle.WobbleAmplitude * (
                0.65 * Math.Sin(i * contour.Rate1 + t * 2.4 + contour.Phase) +
                0.35 * Math.Sin(i * contour.Rate2 - t * 1.6 + contour.Phase * 1.7));

            foreach (var burst in _bursts)
            {
                double age = t - burst.Start;
                double d = (p - burst.Origin).Length;
                w += DreamStyle.RippleAmplitude * Math.Exp(-age * 3) * Math.Exp(-d / 70) * Math.Sin(d * 0.18 - age * 16);
            }

            var n = contour.Normals[i];
            contour.X[i] = p.X + n.X * w;
            contour.Y[i] = p.Y + n.Y * w;
        }
    }

    /// <summary>Scanline fill of the wobbled outline (non-zero winding, pixel centres).</summary>
    private void FillVoid()
    {
        // Edges go into buckets by the first row whose centre they reach (a counting sort: no
        // comparisons, and the buckets are all the row loop needs).
        if (_rowStart.Length < Height + 2)
            _rowStart = new int[Height + 2];
        var rowStart = _rowStart.AsSpan(0, Height + 2);
        rowStart.Clear();
        int count = 0;
        foreach (var contour in _contours)
        {
            var xs = contour.X;
            var ys = contour.Y;
            for (int i = 0; i < contour.Count; i++)
            {
                int j = i + 1 == contour.Count ? 0 : i + 1;
                double y0 = ys[i], y1 = ys[j];
                if (y0 == y1)
                    continue;
                var edge = y0 < y1
                    ? new Edge(y0, y1, xs[i], (xs[j] - xs[i]) / (y1 - y0), 1)
                    : new Edge(y1, y0, xs[j], (xs[i] - xs[j]) / (y0 - y1), -1);
                _edges[count++] = edge;
                rowStart[FirstRow(edge.YMin) + 1]++;
            }
        }
        for (int row = 1; row < rowStart.Length; row++)
            rowStart[row] += rowStart[row - 1];
        if (_sorted.Length < count)
            _sorted = new Edge[_edges.Length];
        if (_placed.Length < rowStart.Length)
            _placed = new int[rowStart.Length];
        rowStart.CopyTo(_placed);
        for (int i = 0; i < count; i++)
            _sorted[_placed[FirstRow(_edges[i].YMin)]++] = _edges[i];

        _active.Clear();
        for (int row = 0; row < Height; row++)
        {
            double yc = row + 0.5;
            for (int k = rowStart[row]; k < rowStart[row + 1]; k++)
                _active.Add(k);

            int crossings = 0;
            for (int k = _active.Count - 1; k >= 0; k--)
            {
                ref readonly var edge = ref _sorted[_active[k]];
                if (edge.YMax <= yc)
                {
                    _active[k] = _active[^1];
                    _active.RemoveAt(_active.Count - 1);
                    continue;
                }

                // Insertion sort by x; rows cross only a handful of edges.
                var crossing = new Crossing(edge.XAtYMin + (yc - edge.YMin) * edge.Slope, edge.Winding);
                int at = crossings++;
                while (at > 0 && _crossings[at - 1].X > crossing.X)
                {
                    _crossings[at] = _crossings[at - 1];
                    at--;
                }
                _crossings[at] = crossing;
            }

            int winding = 0;
            int rowOffset = row * Width;
            for (int k = 0; k < crossings - 1; k++)
            {
                winding += _crossings[k].Winding;
                if (winding == 0)
                    continue;
                int x0 = Math.Max(0, (int)Math.Ceiling(_crossings[k].X - 0.5));
                int x1 = Math.Min(Width, (int)Math.Ceiling(_crossings[k + 1].X - 0.5));
                if (x1 > x0)
                    _next.AsSpan(rowOffset + x0, x1 - x0).Fill(VoidPixel);
            }
        }
    }

    /// <summary>The first row whose pixel centre an edge starting at <paramref name="yMin"/> reaches (Height when none).</summary>
    private int FirstRow(double yMin) => (int)Math.Clamp(Math.Ceiling(yMin - 0.5), 0, Height);

    /// <summary>The shared star field, seen through this space; stars only land on the void.</summary>
    private void DrawStars(FrameState frame)
    {
        for (int li = 0; li < DreamStyle.Layers.Length; li++)
        {
            var layer = DreamStyle.Layers[li];
            var palette = LayerPixels[li];
            var offset = frame.LayerOffset(layer);
            double cell = layer.CellSize;
            int x0 = (int)Math.Floor((_world.Left - offset.X) / cell) - 1;
            int x1 = (int)Math.Floor((_world.Right - offset.X) / cell) + 1;
            int y0 = (int)Math.Floor((_world.Top - offset.Y) / cell) - 1;
            int y1 = (int)Math.Floor((_world.Bottom - offset.Y) / cell) + 1;
            uint threshold = (uint)(layer.Density * 0x10000);

            for (int cy = y0; cy <= y1; cy++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    uint h = Hash(cx, cy, li);
                    if ((h & 0xFFFF) >= threshold)
                        continue;
                    uint h2 = Mix(h);

                    double xDip = cx * cell + ((h >> 16) & 0xFF) / 256.0 * cell + offset.X - _world.Left;
                    double yDip = cy * cell + (h2 & 0xFF) / 256.0 * cell + offset.Y - _world.Top;
                    int x = (int)Math.Floor(xDip / DreamStyle.ArtPixel);
                    int y = (int)Math.Floor(yDip / DreamStyle.ArtPixel);
                    if (x < -3 || y < -3 || x > Width + 3 || y > Height + 3)
                        continue;

                    int colour = palette[(int)((h2 >> 8) % (uint)palette.Length)];
                    double phase = ((h2 >> 16) & 0xFF) / 64.0;
                    double speed = layer.TwinkleSpeed * (0.6 + ((h2 >> 24) & 0xFF) / 320.0);
                    int step = (int)(frame.Time * speed + phase) & 3;
                    DrawSprite(x, y, layer.Frames[step], colour);
                }
            }
        }
    }

    private void DrawSparks()
    {
        foreach (var burst in _bursts)
        {
            double age = _time - burst.Start;
            double travel = (1 - Math.Exp(-age * 3)) / 3;
            int frame = Math.Max(0, 3 - (int)(age / BurstLife * 4));
            foreach (var spark in burst.Sparks)
            {
                var p = burst.Origin + spark.Direction * (spark.Speed * travel);
                DrawSprite((int)Math.Floor(p.X), (int)Math.Floor(p.Y), frame, spark.Pixel);
            }
        }
    }

    /// <summary>Sprite frames: 0 dot, 1 small plus, 2 large plus, 3 large plus with a solid core.</summary>
    private void DrawSprite(int x, int y, int frame, int colour)
    {
        PlotOnVoid(x, y, colour);
        if (frame == 0)
            return;
        PlotOnVoid(x - 1, y, colour);
        PlotOnVoid(x + 1, y, colour);
        PlotOnVoid(x, y - 1, colour);
        PlotOnVoid(x, y + 1, colour);
        if (frame == 1)
            return;
        PlotOnVoid(x - 2, y, colour);
        PlotOnVoid(x + 2, y, colour);
        PlotOnVoid(x, y - 2, colour);
        PlotOnVoid(x, y + 2, colour);
        if (frame == 2)
            return;
        PlotOnVoid(x - 1, y - 1, colour);
        PlotOnVoid(x + 1, y - 1, colour);
        PlotOnVoid(x - 1, y + 1, colour);
        PlotOnVoid(x + 1, y + 1, colour);
    }

    private void PlotOnVoid(int x, int y, int colour)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;
        ref int pixel = ref _next[y * Width + x];
        if (pixel != 0)
            pixel = colour;
    }

    /// <summary>A one-art-pixel line through the wobbled outline.</summary>
    private void DrawRim()
    {
        foreach (var contour in _contours)
        {
            int n = contour.Count;
            int px = (int)Math.Floor(contour.X[n - 1]), py = (int)Math.Floor(contour.Y[n - 1]);
            for (int i = 0; i < n; i++)
            {
                int x = (int)Math.Floor(contour.X[i]), y = (int)Math.Floor(contour.Y[i]);
                Line(px, py, x, y);
                px = x;
                py = y;
            }
        }
    }

    private void Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)Width && (uint)y0 < (uint)Height)
                _next[y0 * Width + x0] = RimPixel;
            if (x0 == x1 && y0 == y1)
                return;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static int Pack(Color c)
    {
        // Premultiplied BGRA as a little-endian int.
        int a = c.A;
        int r = c.R * a / 255, g = c.G * a / 255, b = c.B * a / 255;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static uint Hash(int x, int y, int z)
    {
        unchecked
        {
            return Mix((uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu);
        }
    }

    private static uint Mix(uint h)
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

    private readonly record struct Edge(double YMin, double YMax, double XAtYMin, double Slope, int Winding);

    private readonly record struct Crossing(double X, int Winding);

    private readonly record struct Spark(Vector Direction, double Speed, int Pixel);

    private sealed record Burst(Point Origin, double Start, Spark[] Sparks);

    /// <summary>
    /// A closed outline in art pixels, resampled at even spacing, with normals for the wobble. Its
    /// arrays may be longer than it (<see cref="Count"/>): a reshaped space's outline is built again
    /// every frame, into the arrays of the one before when they are big enough.
    /// </summary>
    private sealed class Contour
    {
        private const double Spacing = 1.0;

        /// <summary>The figure's corners in art pixels, while building (the UI thread's, reused).</summary>
        [ThreadStatic]
        private static List<Point>? t_raw;

        public Point[] Points { get; private set; } = [];

        public Vector[] Normals { get; private set; } = [];

        /// <summary>How many samples it has (its arrays may be longer).</summary>
        public int Count { get; private set; }

        /// <summary>Wave phase advance per sample; a whole number of waves fits the loop, so the rim has no seam.</summary>
        public double Rate1 { get; private set; }

        public double Rate2 { get; private set; }

        public double Phase { get; private set; }

        /// <summary>This frame's wobbled positions.</summary>
        public double[] X { get; private set; } = [];

        public double[] Y { get; private set; } = [];

        /// <param name="reuse">A contour of the raster being replaced, to build into (its arrays are reused if big enough).</param>
        /// <returns>null for a figure too small to have an outline.</returns>
        public static Contour? Build(PathFigure figure, Matrix toArt, Contour? reuse)
        {
            var raw = t_raw ??= [];
            raw.Clear();
            raw.Add(toArt.Transform(figure.StartPoint));
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                    raw.Add(toArt.Transform(line.Point));
                else if (segment is PolyLineSegment poly)
                    foreach (var p in poly.Points)
                        raw.Add(toArt.Transform(p));
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

            int count = Math.Max(8, (int)Math.Round(perimeter / Spacing));
            var contour = reuse ?? new Contour();
            contour.Reserve(count);
            var points = contour.Points;
            double step = perimeter / count;
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

            var normals = contour.Normals;
            const int span = 3;
            for (int k = 0; k < count; k++)
            {
                var tangent = points[(k + span) % count] - points[(k - span + count) % count];
                if (tangent.LengthSquared < 1e-12)
                    tangent = new Vector(1, 0);
                tangent.Normalize();
                normals[k] = new Vector(-tangent.Y, tangent.X);
            }

            double waves1 = Math.Max(1, Math.Round(perimeter * 0.18 / Math.Tau));
            double waves2 = Math.Max(1, Math.Round(perimeter * 0.074 / Math.Tau));
            contour.Count = count;
            contour.Rate1 = Math.Tau * waves1 / count;
            contour.Rate2 = Math.Tau * waves2 / count;
            contour.Phase = perimeter * 0.618 % Math.Tau;
            return contour;
        }

        /// <summary>Arrays for <paramref name="count"/> samples: the ones it has if they are big enough, else bigger ones (a growing outline keeps growing).</summary>
        private void Reserve(int count)
        {
            if (Points.Length >= count)
                return;
            int capacity = Points.Length == 0 ? count : count + count / 2;
            Points = new Point[capacity];
            Normals = new Vector[capacity];
            X = new double[capacity];
            Y = new double[capacity];
        }
    }
}
