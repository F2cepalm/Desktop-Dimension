using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Rendering;

/// <summary>A space's outline as Direct2D sees it: flattened figures, relative to a window's top-left.</summary>
internal readonly record struct Outline2D(Vector2[][] Figures, FillMode FillMode, Rect Bounds = default)
{
    public static Outline2D Empty { get; } = new([], FillMode.Winding, Rect.Empty);

    [ThreadStatic]
    private static Geometry? _lastGeometry;

    [ThreadStatic]
    private static Matrix _lastTransform;

    [ThreadStatic]
    private static Point _lastOrigin;

    [ThreadStatic]
    private static Outline2D _lastOutline;

    [ThreadStatic]
    private static Geometry? _warpGeometry;

    [ThreadStatic]
    private static List<Point>? _warpCorners;

    [ThreadStatic]
    private static List<(Point[] Points, int Count)>? _warpFigures;

    [ThreadStatic]
    private static FillMode _warpFillMode;

    [ThreadStatic]
    private static Vector2[][]? _warpCachedFigures;

    [ThreadStatic]
    private static Vector2[][]? _liveCachedFigures;

    /// <summary>Whether <paramref name="outline"/> has a pending jelly warp from <see cref="WarpNext"/>.</summary>
    public static bool IsWarped(Geometry outline) => ReferenceEquals(outline, _warpGeometry);

    /// <summary>
    /// Warps <paramref name="outline"/> into reusable buffers for the next <see cref="From"/> calls on this
    /// thread, returning the warped screen bounds without allocating a WPF <see cref="PathGeometry"/>.
    /// </summary>
    public static void WarpNext(Geometry outline, Func<Point, Point> map, double step, out Rect bounds)
    {
        var flat = SpaceGeometry.Flat(outline, out var transform);
        bool hasTransform = !transform.IsIdentity;
        var corners = _warpCorners ??= [];
        var figures = _warpFigures ??= [];
        int figCount = 0;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

        foreach (var figure in flat.Figures)
        {
            corners.Clear();
            corners.Add(hasTransform ? transform.Transform(figure.StartPoint) : figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                {
                    corners.Add(hasTransform ? transform.Transform(line.Point) : line.Point);
                }
                else if (segment is PolyLineSegment poly)
                {
                    var pts = poly.Points;
                    int n = pts.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var p = pts[i];
                        corners.Add(hasTransform ? transform.Transform(p) : p);
                    }
                }
            }
            if (corners.Count < 3)
                continue;

            int total = corners.Count;
            for (int i = 0; i < corners.Count; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % corners.Count];
                int pieces = (int)Math.Ceiling((to - from).Length / step);
                if (pieces > 1)
                    total += pieces - 1;
            }

            Point[] buffer;
            if (figCount < figures.Count && figures[figCount].Points.Length >= total)
                buffer = figures[figCount].Points;
            else
                buffer = new Point[total + total / 2];

            int at = 0;
            for (int i = 0; i < corners.Count; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % corners.Count];
                var wf = map(from);
                buffer[at++] = wf;
                if (wf.X < minX) minX = wf.X;
                if (wf.X > maxX) maxX = wf.X;
                if (wf.Y < minY) minY = wf.Y;
                if (wf.Y > maxY) maxY = wf.Y;
                int pieces = (int)Math.Ceiling((to - from).Length / step);
                for (int k = 1; k < pieces; k++)
                {
                    var wp = map(from + (to - from) * ((double)k / pieces));
                    buffer[at++] = wp;
                    if (wp.X < minX) minX = wp.X;
                    if (wp.X > maxX) maxX = wp.X;
                    if (wp.Y < minY) minY = wp.Y;
                    if (wp.Y > maxY) maxY = wp.Y;
                }
            }

            if (figCount < figures.Count)
                figures[figCount] = (buffer, at);
            else
                figures.Add((buffer, at));
            figCount++;
        }
        if (figures.Count > figCount)
            figures.RemoveRange(figCount, figures.Count - figCount);

        _warpGeometry = outline;
        _warpFillMode = flat.FillRule == FillRule.EvenOdd ? FillMode.Alternate : FillMode.Winding;
        _lastGeometry = null;
        bounds = figCount > 0 && maxX >= minX && maxY >= minY
            ? new Rect(minX, minY, maxX - minX, maxY - minY)
            : Rect.Empty;
    }

    /// <summary>Clears any pending warp from <see cref="WarpNext"/>.</summary>
    public static void EndWarp() => _warpGeometry = null;

    /// <summary>Flattens an outline (screen DIPs) into figures in DIPs from <paramref name="origin"/>.</summary>
    public static Outline2D From(Geometry outline, Point origin)
    {
        if (ReferenceEquals(outline, _warpGeometry) && _warpFigures is { } warped)
        {
            if (ReferenceEquals(outline, _lastGeometry) && origin == _lastOrigin)
                return _lastOutline;

            if (_warpCachedFigures is null || _warpCachedFigures.Length != warped.Count)
                _warpCachedFigures = new Vector2[warped.Count][];

            float wMinX = float.PositiveInfinity, wMinY = float.PositiveInfinity;
            float wMaxX = float.NegativeInfinity, wMaxY = float.NegativeInfinity;
            for (int f = 0; f < warped.Count; f++)
            {
                var (src, count) = warped[f];
                var dst = _warpCachedFigures[f];
                if (dst is null || dst.Length != count)
                    _warpCachedFigures[f] = dst = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    var p = V(src[i], origin);
                    dst[i] = p;
                    if (p.X < wMinX) wMinX = p.X;
                    if (p.X > wMaxX) wMaxX = p.X;
                    if (p.Y < wMinY) wMinY = p.Y;
                    if (p.Y > wMaxY) wMaxY = p.Y;
                }
            }
            var wBounds = warped.Count > 0 && wMaxX >= wMinX && wMaxY >= wMinY
                ? new Rect(wMinX, wMinY, wMaxX - wMinX, wMaxY - wMinY)
                : Rect.Empty;
            var wResult = new Outline2D(_warpCachedFigures, _warpFillMode, wBounds);
            _lastGeometry = outline;
            _lastOrigin = origin;
            _lastOutline = wResult;
            return wResult;
        }

        var flattened = SpaceGeometry.Flat(outline, out var transform);
        if (ReferenceEquals(outline, _lastGeometry) && origin == _lastOrigin && transform == _lastTransform)
            return _lastOutline;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        Vector2[][] resultFigures;
        if (!outline.IsFrozen && flattened.Figures.Count == 1)
        {
            _liveCachedFigures ??= new Vector2[1][];
            var points = Figure(flattened.Figures[0], transform, origin, _liveCachedFigures[0]);
            if (points.Length >= 3)
            {
                _liveCachedFigures[0] = points;
                for (int i = 0; i < points.Length; i++)
                {
                    var p = points[i];
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
                resultFigures = _liveCachedFigures;
            }
            else
            {
                resultFigures = [];
            }
        }
        else
        {
            var outFigures = new List<Vector2[]>(flattened.Figures.Count);
            foreach (var figure in flattened.Figures)
            {
                var points = Figure(figure, transform, origin, null);
                if (points.Length >= 3)
                {
                    outFigures.Add(points);
                    for (int i = 0; i < points.Length; i++)
                    {
                        var p = points[i];
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                }
            }
            resultFigures = [.. outFigures];
        }
        var bounds = resultFigures.Length > 0 && maxX >= minX && maxY >= minY
            ? new Rect(minX, minY, maxX - minX, maxY - minY)
            : Rect.Empty;
        var result = new Outline2D(resultFigures, flattened.FillRule == FillRule.EvenOdd ? FillMode.Alternate : FillMode.Winding, bounds);
        _lastGeometry = outline;
        _lastTransform = transform;
        _lastOrigin = origin;
        _lastOutline = result;
        return result;
    }

    /// <summary>The same figures, moved by an offset (DIPs).</summary>
    public Outline2D Offset(Vector by)
    {
        var shift = new Vector2((float)by.X, (float)by.Y);
        var figures = new Vector2[Figures.Length][];
        for (int f = 0; f < figures.Length; f++)
        {
            var source = Figures[f];
            var moved = new Vector2[source.Length];
            for (int i = 0; i < moved.Length; i++)
                moved[i] = source[i] + shift;
            figures[f] = moved;
        }
        var bounds = Bounds.IsEmpty ? Rect.Empty : new Rect(Bounds.X + by.X, Bounds.Y + by.Y, Bounds.Width, Bounds.Height);
        return new Outline2D(figures, FillMode, bounds);
    }

    /// <summary>A Direct2D geometry of the figures; the caller owns it.</summary>
    public ID2D1PathGeometry ToPath(ID2D1Factory1 factory)
    {
        var path = factory.CreatePathGeometry();
        using (var sink = path.Open())
        {
            sink.SetFillMode(FillMode);
            foreach (var figure in Figures)
            {
                sink.BeginFigure(figure[0], FigureBegin.Filled);
                sink.AddLines(figure);
                sink.EndFigure(FigureEnd.Closed);
            }
            sink.Close();
        }
        return path;
    }

    /// <summary>Whether <paramref name="box"/> (in DIPs from the same origin) lies wholly inside the outline.</summary>
    public bool Covers(Rect box)
    {
        if (Figures.Length == 0 || box.IsEmpty)
            return false;
        float left = (float)box.Left, top = (float)box.Top, right = (float)box.Right, bottom = (float)box.Bottom;
        float cx = (left + right) * 0.5f, cy = (top + bottom) * 0.5f;
        int winding = 0;
        foreach (var figure in Figures)
        {
            for (int i = 0; i < figure.Length; i++)
            {
                var a = figure[i];
                var b = figure[i + 1 < figure.Length ? i + 1 : 0];
                if (!((a.X <= left && b.X <= left) || (a.X >= right && b.X >= right) || (a.Y <= top && b.Y <= top) || (a.Y >= bottom && b.Y >= bottom)))
                    return false;
                if ((a.Y <= cy && b.Y > cy) || (b.Y <= cy && a.Y > cy))
                {
                    float x = a.X + (cy - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (x > cx)
                        winding += a.Y < b.Y ? 1 : -1;
                }
            }
        }
        return FillMode == FillMode.Alternate ? (winding & 1) != 0 : winding != 0;
    }

    private static Vector2[] Figure(PathFigure figure, Matrix transform, Point origin, Vector2[]? reuse)
    {
        bool hasTransform = !transform.IsIdentity;
        int count = 1;
        foreach (var segment in figure.Segments)
            count += segment is PolyLineSegment poly ? poly.Points.Count : 1;
        var points = reuse is not null && reuse.Length == count ? reuse : new Vector2[count];
        int at = 0;
        points[at++] = V(hasTransform ? transform.Transform(figure.StartPoint) : figure.StartPoint, origin);
        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment line)
            {
                points[at++] = V(hasTransform ? transform.Transform(line.Point) : line.Point, origin);
            }
            else if (segment is PolyLineSegment poly)
            {
                var pts = poly.Points;
                int n = pts.Count;
                for (int i = 0; i < n; i++)
                {
                    var p = pts[i];
                    points[at++] = V(hasTransform ? transform.Transform(p) : p, origin);
                }
            }
        }
        return at == count ? points : points[..at];
    }

    private static Vector2 V(Point p, Point origin) => new((float)(p.X - origin.X), (float)(p.Y - origin.Y));
}
