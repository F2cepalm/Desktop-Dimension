namespace InfiniteSpace.Rendering;

/// <summary>
/// How big to make surfaces and bitmaps that are drawn again at every new shape: with room to spare,
/// so a space reshaped or wobbling frame by frame keeps drawing into the same ones.
/// </summary>
internal static class Headroom
{
    /// <summary>Sizes go in steps of this many pixels.</summary>
    public const int Step = 256;

    /// <summary>
    /// The size (pixels, one side) to make something that must now hold <paramref name="needed"/>,
    /// having held <paramref name="had"/> (0 for nothing yet): in whole steps, and when it grows, half
    /// as big again, so something growing every frame is made anew only now and then.
    /// </summary>
    public static int Size(int needed, int had) =>
        RoundUp(had > 0 && needed > had ? Math.Max(needed, had + had / 2) : needed);

    /// <summary>
    /// How much of a side to make room for: all of it, and as much again as <paramref name="room"/> asks
    /// beyond 1 (a space picked up to be reshaped makes room at once, so its first frames don't grow it).
    /// </summary>
    public static int Wanted(int side, double room) => room <= 1 ? side : (int)Math.Min(int.MaxValue / 2, Math.Ceiling(side * room));

    private static int RoundUp(int pixels) => Math.Max(Step, (pixels + Step - 1) / Step * Step);
}
