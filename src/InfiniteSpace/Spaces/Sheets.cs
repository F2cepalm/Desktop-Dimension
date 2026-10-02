using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>Where screen notes are, and where they take the mouse.</summary>
internal static class Sheets
{
    /// <summary>Room (DIPs) kept around screen notes' ink inside their window.</summary>
    public const double Margin = 24;

    /// <summary>How big (DIPs) a piece of a stroke may get both ways before a new one starts (see <see cref="HitRects"/>).</summary>
    private const double PieceSide = 12;

    /// <summary>Most points in one piece of a stroke.</summary>
    private const int PiecePoints = 64;

    /// <summary>Everything on the sheet as shown on screen, plus <see cref="Margin"/>; null when it has no ink.</summary>
    public static Rect? Bounds(SpaceRecord record, Matrix toScreen)
    {
        var area = Rect.Empty;
        foreach (var stroke in record.Strokes)
            area.Union(InkHit.Bounds(stroke, toScreen, record.Anchor));
        foreach (var note in record.Notes)
        {
            if (note.Text.Length > 0)
                area.Union(InkHit.Bounds(note, toScreen, record.Anchor));
        }
        if (area.IsEmpty)
            return null;
        area.Inflate(Margin, Margin);
        return area;
    }

    /// <summary>
    /// Rectangles (screen DIPs) where screen notes take the mouse: each note's box, and each stroke in
    /// pieces that overlap by a point. A piece grows along the stroke until it would be more than
    /// <see cref="PieceSide"/> both wide and tall, so a diagonal stroke doesn't block a whole box of the
    /// app while a straight one is a few long rectangles (each is a region call).
    /// </summary>
    public static IEnumerable<Rect> HitRects(SpaceRecord record, Matrix toScreen)
    {
        foreach (var stroke in record.Strokes)
        {
            var placed = stroke.Placed(toScreen, record.Anchor);
            double reach = stroke.Thickness / 2 + InkHit.Reach;
            var xy = stroke.Points;
            int count = xy.Length / 2;
            int start = 0;
            while (start < count)
            {
                var box = Rect.Empty;
                int i = start;
                for (; i < count; i++)
                {
                    var grown = box;
                    grown.Union(placed.Transform(new Point(xy[2 * i], xy[2 * i + 1])));
                    // At least two points a piece, so the next piece starts further on.
                    if (i - start >= 2 && ((grown.Width > PieceSide && grown.Height > PieceSide) || i - start >= PiecePoints))
                        break;
                    box = grown;
                }
                box.Inflate(reach, reach);
                yield return box;
                if (i >= count)
                    break;
                start = i - 1;
            }
        }
        foreach (var note in record.Notes)
        {
            if (note.Text.Length == 0)
                continue;
            var box = NoteText.Bounds(note, toScreen);
            box.Inflate(InkHit.Reach, InkHit.Reach);
            yield return box;
        }
    }
}
