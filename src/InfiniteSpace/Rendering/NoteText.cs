using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;
using Vortice.DirectWrite;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace InfiniteSpace.Rendering;

/// <summary>
/// How typed notes are laid out: the system UI font at the note's size, lines wrapped at its
/// <see cref="TextNote.Width"/> (a word longer than that runs over rather than being split, as in
/// the text box that types them). Laid out with DirectWrite, which draws them on the GPU.
/// </summary>
internal static class NoteText
{
    /// <summary>Sizes of notes as last laid out, so hovering over text doesn't lay it out again and again.</summary>
    private static readonly ConditionalWeakTable<TextNote, MeasuredNote> Measured = new();

    private static readonly Dictionary<float, IDWriteTextFormat> Formats = [];

    /// <summary>DirectWrite's shared factory (it is thread-safe).</summary>
    public static IDWriteFactory Factory { get; } = DWrite.DWriteCreateFactory<IDWriteFactory>(FactoryType.Shared);

    /// <summary>The system UI font, used for typed notes and by the text box that types them.</summary>
    public static Typeface Typeface { get; } =
        new(SystemFonts.MessageFontFamily, SystemFonts.MessageFontStyle, SystemFonts.MessageFontWeight, FontStretches.Normal);

    /// <summary>Height of one line, per DIP of type size: every line is this tall, whatever fonts fill in for missing letters.</summary>
    public static double LineSpacing => Typeface.FontFamily.LineSpacing;

    /// <summary>
    /// A note laid out for drawing (DIPs, its top-left at 0,0). The caller owns it.
    /// </summary>
    public static IDWriteTextLayout Layout(TextNote note) => Layout(note.Text, note.FontSize, note.Width, note.Bold, note.Italic);

    /// <param name="width">Where lines wrap (DIPs); null keeps each line as long as it was typed.</param>
    /// <param name="bold">All of it in bold.</param>
    /// <param name="italic">All of it in italics.</param>
    public static IDWriteTextLayout Layout(string text, double fontSize, double? width, bool bold = false, bool italic = false)
    {
        bool wraps = width is { } w && w > 0;
        var layout = Factory.CreateTextLayout(text, Format((float)fontSize), wraps ? (float)width!.Value : 1e6f, 1e6f);
        layout.WordWrapping = wraps ? WordWrapping.WholeWord : WordWrapping.NoWrap;
        var all = new TextRange(0, (uint)text.Length);
        if (bold)
            layout.SetFontWeight(DWriteFontWeight.Bold, all);
        if (italic)
            layout.SetFontStyle(DWriteFontStyle.Italic, all);
        return layout;
    }

    /// <summary>
    /// The area a note's text takes up, in DIPs: at least its wrap width across, if it has one. An
    /// opened file takes up as much as it shows (its text scrolls within).
    /// </summary>
    public static Size Measure(TextNote note)
    {
        if (note.File is { } file)
            return new Size(note.Width ?? NoteDocument.DefaultWidth, file.Height);
        if (Measured.TryGetValue(note, out var known) && known.Matches(note))
            return known.Size;

        Size size;
        using (var layout = Layout(note))
        {
            var metrics = layout.Metrics;
            size = new Size(Math.Max(metrics.Width, note.Width ?? 0), metrics.Height);
        }
        Measured.AddOrUpdate(note, new MeasuredNote(note.Text, note.FontSize, note.Width, note.Bold, note.Italic, size));
        return size;
    }

    /// <summary>How far an opened file scrolls, at the height it shows (0 for a typed note).</summary>
    public static double MaxScroll(TextNote note)
    {
        if (note.File is not { } file)
            return 0;
        if (!MeasuredFiles.TryGetValue(note, out var known) || !known.Matches(note))
        {
            using var document = NoteDocument.Build(note);
            known = new MeasuredFile(note.Text, note.FontSize, note.Width, file.Markdown, document.HeaderHeight, document.ContentHeight);
            MeasuredFiles.AddOrUpdate(note, known);
        }
        return Math.Max(0, known.Content - (file.Height - known.Header));
    }

