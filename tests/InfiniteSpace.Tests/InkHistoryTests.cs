using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class InkHistoryTests
{
    private static InkStroke Stroke(long order) => new() { Points = [order, order], Order = order };

    private static TextNote Note(long order) => new() { Text = $"note {order}", Order = order };

    [Fact]
    public void Undo_TakesTheNewestInk_AcrossStrokesAndNotes()
    {
        var space = new SpaceRecord { Strokes = [Stroke(1), Stroke(4)], Notes = [Note(2), Note(3)] };

        Assert.Same(space.Strokes[1], space.TakeNewestInk());
        Assert.Equal("note 3", Assert.IsType<TextNote>(space.TakeNewestInk()).Text);
        Assert.Equal("note 2", Assert.IsType<TextNote>(space.TakeNewestInk()).Text);
        Assert.Equal(1, Assert.IsType<InkStroke>(space.TakeNewestInk()).Order);
        Assert.Null(space.TakeNewestInk());
    }

    [Fact]
    public void Redo_PutsInkBack_SoUndoFindsItAgain()
    {
        var space = new SpaceRecord { Strokes = [Stroke(1)], Notes = [Note(2)] };

        var taken = space.TakeNewestInk()!;
        space.PutBack(taken);

        Assert.Single(space.Notes);
        Assert.Same(taken, space.TakeNewestInk());
    }

    [Fact]
    public void InkSavedBeforeOrdering_IsUndoneLastAddedFirst()
    {
        var space = new SpaceRecord { Strokes = [Stroke(0), Stroke(0)] };
        var second = space.Strokes[1];

        Assert.Same(second, space.TakeNewestInk());
    }

    [Fact]
    public void UndoingAnEdit_BringsTheNoteBackAsItWas_AndRedoEditsItAgain()
    {
        var original = Note(1);
        var space = new SpaceRecord { Notes = [original], Strokes = [Stroke(2)] };
        var edited = new TextNote { Text = "edited", X = 50, Order = 3 };

        space.Replace(original, edited);
        Assert.Equal(["edited"], space.Notes.Select(n => n.Text));

        Assert.Same(edited, space.TakeNewestInk());
        Assert.Same(original, Assert.Single(space.Notes));
        Assert.Single(space.Strokes); // the stroke painted before the edit stays

        space.PutBack(edited);
        Assert.Same(edited, Assert.Single(space.Notes));
    }

    [Fact]
    public void EditingANotesTextAway_RemovesIt_AndUndoBringsItBack()
    {
        var original = Note(1);
        var space = new SpaceRecord { Notes = [original] };

        space.Replace(original, new TextNote { Text = "", Order = 2 });

        Assert.All(space.Notes, n => Assert.Empty(n.Text)); // nothing left to show
        space.TakeNewestInk();
        Assert.Same(original, Assert.Single(space.Notes));
    }

    [Fact]
    public void EditingTwice_IsUndoneOneEditAtATime()
    {
        var first = Note(1);
        var space = new SpaceRecord { Notes = [first] };
        var second = new TextNote { Text = "second", Order = 2 };
        var third = new TextNote { Text = "third", Order = 3 };

        space.Replace(first, second);
        space.Replace(second, third);

        space.TakeNewestInk();
        Assert.Equal("second", Assert.Single(space.Notes).Text);
        space.TakeNewestInk();
        Assert.Equal("note 1", Assert.Single(space.Notes).Text);
    }

    [Fact]
    public void MovingASpace_MovesItsNotesEarlierVersionsToo()
    {
        var original = new TextNote { X = 10, Y = 10, Text = "before" };
        var space = new SpaceRecord { Notes = [original] };
        space.Replace(original, new TextNote { X = 30, Y = 10, Text = "after" });

        space.Transform(new System.Windows.Media.Matrix(1, 0, 0, 1, 100, 5));
        space.TakeNewestInk();

        Assert.Equal((110, 15), (space.Notes[0].X, space.Notes[0].Y));
    }

    [Fact]
    public void AnEditThatChangesNothing_IsNotAnEdit()
    {
        var note = new TextNote { X = 1, Y = 2, Text = "same", FontSize = 20, Color = "#FF5A5F", Width = 120 };
        var copy = new TextNote { X = 1, Y = 2, Text = "same", FontSize = 20, Color = "#FF5A5F", Width = 120 };

        Assert.True(SpaceManager.SameNote(note, copy));
        copy.Width = null;
        Assert.False(SpaceManager.SameNote(note, copy));
    }

    [Fact]
    public void InkOrder_AlwaysIncreases()
    {
        long a = InkOrder.Next(), b = InkOrder.Next(), c = InkOrder.Next();

        Assert.True(a < b && b < c);
        Assert.True(a >= DateTime.UtcNow.AddMinutes(-1).Ticks, "orders are wall-clock based");
    }

    [Fact]
    public void AMoveOfAStrokeAndANote_IsOneUndo_AndRedoPutsItBack()
    {
        var stroke = Stroke(1);
        var note = Note(2);
        var space = new SpaceRecord { Strokes = [stroke], Notes = [note] };
        var movedStroke = stroke.MovedBy(new System.Windows.Vector(5, 5), 10);
        var movedNote = note.MovedTo(new System.Windows.Point(5, 5), 10);
        space.Replace(stroke, movedStroke);
        space.Replace(note, movedNote);

        var batch = space.TakeNewestBatch();

        Assert.Equal(2, batch.Length);
        Assert.Equal([stroke], space.Strokes);
        Assert.Equal([note], space.Notes);

        foreach (var ink in batch.Reverse())
            space.PutBack(ink);
        Assert.Equal([movedStroke], space.Strokes);
        Assert.Equal([movedNote], space.Notes);
    }

    [Fact]
    public void InkSavedBeforeOrdering_StillComesBackOneAtATime()
    {
        var space = new SpaceRecord { Strokes = [Stroke(0), Stroke(0)] };

        Assert.Single(space.TakeNewestBatch());
        Assert.Single(space.Strokes);
    }

    [Fact]
    public void ATranslation_MovesAStroke_AndTheVersionBeforeItsMove()
    {
        var stroke = Stroke(1);
        var space = new SpaceRecord { Strokes = [stroke] };
        space.Replace(stroke, stroke.MovedBy(new System.Windows.Vector(10, 0), 2));

        space.Transform(new System.Windows.Media.Matrix(1, 0, 0, 1, 3, 4));

        Assert.Equal([14.0, 5.0], space.Strokes[0].Points);
        Assert.Equal([4.0, 5.0], stroke.Points);
    }

    [Fact]
    public void AnErasedSweep_IsOneUndo_AndLeavesNothingToHit()
    {
        var stroke = new InkStroke { Points = [0, 0, 10, 0], Thickness = 3, Order = 1 };
        var note = Note(2);
        var space = new SpaceRecord { Strokes = [stroke], Notes = [note] };
        space.Replace(stroke, stroke.Erased(9));
        space.Replace(note, note.Erased(9));

        Assert.Null(InkHit.At(space, System.Windows.Media.Matrix.Identity, new System.Windows.Point(5, 0)));
        Assert.Equal(2, space.TakeNewestBatch().Length);
        Assert.Equal([stroke], space.Strokes);
        Assert.Equal([note], space.Notes);
    }

    [Fact]
    public void MovedBy_KeepsThicknessColourAndGroup()
    {
        var stroke = new InkStroke { Points = [1, 2, 3, 4], Thickness = 7, Color = "#FF5A5F", Group = Guid.NewGuid(), Order = 1 };

        var moved = stroke.MovedBy(new System.Windows.Vector(1, -1), 2);

        Assert.Equal([2.0, 1.0, 4.0, 3.0], moved.Points);
        Assert.Equal((7.0, "#FF5A5F", stroke.Group, 2L), (moved.Thickness, moved.Color, moved.Group, moved.Order));
    }
}
