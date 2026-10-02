using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>
/// Text cards: text selected in an app becomes a space with the text inside (text wrap), and the space
/// hugs that text from then on. A card moves as one with its text, and reflows it when made narrower.
/// </summary>
internal sealed partial class SpaceManager
{
    /// <summary>The smallest type text wraps at: smaller selections are made this big, so they read.</summary>
    internal const double SmallestWrappedType = 11;

    /// <summary>Room between a card's edge and its text, at least clear of the rim and the edge's wobble.</summary>
    internal static double CardPadding(double fontSize) => SnapUp(Math.Max(10, fontSize * 0.75));

    /// <summary>The narrowest a card gets: a few letters between its margins.</summary>
    internal static double MinCardWidth(double fontSize) => 2 * CardPadding(fontSize) + 4 * fontSize;

    /// <summary>
    /// A card's outer size at outer width <paramref name="width"/> (no narrower than <see cref="MinCardWidth"/>):
    /// its note wrapped inside the margins, and as tall as that text plus the margins.
    /// </summary>
    internal static Size CardSize(TextNote note, double width)
    {
        double pad = CardPadding(note.FontSize);
        width = SnapUp(Math.Max(width, MinCardWidth(note.FontSize)));
        using var layout = NoteText.Layout(note.Text, note.FontSize, Math.Round(width - 2 * pad, 2), note.Bold, note.Italic);
        return new Size(width, SnapUp(layout.Metrics.Height + 2 * pad));
    }

    /// <summary>
    /// A text card at the top-left of <paramref name="area"/> (screen DIPs) on the layer of <paramref name="host"/>,
    /// as wide as the area and as tall as <paramref name="text"/> needs, wrapped inside even margins at
    /// <paramref name="fontSize"/> (no smaller than <see cref="SmallestWrappedType"/>). It never merges with other spaces.
    /// </summary>
    /// <param name="color">The text's colour in the app; the theme's ink is used when it wouldn't read on the space.</param>
    /// <returns>false when there is no text, or the area is empty.</returns>
    public bool WrapText(string text, Rect area, IntPtr host, double fontSize, bool bold, bool italic, Color? color)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "").TrimEnd();
        if (text.Trim().Length == 0 || area.IsEmpty)
            return false;
        var note = new TextNote
        {
            Text = text,
            FontSize = Math.Clamp(Math.Round(fontSize * 2) / 2, SmallestWrappedType, InkPen.MaxFontSize),
            Bold = bold,
            Italic = italic,
            Color = ReadableColor(color),
            Order = InkOrder.Next(),
        };
        var record = new SpaceRecord { Host = Describe(host), Notes = [note], Card = true };
        ShapeCard(record, new Point(Math.Floor(area.Left / Grid) * Grid, Math.Floor(area.Top / Grid) * Grid), area.Width);
        Place(record, host, crossing: []);
        Persist();
        return true;
    }

    /// <summary>
    /// Fits a card's record to its note at <paramref name="width"/> with its top-left at <paramref name="topLeft"/>
    /// (record coordinates): the note goes inside the margins, wrapped, and the outline round it.
    /// </summary>
    /// <returns>The card's outer bounds.</returns>
    internal static Rect ShapeCard(SpaceRecord record, Point topLeft, double width)
    {
        var note = record.Notes[0];
        var size = CardSize(note, width);
        double pad = CardPadding(note.FontSize);
        note.X = Math.Round(topLeft.X + pad, 2);
        note.Y = Math.Round(topLeft.Y + pad, 2);
        note.Width = Math.Round(size.Width - 2 * pad, 2);
        var box = new Rect(topLeft, size);
        var outline = SpaceGeometry.FromStroke([box.TopLeft, box.TopRight, box.BottomRight, box.BottomLeft], new Vector(0, 0));
        record.Figures = SpaceGeometry.Encode(outline, out var fillRule);
        record.FillRule = fillRule;
        return box;
    }

    private const double Grid = DreamStyle.ArtPixel;

    private static double SnapUp(double value) => Math.Ceiling(Math.Round(value / Grid, 6)) * Grid;

    /// <summary>"#RRGGBB" for a colour that reads on the theme's spaces (3:1 or more); null (the theme's ink) otherwise.</summary>
    internal string? ReadableColor(Color? color) =>
        color is { } c && Themes.Contrast(c, Themes.ChromeOf(Theme).Back) >= 3 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
}
