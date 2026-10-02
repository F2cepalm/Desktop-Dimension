using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class SpaceGeometryTests
{
    private static List<Point> Rectangle(double x, double y, double width, double height) =>
        [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)];

    [Fact]
    public void FreehandStroke_KeepsEveryDrawnPoint()
    {
        // A wobbly loop: nothing should be smoothed or simplified away.
        var points = Enumerable.Range(0, 400)
            .Select(i =>
            {
                double a = i / 400.0 * Math.Tau;
                double r = 120 + 9 * Math.Sin(a * 23);
                return new Point(300 + r * Math.Cos(a), 300 + r * Math.Sin(a));
            })
            .ToList();

        var figures = SpaceGeometry.Encode(SpaceGeometry.FromStroke(points, new Vector(0, 0)), out _);

        var figure = Assert.Single(figures);
        Assert.Equal(points.Count * 2, figure.Length);
        Assert.Equal(points[57].X, figure[57 * 2], precision: 2);
        Assert.Equal(points[57].Y, figure[57 * 2 + 1], precision: 2);
    }

    [Fact]
    public void FreehandStroke_IsShiftedIntoScreenCoordinates()
    {
        var outline = SpaceGeometry.FromStroke(Rectangle(0, 0, 100, 50), new Vector(-1920, 40));

        Assert.Equal(new Rect(-1920, 40, 100, 50), outline.Bounds);
    }

    [Fact]
    public void EncodeDecode_RoundTripsTheOutline()
    {
        var outline = SpaceGeometry.FromStroke(Rectangle(10, 20, 200, 100), new Vector(0, 0));
        var record = new SpaceRecord { Figures = SpaceGeometry.Encode(outline, out var fillRule), FillRule = fillRule };

        var decoded = SpaceGeometry.Decode(record);

        Assert.Equal(FillRule.Nonzero, decoded.FillRule);
        Assert.Equal(20_000, SpaceGeometry.Area(decoded), precision: 0);
        Assert.Equal(outline.Bounds, decoded.Bounds);
    }

    [Fact]
    public void Erasing_ABandThroughTheMiddle_SplitsTheSpace()
    {
        var outline = SpaceGeometry.FromStroke(Rectangle(0, 0, 200, 100), new Vector(0, 0));
        var eraser = SpaceGeometry.EraserStroke([new(100, -50), new(100, 150)], new Vector(0, 0), radius: 10);

        var remaining = SpaceGeometry.Subtract(outline, eraser);

        Assert.NotNull(remaining);
        Assert.Equal(20_000 - 20 * 100, SpaceGeometry.Area(remaining), precision: 0);
        var record = new SpaceRecord { Figures = SpaceGeometry.Encode(remaining, out _) };
        Assert.Equal(2, record.Figures.Count);
    }

    [Fact]
    public void Erasing_ADotInside_LeavesAHole()
    {
        var outline = SpaceGeometry.FromStroke(Rectangle(0, 0, 200, 200), new Vector(0, 0));
        var eraser = SpaceGeometry.EraserStroke([new(100, 100)], new Vector(0, 0), radius: 20);

        var remaining = SpaceGeometry.Subtract(outline, eraser);

        Assert.NotNull(remaining);
        // The brush circle is flattened to a polygon, so the hole is slightly smaller than πr².
        Assert.InRange(SpaceGeometry.Area(remaining), 40_000 - Math.PI * 400 - 1, 40_000 - Math.PI * 400 + 15);
        var record = new SpaceRecord { Figures = SpaceGeometry.Encode(remaining, out var fillRule), FillRule = fillRule };
        // The hole must survive a save/load cycle.
        Assert.Equal(SpaceGeometry.Area(remaining), SpaceGeometry.Area(SpaceGeometry.Decode(record)), precision: 0);
    }

    [Fact]
    public void Erasing_Everything_RemovesTheSpace()
    {
        var outline = SpaceGeometry.FromStroke(Rectangle(0, 0, 100, 40), new Vector(0, 0));
        var eraser = SpaceGeometry.EraserStroke([new(0, 20), new(98, 20)], new Vector(0, 0), radius: 25);

        Assert.Null(SpaceGeometry.Subtract(outline, eraser));
    }

    [Fact]
    public void Erasing_AllButSlivers_RemovesTheSpace()
    {
        // Leaves 1×40 and 5×40 strips: 240 DIP², below the minimum.
        var outline = SpaceGeometry.FromStroke(Rectangle(0, 0, 100, 40), new Vector(0, 0));
        var eraser = SpaceGeometry.EraserStroke([new(48, -100), new(48, 140)], new Vector(0, 0), radius: 47);

        Assert.Null(SpaceGeometry.Subtract(outline, eraser));
    }

    [Fact]
    public void Stroke_WithTooFewPoints_HasNoArea()
    {
        var outline = SpaceGeometry.FromStroke([new(0, 0), new(10, 10)], new Vector(0, 0));

        Assert.Equal(0, SpaceGeometry.Area(outline));
    }
}
