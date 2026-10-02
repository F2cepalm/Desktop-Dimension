using System.IO;
using System.Windows;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace InfiniteSpace.Rendering;

/// <summary>
/// An opened file laid out for showing on a space: its name at the top, then its paragraphs one under
/// another (Markdown styled, plain text as it is), wrapped at the note's width. Laid out once; drawing
/// then only draws the paragraphs in view.
/// </summary>
/// <remarks>Sizes follow the note's type size: headings are bigger, code a little smaller.</remarks>
internal sealed class NoteDocument : IDisposable
{
    /// <summary>How wide an opened file is at first, in DIPs.</summary>
    public const double DefaultWidth = 420;

    /// <summary>The most an opened file shows at first, in DIPs; the rest scrolls.</summary>
    public const double DefaultHeight = 480;

    private static readonly double[] HeadingScale = [1.75, 1.45, 1.2, 1.05, 1, 1];

    private readonly List<LaidParagraph> _paragraphs = [];
    private readonly string _text;
    private readonly double _fontSize;
    private readonly double? _width;
    private readonly bool _markdown;
    private readonly string _path;

    private NoteDocument(TextNote note)
    {
        _text = note.Text;
        _fontSize = note.FontSize;
        _width = note.Width;
        _markdown = note.File?.Markdown ?? false;
        _path = note.File?.Path ?? "";
        Width = note.Width ?? DefaultWidth;
    }

    /// <summary>Across, in DIPs (the note's wrap width).</summary>
    public double Width { get; }

    /// <summary>The file's name at the top, and the gap under it, in DIPs.</summary>
    public double HeaderHeight { get; private set; }

    /// <summary>All the paragraphs one under another, in DIPs (without the name at the top).</summary>
    public double ContentHeight { get; private set; }

    /// <summary>The file's name, as shown at the top.</summary>
    public IDWriteTextLayout Header { get; private set; } = null!;

    public IReadOnlyList<LaidParagraph> Paragraphs => _paragraphs;

    /// <summary>Space above a paragraph's text for the name's hairline, in DIPs from the top.</summary>
    public double RuleY { get; private set; }

    /// <summary>
    /// Lays an opened file out. With <paramref name="linkBrush"/>, links are drawn with it (for
    /// drawing; measuring needs no brushes).
    /// </summary>
    public static NoteDocument Build(TextNote note, ID2D1Brush? linkBrush = null)
    {
        var document = new NoteDocument(note);
        document.Lay(linkBrush);
        return document;
    }

    /// <summary>Whether it was laid out from this text, size, width and kind.</summary>
    public bool Matches(TextNote note) =>
        note.Text == _text && note.FontSize == _fontSize && note.Width == _width &&
        (note.File?.Markdown ?? false) == _markdown && (note.File?.Path ?? "") == _path;

    /// <summary>How far it scrolls when <paramref name="height"/> of it (its name included) shows.</summary>
    public double MaxScroll(double height) => Math.Max(0, ContentHeight - (height - HeaderHeight));

    /// <summary>The height to show at first: all of it, up to <see cref="DefaultHeight"/>.</summary>
    public double FirstHeight => Math.Min(HeaderHeight + ContentHeight, Math.Max(DefaultHeight, HeaderHeight + 3 * Line(_fontSize)));

    /// <summary>The web address of a link at a point (DIPs from the note's top-left), scrolled so far; null elsewhere.</summary>
    public string? LinkAt(Point at, double scroll)
    {
        double y = at.Y - HeaderHeight + scroll;
        foreach (var paragraph in _paragraphs)
        {
            if (paragraph.Layout is null || y < paragraph.Top || y >= paragraph.Top + paragraph.Height)
                continue;
            paragraph.Layout.HitTestPoint((float)(at.X - paragraph.Left), (float)(y - paragraph.TextTop), out var trailing, out var inside, out var metrics);
            if (!inside)
                return null;
            int position = (int)metrics.TextPosition;
            foreach (var run in paragraph.Source.Runs)
            {
                if (run.Url is not null && position >= run.Start && position < run.Start + run.Length)
                    return run.Url;
            }
            return null;
        }
        return null;
    }

    /// <summary>Whether a point (DIPs from the note's top-left) is on the file's name.</summary>
    public bool IsOnHeader(Point at) => at.Y >= 0 && at.Y < HeaderHeight && at.X >= 0 && at.X < Width;

    public void Dispose()
    {
        Header?.Dispose();
        foreach (var paragraph in _paragraphs)
        {
            paragraph.Layout?.Dispose();
            paragraph.Marker?.Dispose();
        }
        _paragraphs.Clear();
    }

    private static double Line(double fontSize) => fontSize * NoteText.LineSpacing;

    private void Lay(ID2D1Brush? linkBrush)
    {
        double s = _fontSize;
        float headerSize = (float)(s * 0.8);
        Header = NoteText.Factory.CreateTextLayout(Path.GetFileName(_path), NoteText.Format(headerSize), (float)Width, 1e6f);
        Header.WordWrapping = WordWrapping.NoWrap;
        Header.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, null);
        RuleY = Line(headerSize) + s * 0.25;
        HeaderHeight = RuleY + s * 0.5;

