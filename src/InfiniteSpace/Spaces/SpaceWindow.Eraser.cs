using System.Windows;

namespace InfiniteSpace.Spaces;

/// <summary>The eraser on a space: a sweep takes away every stroke and note it passes over.</summary>
internal sealed partial class SpaceWindow
{
    /// <summary>How far apart (DIPs) the eraser looks along a sweep, so a fast one misses nothing.</summary>
    private const double EraseStep = 3;

    private long _eraseOrder;  // shared by everything one sweep erases: one undo brings it all back
    private Point _erasedTo;

    /// <summary>The eraser passed over ink here; everything a sweep erases has the same order.</summary>
    public event Action<SpaceWindow, IReadOnlyList<IInk>, long>? InkErased;

    private void EraseDown(Point at)
    {
        _gesture = Gesture.Erasing;
        _eraseOrder = InkOrder.Next();
        _erasedTo = at;
        PaintingStarted?.Invoke(this); // the palette and a text box get out of the way, as for paint
        EraseAt([at]);
    }

    private void EraseAlong(Point at)
    {
        var from = _erasedTo;
        _erasedTo = at;
        int steps = Math.Max(1, (int)Math.Ceiling((at - from).Length / EraseStep));
        EraseAt([.. Enumerable.Range(1, steps).Select(i => from + (at - from) * i / steps)]);
    }

    private void EraseAt(IEnumerable<Point> points)
    {
        var hit = new List<IInk>();
        foreach (var p in points)
        {
            if (InkHit.At(_record, _toScreen, p, _editing) is { } ink && !hit.Contains(ink))
                hit.Add(ink);
        }
        if (hit.Count > 0)
            InkErased?.Invoke(this, hit, _eraseOrder);
    }
}
