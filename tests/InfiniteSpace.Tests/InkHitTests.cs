using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class InkHitTests
{
    /// <summary>A horizontal stroke from (100, 100) to (200, 100), a point every DIP.</summary>
    private static InkStroke Line(long order = 1, double thickness = 3) =>
        new() { Points = [.. Enumerable.Range(0, 101).SelectMany(i => new double[] { 100 + i, 100 })], Thickness = thickness, Order = order };

    [Fact]
    public void APointNearAStroke_HitsIt_AndOneFarAway_Misses()
    {
        var record = new SpaceRecord { Strokes = [Line()] };

        Assert.Same(record.Strokes[0], InkHit.At(record, Matrix.Identity, new Point(150, 103)));
        Assert.Null(InkHit.At(record, Matrix.Identity, new Point(150, 108)));
    }

    [Fact]
    public void ADot_IsHit()
    {
        var record = new SpaceRecord { Strokes = [new InkStroke { Points = [50, 50], Thickness = 3, Order = 1 }] };

        Assert.NotNull(InkHit.At(record, Matrix.Identity, new Point(52, 51)));
    }

    [Fact]
    public void NotesWinOverStrokes_AndTheNewerNoteWins()
    {
        Gpu.RunOnSta(() =>
        {
            var older = new TextNote { X = 120, Y = 92, Text = "older", Order = 5 };
            var newer = new TextNote { X = 120, Y = 92, Text = "newer", Order = 6 };
            var record = new SpaceRecord { Strokes = [Line(order: 9)], Notes = [older, newer] };

            Assert.Same(newer, InkHit.At(record, Matrix.Identity, new Point(125, 100)));
            Assert.Same(older, InkHit.At(record, Matrix.Identity, new Point(125, 100), skip: newer));
        });
    }

    [Fact]
    public void TheNoteBeingEdited_IsSkipped()
    {
        Gpu.RunOnSta(() =>
        {
            var editing = new TextNote { X = 10, Y = 10, Text = "edited", Order = 1 };
            var record = new SpaceRecord { Notes = [editing] };

            Assert.Same(editing, InkHit.At(record, Matrix.Identity, new Point(15, 15)));
            Assert.Null(InkHit.At(record, Matrix.Identity, new Point(15, 15), skip: editing));
        });
    }

    [Fact]
    public void ARectangleAcrossAStroke_SelectsIt_ThoughItHoldsNoneOfItsEnds()
    {
        var record = new SpaceRecord { Strokes = [Line()] };

        Assert.Single(InkHit.Touching(record, Matrix.Identity, new Rect(140, 80, 20, 40)));
        Assert.Empty(InkHit.Touching(record, Matrix.Identity, new Rect(140, 110, 20, 40)));
    }

    [Fact]
    public void WithGroup_GivesEveryLiveMember()
    {
        var group = Guid.NewGuid();
        var a = Line(1);
        var b = Line(2);
        var loner = Line(3);
        var emptied = new TextNote { Text = "", Group = group, Order = 4 };
        var note = new TextNote { Text = "member", Group = group, Order = 5 };
        a.Group = b.Group = group;
        var record = new SpaceRecord { Strokes = [a, b, loner], Notes = [emptied, note] };

        Assert.Equal([a, b, note], InkHit.WithGroup(record, a));
        Assert.Equal([loner], InkHit.WithGroup(record, loner));
    }

    [Theory]
    [InlineData(-3, 4, 5)]  // beyond the start
    [InlineData(13, 4, 5)]  // beyond the end
    [InlineData(5, 2, 2)]   // beside the middle
    public void DistanceToSegment_MeasuresToTheNearestPoint(double x, double y, double expected)
    {
        Assert.Equal(expected, InkHit.DistanceToSegment(new Point(x, y), new Point(0, 0), new Point(10, 0)), 9);
    }
}
