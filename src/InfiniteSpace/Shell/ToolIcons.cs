using System.Windows.Media;
using Path = System.Windows.Shapes.Path;

namespace InfiniteSpace.Shell;

/// <summary>The tools' icons: outlines in a 16×16 box, drawn with round 1.5-DIP strokes in the chrome's foreground.</summary>
internal static class ToolIcons
{
    /// <summary>An open hand.</summary>
    public const string Hand = "M5,10.5 L5,3.5 M7.5,8 L7.5,2 M10,8 L10,2.5 M12.5,8.5 L12.5,4.5 M5,10.5 C5,13 6.8,14.5 9,14.5 C11.4,14.5 12.5,13 12.5,11 L12.5,8.5 M5,10.5 L3,8.5";

    /// <summary>A pen, nib down to the left.</summary>
    public const string Pen = "M2.5,13.5 L3,10.5 L11,2.5 L13.5,5 L5.5,13 Z M9.5,4 L12,6.5";

    /// <summary>A capital T.</summary>
    public const string Text = "M3,3 L13,3 M8,3 L8,13.5 M6,13.5 L10,13.5";

    /// <summary>A cube seen from a corner.</summary>
    public const string Cube = "M8,1.5 L14,4.75 L14,11.25 L8,14.5 L2,11.25 L2,4.75 Z M2,4.75 L8,8 L14,4.75 M8,8 L8,14.5";

    /// <summary>A gauge with its needle.</summary>
    public const string Meter = "M2,12 A6,6 0 0 1 14,12 M8,12 L11,7.5 M2,12 L14,12";

    /// <summary>Lines with one wrapping round.</summary>
    public const string Wrap = "M2,3.5 L14,3.5 M2,7.5 L11.5,7.5 C13.5,7.5 13.5,11.5 11.5,11.5 L8,11.5 M9.5,10 L8,11.5 L9.5,13 M2,11.5 L5,11.5";

    /// <summary>An eraser on its edge.</summary>
    public const string Eraser = "M6.5,13.5 L2.5,9.5 L9,3 L14,8 L8.5,13.5 Z M5.5,6.5 L10.5,11.5 M8.5,13.5 L14,13.5";

    /// <summary>A bulleted list.</summary>
    public const string List = "M6,4 L14,4 M6,8 L14,8 M6,12 L14,12 M2.5,4 L3,4 M2.5,8 L3,8 M2.5,12 L3,12";

    /// <summary>A floppy disk.</summary>
    public const string Save = "M2.5,2.5 L11.5,2.5 L13.5,4.5 L13.5,13.5 L2.5,13.5 Z M5,2.5 L5,6 L10.5,6 L10.5,2.5 M5,13.5 L5,9.5 L11,9.5 L11,13.5";

    /// <summary>An open folder.</summary>
    public const string Open = "M2,13 L2,3.5 L6,3.5 L7.5,5 L12.5,5 L12.5,7 M2,13 L4.5,7 L14.5,7 L12,13 Z";

    /// <summary>An icon ready to show, 16×16 DIPs.</summary>
    public static Path Shape(string data, Brush ink)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return new Path
        {
            Data = geometry,
            Stroke = ink,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
        };
    }
}
