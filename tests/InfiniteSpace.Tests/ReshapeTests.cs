using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Shell;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

/// <summary>Moving and reshaping a space with Ctrl+Alt+M: text and paint stay put while the space changes shape.</summary>
public class ReshapeTests
{
    private static readonly Rect Space = new(100, 100, 200, 100);
    private static readonly Rect Note = new(150, 120, 40, 20);

    [Fact]
    public void GrowingASpace_LeavesItsTextWhereItIs()
    {
        var wider = BlockEdit.Grow(Space, BlockGrip.Right, 200, min: 24);
        var taller = BlockEdit.Grow(Space, BlockGrip.Top, 50, min: 24);
        var bigger = BlockEdit.Scale(Space, 1.5, min: 24);

        Assert.Equal(Note.TopLeft, SpaceManager.PlaceNote(Note, Space, wider, new Vector()));
        Assert.Equal(Note.TopLeft, SpaceManager.PlaceNote(Note, Space, taller, new Vector()));
        Assert.Equal(Note.TopLeft, SpaceManager.PlaceNote(Note, Space, bigger, new Vector()));
    }

    [Fact]
    public void ShrinkingASpace_LeavesItsTextWhereItIs_UntilAnEdgeReachesIt()
    {
        var notYet = BlockEdit.Grow(Space, BlockGrip.Right, -100, min: 24); // right edge at 200, past the note's 190
        var reached = BlockEdit.Grow(Space, BlockGrip.Right, -130, min: 24); // right edge at 170

        Assert.Equal(Note.TopLeft, SpaceManager.PlaceNote(Note, Space, notYet, new Vector()));
        Assert.Equal(new Point(130, 120), SpaceManager.PlaceNote(Note, Space, reached, new Vector())); // pushed along
    }

    [Fact]
    public void EdgesComingIn_PushTextFromEitherSide()
    {
        var fromLeft = BlockEdit.Grow(Space, BlockGrip.Left, -70, min: 24);   // left edge at 170
        var fromBelow = BlockEdit.Grow(Space, BlockGrip.Bottom, -70, min: 24); // bottom edge at 130

        Assert.Equal(new Point(170, 120), SpaceManager.PlaceNote(Note, Space, fromLeft, new Vector()));
        Assert.Equal(new Point(150, 110), SpaceManager.PlaceNote(Note, Space, fromBelow, new Vector()));
    }

    [Fact]
    public void TextWiderThanTheSpace_KeepsItsStartInside()
    {
        var narrow = new Rect(160, 100, 24, 100);

        Assert.Equal(new Point(160, 120), SpaceManager.PlaceNote(Note, Space, narrow, new Vector()));
    }

    [Fact]
    public void MovingASpace_CarriesItsText()
    {
        var carried = new Vector(40, -25);
        var moved = Space;
        moved.Offset(carried);

        Assert.Equal(Note.TopLeft + carried, SpaceManager.PlaceNote(Note, Space, moved, carried));
    }

    [Fact]
    public void MovingThenReshaping_CarriesTextOnlyAsFarAsTheMoves()
    {
        var carried = new Vector(300, 0);
        var moved = Space;
        moved.Offset(carried);
        var reshaped = BlockEdit.Grow(moved, BlockGrip.Left, 80, min: 24);

        Assert.Equal(Note.TopLeft + carried, SpaceManager.PlaceNote(Note, Space, reshaped, carried));
    }

    [Fact]
    public void TextThatAlreadyStuckOut_StaysAsItWas_ButGoesNoFurther()
    {
        var overhanging = new Rect(280, 120, 40, 20); // 20 past the right edge at 300

        Assert.Equal(overhanging.TopLeft, SpaceManager.PlaceNote(overhanging, Space, Space, new Vector()));
        var narrower = BlockEdit.Grow(Space, BlockGrip.Right, -10, min: 24);
        Assert.Equal(new Point(270, 120), SpaceManager.PlaceNote(overhanging, Space, narrower, new Vector()));
    }

    [Fact]
    public void ReshapingASpace_LeavesItsPaintWhereItIs_EvenWhenAnEdgeReachesIt()
    {
        var stroke = new InkStroke { Points = [150, 130, 190, 130], Thickness = 4 }; // covers 148..192 across
        var space = new SpaceManager.MovingSpace(null!, Matrix.Identity, Space);

        space.Now = BlockEdit.Grow(Space, BlockGrip.Right, 200, min: 24);
        Assert.Equal(Matrix.Identity, space.PlaceStroke(stroke)); // not stretched

        space.Now = BlockEdit.Grow(Space, BlockGrip.Right, -130, min: 24); // right edge at 170
        Assert.Equal(Matrix.Identity, space.PlaceStroke(stroke)); // not pushed: cut off by the edge instead
    }

    [Fact]
    public void PaintOnAnApp_KeepsItsPlaceFromTheAppsTopLeft_AsTheAppIsResized()
    {
        var stroke = new InkStroke { Points = [150, 130, 190, 130] };
        var frame = new Rect(100, 100, 400, 300);
        var wider = new Rect(100, 100, 800, 600); // twice the size, same top-left
        var toScreen = HostFrames.Map(frame, wider);

        Assert.Equal(Matrix.Identity, stroke.Placed(toScreen, frame.TopLeft));
        var moved = HostFrames.Map(frame, new Rect(130, 90, 800, 600)); // resized from the left and top edges
        Assert.Equal(new Matrix(1, 0, 0, 1, 30, -10), stroke.Placed(moved, frame.TopLeft));
    }

