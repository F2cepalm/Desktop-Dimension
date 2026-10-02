using System.Windows;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class InkyLookTests
{
    private static InkyLook Create()
    {
        var outline = SpaceGeometry.FromStroke([new(100, 100), new(300, 100), new(300, 250), new(100, 250)], new Vector(0, 0));
        var look = new InkyLook();
        look.SetShape(outline, new Rect(86, 86, 228, 178));
        return look;
    }

    [Fact]
    public void InsideIsTheShape_NotItsSurroundings()
    {
        using var look = Create();

        Assert.True(look.IsInside(new Point(200, 175)));
        Assert.False(look.IsInside(new Point(90, 90)));
        Assert.False(look.IsInside(new Point(500, 500)));
    }

    [Fact]
    public void TheLineBoils_AboutTenTimesASecond_AndIsStillInBetween()
    {
        using var look = Create();

        Assert.True(look.Render(new FrameState(10.00, new Vector())));   // first frame
        Assert.False(look.Render(new FrameState(10.03, new Vector())));  // same boil frame: nothing to redraw
        Assert.False(look.Render(new FrameState(10.09, new Vector())));
        Assert.True(look.Render(new FrameState(10.11, new Vector())));   // next boil frame
    }

    [Fact]
    public void TheCursorDoesNotMoveIt()
    {
        using var look = Create();
        look.Render(new FrameState(5, new Vector()));

        Assert.False(look.Render(new FrameState(5.01, new Vector(300, -200))));
    }

    [Fact]
    public void AClick_SplashesInk_EveryFrameUntilItSettles()
    {
        using var look = Create();
        look.Render(new FrameState(20, new Vector()));

        look.AddBurst(new Point(200, 175));

        Assert.True(look.HasBursts);
        Assert.True(look.Render(new FrameState(20.01, new Vector())));
        Assert.True(look.Render(new FrameState(20.02, new Vector())));
        look.Render(new FrameState(22, new Vector()));
        Assert.False(look.HasBursts);
    }
}
