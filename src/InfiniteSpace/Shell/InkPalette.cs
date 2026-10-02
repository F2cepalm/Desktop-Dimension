using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>
/// Ink controls on a small dark strip: colour swatches (the first one is the theme's own ink), and
/// steppers for the width of paint and the size of text, and bold and italic toggles for text. Nothing on it takes keyboard focus, so a
/// text box next to it keeps typing.
/// </summary>
internal sealed class InkPalette : Border
{
    private const double Cell = 22;

    private static readonly Brush Chrome = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E)));
    private static readonly Brush Hover = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush On = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush SwatchEdge = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)));

    private readonly Color _themeInk;
    private readonly Dictionary<string, Ellipse> _rings = [];
    private readonly TextBlock _fontSizeLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, MinWidth = 26 };
    private readonly Ellipse _thicknessDot = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private double _thickness;
    private double _fontSize;
    private readonly Border _boldButton;
    private readonly Border _italicButton;
    private bool _bold;
    private bool _italic;

    /// <param name="themeInk">What the theme's own ink looks like now.</param>
    /// <param name="stacked">Colours, paint and text on rows of their own (a popup); otherwise one row (a toolbar).</param>
    /// <param name="paint">Whether to offer the width of paint.</param>
    /// <param name="leading">Something to put first on the strip (a move grip).</param>
    /// <param name="bold">Whether text is bold now.</param>
    /// <param name="italic">Whether text is italic now.</param>
    public InkPalette(Color themeInk, string? color, double thickness, double fontSize, bool stacked, bool paint, UIElement? leading = null, bool bold = false, bool italic = false)
    {
        _themeInk = themeInk;
        Background = Chrome;
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(4);
        TextElement.SetForeground(this, Brushes.White);
        TextElement.SetFontSize(this, 12);

        var colours = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string? option in InkColors.Palette)
            colours.Children.Add(Swatch(option));

        _boldButton = Button(Styled("B", FontWeights.Bold, FontStyles.Normal), "Bold text (Ctrl+B)", () => PickStyle(!_bold, _italic));
        _italicButton = Button(Styled("I", FontWeights.Normal, FontStyles.Italic), "Italic text (Ctrl+I)", () => PickStyle(_bold, !_italic));
        var textSize = Row(
            Button(Letter(10), "Smaller text", () => PickFontSize(InkColors.Step(InkColors.FontSizes, _fontSize, -1))),
            _fontSizeLabel,
            Button(Letter(16), "Bigger text", () => PickFontSize(InkColors.Step(InkColors.FontSizes, _fontSize, +1))),
            _boldButton,
            _italicButton);

        UIElement? paintWidth = paint
            ? Row(
                Button(Glyph("−"), "Thinner paint", () => PickThickness(InkColors.Step(InkColors.Thicknesses, _thickness, -1))),
                new Grid { Width = Cell, Height = Cell, Children = { _thicknessDot } },
                Button(Glyph("+"), "Thicker paint", () => PickThickness(InkColors.Step(InkColors.Thicknesses, _thickness, +1))))
            : null;

        if (stacked)
        {
            var rows = new StackPanel();
            if (leading is not null)
                rows.Children.Add(leading);
            rows.Children.Add(colours);
            if (paintWidth is not null)
                rows.Children.Add(Labelled("Paint", paintWidth));
            rows.Children.Add(Labelled("Text", textSize));
            Child = rows;
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (leading is not null)
                row.Children.Add(leading);
            row.Children.Add(colours);
            row.Children.Add(Divider());
            if (paintWidth is not null)
            {
                row.Children.Add(paintWidth);
                row.Children.Add(Divider());
            }
            row.Children.Add(textSize);
            Child = row;
        }

        ShowColor(color);
        ShowThickness(thickness);
        ShowFontSize(fontSize);
        ShowStyle(bold, italic);
    }

    /// <summary>A swatch was clicked: "#RRGGBB", or null for the theme's ink.</summary>
    public event Action<string?>? ColorPicked;

    public event Action<double>? ThicknessPicked;

    public event Action<double>? FontSizePicked;

    /// <summary>Bold or italic was switched on or off: whether text is bold, and whether italic, now.</summary>
    public event Action<bool, bool>? StylePicked;

    /// <summary>Marks whether text is bold and italic (after it changed elsewhere, or was picked here).</summary>
    public void ShowStyle(bool bold, bool italic)
    {
        (_bold, _italic) = (bold, italic);
        _boldButton.Background = bold ? On : Brushes.Transparent;
        _italicButton.Background = italic ? On : Brushes.Transparent;
    }

    private void PickStyle(bool bold, bool italic)
    {
        ShowStyle(bold, italic);
        StylePicked?.Invoke(bold, italic);
    }

    /// <summary>Marks the colour in use (after it changed elsewhere, or was picked here).</summary>
    public void ShowColor(string? color)
    {
        foreach (var (key, ring) in _rings)
            ring.Visibility = key == SwatchKey(color) ? Visibility.Visible : Visibility.Hidden;
        _thicknessDot.Fill = new SolidColorBrush(InkColors.Resolve(color, _themeInk));
    }

    public void ShowThickness(double thickness)
    {
        _thickness = thickness;
        _thicknessDot.Width = _thicknessDot.Height = Math.Clamp(thickness, 2, Cell - 4);
    }

    public void ShowFontSize(double fontSize)
    {
        _fontSize = fontSize;
        _fontSizeLabel.Text = Math.Round(fontSize).ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    private void PickThickness(double thickness)
    {
        ShowThickness(thickness);
        ThicknessPicked?.Invoke(thickness);
    }

    private void PickFontSize(double fontSize)
    {
        ShowFontSize(fontSize);
        FontSizePicked?.Invoke(fontSize);
    }

    private static string SwatchKey(string? color) => color ?? "";

    private FrameworkElement Swatch(string? color)
    {
        var dot = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = new SolidColorBrush(InkColors.Resolve(color, _themeInk)),
            Stroke = SwatchEdge,
            StrokeThickness = 1,
        };
        if (color is null)
            dot.StrokeDashArray = [2, 1.5]; // the theme's own ink, which changes with the theme
        var ring = new Ellipse { Width = Cell - 2, Height = Cell - 2, Stroke = Brushes.White, StrokeThickness = 2, Visibility = Visibility.Hidden };
        _rings[SwatchKey(color)] = ring;

        var cell = new Grid
        {
            Width = Cell,
            Height = Cell,
            Background = Brushes.Transparent, // the whole cell takes the click, not just the dot
            Cursor = Cursors.Hand,
            ToolTip = color is null ? "Theme ink" : color,
            Children = { ring, dot },
        };
        cell.MouseEnter += (_, _) => dot.Width = dot.Height = 16;
        cell.MouseLeave += (_, _) => dot.Width = dot.Height = 14;
        Clickable(cell, () =>
        {
            ShowColor(color);
            ColorPicked?.Invoke(color);
        });
        return cell;
    }

    private static Border Button(UIElement content, string tip, Action click)
    {
        var button = new Border
        {
            Width = Cell,
            Height = Cell,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content,
        };
        // A toggle that is on keeps showing so, under the mouse and after it.
        button.MouseEnter += (_, _) =>
        {
            if (button.Background != On)
                button.Background = Hover;
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Background == Hover)
                button.Background = Brushes.Transparent;
        };
        Clickable(button, click);
        return button;
    }

    /// <summary>Clicks act on release, and never reach whatever is behind (a toolbar being dragged).</summary>
    private static void Clickable(UIElement element, Action click)
    {
        element.MouseLeftButtonDown += (_, e) => e.Handled = true;
        element.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
    }

    private static TextBlock Letter(double size) => new()
    {
        Text = "A",
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Styled(string letter, FontWeight weight, FontStyle style) => new()
    {
        Text = letter,
        FontSize = 14,
        FontWeight = weight,
        FontStyle = style,
        FontFamily = new FontFamily("Georgia"),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static StackPanel Labelled(string label, UIElement controls) => Row(
        new TextBlock { Text = label, Width = 40, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 },
        controls);

    private static Rectangle Divider() => new()
    {
        Width = 1,
        Margin = new Thickness(4, 3, 4, 3),
        Fill = SwatchEdge,
    };

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
