using System.Windows;
using System.Windows.Media;

namespace InfiniteSpace.Spaces;

/// <summary>Conversions between freehand strokes, WPF geometry and the persisted figure format.</summary>
internal static class SpaceGeometry
{
    /// <summary>Flattening tolerance in DIPs; well below one pixel, so outlines stay as drawn.</summary>
    public const double Tolerance = 0.25;

    /// <summary>Anything smaller than this (DIP²) is not worth keeping as a space.</summary>
    public const double MinArea = 300;

    /// <summary>Closes a freehand stroke into an outline. Every point is kept: the shape is exactly what was drawn.</summary>
    public static PathGeometry FromStroke(IReadOnlyList<Point> points, Vector offset)
    {
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        if (points.Count < 3)
            return geometry;

        var rest = new PointCollection(points.Count - 1);
        for (int i = 1; i < points.Count; i++)
            rest.Add(points[i] + offset);

        var figure = new PathFigure { StartPoint = points[0] + offset, IsClosed = true };
        figure.Segments.Add(new PolyLineSegment { Points = rest, IsStroked = true });
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>The area swept by a round brush of <paramref name="radius"/> along the stroke.</summary>
    public static Geometry EraserStroke(IReadOnlyList<Point> points, Vector offset, double radius)
    {
        if (points.Count == 0)
            return Geometry.Empty;
        if (points.Count == 1)
            return new EllipseGeometry(points[0] + offset, radius, radius);

        var rest = new PointCollection(points.Count - 1);
        for (int i = 1; i < points.Count; i++)
            rest.Add(points[i] + offset);

        var figure = new PathFigure { StartPoint = points[0] + offset, IsClosed = false };
        figure.Segments.Add(new PolyLineSegment { Points = rest, IsStroked = true });
        var path = new PathGeometry();
        path.Figures.Add(figure);
        var pen = new Pen(Brushes.Black, radius * 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var widened = path.GetWidenedPathGeometry(pen, Tolerance, ToleranceType.Absolute);
        widened.FillRule = FillRule.Nonzero;
        return widened;
    }

    public static double Area(Geometry geometry) => geometry.GetArea(Tolerance, ToleranceType.Absolute);

    /// <summary>What is left of <paramref name="shape"/> after erasing, or null when nothing meaningful remains.</summary>
    public static PathGeometry? Subtract(Geometry shape, Geometry eraser)
    {
        var remaining = Geometry.Combine(shape, eraser, GeometryCombineMode.Exclude, null, Tolerance, ToleranceType.Absolute);
        return Area(remaining) < MinArea ? null : remaining;
    }

    /// <summary>Whether two outlines share area (not merely overlapping bounding boxes).</summary>
    public static bool Overlap(Geometry a, Geometry b)
    {
        if (!a.Bounds.IntersectsWith(b.Bounds))
            return false;
        var common = Geometry.Combine(a, b, GeometryCombineMode.Intersect, null, Tolerance, ToleranceType.Absolute);
        return Area(common) > 1;
    }

    /// <summary>One outline covering all of them; crossing rims disappear into a single boundary.</summary>
    public static Geometry Union(IReadOnlyList<Geometry> outlines)
    {
        Geometry result = outlines[0];
        for (int i = 1; i < outlines.Count; i++)
            result = Geometry.Combine(result, outlines[i], GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);
        return result;
    }

    /// <summary>
    /// The outline as straight sides: itself when it is already (as spaces' outlines are, from
    /// <see cref="Decode"/>, <see cref="Transform"/> and <see cref="Warp"/>), else flattened. Flattening
    /// copies every point, and a space being reshaped needs its outline every frame.
    /// </summary>
    public static PathGeometry Flat(Geometry outline)
    {
        if (outline is PathGeometry path && (path.Transform is null || path.Transform.Value.IsIdentity) && IsFlat(path))
            return path;
        return outline.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute);
    }

    private static bool IsFlat(PathGeometry path)
    {
        foreach (var figure in path.Figures)
        {
            foreach (var segment in figure.Segments)
            {
                if (segment is not (LineSegment or PolyLineSegment))
                    return false;
            }
        }
        return true;
    }

    /// <summary>A flattened outline (as from <see cref="Decode"/>) with every point mapped through a transform.</summary>
    public static PathGeometry Transform(PathGeometry outline, Matrix m)
    {
        if (m.IsIdentity)
            return outline;
        var geometry = new PathGeometry { FillRule = outline.FillRule };
        foreach (var figure in outline.Figures)
        {
            int count = 0;
            foreach (var segment in figure.Segments)
                count += segment is PolyLineSegment poly ? poly.Points.Count : 1;
            var points = new PointCollection(count);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                    points.Add(m.Transform(line.Point));
                else if (segment is PolyLineSegment poly)
                    foreach (var p in poly.Points)
                        points.Add(m.Transform(p));
            }
            var mapped = new PathFigure { StartPoint = m.Transform(figure.StartPoint), IsClosed = figure.IsClosed };
            mapped.Segments.Add(new PolyLineSegment { Points = points, IsStroked = true });
            geometry.Figures.Add(mapped);
        }
        geometry.Freeze();
        return geometry;
    }

