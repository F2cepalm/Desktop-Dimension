using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class SpaceRemovalTests
{
    private static Geometry Square(double x, double y, double size) =>
        SpaceGeometry.FromStroke([new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)], new Vector(0, 0));

    [Fact]
    public void NoSpaces_NothingToRemove()
    {
        Assert.Equal(-1, SpaceManager.IndexToRemove([], new Point(10, 10)));
    }

    [Fact]
    public void CursorInsideASpace_RemovesThatSpace()
    {
        Geometry[] outlines = [Square(0, 0, 100), Square(300, 0, 100), Square(600, 0, 100)];

        Assert.Equal(1, SpaceManager.IndexToRemove(outlines, new Point(350, 50)));
        Assert.Equal(0, SpaceManager.IndexToRemove(outlines, new Point(50, 50)));
    }

    [Fact]
    public void CursorOutsideEverySpace_RemovesTheNewest()
    {
        Geometry[] outlines = [Square(0, 0, 100), Square(300, 0, 100)];

        Assert.Equal(1, SpaceManager.IndexToRemove(outlines, new Point(1000, 1000)));
    }

    [Fact]
    public void OverlappingSpaces_RemovesTheNewestUnderTheCursor()
    {
        Geometry[] outlines = [Square(0, 0, 200), Square(100, 100, 200), Square(900, 900, 50)];

        Assert.Equal(1, SpaceManager.IndexToRemove(outlines, new Point(150, 150)));
    }
}
