using System.IO;
using System.Text;
using System.Windows;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class SpaceFileTests
{
    private static SpaceRecord Shown()
    {
        var older = new TextNote { X = 150, Y = 130, Text = "Old", Order = 1 };
        return new SpaceRecord
        {
            Figures = [[100, 100, 400, 100, 400, 300, 100, 300]],
            Card = true,
            Host = new HostInfo { App = "app.exe", Frame = new Rect(0, 0, 800, 600) },
            Strokes = [new InkStroke { Points = [120, 120, 200, 200], Thickness = 3, Color = "#FF0000", Order = 2, Previous = new InkStroke { Points = [1, 1, 2, 2] } }],
            Notes =
            [
                new TextNote { X = 150, Y = 130, Text = "Plan: Q4/launch\nsecond line", FontSize = 18, Bold = true, Color = "#00FF00", Order = 3, Previous = older },
                new TextNote { X = 110, Y = 250, Text = "file text", Order = 4, File = new NoteFile { Path = @"C:\a.md", Markdown = true, Height = 80, Stored = @"notes\x.md" } },
            ],
        };
    }

    private static SpaceRecord RoundTrip(SpaceRecord snapshot)
    {
        using var stream = new MemoryStream();
        SpaceFile.Write(stream, snapshot, "Dreamy");
        stream.Position = 0;
        return SpaceFile.Read(stream);
    }

    [Fact]
    public void ASavedSpace_ComesBackAsItWas_ItsTopLeftAtTheOrigin()
    {
        var shown = Shown();

        var read = RoundTrip(SpaceFile.Snapshot(shown));

        Assert.Equal(new Rect(0, 0, 300, 200), SpaceGeometry.Decode(read).Bounds);
        Assert.True(read.Card);
        var stroke = Assert.Single(read.Strokes);
        Assert.Equal(new double[] { 20, 20, 100, 100 }, stroke.Points);
        Assert.Equal(3.0, stroke.Thickness);
        Assert.Equal("#FF0000", stroke.Color);
        var note = read.Notes[0];
        Assert.Equal((50.0, 30.0, "Plan: Q4/launch\nsecond line", 18.0, true, "#00FF00"), (note.X, note.Y, note.Text, note.FontSize, note.Bold, note.Color));
        Assert.Equal("file text", read.Notes[1].Text); // an opened file's text is in the file itself
        Assert.True(read.Notes[1].File is { Markdown: true, Stored: null });
    }

    [Fact]
    public void ASave_HasNoHistory_NoApp_AndANewId_AndLeavesTheSpaceAlone()
    {
        var shown = Shown();

        var read = RoundTrip(SpaceFile.Snapshot(shown));

        Assert.Null(read.Host);
        Assert.Null(read.Strokes[0].Previous);
        Assert.Null(read.Notes[0].Previous);
        Assert.NotEqual(shown.Id, read.Id);
        Assert.Equal(150, shown.Notes[0].X); // the space itself is untouched
        Assert.NotNull(shown.Notes[0].Previous);
        Assert.Equal(@"notes\x.md", shown.Notes[1].File!.Stored);
    }

    [Theory]
    [InlineData("{\"Format\":\"Something.Else\",\"Version\":1,\"Space\":{\"Figures\":[[0,0,1,0,1,1]]}}")]
    [InlineData("{\"Format\":\"InfiniteSpace.Space\",\"Version\":1}")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void AnythingElse_IsRefused(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        Assert.Throws<SpaceFileException>(() => SpaceFile.Read(stream));
    }

    [Fact]
    public void BinaryFiles_AreRefused()
    {
        using var stream = new MemoryStream([0x00, 0xA4, 0x03, 0x1F, 0xFF, 0xFE, 0x80, 0x00]);

        Assert.Throws<SpaceFileException>(() => SpaceFile.Read(stream));
    }

    [Fact]
    public void ASpaceFromANewerVersion_IsRefused_WithAReason()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Format\":\"InfiniteSpace.Space\",\"Version\":2,\"Space\":{\"Figures\":[[0,0,1,0,1,1]]}}"));

        var refused = Assert.Throws<SpaceFileException>(() => SpaceFile.Read(stream));
        Assert.Contains("newer", refused.Message);
    }

    [Fact]
    public void TheSuggestedName_IsTheFirstLine_MadeSafe_OrTheTime()
    {
        var now = new DateTime(2026, 10, 2, 14, 30, 0);

        Assert.Equal("Plan Q4 launch", SpaceFile.SuggestedName(new SpaceRecord { Notes = [new TextNote { Text = "Plan: Q4/launch\nmore" }] }, now));
        Assert.Equal(40, SpaceFile.SuggestedName(new SpaceRecord { Notes = [new TextNote { Text = new string('a', 90) }] }, now).Length);
        Assert.Equal("Space 2026-10-02 14-30", SpaceFile.SuggestedName(new SpaceRecord(), now));
        Assert.Equal("Space 2026-10-02 14-30", SpaceFile.SuggestedName(new SpaceRecord { Notes = [new TextNote { Text = "  ???  " }] }, now));
    }
}