    [ThreadStatic]
    private static List<Point>? t_corners;

    /// <summary>
    /// An outline with every point moved by <paramref name="map"/>, which need not keep lines straight
    /// (a wobble bends them): sides longer than <paramref name="step"/> DIPs get points in between first.
    /// </summary>
    public static PathGeometry Warp(Geometry outline, Func<Point, Point> map, double step)
    {
        var flat = outline as PathGeometry ?? outline.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute);
        var geometry = new PathGeometry { FillRule = flat.FillRule };
        var corners = t_corners ??= [];
        foreach (var figure in flat.Figures)
        {
            corners.Clear();
            corners.Add(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                    corners.Add(line.Point);
                else if (segment is PolyLineSegment poly)
                    foreach (var p in poly.Points)
                        corners.Add(p);
                else
                    return Warp(outline.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute), map, step); // curves: flattened first
            }

            int total = Math.Max(0, corners.Count - 1);
            for (int i = 0; i < corners.Count; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % corners.Count];
                int pieces = (int)Math.Ceiling((to - from).Length / step);
                if (pieces > 1)
                    total += pieces - 1;
            }
            var points = new PointCollection(total);
            for (int i = 0; i < corners.Count; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % corners.Count]; // closed: the last side runs back to the start
                if (i > 0)
                    points.Add(map(from));
                int pieces = (int)Math.Ceiling((to - from).Length / step);
                for (int k = 1; k < pieces; k++)
                    points.Add(map(from + (to - from) * ((double)k / pieces)));
            }
            var warped = new PathFigure { StartPoint = map(figure.StartPoint), IsClosed = true };
            warped.Segments.Add(new PolyLineSegment { Points = points, IsStroked = true });
            geometry.Figures.Add(warped);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// The outline seen through a transform, without copying its points: cheap enough for every frame
    /// of a move, and fine for hit-testing and bounds.
    /// </summary>
    public static Geometry Mapped(PathGeometry outline, Matrix m)
    {
        if (m.IsIdentity)
            return outline;
        var geometry = new PathGeometry(outline.Figures, outline.FillRule, new MatrixTransform(m));
        geometry.Freeze();
        return geometry;
    }

    public static List<double[]> Encode(Geometry geometry, out FillRule fillRule)
    {
        var flattened = geometry.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute);
        fillRule = flattened.FillRule;

        var figures = new List<double[]>(flattened.Figures.Count);
        var coords = new List<double>();
        foreach (var figure in flattened.Figures)
        {
            coords.Clear();
            Point? last = null;
            void Add(Point p)
            {
                if (last is { } l && Math.Abs(l.X - p.X) < 0.01 && Math.Abs(l.Y - p.Y) < 0.01)
                    return;
                coords.Add(Math.Round(p.X, 2));
                coords.Add(Math.Round(p.Y, 2));
                last = p;
            }

            Add(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        Add(line.Point);
                        break;
                    case PolyLineSegment poly:
                        foreach (var p in poly.Points)
                            Add(p);
                        break;
                }
            }

            // Figures are closed implicitly; flattening may repeat the start point at the end.
            if (coords.Count >= 4 && Math.Abs(coords[^2] - coords[0]) < 0.01 && Math.Abs(coords[^1] - coords[1]) < 0.01)
                coords.RemoveRange(coords.Count - 2, 2);

            if (coords.Count >= 6)
                figures.Add([.. coords]);
        }
        return figures;
    }

    public static PathGeometry Decode(SpaceRecord record)
    {
        var geometry = new PathGeometry { FillRule = record.FillRule };
        foreach (var flat in record.Figures)
        {
            if (flat.Length < 6)
                continue;

            var rest = new PointCollection(flat.Length / 2 - 1);
            for (int i = 2; i + 1 < flat.Length; i += 2)
                rest.Add(new Point(flat[i], flat[i + 1]));

            geometry.Figures.Add(new PathFigure(new Point(flat[0], flat[1]), [new PolyLineSegment(rest, isStroked: true)], closed: true));
        }
        geometry.Freeze();
        return geometry;
    }
}
