using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class SpaceMergeTests
{
    private static PathGeometry Square(double x, double y, double size) =>
        SpaceGeometry.FromStroke([new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)], new Vector(0, 0));

    private static SpaceManager.SpacePart Part(PathGeometry outline, string note)
    {
        var record = new SpaceRecord
        {
            Figures = SpaceGeometry.Encode(outline, out var fillRule),
            FillRule = fillRule,
            Notes = [new TextNote { Text = note }],
            Strokes = [new InkStroke { Points = [1, 2, 3, 4] }],
        };
        return new SpaceManager.SpacePart(record, SpaceGeometry.Decode(record));
    }

    [Fact]
    public void CrossingOutlines_Overlap()
    {
        Assert.True(SpaceGeometry.Overlap(Square(0, 0, 100), Square(50, 50, 100)));
    }

    [Fact]
    public void OutlinesWithOverlappingBoundsButNoSharedArea_DoNotOverlap()
    {
        // Two triangles sharing a bounding box but lying on either side of its diagonal.
        var upper = SpaceGeometry.FromStroke([new(0, 0), new(100, 0), new(0, 100)], new Vector(0, 0));
        var lower = SpaceGeometry.FromStroke([new(100, 10), new(100, 100), new(10, 100)], new Vector(0, 0));

        Assert.False(SpaceGeometry.Overlap(upper, lower));
        Assert.False(SpaceGeometry.Overlap(Square(0, 0, 100), Square(300, 0, 100)));
    }

    [Fact]
    public void Merge_UnitesOutlinesAndKeepsAllInk_UnderTheOldestIdentity()
    {
        var oldest = Part(Square(0, 0, 100), "first");
        var newer = Part(Square(50, 50, 100), "second");

        var merged = SpaceManager.Merge([oldest, newer]);

        Assert.Equal(oldest.Record.Id, merged.Id);
        Assert.Equal(oldest.Record.CreatedAt, merged.CreatedAt);
        Assert.Equal(10_000 + 10_000 - 2_500, SpaceGeometry.Area(SpaceGeometry.Decode(merged)), precision: 0);
        Assert.Single(merged.Figures); // one rim around the whole shape
        Assert.Equal(["first", "second"], merged.Notes.Select(n => n.Text));
        Assert.Equal(2, merged.Strokes.Count);
    }

    [Fact]
    public void Merge_OfOneSpace_LeavesItAsItIs()
    {
        var only = Part(Square(0, 0, 100), "only");

        Assert.Same(only.Record, SpaceManager.Merge([only]));
    }

    [Fact]
    public void OnlySpacesOnTheSameLayer_Merge()
    {
        IntPtr editor = new(0x1234), browser = new(0x5678), desktop = IntPtr.Zero;

        Assert.True(SpaceManager.SameLayer(editor, editor));
        Assert.True(SpaceManager.SameLayer(desktop, desktop));
        Assert.False(SpaceManager.SameLayer(editor, browser));
        Assert.False(SpaceManager.SameLayer(editor, desktop));
    }
}
