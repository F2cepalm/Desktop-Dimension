using System.Windows;
using InfiniteSpace.Shell;

namespace InfiniteSpace.Tests;

public class WheelGeometryTests
{
    [Theory]
    [InlineData(0, -80, 0)]
    [InlineData(60, -60, 1)]
    [InlineData(80, 0, 2)]
    [InlineData(60, 60, 3)]
    [InlineData(0, 80, 4)]
    [InlineData(-60, 60, 5)]
    [InlineData(-80, 0, 6)]
    [InlineData(-60, -60, 7)]
    public void ADirection_PointsAtItsSector(double x, double y, int sector)
    {
        Assert.Equal(sector, WheelGeometry.SegmentAt(new Vector(x, y), 40));
    }

    [Fact]
    public void TheMiddle_PointsAtNone()
    {
        Assert.Null(WheelGeometry.SegmentAt(new Vector(10, 10), 40));
    }

    [Fact]
    public void Polar_GoesClockwiseFromUp()
    {
        var c = new Point(100, 50);

        var right = WheelGeometry.Polar(c, 10, 90);

        Assert.Equal(110, right.X, 9);
        Assert.Equal(50, right.Y, 9);
    }

    [Fact]
    public void TheWheel_BuildsInEveryTheme()
    {
        Gpu.RunOnSta(() =>
        {
            foreach (string theme in Rendering.Themes.All)
                Assert.NotNull(new ToolWheelWindow(new Point(500, 500), Rendering.Themes.ChromeOf(theme), Spaces.Tool.Pen).Content);
        });
    }

    [Fact]
    public void Sectors_AreRingPieces()
    {
        Gpu.RunOnSta(() =>
        {
            var c = new Point(150, 150);
            var top = WheelGeometry.Sector(0, 40, 116, c);

            Assert.True(top.FillContains(new Point(150, 70)));
            Assert.False(top.FillContains(new Point(150, 140))); // inside the inner circle
            Assert.False(top.FillContains(new Point(230, 150))); // another sector
        });
    }
}
