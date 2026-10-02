using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>Text wrap: text selected in an app becomes a space of its exact size, with the text inside.</summary>
internal sealed partial class SpaceManager
{
    /// <summary>The smallest type text wrap shrinks to, so a selection's text fits its space.</summary>
    internal const double SmallestWrappedType = 6;

    /// <summary>
    /// A space over <paramref name="area"/> (screen DIPs, edges taken outward to whole art pixels) on the
    /// layer of <paramref name="host"/>, holding <paramref name="text"/> wrapped at its width, as large as
    /// <paramref name="fontSize"/> but no larger than fits. Like a block, it merges with spaces it crosses there.
    /// </summary>
    /// <param name="color">The text's colour in the app; the theme's ink is used when it wouldn't read on the space.</param>
    /// <returns>false when there is no text, or the area is too small for a space.</returns>
    public bool WrapText(string text, Rect area, IntPtr host, double fontSize, bool bold, bool italic, Color? color)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "").TrimEnd();
        if (text.Trim().Length == 0 || area.IsEmpty)
            return false;
        const double grid = DreamStyle.ArtPixel;
        var box = new Rect(
            new Point(Math.Floor(area.Left / grid) * grid, Math.Floor(area.Top / grid) * grid),
            new Point(Math.Ceiling(area.Right / grid) * grid, Math.Ceiling(area.Bottom / grid) * grid));
        if (box.Width * box.Height < SpaceGeometry.MinArea)
            return false;

        var (size, width) = FitWrappedText(text, box.Size, fontSize, bold, italic);
        var note = new TextNote
        {
            X = box.Left,
            Y = box.Top,
            Text = text,
            FontSize = size,
            Width = width,
            Bold = bold,
            Italic = italic,
            Color = ReadableColor(color),
            Order = InkOrder.Next(),
        };
        var outline = SpaceGeometry.FromStroke([box.TopLeft, box.TopRight, box.BottomRight, box.BottomLeft], new Vector(0, 0));
        var record = new SpaceRecord { Figures = SpaceGeometry.Encode(outline, out var fillRule), Host = Describe(host), Notes = [note] };
        record.FillRule = fillRule;
        Place(record, host);
        Persist();
        return true;
    }

    /// <summary>
    /// The largest type (to a half point, from <paramref name="fontSize"/> down to <see cref="SmallestWrappedType"/>)
    /// at which the text, wrapped at the box's width, fits the box; at the smallest, the rest runs over.
    /// </summary>
    /// <returns>The size, and the width the lines wrap at (the box's).</returns>
    internal static (double FontSize, double Width) FitWrappedText(string text, Size box, double fontSize, bool bold, bool italic)
    {
        double width = Math.Round(box.Width, 2);
        bool Fits(double size)
        {
            using var layout = NoteText.Layout(text, size, width, bold, italic);
            var m = layout.Metrics;
            return m.Width <= width + 0.5 && m.Height <= box.Height + 0.5;
        }

        double high = Math.Clamp(Math.Round(fontSize * 2) / 2, SmallestWrappedType, InkPen.MaxFontSize);
        if (Fits(high))
            return (high, width);
        double low = SmallestWrappedType;
        if (!Fits(low))
            return (low, width);
        while (high - low > 0.5)
        {
            double middle = Math.Round(low + high) / 2; // halfway, to a half point
            if (middle <= low || middle >= high)
                break;
            if (Fits(middle))
                low = middle;
            else
                high = middle;
        }
        return (low, width);
    }

    /// <summary>"#RRGGBB" for a colour that reads on the theme's spaces (3:1 or more); null (the theme's ink) otherwise.</summary>
    internal string? ReadableColor(Color? color) =>
        color is { } c && Themes.Contrast(c, Themes.ChromeOf(Theme).Back) >= 3 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
}