        var source = _markdown ? NoteParagraphs.Markdown(_text) : NoteParagraphs.Plain(_text);
        double y = 0;
        for (int i = 0; i < source.Count; i++)
        {
            var paragraph = source[i];
            if (i > 0)
                y += SpaceBefore(paragraph, s);
            var laid = LayParagraph(paragraph, s, y, linkBrush);
            _paragraphs.Add(laid);
            y += laid.Height;
        }
        ContentHeight = y + s * 0.4; // a little room under the last line
    }

    private double SpaceBefore(NoteParagraph paragraph, double s) => paragraph switch
    {
        { Kind: ParagraphKind.Heading, Level: <= 2 } => s * 1.1,
        { Kind: ParagraphKind.Heading } => s * 0.8,
        _ when !_markdown => 0, // plain text comes in pieces of lines: they follow on
        { Tight: true } => s * 0.2,
        _ => s * 0.6,
    };

    private LaidParagraph LayParagraph(NoteParagraph paragraph, double s, double top, ID2D1Brush? linkBrush)
    {
        double left = paragraph.Indent * s * 1.6 + paragraph.Quote * s * 1.1;
        if (paragraph.Kind == ParagraphKind.Rule)
            return new LaidParagraph(paragraph, top, left, Width - left, s * 0.8, 0, null, null);

        bool code = paragraph.Kind == ParagraphKind.Code;
        double size = paragraph.Kind == ParagraphKind.Heading ? s * HeadingScale[Math.Clamp(paragraph.Level, 1, 6) - 1] : code ? s * 0.9 : s;
        double pad = code ? s * 0.45 : 0;
        float wrap = (float)Math.Max(8, Width - left - 2 * pad);
        var layout = NoteText.Factory.CreateTextLayout(paragraph.Text, NoteText.Format((float)size, monospace: code), wrap, 1e6f);
        // Nothing is cut off: a word longer than a line breaks (code anywhere).
        layout.WordWrapping = code ? WordWrapping.Character : WordWrapping.EmergencyBreak;
        if (paragraph.Kind == ParagraphKind.Heading)
            layout.SetFontWeight(Vortice.DirectWrite.FontWeight.Bold, new TextRange(0, (uint)paragraph.Text.Length));
        foreach (var run in paragraph.Runs)
            Style(layout, run, size, linkBrush);

        double height = layout.Metrics.Height + 2 * pad;
        IDWriteTextLayout? marker = null;
        if (paragraph.Marker is { } text)
        {
            marker = NoteText.Factory.CreateTextLayout(text, NoteText.Format((float)size), (float)(s * 1.6), 1e6f);
            marker.WordWrapping = WordWrapping.NoWrap;
            marker.TextAlignment = Vortice.DirectWrite.TextAlignment.Trailing;
        }
        return new LaidParagraph(paragraph, top, left, Width - left, height, pad, layout, marker);
    }

    private static void Style(IDWriteTextLayout layout, StyledRun run, double size, ID2D1Brush? linkBrush)
    {
        var range = new TextRange((uint)run.Start, (uint)run.Length);
        if (run.Style.HasFlag(TextStyle.Bold))
            layout.SetFontWeight(Vortice.DirectWrite.FontWeight.Bold, range);
        if (run.Style.HasFlag(TextStyle.Italic))
            layout.SetFontStyle(Vortice.DirectWrite.FontStyle.Italic, range);
        if (run.Style.HasFlag(TextStyle.Strike))
            layout.SetStrikethrough(true, range);
        if (run.Style.HasFlag(TextStyle.Code))
        {
            layout.SetFontFamilyName(NoteText.MonospaceFamily, range);
            layout.SetFontSize((float)(size * 0.9), range);
        }
        if (run.Style.HasFlag(TextStyle.Link))
        {
            layout.SetUnderline(true, range);
            if (linkBrush is not null)
                layout.SetDrawingEffect(linkBrush, range);
        }
    }
}

/// <summary>A paragraph of an opened file, laid out: where it goes (DIPs, from the top of the text), and its text.</summary>
/// <param name="Padding">Room between the paragraph's backing and its text (code only).</param>
/// <param name="Marker">Its bullet, number or box, laid out right-aligned in the margin before it.</param>
internal sealed record LaidParagraph(
    NoteParagraph Source,
    double Top,
    double Left,
    double Width,
    double Height,
    double Padding,
    IDWriteTextLayout? Layout,
    IDWriteTextLayout? Marker)
{
    /// <summary>Where its text starts, from the top of the text.</summary>
    public double TextTop => Top + Padding;

    /// <summary>Rectangles behind inline code (DIPs, from the paragraph's text origin), worked out when first drawn.</summary>
    public List<Rect>? CodeBackings { get; set; }
}
