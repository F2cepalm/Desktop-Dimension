using System.Windows;
using InfiniteSpace.Shell;

namespace InfiniteSpace.Tests;

public class BlockEditTests
{
    private static readonly Rect Block = new(100, 100, 200, 100);

    // BlockGrip is internal, so theories take it as an int.
    [Theory]
    [InlineData((int)BlockGrip.Left, 90, 100, 210, 100)]
    [InlineData((int)BlockGrip.Right, 100, 100, 210, 100)]
    [InlineData((int)BlockGrip.Top, 100, 90, 200, 110)]
    [InlineData((int)BlockGrip.Bottom, 100, 100, 200, 110)]
    public void ArrowGrowsThatSide(int side, double x, double y, double width, double height)
    {
        Assert.Equal(new Rect(x, y, width, height), BlockEdit.Grow(Block, (BlockGrip)side, 10, min: 24));
    }

    [Fact]
    public void StretchingPastTheScreen_StopsAtItsEdge()
    {
        var screen = new Rect(0, 0, 400, 300);
        // The bottom-right corner dragged far out: both edges stop at the screen's.
        Assert.Equal(new Rect(100, 100, 300, 200), BlockEdit.Contain(Block, BlockEdit.Drag(Block, BlockGrip.Right | BlockGrip.Bottom, new Vector(900, 900), 24), screen));
        // Scaled up about its centre: every side stops at the screen's.
        Assert.Equal(new Rect(0, 0, 400, 300), BlockEdit.Contain(Block, BlockEdit.Scale(Block, 10, 24), screen));
    }

    [Fact]
    public void ABlockAlreadyPastTheScreen_CanStillBeShrunk_ButNotStretchedFurther()
    {
        var screen = new Rect(0, 0, 250, 300);
        var wide = new Rect(100, 100, 300, 100); // right edge at 400, past the screen's 250
        Assert.Equal(wide, BlockEdit.Contain(wide, BlockEdit.Grow(wide, BlockGrip.Right, 10, 24), screen));
        Assert.Equal(new Rect(100, 100, 290, 100), BlockEdit.Contain(wide, BlockEdit.Grow(wide, BlockGrip.Right, -10, 24), screen));
    }

    [Fact]
    public void ShiftArrowShrinksThatSide_ButNotBelowTheMinimum()
    {
        Assert.Equal(new Rect(110, 100, 190, 100), BlockEdit.Grow(Block, BlockGrip.Left, -10, min: 24));
        Assert.Equal(new Rect(100, 100, 200, 24), BlockEdit.Grow(Block, BlockGrip.Bottom, -500, min: 24));
    }

    [Fact]
    public void DraggingACorner_MovesBothItsEdges()
    {
        var resized = BlockEdit.Drag(Block, BlockGrip.Right | BlockGrip.Bottom, new Vector(30, 20), min: 24);

        Assert.Equal(new Rect(100, 100, 230, 120), resized);
    }

    [Fact]
    public void DraggingTheBody_MovesTheBlock()
    {
        Assert.Equal(new Rect(130, 80, 200, 100), BlockEdit.Drag(Block, BlockGrip.Move, new Vector(30, -20), min: 24));
    }

    [Fact]
    public void DraggingAnEdgePastTheOtherSide_StopsAtTheMinimum()
    {
        var resized = BlockEdit.Drag(Block, BlockGrip.Left, new Vector(400, 0), min: 24);

        Assert.Equal(new Rect(276, 100, 24, 100), resized);
    }

    [Fact]
    public void Scaling_KeepsTheCentre()
    {
        var scaled = BlockEdit.Scale(Block, 2, min: 24);

        Assert.Equal(new Rect(0, 50, 400, 200), scaled);
    }

    [Fact]
    public void Snap_PutsEdgesOnTheGrid()
    {
        Assert.Equal(new Rect(100, 102, 202, 98), BlockEdit.Snap(new Rect(100.7, 101.2, 200.5, 98.3), 2));
    }

    [Theory]
    [InlineData(101, 150, (int)BlockGrip.Left)]
    [InlineData(299, 104, (int)(BlockGrip.Right | BlockGrip.Top))]
    [InlineData(200, 195, (int)BlockGrip.Bottom)]
    [InlineData(200, 150, (int)BlockGrip.Move)]
    [InlineData(20, 20, (int)BlockGrip.None)]
    public void GripAt_FindsEdgesCornersAndBody(double x, double y, int expected)
    {
        Assert.Equal((BlockGrip)expected, BlockEdit.GripAt(Block, new Point(x, y), margin: 8));
    }
}
