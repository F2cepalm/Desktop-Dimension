using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>Where screen notes are, and where they take the mouse.</summary>
internal static class Sheets
{
    /// <summary>Room (DIPs) kept around screen notes' ink inside their window.</summary>
    public const double Margin = 24;

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
    /// short pieces (6 points, overlapping by one), so a diagonal stroke doesn't block a whole box of the app.
    /// </summary>
    public static IEnumerable<Rect> HitRects(SpaceRecord record, Matrix toScreen)
    {
        const int Piece = 6;
        foreach (var stroke in record.Strokes)
        {
            var placed = stroke.Placed(toScreen, record.Anchor);
            double reach = stroke.Thickness / 2 + InkHit.Reach;
            var xy = stroke.Points;
            int count = xy.Length / 2;
            for (int start = 0; start < count; start += Piece - 1)
            {
                var box = Rect.Empty;
                for (int i = start; i < Math.Min(count, start + Piece); i++)
                    box.Union(placed.Transform(new Point(xy[2 * i], xy[2 * i + 1])));
                box.Inflate(reach, reach);
                yield return box;
                if (start + Piece >= count)
                    break;
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
