using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

/// <summary>A resized space wobbles like jelly: it recoils a little, springs back, and rests within half a second.</summary>
public class JellyTests
{
    private const double Frame = 1 / 120.0;
    private static readonly Rect Block = new(100, 100, 200, 100);

    private static Jelly AtRest(Rect rest)
    {
        var jelly = new Jelly();
        jelly.Retarget(rest);
        return jelly;
    }

    /// <summary>The shown right edge, frame by frame, until the wobble is over.</summary>
    private static List<double> RightEdge(Jelly jelly)
    {
        var right = new List<double> { jelly.Shown.Right };
        while (jelly.IsMoving && right.Count < 1000)
        {
            jelly.Step(Frame);
            right.Add(jelly.Shown.Right);
        }
        return right;
    }

    [Fact]
    public void AResize_TakesEffectAtOnce_RecoilsALittle_ThenSpringsBackAndSettles()
    {
        var jelly = AtRest(Block);
        var bigger = new Rect(100, 100, 220, 100); // right edge pushed out by 20

        jelly.Retarget(bigger);
        var right = RightEdge(jelly);

        Assert.Equal(320, right[0]); // there at once
        int back = right.IndexOf(right.Min());
        Assert.InRange(right[back], 320 - 20 * Jelly.Recoil - 1, 320 - 20 * Jelly.Recoil + 1); // a little move back...
        Assert.InRange(back * Frame, 0.02, 0.08);                                               // ...straight away
        Assert.True(right.Skip(back).Max() > 320.5, "springs back out past where it rests");
        Assert.Equal(bigger, jelly.Shown); // and rests exactly there
    }

    [Fact]
    public void TheWobble_IsOverWithinHalfASecond_HoweverBigTheResize()
    {
        foreach (double push in new[] { 3.0, 20, 60, 400 })
        {
            var jelly = AtRest(Block);
            jelly.Retarget(new Rect(100, 100, 200 + push, 100));

            int frames = RightEdge(jelly).Count - 1;

            Assert.False(jelly.IsMoving);
            Assert.InRange(frames * Frame, 0, Jelly.Longest + Frame);
        }
    }

    [Fact]
    public void ABigResize_StillOnlyWobblesALittle()
    {
        var jelly = AtRest(Block);
        jelly.Retarget(new Rect(100, 100, 1200, 100)); // five times as wide at once

        var right = RightEdge(jelly);

        Assert.InRange(1300 - right.Min(), 20, 42); // a swing, but never much more than the largest one (40)
    }

    [Fact]
    public void TheEdgeThatWasNotMoved_StaysPut()
    {
        var jelly = AtRest(Block);
        jelly.Retarget(new Rect(100, 100, 240, 100));

        while (jelly.IsMoving)
        {
            Assert.Equal(100, jelly.Shown.Left, 9);
            jelly.Step(Frame);
        }
    }

    [Fact]
    public void WhileSquashed_ItGetsTallerAndBulges_WhileStretched_ShorterAndThinner()
    {
        var jelly = AtRest(Block);
        var bigger = new Rect(100, 100, 240, 100);
        jelly.Retarget(bigger);
        var topMiddle = new Point(bigger.X + bigger.Width / 2, bigger.Top);

        bool sawSquashed = false, sawStretched = false;
        while (jelly.IsMoving)
        {
            jelly.Step(Frame);
            var shown = jelly.Shown;
            if (shown.Width < bigger.Width - 1)
            {
                sawSquashed = true;
                Assert.True(shown.Height > bigger.Height, "keeps its volume: taller");
                Assert.True(jelly.Map(topMiddle, shown).Y < shown.Top, "the top bulges out in the middle");
            }
            else if (shown.Width > bigger.Width + 0.5)
            {
                sawStretched = true;
                Assert.True(shown.Height < bigger.Height, "keeps its volume: shorter");
                Assert.True(jelly.Map(topMiddle, shown).Y > shown.Top, "the top thins in the middle");
            }
        }
        Assert.True(sawSquashed && sawStretched);
    }

