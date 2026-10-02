using System.IO;
using System.Windows.Media;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfiniteSpace.Tests;

public sealed class SpaceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Tests", Guid.NewGuid().ToString("N"));

    private SpaceStore CreateStore() => new(_directory, NullLogger<SpaceStore>.Instance);

    [Fact]
    public void Load_WithoutFile_IsEmpty()
    {
        Assert.Empty(CreateStore().Load());
    }

    [Fact]
    public void SavedSpaces_AreRestored()
    {
        var space = new SpaceRecord
        {
            FillRule = FillRule.EvenOdd,
            Figures = [[0, 0, 10.5, 0, 10.5, 20.25], [1, 1, 2, 1, 2, 2]],
        };

        CreateStore().Save([space]);
        var restored = Assert.Single(CreateStore().Load());

        Assert.Equal(space.Id, restored.Id);
        Assert.Equal(FillRule.EvenOdd, restored.FillRule);
        Assert.Equal(space.Figures, restored.Figures);
    }

    [Fact]
    public void ATextCard_StaysACard()
    {
        CreateStore().Save([new SpaceRecord { Card = true, Notes = [new TextNote { Text = "Card" }] }, new SpaceRecord()]);

        var restored = CreateStore().Load();

        Assert.Equal([true, false], restored.Select(r => r.Card));
    }

    [Fact]
    public void PaintAndText_AreSavedWithTheSpace()
    {
        var space = new SpaceRecord
        {
            Figures = [[0, 0, 100, 0, 100, 100]],
            Strokes = [new InkStroke { Points = [10, 10, 20, 25.5, 30, 12] }],
            Notes = [new TextNote { X = 12.5, Y = 40, Text = "hello\nworld ✨", FontSize = 16 }],
        };

        CreateStore().Save([space]);
        var restored = Assert.Single(CreateStore().Load());

        Assert.Equal([10, 10, 20, 25.5, 30, 12], Assert.Single(restored.Strokes).Points);
        Assert.Equal(InkStroke.DefaultThickness, restored.Strokes[0].Thickness);
        var note = Assert.Single(restored.Notes);
        Assert.Equal("hello\nworld ✨", note.Text);
        Assert.Equal((12.5, 40.0, 16.0), (note.X, note.Y, note.FontSize));
    }

    [Fact]
    public void InkColoursSizesWrapAndEditHistory_AreSavedWithTheSpace()
    {
        var before = new TextNote { X = 1, Y = 2, Text = "before", Order = 1 };
        var space = new SpaceRecord
        {
            Figures = [[0, 0, 100, 0, 100, 100]],
            Strokes = [new InkStroke { Points = [10, 10, 20, 20], Thickness = 12, Color = "#FF5A5F" }],
        };
        space.Notes.Add(before);
        space.Replace(before, new TextNote { X = 5, Y = 6, Text = "after", FontSize = 32, Color = "#3FA7FF", Width = 140, Order = 2 });

        CreateStore().Save([space]);
        var restored = Assert.Single(CreateStore().Load());

        var stroke = Assert.Single(restored.Strokes);
        Assert.Equal((12.0, "#FF5A5F"), (stroke.Thickness, stroke.Color));
        var note = Assert.Single(restored.Notes);
        Assert.Equal(("after", 32.0, "#3FA7FF", (double?)140), (note.Text, note.FontSize, note.Color, note.Width));
        // Undo after a restart still turns the edit back.
        restored.TakeNewestInk();
        Assert.Equal("before", Assert.Single(restored.Notes).Text);
    }

    [Fact]
    public void InkSavedBeforeColoursExisted_FollowsTheTheme()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, """
            {"Version":1,"Spaces":[{"FillRule":"Nonzero","Figures":[[0,0,10,0,10,10]],
              "Strokes":[{"Thickness":3,"Points":[1,1,2,2]}],
              "Notes":[{"X":1,"Y":1,"Text":"hi","FontSize":16}]}]}
            """);

        var restored = Assert.Single(store.Load());

        Assert.Null(restored.Strokes[0].Color);
        Assert.Null(restored.Notes[0].Color);
        Assert.Null(restored.Notes[0].Width);
        Assert.Null(restored.Notes[0].Previous);
    }

    [Fact]
    public void SpacesSavedBeforeInkExisted_LoadWithNoInk()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, """{"Version":1,"Spaces":[{"FillRule":"Nonzero","Figures":[[0,0,10,0,10,10]]}]}""");

        var restored = Assert.Single(store.Load());

        Assert.Empty(restored.Strokes);
        Assert.Empty(restored.Notes);
    }

    [Fact]
    public void MovesAndGroups_AreSavedWithTheSpace()
    {
        var group = Guid.NewGuid();
        var before = new InkStroke { Points = [1, 2], Order = 1 };
        var space = new SpaceRecord
        {
            Figures = [[0, 0, 10, 0, 10, 10]],
            Strokes = [new InkStroke { Points = [5, 6], Order = 2, Group = group, Previous = before }],
            Notes = [new TextNote { Text = "hi", Group = group, Order = 3 }],
        };

        CreateStore().Save([space]);
        var restored = Assert.Single(CreateStore().Load());

        Assert.Equal(group, restored.Strokes[0].Group);
        Assert.Equal([1.0, 2.0], restored.Strokes[0].Previous!.Points);
        Assert.Equal(group, restored.Notes[0].Group);
    }

    [Fact]
    public void InkSavedBeforeMovesAndGroups_HasNone()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, """{"Version":1,"Spaces":[{"Figures":[[0,0,10,0,10,10]],"Strokes":[{"Points":[1,2]}],"Notes":[{"Text":"hi"}]}]}""");

        var restored = Assert.Single(store.Load());

        Assert.Null(restored.Strokes[0].Group);
        Assert.Null(restored.Strokes[0].Previous);
        Assert.Null(restored.Notes[0].Group);
    }

    [Fact]
    public void ScreenNotes_AreSavedAsSheets_AndSpacesDontSayWhatTheyAre()
    {
        var store = CreateStore();
        store.Save([new SpaceRecord { Kind = SpaceKind.Sheet, Figures = [[0, 0, 10, 0, 10, 10]] }, new SpaceRecord { Figures = [[0, 0, 10, 0, 10, 10]] }]);

        var restored = store.Load();

        Assert.Equal([SpaceKind.Sheet, SpaceKind.Space], restored.Select(r => r.Kind));
        Assert.Contains("\"Sheet\"", File.ReadAllText(store.FilePath));
        store.Save([new SpaceRecord { Figures = [[0, 0, 10, 0, 10, 10]] }]);
        Assert.DoesNotContain("\"Kind\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void SpacesSavedBeforeScreenNotes_AreSpaces()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, """{"Version":1,"Spaces":[{"Figures":[[0,0,10,0,10,10]]}]}""");

        Assert.Equal(SpaceKind.Space, Assert.Single(store.Load()).Kind);
    }

    [Fact]
    public void CorruptFile_StartsEmptyAndKeepsACopy()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Empty(store.Load());
        Assert.Equal("{ not json", File.ReadAllText(store.FilePath + ".corrupt"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
