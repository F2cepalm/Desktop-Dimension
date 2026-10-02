using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class SheetsTests
{
    private static InkStroke Diagonal(int length = 200, double thickness = 4) =>
        new() { Points = [.. Enumerable.Range(0, length).SelectMany(i => new double[] { 100 + i, 100 + i })], Thickness = thickness, Order = 1 };

    [Fact]
    public void Bounds_CoverTheStroke_ItsWidthAndTheMargin()
    {
        var record = new SpaceRecord { Kind = SpaceKind.Sheet, Strokes = [Diagonal(101)] };

        var bounds = Sheets.Bounds(record, Matrix.Identity)!.Value;

        double room = 2 + Sheets.Margin;
        Assert.Equal(new Rect(100 - room, 100 - room, 100 + 2 * room, 100 + 2 * room), bounds);
    }

    [Fact]
    public void Bounds_AreNull_WithoutInk()
    {
        Assert.Null(Sheets.Bounds(new SpaceRecord { Kind = SpaceKind.Sheet }, Matrix.Identity));
    }

    [Fact]
    public void AStrokeKeepsItsSize_WhenItsAppIsStretched()
    {
        var record = new SpaceRecord { Kind = SpaceKind.Sheet, Strokes = [Diagonal(101)] };

        var plain = Sheets.Bounds(record, Matrix.Identity)!.Value;
        var stretched = Sheets.Bounds(record, new Matrix(2, 0, 0, 3, 10, 20))!.Value;

        Assert.Equal(plain.Size, stretched.Size);
    }

    [Fact]
    public void HitRects_CoverEveryPointOfALongStroke()
    {
        var stroke = Diagonal();
        var record = new SpaceRecord { Kind = SpaceKind.Sheet, Strokes = [stroke] };

        var rects = Sheets.HitRects(record, Matrix.Identity).ToList();

        Assert.True(rects.Count > 1);
        for (int i = 0; i + 1 < stroke.Points.Length; i += 2)
        {
            var p = new Point(stroke.Points[i], stroke.Points[i + 1]);
            Assert.Contains(rects, r => r.Contains(p));
        }
    }

    [Fact]
    public void ADot_GivesOneRectangle()
    {
        var record = new SpaceRecord { Kind = SpaceKind.Sheet, Strokes = [new InkStroke { Points = [5, 5], Thickness = 3 }] };

        Assert.Single(Sheets.HitRects(record, Matrix.Identity));
    }

    [Fact]
    public void Notes_AreInTheBoundsAndTakeClicks()
    {
        Gpu.RunOnSta(() =>
        {
            var record = new SpaceRecord { Kind = SpaceKind.Sheet, Notes = [new TextNote { X = 50, Y = 60, Text = "hello" }] };

            Assert.NotNull(Sheets.Bounds(record, Matrix.Identity));
            Assert.Contains(Sheets.HitRects(record, Matrix.Identity), r => r.Contains(new Point(55, 65)));
        });
    }
}
