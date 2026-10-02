using System.Windows;
using InfiniteSpace.Shell;

namespace InfiniteSpace.Tests;

public class WheelGeometryTests
{
    [Fact]
    public void ADirection_PointsAtItsSector_ClockwiseFromTheTop()
    {
        Assert.Equal(10, WheelGeometry.Count); // one per tool
        for (int sector = 0; sector < WheelGeometry.Count; sector++)
        {
            double radians = WheelGeometry.MiddleOf(sector) * Math.PI / 180;
            var direction = new Vector(80 * Math.Sin(radians), -80 * Math.Cos(radians));
            Assert.Equal(sector, WheelGeometry.SegmentAt(direction, 40));
        }
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
