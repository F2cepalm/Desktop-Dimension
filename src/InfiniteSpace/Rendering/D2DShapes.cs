using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Rendering;

/// <summary>A space's outline as Direct2D sees it: flattened figures, relative to a window's top-left.</summary>
internal readonly record struct Outline2D(Vector2[][] Figures, FillMode FillMode)
{
    public static Outline2D Empty { get; } = new([], FillMode.Winding);

    /// <summary>Flattens an outline (screen DIPs) into figures in DIPs from <paramref name="origin"/>.</summary>
    public static Outline2D From(Geometry outline, Point origin)
    {
        var flattened = SpaceGeometry.Flat(outline);
        var figures = new List<Vector2[]>(flattened.Figures.Count);
        foreach (var figure in flattened.Figures)
        {
            var points = Figure(figure, origin);
            if (points.Length >= 3)
                figures.Add(points);
        }
        return new Outline2D([.. figures], flattened.FillRule == FillRule.EvenOdd ? FillMode.Alternate : FillMode.Winding);
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
        return new Outline2D(figures, FillMode);
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

    private static Vector2[] Figure(PathFigure figure, Point origin)
    {
        int count = 1;
        foreach (var segment in figure.Segments)
            count += segment is PolyLineSegment poly ? poly.Points.Count : 1;
        var points = new Vector2[count];
        int at = 0;
        points[at++] = V(figure.StartPoint, origin);
        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment line)
                points[at++] = V(line.Point, origin);
            else if (segment is PolyLineSegment poly)
                foreach (var p in poly.Points)
                    points[at++] = V(p, origin);
        }
        return at == count ? points : points[..at];
    }

    private static Vector2 V(Point p, Point origin) => new((float)(p.X - origin.X), (float)(p.Y - origin.Y));
}
