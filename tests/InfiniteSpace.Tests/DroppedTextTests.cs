using System.Text.Json;
using System.Windows;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

/// <summary>Text dragged in from other apps, fitted to the space it is dropped on; and bold and italic notes.</summary>
public class DroppedTextTests
{
    private const string Heading = "Контекстные spaces: рисунки и заметки, «приклеенные» к конкретному окну или приложению.";

    [Fact]
    public void BigTypeDroppedOnANarrowSpace_ShrinksUntilEveryWordAndLineFits()
    {
        var space = new Rect(1000, 100, 240, 380);
        var (at, size, width) = SpaceManager.FitDroppedText(Heading, space, new Point(1060, 140), 72, bold: true, italic: false);

        Assert.True(size < 72, $"still {size}");
        Assert.True(size >= SpaceManager.SmallestDroppedType);
        var note = new TextNote { Text = Heading, FontSize = size, Width = width, Bold = true };
        using var layout = NoteText.Layout(note);
        var inside = space;
        inside.Inflate(-11.5, -11.5);
        Assert.True(layout.Metrics.Width <= inside.Width, $"a word runs over: {layout.Metrics.Width} across {inside.Width}");
        Assert.True(inside.Contains(new Rect(at, NoteText.Measure(note))), $"{new Rect(at, NoteText.Measure(note))} is not inside {inside}");
    }

    [Fact]
    public void TheLargestSizeThatFits_IsTaken_NotJustAnySmallerOne()
    {
        var space = new Rect(0, 0, 240, 380);
        var (_, size, width) = SpaceManager.FitDroppedText(Heading, space, new Point(20, 20), 72, bold: true, italic: false);
        var bigger = new TextNote { Text = Heading, FontSize = size + 1, Width = width, Bold = true };
        using var layout = NoteText.Layout(bigger);
        Assert.True(layout.Metrics.Width > 216.5 || layout.Metrics.Height > 356.5, $"{size + 1} would have fitted too");
    }

    [Fact]
    public void ShortText_KeepsThePensSize_AndItsLines_FirstLineCentredOnTheDrop()
    {
        var space = new Rect(0, 0, 800, 600);
        var (at, size, width) = SpaceManager.FitDroppedText("A few words", space, new Point(300, 200), 16, bold: false, italic: false);

        Assert.Equal(16, size);
        Assert.Null(width);
        Assert.Equal(300, at.X);
        Assert.Equal(200 - 16 * NoteText.LineSpacing / 2, at.Y, 2);
    }

    [Fact]
    public void TextDroppedNearTheRightEdge_MovesLeftToStayOnTheSpace()
    {
        var space = new Rect(0, 0, 800, 600);
        var (at, size, width) = SpaceManager.FitDroppedText("A sentence that needs some room across", space, new Point(780, 590), 24, bold: false, italic: false);
        var box = NoteText.Measure(new TextNote { Text = "A sentence that needs some room across", FontSize = size, Width = width });

        Assert.Equal(24, size);
        Assert.True(at.X + box.Width <= 788.5, $"runs to {at.X + box.Width}");
        Assert.True(at.Y + box.Height <= 588.5, $"runs down to {at.Y + box.Height}");
    }

    [Fact]
    public void AParagraph_WrapsAtTheSpacesWidth_NotWiderThanReadsWell()
    {
        string paragraph = string.Join(" ", Enumerable.Repeat("words that go on and on", 30));
        var (_, size, width) = SpaceManager.FitDroppedText(paragraph, new Rect(0, 0, 3000, 2000), new Point(100, 100), 16, bold: false, italic: false);

        Assert.Equal(16, size);
        Assert.Equal(NoteDocument.DefaultWidth, width);
    }

    [Fact]
    public void BoldAndItalic_ChangeHowTheTextIsLaidOut()
    {
        var plain = NoteText.Measure(new TextNote { Text = "Some words in a line" });
        var bold = NoteText.Measure(new TextNote { Text = "Some words in a line", Bold = true });
        Assert.True(bold.Width > plain.Width);

        // The same note, restyled, is measured afresh (not taken from what was measured before).
        var note = new TextNote { Text = "Some words in a line" };
        Assert.Equal(plain, NoteText.Measure(note));
        note.Bold = true;
        Assert.Equal(bold, NoteText.Measure(note));
    }

    [Fact]
    public void BoldAndItalic_AreSaved_OnlyWhenOn()
    {
        string styled = JsonSerializer.Serialize(new TextNote { Text = "x", Bold = true, Italic = true });
        string plain = JsonSerializer.Serialize(new TextNote { Text = "x" });

        Assert.Contains("\"Bold\":true", styled);
        Assert.DoesNotContain("Bold", plain);
        Assert.DoesNotContain("Italic", plain);
        var back = JsonSerializer.Deserialize<TextNote>(styled)!;
        Assert.True(back.Bold && back.Italic);
    }

    [Fact]
    public void RestylingANote_IsAnEdit()
    {
        var note = new TextNote { Text = "x" };
        Assert.False(SpaceManager.SameNote(note, new TextNote { Text = "x", Italic = true }));
        Assert.True(SpaceManager.SameNote(note, new TextNote { Text = "x" }));
    }
}