    [Fact]
    public void TheCornersOfItsBounds_StayOnTheBounds()
    {
        var jelly = AtRest(Block);
        var bigger = new Rect(80, 90, 260, 130);
        jelly.Retarget(bigger);
        jelly.Step(0.03);
        var shown = jelly.Shown;

        var topLeft = jelly.Map(bigger.TopLeft, shown);
        var bottomRight = jelly.Map(bigger.BottomRight, shown);
        Assert.Equal(shown.Left, topLeft.X, 6);
        Assert.Equal(shown.Top, topLeft.Y, 6);
        Assert.Equal(shown.Right, bottomRight.X, 6);
        Assert.Equal(shown.Bottom, bottomRight.Y, 6);
        Assert.NotEqual(bigger, shown); // mid-wobble
    }

    [Fact]
    public void AMove_DoesNotWobble()
    {
        var jelly = AtRest(Block);
        var moved = Block;
        moved.Offset(57.3, -12.1);

        jelly.Retarget(moved);

        Assert.False(jelly.IsMoving);
        Assert.Equal(moved, jelly.Shown);
    }

    [Fact]
    public void ABlockMovingOnTheArtPixelGrid_DoesNotJitter()
    {
        // A scaled block (not a whole number of DIPs wide) snapped to the grid as it follows the mouse:
        // its width flickers by a grid step.
        Rect Snapped(int i) => Shell.BlockEdit.Snap(new Rect(100 + i * 3.3, 100.7, 241.3, 117.9), DreamStyle.ArtPixel);
        var jelly = AtRest(Snapped(0));
        Assert.Contains(Enumerable.Range(1, 40), i => Snapped(i).Width != Snapped(0).Width);
        for (int i = 1; i <= 40; i++)
        {
            jelly.Retarget(Snapped(i));
            Assert.False(jelly.IsMoving, $"step {i}");
        }
    }

    [Fact]
    public void SlowResizes_AddUpToAWobble()
    {
        var jelly = AtRest(Block);

        jelly.Retarget(new Rect(100, 100, 201, 100));
        jelly.Retarget(new Rect(100, 100, 202, 100));
        Assert.False(jelly.IsMoving);
        jelly.Retarget(new Rect(100, 100, 203, 100));
        Assert.True(jelly.IsMoving);
    }

    [Fact]
    public void AResizeAfterAMove_WobblesOnlyByTheResize()
    {
        var jelly = AtRest(Block);
        jelly.Shift(new Vector(500, 0)); // moved without telling the wobble (drawn by just shifting it)

        jelly.Retarget(new Rect(600, 100, 210, 100));
        var right = RightEdge(jelly);

        Assert.Equal(600, jelly.Shown.Left);
        Assert.InRange(810 - right.Min(), 10 * Jelly.Recoil - 1, 10 * Jelly.Recoil + 1);
    }

    [Fact]
    public void WithAnimationsOff_AResizeJustHappens()
    {
        var jelly = AtRest(Block);

        jelly.Retarget(new Rect(100, 100, 260, 100), wobble: false);

        Assert.False(jelly.IsMoving);
        Assert.Equal(new Rect(100, 100, 260, 100), jelly.Shown);
    }

    [Fact]
    public void AWobblingOutline_BendsItsSides()
    {
        var square = SpaceGeometry.FromStroke([new(0, 0), new(120, 0), new(120, 60), new(0, 60)], new Vector(0, 0));

        var same = SpaceGeometry.Warp(square, p => p, step: 6);
        var bent = SpaceGeometry.Warp(square, p => new Point(p.X, p.Y + 5 * Math.Sin(Math.PI * p.X / 120) * (p.Y < 30 ? 1 : -1)), step: 6);

        var points = ((PolyLineSegment)same.Figures[0].Segments[0]).Points;
        Assert.True(points.Count > 50, $"{points.Count} points: long sides get points in between");
        Assert.Equal(square.Bounds, same.Bounds);
        Assert.True(bent.FillContains(new Point(60, 3)) == false, "the top came in at the middle");
        Assert.True(bent.FillContains(new Point(3, 3)), "but not at the corner");
    }
}
