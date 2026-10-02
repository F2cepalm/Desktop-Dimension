using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>User choices that are not spaces, kept next to spaces.json.</summary>
internal sealed class AppSettings
{
    private const string FileName = "settings.json";

    /// <summary>Null until the user picks in the tray menu; treated as on.</summary>
    public bool? StartWithWindows { get; set; }

    /// <summary>Look of the spaces (see <see cref="Themes"/>).</summary>
    public string Theme { get; set; } = Themes.Dreamy;

    /// <summary>Colour of new paint and text, "#RRGGBB"; null follows the theme's ink.</summary>
    public string? InkColor { get; set; }

    /// <summary>Width of new paint strokes, in DIPs.</summary>
    public double PaintWidth { get; set; } = InkStroke.DefaultThickness;

    /// <summary>Size of new text, in DIPs.</summary>
    public double TextSize { get; set; } = TextNote.DefaultFontSize;

    /// <summary>Whether new text is bold.</summary>
    public bool TextBold { get; set; }

    /// <summary>Whether new text is italic.</summary>
    public bool TextItalic { get; set; }

    /// <summary>Shows a small graph of frame times in a corner (see <see cref="FrameStatsWindow"/>): set by hand, for diagnosing.</summary>
    public bool ShowFrameStats { get; set; }

    /// <summary>The ink choices as a pen (anything unreadable in the file falls back to the defaults).</summary>
    [JsonIgnore]
    public InkPen Pen
    {
        get => InkPen.Create(InkColor, PaintWidth, TextSize, TextBold, TextItalic);
        set => (InkColor, PaintWidth, TextSize, TextBold, TextItalic) = (value.Color, value.Thickness, value.FontSize, value.Bold, value.Italic);
    }

    public static AppSettings Load(string directory)
    {
        try
        {
            string path = Path.Combine(directory, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this));
    }
}
