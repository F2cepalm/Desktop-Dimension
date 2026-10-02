using System.Windows.Media;

namespace InfiniteSpace.Spaces;

/// <summary>
/// What new paint and text look like: a colour (null: the theme's own ink, which changes with the
/// theme), the width of paint strokes and the size of type, in DIPs, and whether type is bold or italic.
/// </summary>
internal sealed record InkPen(string? Color, double Thickness, double FontSize, bool Bold = false, bool Italic = false)
{
    public const double MinThickness = 1;
    public const double MaxThickness = 40;
    public const double MinFontSize = 8;
    public const double MaxFontSize = 200;

    public static InkPen Default { get; } = new(null, InkStroke.DefaultThickness, TextNote.DefaultFontSize);

    /// <summary>A pen from saved values: unknown colours become the theme's ink, sizes are kept within limits.</summary>
    public static InkPen Create(string? color, double thickness, double fontSize, bool bold = false, bool italic = false) => new(
        InkColors.Parse(color) is { } parsed ? InkColors.Format(parsed) : null,
        double.IsFinite(thickness) ? Math.Clamp(thickness, MinThickness, MaxThickness) : InkStroke.DefaultThickness,
        double.IsFinite(fontSize) ? Math.Clamp(fontSize, MinFontSize, MaxFontSize) : TextNote.DefaultFontSize,
        bold,
        italic);
}

/// <summary>The colours and sizes offered for paint and text, and the saved form of a colour ("#RRGGBB").</summary>
internal static class InkColors
{
    /// <summary>Offered in the palettes, in order; null is the theme's own ink.</summary>
    public static IReadOnlyList<string?> Palette { get; } =
        [null, "#FFFFFF", "#151515", "#FF5A5F", "#FF9F1C", "#FFD93D", "#4CD964", "#3FA7FF", "#9B6DFF", "#FF6DA8"];

    /// <summary>Paint stroke widths the palette steps through.</summary>
    public static IReadOnlyList<double> Thicknesses { get; } = [1.5, 3, 5, 8, 12, 20];

    /// <summary>Type sizes the palette steps through.</summary>
    public static IReadOnlyList<double> FontSizes { get; } = [10, 12, 14, 16, 20, 24, 32, 40, 56, 72, 96];

    /// <summary>Colours read so far: ink is drawn often, and there are only a few colours.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Color?> Parsed = new();

    /// <returns>null for null, empty or unreadable text.</returns>
    public static Color? Parse(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;
        if (Parsed.TryGetValue(color, out var known))
            return known;
        Color? result;
        try
        {
            result = ColorConverter.ConvertFromString(color) is Color parsed ? Color.FromRgb(parsed.R, parsed.G, parsed.B) : null;
        }
        catch (FormatException)
        {
            result = null;
        }
        if (Parsed.Count < 1024)
            Parsed[color] = result;
        return result;
    }

    public static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>The colour ink is drawn in: its own, or else the theme's.</summary>
    public static Color Resolve(string? color, Color themeInk) => Parse(color) ?? themeInk;

    /// <summary>Whether dark ink (which reads best on a light backing).</summary>
    public static bool IsDark(Color color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B < 128;

    /// <summary>
    /// The next size up (<paramref name="direction"/> &gt; 0) or down from <paramref name="current"/>;
    /// a size between steps goes to the neighbouring step. Stays put past either end.
    /// </summary>
    public static double Step(IReadOnlyList<double> steps, double current, int direction)
    {
        if (direction > 0)
            return steps.FirstOrDefault(s => s > current + 1e-6, steps[^1]);
        return steps.LastOrDefault(s => s < current - 1e-6, steps[0]);
    }
}