    [Fact]
    public void MovingASpace_CarriesItsPaint()
    {
        var stroke = new InkStroke { Points = [150, 130, 190, 130] };
        var carried = new Vector(40, -25);
        var moved = Space;
        moved.Offset(carried);
        var space = new SpaceManager.MovingSpace(null!, Matrix.Identity, Space) { Now = moved, Carried = carried };

        Assert.Equal(new Matrix(1, 0, 0, 1, 40, -25), space.PlaceStroke(stroke));
    }

    [Fact]
    public void Dropping_KeepsTextAndPaintWhereTheyWereShown()
    {
        var note = new TextNote { X = 150, Y = 120, Text = "stay" };
        var record = new SpaceRecord
        {
            Figures = [[100, 100, 300, 100, 300, 200, 100, 200]],
            Strokes = [new InkStroke { Points = [100, 150, 300, 150] }],
            Notes = [note],
        };
        var twiceAsWide = new Matrix(2, 0, 0, 1, -100, 0);

        record.Transform(twiceAsWide, n => new Point(n.X, n.Y), _ => new Matrix(1, 0, 0, 1, 5, 0));

        Assert.Equal([100, 100, 500, 100, 500, 200, 100, 200], record.Figures[0]);
        Assert.Equal([105, 150, 305, 150], record.Strokes[0].Points); // shifted, not stretched
    }

    [Fact]
    public void Rebasing_KeepsTextWhereItWasShown_AndPaintUnstretched()
    {
        var note = new TextNote { X = 150, Y = 120, Text = "stay" };
        var earlier = new TextNote { X = 10, Y = 10, Text = "earlier" };
        note.Previous = earlier;
        var record = new SpaceRecord
        {
            Figures = [[100, 100, 300, 100, 300, 200, 100, 200]],
            Strokes = [new InkStroke { Points = [100, 150, 300, 150] }],
            Notes = [note],
        };
        var twiceAsWide = new Matrix(2, 0, 0, 1, -100, 0); // left edge stays at 100

        record.Transform(twiceAsWide, n => new Point(n.X + 1, n.Y + 2));

        Assert.Equal([100, 100, 500, 100, 500, 200, 100, 200], record.Figures[0]);
        Assert.Equal([100, 150, 300, 150], record.Strokes[0].Points); // its start stays at 100; its length too
        Assert.Equal((151, 122), (note.X, note.Y));
        Assert.Equal((11, 12), (earlier.X, earlier.Y)); // its earlier version too, for undo
    }

    [Fact]
    public void WhileReshaping_NotesAreDrawnWhereTheyAreShown()
    {
        Gpu.Run(gpu =>
        {
            var square = SpaceGeometry.FromStroke([new(0, 0), new(200, 0), new(200, 100), new(0, 100)], new Vector(0, 0));
            using var ink = new InkLayer(gpu, 200, 100, 1, new Point(0, 0), square, Colors.White);
            var record = new SpaceRecord { Notes = [new TextNote { X = 10, Y = 10, Text = "Hello" }] };

            ink.Redraw(record, Matrix.Identity, placeNote: _ => new Point(120, 60));
            var pixels = ink.ReadPixels();

            Assert.All(Enumerable.Range(0, 50), y => Assert.Equal(0, pixels[y * 200 + 30])); // not at its old place
            Assert.Contains(Enumerable.Range(120, 60).SelectMany(x => Enumerable.Range(60, 20).Select(y => pixels[y * 200 + x])), p => p != 0);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

/// <summary>T and E belong to a space only while the mouse moved lately.</summary>
public class CursorActivityTests
{
    private static readonly TimeSpan Recent = TimeSpan.FromSeconds(3);

    [Fact]
    public void AMouseThatJustMoved_CountsAsMoving()
    {
        var cursor = new CursorActivity(Recent);

        Assert.True(cursor.Sample(new Point(10, 10), TimeSpan.FromSeconds(100)));
        Assert.True(cursor.Sample(new Point(12, 10), TimeSpan.FromSeconds(101)));
        Assert.True(cursor.Sample(new Point(12, 10), TimeSpan.FromSeconds(103.9)));
    }

    [Fact]
    public void AMouseResting_StopsCounting_UntilItMovesAgain()
    {
        var cursor = new CursorActivity(Recent);
        cursor.Sample(new Point(10, 10), TimeSpan.FromSeconds(100));

        Assert.False(cursor.Sample(new Point(10, 10), TimeSpan.FromSeconds(103)));
        Assert.False(cursor.Sample(new Point(10, 10), TimeSpan.FromSeconds(200)));
        Assert.True(cursor.Sample(new Point(11, 10), TimeSpan.FromSeconds(201)));
    }

    [Fact]
    public void TheWindow_IsAFewSeconds()
    {
        Assert.InRange(ShellController.LetterKeysAfterMouseMove, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }
}
