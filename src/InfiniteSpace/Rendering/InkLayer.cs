using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Color4 = Vortice.Mathematics.Color4;
using Matrix3x2 = System.Numerics.Matrix3x2;
using RawRectF = Vortice.RawRectF;
using SizeI = Vortice.Mathematics.SizeI;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Rendering;

/// <summary>What the hand shows over the ink (screen DIPs): a dashed frame round each selected group or item, and the selection rectangle being dragged.</summary>
internal sealed record InkAdornments(IReadOnlyList<Rect> Frames, Rect? Marquee, Color Accent);

/// <summary>
/// Paint and text on a space, drawn with Direct2D at device resolution (crisp, unlike the pixel-art
/// void) and clipped to the space's outline. The ink lives in a GPU bitmap: it is only redrawn when
/// the ink or the shape changes, a paint stroke draws just its newest piece, and only the part that
/// changed is copied to the screen (see <see cref="Compose"/>).
/// </summary>
/// <remarks>
/// The bitmap holds the ink unclipped; the outline is applied as a mask while copying to the screen, so
/// painting never has to clip. Bitmaps are made with room to spare, so a space being reshaped (a new
/// size every frame) keeps drawing into the same ones.
/// </remarks>
internal sealed class InkLayer : IDisposable
{
    /// <summary>Device pixels added around a painted piece, for antialiasing.</summary>
    private const int Bleed = 2;

    /// <summary>Bitmaps grow in steps of this many device pixels, so small changes of size reuse them.</summary>
    private const int Step = Headroom.Step;

    /// <summary>How much of an opened file's shown height fades at an edge with more to scroll to.</summary>
    private const float Fade = 0.07f;

    private readonly GpuCompositor _gpu;
    private readonly Dictionary<Color, ID2D1SolidColorBrush> _brushes = [];
    private readonly Dictionary<InkStroke, CachedStroke> _strokes = [];
    private readonly Dictionary<TextNote, CachedNote> _notes = [];
    private readonly Dictionary<TextNote, CachedDocument> _documents = [];
    private readonly Dictionary<(bool Top, bool Bottom), ID2D1GradientStopCollection> _fades = [];
    private readonly List<InkStroke> _unusedStrokes = [];
    private readonly List<TextNote> _unusedNotes = [];
    private readonly List<Segment> _pending = [];
    private ID2D1Bitmap1? _ink;
    private ID2D1Bitmap1? _mask;
    private ID2D1StrokeStyle? _round;
    private ID2D1StrokeStyle? _dashed;
    private int _capacityWidth;
    private int _capacityHeight;
    private Point _origin;
    private double _scale = 1;
    private Outline2D _outline = Outline2D.Empty;
    private bool _maskStale = true;
    private int _generation;
    private bool _disposed;

    /// <param name="ink">Colour of paint and text that have none of their own (it follows the theme).</param>
    public InkLayer(GpuCompositor gpu, Color ink)
    {
        _gpu = gpu;
        ThemeInk = ink;
    }

    /// <summary>A layer with a shape already (see <see cref="SetShape"/>).</summary>
    public InkLayer(GpuCompositor gpu, int width, int height, double scale, Point origin, Geometry outline, Color ink)
        : this(gpu, ink)
    {
        SetShape(width, height, scale, origin, outline);
    }

    /// <summary>Colour of paint and text that have none of their own; takes effect on the next <see cref="Redraw"/>.</summary>
    public Color ThemeInk { get; set; }

    /// <summary>What stands out in opened files (headings, links); see <see cref="Themes.AccentOf"/>.</summary>
    public Color ThemeAccent { get; set; } = Themes.AccentOf(Themes.Dreamy);

    /// <summary>Device pixels across.</summary>
    public int Width { get; private set; }

    /// <summary>Device pixels down.</summary>
    public int Height { get; private set; }

    public Int32Rect Bounds => new(0, 0, Width, Height);

