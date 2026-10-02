using System.Windows;
using System.Windows.Media;

namespace InfiniteSpace.Shell;

/// <summary>The tool wheel's sectors: which one a direction points at, and their shapes.</summary>
internal static class WheelGeometry
{
    /// <summary>How many sectors the wheel has.</summary>
    public static readonly int Count = ToolCatalog.All.Count;

    /// <summary>The middle of sector <paramref name="index"/>, in degrees clockwise from straight up.</summary>
    public static double MiddleOf(int index) => index * 360.0 / Count;

    /// <summary>The sector a point (relative to the centre, y down) points at; null inside the inner circle.</summary>
    public static int? SegmentAt(Vector fromCentre, double inner)
    {
        if (fromCentre.Length < inner)
            return null;
        double degrees = Math.Atan2(fromCentre.X, -fromCentre.Y) * 180 / Math.PI; // 0 up, 90 right
        double shifted = (degrees + 360 + 180.0 / Count) % 360;
        return (int)(shifted / (360.0 / Count)) % Count;
    }

    /// <summary>A point <paramref name="radius"/> from the centre, <paramref name="degrees"/> clockwise from up.</summary>
    public static Point Polar(Point centre, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Sin(radians), centre.Y - radius * Math.Cos(radians));
    }

    /// <summary>A ring sector's outline, with a small gap (degrees) between neighbours.</summary>
    public static Geometry Sector(int index, double inner, double outer, Point centre, double gap = 1.5)
    {
        double half = 180.0 / Count - gap / 2;
        double from = MiddleOf(index) - half, to = MiddleOf(index) + half;
        var figure = new PathFigure { StartPoint = Polar(centre, outer, from), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(centre, outer, to), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(centre, inner, to), true));
        figure.Segments.Add(new ArcSegment(Polar(centre, inner, from), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