    /// <summary>Keeps an opened file's heights from a layout made anyway (when it is opened), so scrolling needn't lay it out again.</summary>
    public static void Remember(TextNote note, NoteDocument document) =>
        MeasuredFiles.AddOrUpdate(note, new MeasuredFile(note.Text, note.FontSize, note.Width, note.File?.Markdown ?? false, document.HeaderHeight, document.ContentHeight));

    /// <summary><paramref name="to"/> replaces <paramref name="from"/> with the same text (it was moved): its measurements carry over.</summary>
    public static void Adopt(TextNote from, TextNote to)
    {
        if (Measured.TryGetValue(from, out var measured))
            Measured.AddOrUpdate(to, measured);
        if (MeasuredFiles.TryGetValue(from, out var file))
            MeasuredFiles.AddOrUpdate(to, file);
    }

    /// <summary>Opened files' heights as last laid out (scrolling asks for them on every notch).</summary>
    private static readonly ConditionalWeakTable<TextNote, MeasuredFile> MeasuredFiles = new();

    private sealed record MeasuredFile(string Text, double FontSize, double? Width, bool Markdown, double Header, double Content)
    {
        public bool Matches(TextNote note) =>
            note.Text == Text && note.FontSize == FontSize && note.Width == Width && note.File?.Markdown == Markdown;
    }

    /// <summary>Where a note is on screen (DIPs); only its place follows the mapping, its type keeps its size.</summary>
    public static Rect Bounds(TextNote note, Matrix toScreen) =>
        new(toScreen.Transform(new Point(note.X, note.Y)), Measure(note));

    /// <summary>Where each line of a note ends (character counts, line breaks included), for comparing with the text box.</summary>
    public static int[] LineLengths(TextNote note)
    {
        using var layout = Layout(note);
        var lines = new LineMetrics[layout.Metrics.LineCount];
        layout.GetLineMetrics(lines, out uint count);
        return [.. lines.Take((int)count).Select(l => (int)l.Length)];
    }

    /// <summary>The system UI font (or <paramref name="monospace"/> for code) at a size, every line equally tall.</summary>
    internal static IDWriteTextFormat Format(float fontSize, bool monospace = false)
    {
        lock (Formats)
        {
            float key = monospace ? -fontSize : fontSize;
            if (Formats.TryGetValue(key, out var format))
                return format;
            var family = Typeface.FontFamily;
            format = monospace
                ? Factory.CreateTextFormat(MonospaceFamily, null, DWriteFontWeight.Normal, DWriteFontStyle.Normal, Vortice.DirectWrite.FontStretch.Normal, fontSize, "")
                : Factory.CreateTextFormat(family.Source, null,
                    (DWriteFontWeight)Typeface.Weight.ToOpenTypeWeight(),
                    Typeface.Style == FontStyles.Italic ? DWriteFontStyle.Italic : Typeface.Style == FontStyles.Oblique ? DWriteFontStyle.Oblique : DWriteFontStyle.Normal,
                    Vortice.DirectWrite.FontStretch.Normal, fontSize, "");
            // Lines as tall as the text box makes them, fallback fonts or not.
            format.SetLineSpacing(LineSpacingMethod.Uniform, (float)(family.LineSpacing * fontSize), (float)(family.Baseline * fontSize));
            Formats[key] = format;
            return format;
        }
    }

    /// <summary>The font for code in opened files.</summary>
    internal const string MonospaceFamily = "Consolas";

    private sealed record MeasuredNote(string Text, double FontSize, double? Width, bool Bold, bool Italic, Size Size)
    {
        public bool Matches(TextNote note) =>
            note.Text == Text && note.FontSize == FontSize && note.Width == Width && note.Bold == Bold && note.Italic == Italic;
    }
}
