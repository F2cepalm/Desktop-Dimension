using System.Windows;
using System.Windows.Media;

namespace InfiniteSpace.Rendering;

/// <summary>One parallax layer of the star field.</summary>
/// <param name="CellSize">Stars live on a jittered grid of this pitch (DIPs); sparser for nearer layers.</param>
/// <param name="Density">Chance that a grid cell holds a star.</param>
/// <param name="Depth">Parallax factor: 1 moves with the look offset, 0 is fixed.</param>
/// <param name="Frames">Twinkle cycle: sprite frame per animation step (see <see cref="DreamRaster"/>).</param>
/// <param name="TwinkleSpeed">Animation steps per second.</param>
/// <param name="Colors">Palette of the layer.</param>
internal sealed record StarLayer(
    double CellSize,
    double Density,
    double Depth,
    int[] Frames,
    double TwinkleSpeed,
    Color[] Colors);

/// <summary>Look of a space, after Celeste's dream blocks: a black void, twinkling pixel stars, a wobbling white rim.</summary>
internal static class DreamStyle
{
    /// <summary>Size of one art pixel in DIPs. Spaces are pixel art drawn on this grid.</summary>
    public const double ArtPixel = 2;

    public static readonly Color Void = Color.FromRgb(0, 0, 0);

    public static readonly Color Rim = Colors.White;

    /// <summary>Resting wobble of the rim, in art pixels.</summary>
    public const double WobbleAmplitude = 1.3;

    /// <summary>Peak rim displacement of a click ripple, in art pixels.</summary>
    public const double RippleAmplitude = 2.5;

    /// <summary>How far stars shift per DIP of cursor travel (at depth 1).</summary>
    public const double LookParallax = 0.035;

    /// <summary>Slow drift of the whole field, DIP/s at depth 1.</summary>
    public static readonly Vector Drift = new(4.0, -2.0);

    public static readonly StarLayer[] Layers =
    [
        // Far: small, dense, barely moving.
        new(CellSize: 26, Density: 0.55, Depth: 0.3, Frames: [0, 0, 1, 0], TwinkleSpeed: 2.0,
            Colors: [Hex(0x5B6EE1), Hex(0xCC3B3B), Hex(0x7DAA64)]),
        // Middle.
        new(CellSize: 40, Density: 0.6, Depth: 0.6, Frames: [0, 1, 2, 1], TwinkleSpeed: 2.6,
            Colors: [Hex(0x5FCDE4), Hex(0x7FB25E), Hex(0xE0564C)]),
        // Near: large, sparse, bright.
        new(CellSize: 64, Density: 0.55, Depth: 1.0, Frames: [1, 2, 3, 2], TwinkleSpeed: 3.2,
            Colors: [Hex(0xFFEF11), Hex(0xFF00D0), Hex(0x08A310)]),
    ];

    private static Color Hex(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
