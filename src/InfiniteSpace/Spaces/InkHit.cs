using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>What the hand finds on a space: ink under a point, in a rectangle, and with its group.</summary>
internal static class InkHit
{
    /// <summary>How far around ink (DIPs) the mouse still counts as on it.</summary>
    public const double Reach = 4;

    /// <summary>Where a stroke or note is on screen (DIPs), as shown through <paramref name="toScreen"/>.</summary>
    public static Rect Bounds(IInk ink, Matrix toScreen, Point? anchor)
    {
        if (ink is TextNote note)
            return NoteText.Bounds(note, toScreen);
        var stroke = (InkStroke)ink;
        var box = stroke.Box; // strokes are only ever moved on screen, so their box just moves too
        if (box.IsEmpty)
            return box;
        var placed = stroke.Placed(toScreen, anchor);
        box.Offset(placed.OffsetX, placed.OffsetY);
        box.Inflate(stroke.Thickness / 2, stroke.Thickness / 2);
        return box;
    }

    /// <summary>The topmost ink at a screen point: notes (drawn over strokes) first, newest first. Skips <paramref name="skip"/> and emptied notes.</summary>
    public static IInk? At(SpaceRecord record, Matrix toScreen, Point at, TextNote? skip = null)
    {
        TextNote? note = null;
        foreach (var candidate in record.Notes)
        {
            if (candidate == skip || candidate.Text.Length == 0 || (note is not null && candidate.Order < note.Order))
                continue;
            var box = NoteText.Bounds(candidate, toScreen);
            box.Inflate(Reach, Reach);
            if (box.Contains(at))
                note = candidate;
        }
        if (note is not null)
            return note;

        InkStroke? stroke = null;
        foreach (var candidate in record.Strokes)
        {
            if (stroke is not null && candidate.Order < stroke.Order)
                continue;
            var box = Bounds(candidate, toScreen, record.Anchor);
            if (box.IsEmpty)
                continue; // erased
            box.Inflate(Reach, Reach);
            if (!box.Contains(at))
                continue; // a cheap look at its box first: the mouse is over most strokes' boxes rarely
            if (OnStroke(candidate, candidate.Placed(toScreen, record.Anchor), at, Reach))
                stroke = candidate;
        }
        return stroke;
    }

    /// <summary>Strokes with a point inside <paramref name="area"/>, and notes whose box touches it (screen DIPs).</summary>
    public static List<IInk> Touching(SpaceRecord record, Matrix toScreen, Rect area, TextNote? skip = null)
    {
        var found = new List<IInk>();
        foreach (var stroke in record.Strokes)
        {
            // Points are at most about a DIP apart, so a rectangle across a stroke always holds one.
            if (!Bounds(stroke, toScreen, record.Anchor).IntersectsWith(area))
                continue;
            var placed = stroke.Placed(toScreen, record.Anchor);
            var xy = stroke.Points;
            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                if (area.Contains(placed.Transform(new Point(xy[i], xy[i + 1]))))
                {
                    found.Add(stroke);
                    break;
                }
            }
        }
        foreach (var note in record.Notes)
        {
            if (note != skip && note.Text.Length > 0 && NoteText.Bounds(note, toScreen).IntersectsWith(area))
                found.Add(note);
        }
        return found;
    }

    /// <summary>The item with every other live item of its group (just the item when it has none).</summary>
    public static List<IInk> WithGroup(SpaceRecord record, IInk item)
    {
        if (item.Group is not { } group)
            return [item];
        var members = new List<IInk>();
        foreach (var stroke in record.Strokes)
        {
            if (stroke.Group == group)
                members.Add(stroke);
        }
        foreach (var note in record.Notes)
        {
            if (note.Group == group && note.Text.Length > 0)
                members.Add(note);
        }
        return members;
    }

    /// <summary>Whether a screen point is within <paramref name="reach"/> of a stroke placed by <paramref name="placed"/>.</summary>
    public static bool OnStroke(InkStroke stroke, Matrix placed, Point at, double reach)
    {
        var xy = stroke.Points;
        if (xy.Length < 2)
            return false;
        double near = stroke.Thickness / 2 + reach;
        var previous = placed.Transform(new Point(xy[0], xy[1]));
        if (xy.Length < 4)
            return (at - previous).Length <= near;
        for (int i = 2; i + 1 < xy.Length; i += 2)
        {
            var next = placed.Transform(new Point(xy[i], xy[i + 1]));
            if (DistanceToSegment(at, previous, next) <= near)
                return true;
            previous = next;
        }
        return false;
    }

    /// <summary>How far a point is from the segment between <paramref name="a"/> and <paramref name="b"/>.</summary>
    internal static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-12)
            return (p - a).Length;
        double t = Math.Clamp((p - a) * ab / lengthSquared, 0, 1); // Vector * Vector is the dot product
        return (p - (a + t * ab)).Length;
    }
}