    /// <summary>
    /// The space's new shape: the layer's size, where it is on screen, and the outline ink is clipped
    /// to. What was drawn is stale until the next <see cref="Redraw"/>.
    /// </summary>
    /// <param name="width">Device pixels.</param>
    /// <param name="height">Device pixels.</param>
    /// <param name="scale">Device pixels per DIP.</param>
    /// <param name="origin">Screen DIP position of pixel (0,0).</param>
    /// <param name="outline">The space's outline (screen DIPs); ink outside it is not shown.</param>
    public void SetShape(int width, int height, double scale, Point origin, Geometry outline)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _scale = scale;
        _origin = origin;
        _outline = Outline2D.From(outline, origin);
        _maskStale = true;
        _pending.Clear();
        EnsureCapacity();
    }

    /// <summary>
    /// The space moved on screen with everything on it: the pixels stay as they are, only the screen
    /// position of pixel (0,0) changes.
    /// </summary>
    public void MoveOrigin(Vector offset) => _origin += offset;

    /// <summary>Redraws all strokes and notes of the space, mapped onto the screen by <paramref name="toScreen"/>.</summary>
    /// <param name="skip">A note left out (it is being edited in a text box, which shows it instead).</param>
    /// <param name="placeNote">Where each note goes on screen, when not where <paramref name="toScreen"/> puts it.</param>
    /// <returns>The changed area: all of it.</returns>
    /// <param name="opacity">How opaque each note is drawn (newly opened files fade in); fully, when null.</param>
    /// <param name="placeStroke">How each stroke is mapped onto the screen, when not where <paramref name="toScreen"/> places it (see <see cref="InkStroke.Placed"/>).</param>
    /// <param name="adornments">The hand's selection frames and rectangle, drawn over the ink; none when null.</param>
    public Int32Rect Redraw(SpaceRecord record, Matrix toScreen = default, TextNote? skip = null, Func<TextNote, Point>? placeNote = null, Func<TextNote, float>? opacity = null, Func<InkStroke, Matrix>? placeStroke = null, InkAdornments? adornments = null)
    {
        _pending.Clear();
        _generation++;
        var context = Begin(_ink!);
        context.PushAxisAlignedClip(new RawRectF(0, 0, Width, Height), AntialiasMode.Aliased);
        context.Clear(new Color4(0, 0, 0, 0));
        var world = World();
        foreach (var stroke in record.Strokes)
            DrawStroke(context, stroke, placeStroke?.Invoke(stroke) ?? stroke.Placed(toScreen, record.Anchor), world); // moved, never stretched
        foreach (var note in record.Notes)
        {
            if (note == skip || note.Text.Length == 0) // an empty note was removed by editing; it is kept only for undo
                continue;
            float alpha = opacity?.Invoke(note) ?? 1;
            if (alpha <= 0)
                continue;
            if (alpha < 1)
            {
                context.Transform = Matrix3x2.Identity;
                context.PushLayer(new LayerParameters1
                {
                    ContentBounds = new RawRectF(0, 0, Width, Height),
                    MaskTransform = Matrix3x2.Identity,
                    Opacity = alpha,
                }, null!);
            }
            DrawNote(context, note, placeNote?.Invoke(note) ?? toScreen.Transform(new Point(note.X, note.Y)), world);
            if (alpha < 1)
                context.PopLayer();
        }
        if (adornments is not null)
            DrawAdornments(context, adornments, world);
        context.PopAxisAlignedClip();
        End(context);
        ForgetUnused();
        return Bounds;
    }

    /// <summary>Adds one piece of a stroke being painted (screen DIPs); a zero-length piece is a dot.</summary>
    /// <param name="color">The stroke's colour; null for the theme's ink.</param>
    /// <returns>The changed area, in device pixels.</returns>
    public Int32Rect AddSegment(Point from, Point to, double thickness, Color? color = null)
    {
        var a = new Vector2((float)(from.X - _origin.X), (float)(from.Y - _origin.Y));
        var b = new Vector2((float)(to.X - _origin.X), (float)(to.Y - _origin.Y));
        double reach = thickness / 2;
        int x0 = Math.Clamp((int)Math.Floor((Math.Min(a.X, b.X) - reach) * _scale) - Bleed, 0, Width);
        int y0 = Math.Clamp((int)Math.Floor((Math.Min(a.Y, b.Y) - reach) * _scale) - Bleed, 0, Height);
        int x1 = Math.Clamp((int)Math.Ceiling((Math.Max(a.X, b.X) + reach) * _scale) + Bleed, 0, Width);
        int y1 = Math.Clamp((int)Math.Ceiling((Math.Max(a.Y, b.Y) + reach) * _scale) + Bleed, 0, Height);
        if (x1 <= x0 || y1 <= y0)
            return Int32Rect.Empty;
        _pending.Add(new Segment(a, b, (float)thickness, color ?? ThemeInk));
        return new Int32Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Draws what was painted since the last call and the mask, before <see cref="Compose"/> (not inside another drawing).</summary>
    public void Prepare()
    {
        EnsureMask();
        if (_pending.Count == 0)
            return;
        var context = Begin(_ink!);
        context.Transform = Matrix3x2.CreateScale((float)_scale); // the pieces are in DIPs from the layer's top-left already
        foreach (var segment in _pending)
        {
            var brush = Brush(context, segment.Color);
            if (segment.From == segment.To)
                context.FillEllipse(new Ellipse(segment.From, segment.Thickness / 2, segment.Thickness / 2), brush);
            else
                context.DrawLine(segment.From, segment.To, brush, segment.Thickness, Round());
        }
        End(context);
        _pending.Clear();
    }

    /// <summary>
    /// Copies an area of the ink (device pixels), cut to the outline, into the target being drawn, its
    /// top-left at <paramref name="at"/> (target pixels; the context must be at 96 DPI). Every pixel of
    /// the area is written. Call <see cref="Prepare"/> first.
    /// </summary>
    public void Compose(ID2D1DeviceContext context, Int32Rect area, Vector2 at)
    {
        context.Transform = Matrix3x2.CreateTranslation(at.X - area.X, at.Y - area.Y);
        var rect = new RawRectF(area.X, area.Y, area.X + area.Width, area.Y + area.Height);
        context.PushAxisAlignedClip(rect, AntialiasMode.Aliased);
        context.Clear(new Color4(0, 0, 0, 0));
        var corner = new Vector2(area.X, area.Y);
        context.DrawImage(_ink!, corner, rect, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
        context.DrawImage(_mask!, corner, rect, InterpolationMode.NearestNeighbor, CompositeMode.DestinationIn);
        context.PopAxisAlignedClip();
        context.Transform = Matrix3x2.Identity;
    }

    /// <summary>The ink as it shows (premultiplied BGRA, <see cref="Width"/> per row): for tests.</summary>
    public int[] ReadPixels()
    {
        Prepare();
        using var picture = _gpu.Paint(Width, Height, 1, (context, _) => Compose(context, Bounds, Vector2.Zero));
        return _gpu.ReadPixels(picture);
    }

    /// <summary>The smallest rectangle covering both (either may be empty).</summary>
    public static Int32Rect Union(Int32Rect a, Int32Rect b)
    {
        if (a.IsEmpty || a.Width == 0 || a.Height == 0)
            return b;
        if (b.IsEmpty || b.Width == 0 || b.Height == 0)
            return a;
        int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
        int x1 = Math.Max(a.X + a.Width, b.X + b.Width), y1 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Int32Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// <paramref name="to"/> replaces <paramref name="from"/> with the same text (it was moved): what
    /// was laid out for the one serves the other, so a long file isn't laid out again.
    /// </summary>
    public void Adopt(TextNote from, TextNote to)
    {
        if (_notes.Remove(from, out var note))
            _notes[to] = note;
        if (_documents.Remove(from, out var document))
            _documents[to] = document;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var brush in _brushes.Values)
            brush.Dispose();
        _brushes.Clear();
        foreach (var stroke in _strokes.Values)
            stroke.Geometry.Dispose();
        _strokes.Clear();
        foreach (var note in _notes.Values)
            note.Layout.Dispose();
        _notes.Clear();
        foreach (var document in _documents.Values)
            document.Document.Dispose();
        _documents.Clear();
        foreach (var fade in _fades.Values)
            fade.Dispose();
        _fades.Clear();
        _dashed?.Dispose();
        _round?.Dispose();
        _ink?.Dispose();
        _mask?.Dispose();
    }

    /// <summary>The selection: a dashed frame in the accent colour round each selected group or item, and the rectangle being dragged.</summary>
    private void DrawAdornments(ID2D1DeviceContext context, InkAdornments adornments, Matrix3x2 world)
    {
        context.Transform = world;
        var accent = Brush(context, adornments.Accent);
        _dashed ??= _gpu.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties { DashStyle = Vortice.Direct2D1.DashStyle.Custom }, [4, 3]);
        foreach (var frame in adornments.Frames)
            context.DrawRectangle(R(frame), accent, 1.25f, _dashed);
        if (adornments.Marquee is { } marquee)
        {
            var c = adornments.Accent;
            using var fill = context.CreateSolidColorBrush(new Color4(c.R / 255f, c.G / 255f, c.B / 255f, 0.12f));
            context.FillRectangle(R(marquee), fill);
            context.DrawRectangle(R(marquee), accent, 1.25f, _dashed);
        }

        static RawRectF R(Rect r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
    }

    /// <summary>From screen DIPs to this layer's pixels.</summary>
    private Matrix3x2 World() =>
        Matrix3x2.CreateTranslation((float)-_origin.X, (float)-_origin.Y) * Matrix3x2.CreateScale((float)_scale);

    /// <summary>Makes sure the bitmaps hold the layer's size; they are only made anew when too small (or far too big).</summary>
    private void EnsureCapacity()
    {
        bool fits = _ink is not null && _capacityWidth >= Width && _capacityHeight >= Height;
        bool wasteful = (long)_capacityWidth * _capacityHeight > 4L * Math.Max(Step * Step, (long)Width * Height);
        if (fits && !wasteful)
            return;
        int max = (int)Math.Min(16384, _gpu.D2DContext.MaximumBitmapSize);
        _capacityWidth = Math.Min(max, Headroom.Size(Width, _capacityWidth));
        _capacityHeight = Math.Min(max, Headroom.Size(Height, _capacityHeight));
        Width = Math.Min(Width, _capacityWidth);
        Height = Math.Min(Height, _capacityHeight);
        _ink?.Dispose();
        _mask?.Dispose();
        var size = new SizeI(_capacityWidth, _capacityHeight);
        _ink = _gpu.D2DContext.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(GpuCompositor.Bgra, 96, 96, BitmapOptions.Target));
        _mask = _gpu.D2DContext.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(GpuCompositor.Bgra, 96, 96, BitmapOptions.Target));
        _maskStale = true;

    }

    /// <summary>The outline, filled, in the mask's alpha.</summary>
    private void EnsureMask()
    {
        if (!_maskStale)
            return;
        _maskStale = false;
        var context = Begin(_mask!);
        context.Clear(new Color4(0, 0, 0, 0));
        if (_outline.Figures.Length > 0)
        {
            using var shape = _outline.ToPath(_gpu.D2DFactory);
            context.Transform = Matrix3x2.CreateScale((float)_scale);
            context.FillGeometry(shape, Brush(context, Colors.White));
        }
        End(context);
    }

    private ID2D1DeviceContext Begin(ID2D1Bitmap1 target)
    {
        var context = _gpu.D2DContext;
        context.Target = target;
        context.SetDpi(96, 96);
        context.BeginDraw();
        context.Transform = Matrix3x2.Identity;
        context.AntialiasMode = AntialiasMode.PerPrimitive;
        context.TextAntialiasMode = TextAntialiasMode.Grayscale; // text on see-through ink can't be ClearType
        return context;
    }

    private static void End(ID2D1DeviceContext context)
    {
        context.Transform = Matrix3x2.Identity;
        context.EndDraw().CheckError();
        context.Target = null;
    }

    private void DrawStroke(ID2D1DeviceContext context, InkStroke stroke, Matrix toScreen, Matrix3x2 world)
    {
        var points = stroke.Points;
        if (points.Length < 2)
            return;
        var brush = Brush(context, InkColors.Resolve(stroke.Color, ThemeInk));
        float thickness = (float)stroke.Thickness;
        if (points.Length < 4)
        {
            var dot = toScreen.Transform(new Point(points[0], points[1]));
            context.Transform = world;
            context.FillEllipse(new Ellipse(new Vector2((float)dot.X, (float)dot.Y), thickness / 2, thickness / 2), brush);
            return;
        }

        var geometry = Geometry(stroke); // in the record's coordinates
        if (toScreen.M11 == 1 && toScreen.M22 == 1 && toScreen.M12 == 0 && toScreen.M21 == 0)
        {
            // Only moved: the cached geometry as it is.
            context.Transform = Matrix3x2.CreateTranslation((float)toScreen.OffsetX, (float)toScreen.OffsetY) * world;
            context.DrawGeometry(geometry, brush, thickness, Round());
            return;
        }
        // Stretched: the points go through the mapping, the pen keeps its width.
        var mapping = new Matrix3x2((float)toScreen.M11, (float)toScreen.M12, (float)toScreen.M21, (float)toScreen.M22, (float)toScreen.OffsetX, (float)toScreen.OffsetY);
        using var mapped = _gpu.D2DFactory.CreateTransformedGeometry(geometry, mapping);
        context.Transform = world;
        context.DrawGeometry(mapped, brush, thickness, Round());
    }

    /// <param name="at">Screen DIP position of its top-left. Only the note's place follows the window; its type keeps its size.</param>
    private void DrawNote(ID2D1DeviceContext context, TextNote note, Point at, Matrix3x2 world)
    {
        if (note.File is not null)
        {
            DrawFile(context, note, at, world);
            return;
        }
        if (!_notes.TryGetValue(note, out var cached) || !cached.Matches(note))
        {
            cached?.Layout.Dispose();
            cached = new CachedNote(NoteText.Layout(note), note.Text, note.FontSize, note.Width, note.Bold, note.Italic);
            _notes[note] = cached;
        }
        cached.Generation = _generation;
        context.Transform = Matrix3x2.CreateTranslation((float)at.X, (float)at.Y) * world;
        context.DrawTextLayout(Vector2.Zero, cached.Layout, Brush(context, InkColors.Resolve(note.Color, ThemeInk)), DrawTextOptions.EnableColorFont);
    }

    /// <summary>
    /// An opened file: its name at the top over a hairline, and under it the part of its text scrolled
    /// to, cut off at its height and fading out at an edge with more beyond it. Only the paragraphs in
    /// view are drawn.
    /// </summary>
    private void DrawFile(ID2D1DeviceContext context, TextNote note, Point at, Matrix3x2 world)
    {
        var file = note.File!;
        var document = Document(note);
        var ink = InkColors.Resolve(note.Color, ThemeInk);
        var inkBrush = Brush(context, ink);
        var faint = Brush(context, Color.FromArgb(0xA0, ink.R, ink.G, ink.B));
        var place = Matrix3x2.CreateTranslation((float)at.X, (float)at.Y) * world;
        float width = (float)document.Width, height = (float)Math.Max(document.HeaderHeight, file.Height);

        // A soft backing that the text reads well on, whatever is behind it: light under dark ink,
        // dark under light ink (as the text box has).
        context.Transform = place;
        float pad = (float)(note.FontSize * 0.6);
        var backing = InkColors.IsDark(ink) ? Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0x00, 0x00, 0x00);
        context.FillRoundedRectangle(new RoundedRectangle(new RawRectF(-pad, -pad * 0.8f, width + pad, height + pad * 0.6f), pad, pad), Brush(context, backing));

        context.PushAxisAlignedClip(new RawRectF(0, 0, width, (float)document.HeaderHeight), AntialiasMode.Aliased);
        context.DrawTextLayout(Vector2.Zero, document.Header, faint, DrawTextOptions.None);
        context.PopAxisAlignedClip();
        float rule = (float)document.RuleY;
        context.FillRectangle(new RawRectF(0, rule, width, rule + 1), Brush(context, Color.FromArgb(0x50, ink.R, ink.G, ink.B)));

        float top = (float)document.HeaderHeight;
        double shown = height - top;
        double scroll = Math.Clamp(file.Scroll, 0, document.MaxScroll(height));
        if (shown <= 0)
            return;
        context.PushAxisAlignedClip(new RawRectF(0, top, width, height), AntialiasMode.Aliased);
        bool fadeTop = scroll > 0.5, fadeBottom = scroll < document.MaxScroll(height) - 0.5;
        ID2D1LinearGradientBrush? fade = null;
        if (fadeTop || fadeBottom)
        {
            fade = context.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(0, top), new Vector2(0, height)), Fades(context, fadeTop, fadeBottom));
            context.PushLayer(new LayerParameters1
            {
                ContentBounds = new RawRectF(0, top, width, height),
                MaskTransform = Matrix3x2.Identity,
                Opacity = 1,
                OpacityBrush = fade,
            }, null!);
        }

        context.Transform = Matrix3x2.CreateTranslation(0, (float)(top - scroll)) * place;
        var accent = Brush(context, ThemeAccent);
        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.Top + paragraph.Height < scroll)
                continue;
            if (paragraph.Top > scroll + shown)
                break;
            DrawParagraph(context, paragraph, note.FontSize, inkBrush, accent, ink);
        }

        context.Transform = place;
        if (fade is not null)
        {
            context.PopLayer();
            fade.Dispose();
        }
        context.PopAxisAlignedClip();
    }

    private void DrawParagraph(ID2D1DeviceContext context, LaidParagraph paragraph, double fontSize, ID2D1Brush ink, ID2D1Brush accent, Color inkColor)
    {
        float x = (float)paragraph.Left, y = (float)paragraph.Top, s = (float)fontSize;
        var source = paragraph.Source;
        // Quote bars down the margin, one per quote it is in.
        for (int q = 0; q < source.Quote; q++)
        {
            float bar = x - (source.Quote - q) * s * 1.1f + s * 0.25f;
            context.FillRectangle(new RawRectF(bar, y - s * 0.15f, bar + Math.Max(2, s * 0.16f), y + (float)paragraph.Height + s * 0.15f), Brush(context, Color.FromArgb(0x99, ThemeAccent.R, ThemeAccent.G, ThemeAccent.B)));
        }
        switch (source.Kind)
        {
            case ParagraphKind.Rule:
                float middle = y + (float)paragraph.Height / 2;
                context.FillRectangle(new RawRectF(x, middle, x + (float)paragraph.Width, middle + 1), Brush(context, Color.FromArgb(0x60, inkColor.R, inkColor.G, inkColor.B)));
                return;
            case ParagraphKind.Code:
                context.FillRoundedRectangle(new RoundedRectangle(new RawRectF(x, y, x + (float)paragraph.Width, y + (float)paragraph.Height), s * 0.3f, s * 0.3f),
                    Brush(context, Color.FromArgb(0x24, inkColor.R, inkColor.G, inkColor.B)));
                break;
        }
        if (paragraph.Layout is null)
            return;
        float textX = x + (float)paragraph.Padding, textY = (float)paragraph.TextTop;
        if (source.Kind == ParagraphKind.Text && source.Runs.Any(r => r.Style.HasFlag(TextStyle.Code)))
        {
            paragraph.CodeBackings ??= CodeBackings(paragraph);
            var backing = Brush(context, Color.FromArgb(0x24, inkColor.R, inkColor.G, inkColor.B));
            foreach (var rect in paragraph.CodeBackings)
                context.FillRoundedRectangle(new RoundedRectangle(new RawRectF(textX + (float)rect.Left - 2, textY + (float)rect.Top, textX + (float)rect.Right + 2, textY + (float)rect.Bottom), 3, 3), backing);
        }
        if (paragraph.Marker is not null)
            context.DrawTextLayout(new Vector2(x - s * 1.6f - s * 0.35f, textY), paragraph.Marker, ink, DrawTextOptions.None); // boxes in the ink's colour, not as emoji
        context.DrawTextLayout(new Vector2(textX, textY), paragraph.Layout, source.Kind == ParagraphKind.Heading ? accent : ink, DrawTextOptions.EnableColorFont);
    }

    /// <summary>Where a paragraph's inline code sits, line by line, to draw its backing.</summary>
    private static List<Rect> CodeBackings(LaidParagraph paragraph)
    {
        var rects = new List<Rect>();
        var metrics = new Vortice.DirectWrite.HitTestMetrics[32];
        foreach (var run in paragraph.Source.Runs)
        {
            if (!run.Style.HasFlag(TextStyle.Code) || run.Length == 0)
                continue;
            paragraph.Layout!.HitTestTextRange((uint)run.Start, (uint)run.Length, 0, 0, metrics, out uint count);
            for (int i = 0; i < Math.Min(count, (uint)metrics.Length); i++)
                rects.Add(new Rect(metrics[i].Left, metrics[i].Top, metrics[i].Width, metrics[i].Height));
        }
        return rects;
    }

    /// <summary>An opened file laid out, once per version of its text, size and width.</summary>
    private NoteDocument Document(TextNote note)
    {
        if (_documents.TryGetValue(note, out var cached) && cached.Document.Matches(note) && cached.Accent == ThemeAccent)
        {
            cached.Generation = _generation;
            return cached.Document;
        }
        cached?.Document.Dispose();
        var context = _gpu.D2DContext;
        var document = NoteDocument.Build(note, Brush(context, ThemeAccent));
        _documents[note] = new CachedDocument(document, ThemeAccent) { Generation = _generation };
        return document;
    }

    /// <summary>The opacity of a scrolled file's text: fading out at the top and bottom, where there is more.</summary>
    private ID2D1GradientStopCollection Fades(ID2D1DeviceContext context, bool top, bool bottom)
    {
        if (!_fades.TryGetValue((top, bottom), out var stops))
        {
            stops = context.CreateGradientStopCollection(
            [
                new Vortice.Direct2D1.GradientStop(0, new Color4(0, 0, 0, top ? 0 : 1)),
                new Vortice.Direct2D1.GradientStop(Fade, new Color4(0, 0, 0, 1)),
                new Vortice.Direct2D1.GradientStop(1 - Fade, new Color4(0, 0, 0, 1)),
                new Vortice.Direct2D1.GradientStop(1, new Color4(0, 0, 0, bottom ? 0 : 1)),
            ], ExtendMode.Clamp);
            _fades[(top, bottom)] = stops;
        }
        return stops;
    }

    /// <summary>A stroke's path in the record's coordinates, made once per version of its points.</summary>
    private ID2D1PathGeometry Geometry(InkStroke stroke)
    {
        if (_strokes.TryGetValue(stroke, out var cached) && cached.Points == stroke.Points && cached.Revision == stroke.Revision)
        {
            cached.Generation = _generation;
            return cached.Geometry;
        }
        cached?.Geometry.Dispose();
        var points = stroke.Points;
        var path = _gpu.D2DFactory.CreatePathGeometry();
        using (var sink = path.Open())
        {
            sink.BeginFigure(new Vector2((float)points[0], (float)points[1]), FigureBegin.Hollow);
            for (int i = 2; i + 1 < points.Length; i += 2)
                sink.AddLine(new Vector2((float)points[i], (float)points[i + 1]));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        _strokes[stroke] = new CachedStroke(path, stroke.Points, stroke.Revision) { Generation = _generation };
        return path;
    }

    /// <summary>Lets go of paths and layouts of ink that was not drawn this time (undone, erased, edited).</summary>
    private void ForgetUnused()
    {
        foreach (var (stroke, cached) in _strokes)
        {
            if (cached.Generation != _generation)
                _unusedStrokes.Add(stroke);
        }
        foreach (var stroke in _unusedStrokes)
        {
            _strokes[stroke].Geometry.Dispose();
            _strokes.Remove(stroke);
        }
        _unusedStrokes.Clear();
        foreach (var (note, cached) in _notes)
        {
            if (cached.Generation != _generation)
                _unusedNotes.Add(note);
        }
        foreach (var note in _unusedNotes)
        {
            _notes[note].Layout.Dispose();
            _notes.Remove(note);
        }
        _unusedNotes.Clear();
        foreach (var (note, cached) in _documents)
        {
            if (cached.Generation != _generation)
                _unusedNotes.Add(note);
        }
        foreach (var note in _unusedNotes)
        {
            _documents[note].Document.Dispose();
            _documents.Remove(note);
        }
        _unusedNotes.Clear();
    }

    private ID2D1SolidColorBrush Brush(ID2D1DeviceContext context, Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = context.CreateSolidColorBrush(new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
            _brushes[color] = brush;
        }
        return brush;
    }

    /// <summary>Round caps and joins, as a pen is.</summary>
    private ID2D1StrokeStyle Round() =>
        _round ??= _gpu.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            DashCap = CapStyle.Round,
            LineJoin = LineJoin.Round,
        });

    private readonly record struct Segment(Vector2 From, Vector2 To, float Thickness, Color Color);

    private sealed class CachedDocument(NoteDocument document, Color accent)
    {
        public NoteDocument Document { get; } = document;

        public Color Accent { get; } = accent;

        public int Generation { get; set; }
    }

    private sealed class CachedStroke(ID2D1PathGeometry geometry, double[] points, int revision)
    {
        public ID2D1PathGeometry Geometry { get; } = geometry;

        public double[] Points { get; } = points;

        public int Revision { get; } = revision;

        public int Generation { get; set; }
    }

    private sealed class CachedNote(Vortice.DirectWrite.IDWriteTextLayout layout, string text, double fontSize, double? width, bool bold, bool italic)
    {
        public Vortice.DirectWrite.IDWriteTextLayout Layout { get; } = layout;

        public int Generation { get; set; }

        public bool Matches(TextNote note) =>
            note.Text == text && note.FontSize == fontSize && note.Width == width && note.Bold == bold && note.Italic == italic;
    }
}
