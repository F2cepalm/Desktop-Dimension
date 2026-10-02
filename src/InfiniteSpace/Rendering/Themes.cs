using System.Windows.Media;

namespace InfiniteSpace.Rendering;

/// <summary>Colours and shape of the tool wheel and the toolbar in a theme (see <see cref="Themes.ChromeOf"/>).</summary>
internal sealed record ToolChrome(Color Back, Color Edge, Color Fore, Color Accent, double EdgeWidth, double Corner);

/// <summary>The looks a space can have, as offered in the tray menu.</summary>
internal static class Themes
{
    /// <summary>Celeste-style dream blocks: black void, twinkling pixel stars, wobbling white rim (<see cref="DreamStyle"/>).</summary>
    public const string Dreamy = "Dreamy";

    /// <summary>Flat clay orange with a bold, hand-drawn black ink outline (<see cref="ClaudishStyle"/>).</summary>
    public const string Claudish = "Claudish";

    /// <summary>Golden light and shadows: shafts of light and drifting golden motes in a dark hall (<see cref="GloryLook"/>).</summary>
    public const string Glory = "Glory";

    /// <summary>Red particles and shadows: embers streaming up from an unseen fire (<see cref="BurnLook"/>).</summary>
    public const string Burn = "Burn";

    /// <summary>A pinkish-yellow sky gradient with a hazy sun (<see cref="SundayLook"/>).</summary>
    public const string Sunday = "Sunday";

    /// <summary>Greens and grey in layers that shift with the cursor (<see cref="ParallaxLook"/>).</summary>
    public const string Parallax = "Parallax";

    /// <summary>Aqua blue water with rolling waves (<see cref="OceanLook"/>).</summary>
    public const string Ocean = "Ocean";

    /// <summary>Pure black with a white outline (<see cref="VoidLook"/>).</summary>
    public const string Void = "Void";

    public static IReadOnlyList<string> All { get; } = [Dreamy, Claudish, Glory, Burn, Sunday, Parallax, Ocean, Void];

    /// <summary>The theme to use for a saved choice; unknown or missing names fall back to <see cref="Dreamy"/>.</summary>
    public static string Resolve(string? name) => name is not null && All.Contains(name) ? name : Dreamy;

    /// <summary>Colour of paint and typed text on a space.</summary>
    public static Color InkOf(string theme) => theme switch
    {
        Claudish => ClaudishStyle.Ink,
        Glory => GloryLook.Ink,
        Burn => BurnLook.Ink,
        Sunday => SundayLook.Ink,
        Parallax => ParallaxLook.Ink,
        Ocean => OceanLook.Ink,
        Void => VoidLook.Ink,
        _ => Colors.White,
    };

    /// <summary>Colour of screen notes painted or typed in the theme's ink: the palette's red, which reads on light and dark apps alike.</summary>
    public static readonly Color SheetInk = Color.FromRgb(0xFF, 0x5A, 0x5F);

    /// <summary>
    /// The theme's accent, for what stands out in opened files (headings, links, quote bars): picked
    /// from the theme's own colours so it reads on its background.
    /// </summary>
    public static Color AccentOf(string theme) => theme switch
    {
        Claudish => Color.FromRgb(0x5C, 0x1D, 0x0B),  // burnt umber on clay
        Glory => Color.FromRgb(0xFF, 0xC6, 0x5C),     // the gilt of its glow
        Burn => Color.FromRgb(0xFF, 0x9A, 0x3C),      // ember orange
        Sunday => Color.FromRgb(0xB8, 0x3B, 0x5B),    // deep rose
        Parallax => Color.FromRgb(0x2E, 0x6B, 0x3A),  // forest green
        Ocean => Color.FromRgb(0xFF, 0xE9, 0x8A),     // a glint of sun
        Void => Color.FromRgb(0x9B, 0xD3, 0xFF),      // pale blue
        _ => Color.FromRgb(0x5F, 0xCD, 0xE4),         // Dreamy: a cyan star
    };

    /// <summary>How the tool wheel and the toolbar are dressed in a theme: a small piece of the theme itself.</summary>
    public static ToolChrome ChromeOf(string theme) => theme switch
    {
        Claudish => new(ClaudishStyle.Clay, ClaudishStyle.Ink, ClaudishStyle.Ink, ClaudishStyle.Paper, 2.5, 7),
        Glory => new(Color.FromRgb(0x24, 0x18, 0x08), Color.FromRgb(0xFF, 0xC6, 0x5C), GloryLook.Ink, Color.FromRgb(0xFF, 0xC6, 0x5C), 1.5, 6),
        Burn => new(Color.FromRgb(0x1E, 0x07, 0x05), Color.FromRgb(0xFF, 0x9A, 0x3C), BurnLook.Ink, Color.FromRgb(0xFF, 0x9A, 0x3C), 1.5, 6),
        Sunday => new(Color.FromRgb(0xF7, 0xD9, 0xCF), Color.FromRgb(0xB8, 0x3B, 0x5B), SundayLook.Ink, Color.FromRgb(0xB8, 0x3B, 0x5B), 1.5, 8),
        Parallax => new(Color.FromRgb(0xB7, 0xC4, 0xAE), Color.FromRgb(0x2E, 0x6B, 0x3A), ParallaxLook.Ink, Color.FromRgb(0x2E, 0x6B, 0x3A), 1.5, 6),
        Ocean => new(Color.FromRgb(0x0E, 0x6F, 0x80), Colors.White, Colors.White, Color.FromRgb(0xFF, 0xE9, 0x8A), 1.5, 8),
        Void => new(Colors.Black, Colors.White, Colors.White, Color.FromRgb(0x9B, 0xD3, 0xFF), 1, 0),
        _ => new(Colors.Black, Colors.White, Colors.White, Color.FromRgb(0x5F, 0xCD, 0xE4), 2, 0), // Dreamy: a dream block, square like its pixels
    };

    /// <summary>A look for one space in this theme.</summary>
    public static ISpaceLook CreateLook(string theme) => theme switch
    {
        Claudish => new InkyLook(),
        Glory => new GloryLook(),
        Burn => new BurnLook(),
        Sunday => new SundayLook(),
        Parallax => new ParallaxLook(),
        Ocean => new OceanLook(),
        Void => new VoidLook(),
        _ => new DreamyLook(),
    };
}

/// <summary>Colours and ink of the Claudish theme, taken from the reference illustration.</summary>
internal static class ClaudishStyle
{
    public static readonly Color Clay = Color.FromRgb(0xDB, 0x78, 0x59);

    public static readonly Color Ink = Color.FromRgb(0x15, 0x15, 0x15);

    public static readonly Color Paper = Color.FromRgb(0xFA, 0xF9, 0xF7);

    /// <summary>Average width of the outline, in DIPs; it swells and thins around the shape like a brush.</summary>
    public const double StrokeWidth = 5.5;

    /// <summary>How often the hand-drawn line re-draws itself ("line boil"), per second.</summary>
    public const double BoilRate = 10;

    /// <summary>How far the line shifts between boil frames, in DIPs.</summary>
    public const double BoilAmplitude = 0.7;
}
