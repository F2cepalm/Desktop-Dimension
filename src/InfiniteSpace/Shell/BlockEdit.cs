using System.Windows;

namespace InfiniteSpace.Shell;

/// <summary>Which parts of a block a drag moves. <see cref="Move"/> moves every edge: the whole block.</summary>
[Flags]
internal enum BlockGrip
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Move = Left | Top | Right | Bottom,
}

/// <summary>Rectangle editing for spawned blocks: dragging edges, growing sides, scaling, snapping.</summary>
internal static class BlockEdit
{
    /// <summary>Moves the gripped edges by <paramref name="delta"/>, never letting the block get smaller than <paramref name="min"/>.</summary>
    public static Rect Drag(Rect rect, BlockGrip grip, Vector delta, double min)
    {
        double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
        if (grip.HasFlag(BlockGrip.Left)) left += delta.X;
        if (grip.HasFlag(BlockGrip.Right)) right += delta.X;
        if (grip.HasFlag(BlockGrip.Top)) top += delta.Y;
        if (grip.HasFlag(BlockGrip.Bottom)) bottom += delta.Y;

        if (right - left < min)
        {
            if (grip.HasFlag(BlockGrip.Left)) left = right - min;
            else right = left + min;
        }
        if (bottom - top < min)
        {
            if (grip.HasFlag(BlockGrip.Top)) top = bottom - min;
            else bottom = top + min;
        }
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>Pushes one side outwards (positive <paramref name="amount"/>) or pulls it in (negative).</summary>
    public static Rect Grow(Rect rect, BlockGrip side, double amount, double min)
    {
        var outward = side switch
        {
            BlockGrip.Left => new Vector(-amount, 0),
            BlockGrip.Right => new Vector(amount, 0),
            BlockGrip.Top => new Vector(0, -amount),
            BlockGrip.Bottom => new Vector(0, amount),
            _ => new Vector(0, 0),
        };
        return Drag(rect, side, outward, min);
    }

    /// <summary>Scales about the centre.</summary>
    public static Rect Scale(Rect rect, double factor, double min)
    {
        double width = Math.Max(min, rect.Width * factor), height = Math.Max(min, rect.Height * factor);
        var centre = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        return new Rect(centre.X - width / 2, centre.Y - height / 2, width, height);
    }

    /// <summary>How far past the screen's edge a block may be stretched, in DIPs.</summary>
    public const double Spill = 40;

    /// <summary>
    /// A reshape from <paramref name="before"/> to <paramref name="after"/> with every edge it pushed
    /// outward stopped at <paramref name="area"/>'s (or where that edge was, if it was already past it).
    /// Edges pulled inward are left as they are. For reshapes only: a move would be cut short.
    /// </summary>
    public static Rect Contain(Rect before, Rect after, Rect area)
    {
        double left = after.Left, top = after.Top, right = after.Right, bottom = after.Bottom;
        if (left < before.Left) left = Math.Max(left, Math.Min(before.Left, area.Left));
        if (top < before.Top) top = Math.Max(top, Math.Min(before.Top, area.Top));
        if (right > before.Right) right = Math.Min(right, Math.Max(before.Right, area.Right));
        if (bottom > before.Bottom) bottom = Math.Min(bottom, Math.Max(before.Bottom, area.Bottom));
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>Snaps the edges to a grid (the art-pixel grid keeps the block's rim straight and crisp).</summary>
    public static Rect Snap(Rect rect, double grid)
    {
        double left = Math.Round(rect.Left / grid) * grid, top = Math.Round(rect.Top / grid) * grid;
        double right = Math.Max(left + grid, Math.Round(rect.Right / grid) * grid);
        double bottom = Math.Max(top + grid, Math.Round(rect.Bottom / grid) * grid);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>What a press at <paramref name="point"/> would grab: an edge, a corner, the body, or nothing.</summary>
    public static BlockGrip GripAt(Rect rect, Point point, double margin)
    {
        var reach = rect;
        reach.Inflate(margin, margin);
        if (!reach.Contains(point))
            return BlockGrip.None;

        var grip = BlockGrip.None;
        if (Math.Abs(point.X - rect.Left) <= margin) grip |= BlockGrip.Left;
        else if (Math.Abs(point.X - rect.Right) <= margin) grip |= BlockGrip.Right;
        if (Math.Abs(point.Y - rect.Top) <= margin) grip |= BlockGrip.Top;
        else if (Math.Abs(point.Y - rect.Bottom) <= margin) grip |= BlockGrip.Bottom;

        return grip != BlockGrip.None ? grip : rect.Contains(point) ? BlockGrip.Move : BlockGrip.None;
    }
}
