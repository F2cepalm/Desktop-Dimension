using System.Windows;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

/// <summary>When the painted themes redraw (drawing itself needs a GPU and a window; see the app).</summary>
public class SceneLookTests
{
    private static ISpaceLook Create(string theme)
    {
        var outline = SpaceGeometry.FromStroke([new(100, 100), new(300, 100), new(300, 250), new(100, 250)], new Vector(0, 0));
        var look = Themes.CreateLook(theme);
        look.SetShape(outline, new Rect(86, 86, 228, 178));
        return look;
    }

    [Theory]
    [InlineData("Glory")]
    [InlineData("Burn")]
    [InlineData("Sunday")]
    [InlineData("Parallax")]
    [InlineData("Ocean")]
    [InlineData("Void")]
    public void InsideIsTheShape_NotItsSurroundings(string theme)
    {
        using var look = Create(theme);

        Assert.True(look.IsInside(new Point(200, 175)));
        Assert.False(look.IsInside(new Point(90, 90)));
        Assert.False(look.IsInside(new Point(500, 500)));
    }

    [Theory]
    [InlineData("Glory")]
    [InlineData("Burn")]
    [InlineData("Ocean")]
    public void LivelyThemes_MoveEveryFrame(string theme)
    {
        using var look = Create(theme);
        Assert.True(look.Render(new FrameState(10.000, new Vector())));

        Assert.True(look.Render(new FrameState(10.016, new Vector())));
        Assert.True(look.Render(new FrameState(10.032, new Vector())));
    }

    [Fact]
    public void Sunday_DriftsAtThirtyFramesASecond_AndFollowsTheCursor()
    {
        using var look = Create(Themes.Sunday);
        Assert.True(look.Render(new FrameState(10.000, new Vector())));

        Assert.False(look.Render(new FrameState(10.016, new Vector())));   // same 1/30 s step
        Assert.True(look.Render(new FrameState(10.034, new Vector())));    // next step
        Assert.True(look.Render(new FrameState(10.040, new Vector(80, 0)))); // the cursor moved: parallax
        Assert.False(look.Render(new FrameState(10.045, new Vector(80, 0))));
    }

    [Fact]
    public void Parallax_OnlyRedraws_WhenTheCursorMoves()
    {
        using var look = Create(Themes.Parallax);
        Assert.True(look.Render(new FrameState(5, new Vector())));

        Assert.False(look.Render(new FrameState(6, new Vector())));
        Assert.False(look.Render(new FrameState(7, new Vector(0.5, 0))));  // too small to see
        Assert.True(look.Render(new FrameState(7.01, new Vector(40, -10))));
        Assert.False(look.Render(new FrameState(9, new Vector(40, -10))));
    }

    [Fact]
    public void Void_IsStill_ExceptForAClick()
    {
        using var look = Create(Themes.Void);
        Assert.True(look.Render(new FrameState(5, new Vector())));
        Assert.False(look.Render(new FrameState(6, new Vector(300, 200))));

        look.AddBurst(new Point(200, 175));

        Assert.True(look.HasBursts);
        Assert.True(look.Render(new FrameState(6.1, new Vector())));
        Assert.True(look.Render(new FrameState(7, new Vector())));   // the ring is gone: wiped once
        Assert.False(look.HasBursts);
        Assert.False(look.Render(new FrameState(8, new Vector())));
    }

    [Fact]
    public void ANewShape_IsAlwaysDrawn()
    {
        using var look = Create(Themes.Void);
        look.Render(new FrameState(5, new Vector()));
        Assert.False(look.Render(new FrameState(6, new Vector())));

        look.SetShape(SpaceGeometry.FromStroke([new(0, 0), new(50, 0), new(50, 50)], new Vector(0, 0)), new Rect(-14, -14, 78, 78));

        Assert.True(look.Render(new FrameState(6.5, new Vector())));
    }
}
