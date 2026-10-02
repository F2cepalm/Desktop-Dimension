using System.Windows;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class TextCardTests
{
    private static readonly TextNote Note = new() { Text = "A card of a few words, long enough to wrap onto more lines when narrowed.", FontSize = 16 };

    [Fact]
    public void Narrowing_ReflowsTheText_AndTheCardGrowsDownwards()
    {
        Gpu.RunOnSta(() =>
        {
            var was = new Rect(new Point(100, 100), SpaceManager.CardSize(Note, 400));

            var narrowed = SpaceManager.CardBounds(Note, was, new Rect(100, 100, 200, was.Height)); // right edge dragged in

            Assert.Equal((100.0, 100.0, 200.0), (narrowed.Left, narrowed.Top, narrowed.Width));
            Assert.True(narrowed.Height > was.Height);
        });
    }

    [Fact]
    public void Widening_NeverMakesItTaller()
    {
        Gpu.RunOnSta(() =>
        {
            var was = new Rect(new Point(100, 100), SpaceManager.CardSize(Note, 200));

            var widened = SpaceManager.CardBounds(Note, was, new Rect(60, 100, 240, was.Height)); // left edge dragged out

            Assert.Equal((60.0, 240.0), (widened.Left, widened.Width));
            Assert.True(widened.Height <= was.Height);
        });
    }

    [Fact]
    public void DraggingTheTopOrBottom_ChangesNothing()
    {
        Gpu.RunOnSta(() =>
        {
            var was = new Rect(new Point(100, 100), SpaceManager.CardSize(Note, 300));

            Assert.Equal(was, SpaceManager.CardBounds(Note, was, new Rect(100, 40, 300, was.Height + 60)));
            Assert.Equal(was, SpaceManager.CardBounds(Note, was, new Rect(100, 100, 300, was.Height + 80)));
        });
    }

    [Fact]
    public void AMove_TakesTheCardAlong_AsItIs()
    {
        Gpu.RunOnSta(() =>
        {
            var was = new Rect(new Point(100, 100), SpaceManager.CardSize(Note, 300));
            var moved = Rect.Offset(was, new Vector(-35, 70));

            Assert.Equal(moved, SpaceManager.CardBounds(Note, was, moved));
        });
    }

    [Fact]
    public void ACardHeldAtItsNarrowest_KeepsTheSideThatWasntDragged()
    {
        Gpu.RunOnSta(() =>
        {
            var was = new Rect(new Point(100, 100), SpaceManager.CardSize(Note, 300));

            var squeezed = SpaceManager.CardBounds(Note, was, new Rect(390, 100, 10, was.Height)); // left edge dragged past the right

            Assert.Equal(SpaceManager.MinCardWidth(16), squeezed.Width);
            Assert.Equal(was.Right, squeezed.Right);
        });
    }
}
