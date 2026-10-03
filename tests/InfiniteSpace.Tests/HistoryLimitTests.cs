using System.IO;
using System.Windows;
using System.Windows.Threading;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfiniteSpace.Tests;

public sealed class HistoryLimitTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Tests", Guid.NewGuid().ToString("N"));

    private static InkStroke Stroke(long order) => new() { Points = [order, order], Order = order };

    private static int UndoAll(SpaceRecord space)
    {
        int undone = 0;
        while (space.TakeNewestBatch().Length > 0)
            undone++;
        return undone;
    }

    [Fact]
    public void UndoGoesBackAsManyChangesAsKept_ThenStops_LeavingTheInk()
    {
        var space = new SpaceRecord { Strokes = [Stroke(1)] };
        for (long order = 2; order <= 12; order++)
            space.Replace(space.Strokes[0], space.Strokes[0].MovedBy(new Vector(1, 0), order));

        Assert.True(space.TrimHistory(steps: 4));

        Assert.Equal(4, UndoAll(space));
        var stroke = Assert.Single(space.Strokes);
        Assert.Equal(8, stroke.Order); // four moves back, and no further
        Assert.Null(stroke.Previous);
    }

    [Fact]
    public void ErasedInkOutOfReach_IsLetGo_AndWithinReach_ComesBack()
    {
        SpaceRecord Erased()
        {
            var space = new SpaceRecord { Strokes = [Stroke(1), Stroke(2)] };
            space.Replace(space.Strokes[0], space.Strokes[0].Erased(3));
            space.Strokes.Add(Stroke(4));
            space.Strokes.Add(Stroke(5));
            return space;
        }

        var far = Erased();
        far.TrimHistory(steps: 2); // the two newest strokes: the erasure is out of reach
        Assert.Equal([2L, 4L, 5L], far.Strokes.Select(s => s.Order));

        var near = Erased();
        near.TrimHistory(steps: 3); // the erasure is the third change back: undo still brings the stroke back
        Assert.Equal(3, UndoAll(near));
        Assert.Equal([1L, 2L], near.Strokes.Select(s => s.Order).Order());
    }

    [Fact]
    public void WithinTheLimit_NothingChanges()
    {
        var space = new SpaceRecord { Strokes = [Stroke(1)], Notes = [new TextNote { Text = "a", Order = 2 }] };
        space.Replace(space.Strokes[0], space.Strokes[0].MovedBy(new Vector(5, 5), 3));

        Assert.False(space.TrimHistory(steps: 10));

        Assert.Equal(3, UndoAll(space));
        Assert.Empty(space.Strokes);
        Assert.Empty(space.Notes);
    }

    [Fact]
    public void InkFromBeforeOrdering_IsLeftAlone()
    {
        var space = new SpaceRecord { Strokes = [Stroke(0), Stroke(0)] };

        Assert.False(space.TrimHistory(steps: 1));
        Assert.Equal(2, UndoAll(space));
    }

    [Fact]
    public void AManyTimesMovedSpace_StaysBounded()
    {
        var space = new SpaceRecord { Strokes = [.. Enumerable.Range(1, 20).Select(i => Stroke(i))] };
        long order = 100;
        for (int move = 0; move < 1000; move++)
        {
            order++;
            foreach (var stroke in space.Strokes.ToList())
                space.Replace(stroke, stroke.MovedBy(new Vector(1, 0), order)); // all twenty moved as one change
            if (move % 50 == 0)
                space.TrimHistory();
        }
        space.TrimHistory();

        int versions = space.Strokes.Sum(s => Chain(s));
        Assert.True(versions <= 20 * (SpaceRecord.UndoSteps + 1), $"{versions} versions kept");
        Assert.Equal(SpaceRecord.UndoSteps, UndoAll(space));

        static int Chain(InkStroke stroke) => stroke.Previous is null ? 1 : 1 + Chain(stroke.Previous);
    }

    [Fact]
    public void WhereUndoStops_IsSaved()
    {
        var space = new SpaceRecord { Figures = [[0, 0, 10, 0, 10, 10]], Strokes = [Stroke(1)] };
        space.Replace(space.Strokes[0], space.Strokes[0].MovedBy(new Vector(1, 0), 2));
        space.TrimHistory(steps: 1);
        var store = new SpaceStore(_directory, NullLogger<SpaceStore>.Instance);

        store.Save([space]);
        var restored = Assert.Single(store.Load());

        Assert.Equal(1, UndoAll(restored));
        Assert.Single(restored.Strokes);
    }

    [Fact]
    public void Flush_SavesWhatWasAskedFor_RightAway()
    {
        Gpu.RunOnSta(() =>
        {
            var store = new SpaceStore(_directory, NullLogger<SpaceStore>.Instance);
            var records = new List<SpaceRecord> { new() { Figures = [[0, 0, 10, 0, 10, 10]], Strokes = [Stroke(1)] } };
            var saves = new SaveQueue(store, NullLogger.Instance, Dispatcher.CurrentDispatcher, () => records);

            saves.Request();
            records[0].Strokes.Add(Stroke(2)); // changed again before the save was due
            saves.Request();
            saves.Flush();

            Assert.Equal(2, Assert.Single(store.Load()).Strokes.Count);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
