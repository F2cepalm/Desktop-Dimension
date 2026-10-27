using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Spaces;
using Vortice.Direct2D1;
using Color4 = Vortice.Mathematics.Color4;
using Int2 = Vortice.Mathematics.Int2;
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
    private readonly Dictionary<(bool Top, bool Bottom), ID2D1LinearGradientBrush> _fadeBrushes = [];
    private readonly List<InkStroke> _unusedStrokes = [];
    private readonly List<TextNote> _unusedNotes = [];
    private readonly List<Segment> _pending = [];
    private readonly List<DrawnStroke> _drawnStrokes = [];
    private readonly List<DrawnNote> _drawnNotes = [];
    private Rect _drawnStrokesBounds = Rect.Empty;
    private ID2D1Bitmap1? _ink;
    private ID2D1BitmapBrush1? _inkBrush;
    private ID2D1Bitmap1? _mask;
    private ID2D1Bitmap1? _still;             // the ink that stays put while a selection is dragged or notes are pushed (see Redraw)
    private Func<IInk, bool>? _stillWithout;  // what moves over it; null when there is no still picture
    private bool _stillHasStrokes;            // whether _still holds the stationary strokes while notes are pushed during reshape
    private ID2D1DeviceContext1? _context1;
    private IntPtr _context1Of;
    private ID2D1StrokeStyle? _round;
    private ID2D1StrokeStyle? _dashed;
    private int _capacityWidth;
    private int _capacityHeight;
    private Point _origin;
    private double _scale = 1;
    private Outline2D _outline = Outline2D.Empty;
    private bool _maskStale = true;
    private bool _inkDrawn;
    private bool _cropped;
    private Point _drawnOrigin;
    private int _drawnWidth = 1;
    private int _drawnHeight = 1;
    private int _inkRevision;
    private double _drawnScale;
    private Color _drawnThemeInk;
    private Color _drawnThemeAccent;
    private TextNote? _drawnSkip;
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

    /// <summary>Whether <see cref="_ink"/> currently holds a tight bounding box of the ink rather than the whole window.</summary>
    public bool IsCropped => _cropped && _inkDrawn && _pending.Count == 0;

    /// <summary>Version of the drawn ink bitmap; changes each time <see cref="_ink"/> is redrawn.</summary>
    public int Revision => _inkRevision;

    /// <summary>Device pixel width of the drawn ink when <see cref="IsCropped"/> is true.</summary>
    public int DrawnWidth => _drawnWidth;

    /// <summary>Device pixel height of the drawn ink when <see cref="IsCropped"/> is true.</summary>
    public int DrawnHeight => _drawnHeight;

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
        int max = (int)Math.Min(16384, _gpu.D2DContext.MaximumBitmapSize);
        Width = Math.Clamp(width, 1, max);
        Height = Math.Clamp(height, 1, max);
        _scale = scale;
        _origin = origin;
        if (!_inkDrawn)
        {
            _drawnOrigin = origin;
            _drawnWidth = Width;
            _drawnHeight = Height;
        }
        _outline = Outline2D.From(outline, origin);
        _maskStale = true;
        _pending.Clear();
        _stillWithout = null; // the still picture of a drag is at the old shape
        if (_room <= 1 || _ink is null)
            EnsureCapacity(_cropped ? _drawnWidth : Width, _cropped ? _drawnHeight : Height);
    }

    /// <summary>
    /// The space moved on screen with everything on it: the pixels stay as they are, only the screen
    /// position of pixel (0,0) changes.
    /// </summary>
    public void MoveOrigin(Vector offset)
    {
        _origin += offset;
        _drawnOrigin += offset;
    }

    /// <summary>Redraws all strokes and notes of the space, mapped onto the screen by <paramref name="toScreen"/>.</summary>
    /// <param name="skip">A note left out (it is being edited in a text box, which shows it instead).</param>
    /// <param name="placeNote">Where each note goes on screen, when not where <paramref name="toScreen"/> puts it.</param>
    /// <returns>The changed area: all of it.</returns>
    /// <param name="opacity">How opaque each note is drawn (newly opened files fade in); fully, when null.</param>
    /// <param name="placeStroke">How each stroke is mapped onto the screen, when not where <paramref name="toScreen"/> places it (see <see cref="InkStroke.Placed"/>).</param>
    /// <param name="adornments">The hand's selection frames and rectangle, drawn over the ink; none when null.</param>
    /// <param name="moving">
    /// The ink being dragged, while a drag lasts (the same function each frame): the rest is painted once
    /// and kept, so each frame of the drag only copies that and draws what moves, over it. Null otherwise.
    /// </param>
    public Int32Rect Redraw(SpaceRecord record, Matrix toScreen = default, TextNote? skip = null, Func<TextNote, Point>? placeNote = null, Func<TextNote, float>? opacity = null, Func<InkStroke, Matrix>? placeStroke = null, InkAdornments? adornments = null, Func<IInk, bool>? moving = null)
    {
        _pending.Clear();
        if (moving is not null)
        {
            _cropped = false;
            _drawnOrigin = _origin;
            _drawnWidth = Width;
            _drawnHeight = Height;
            EnsureCapacity(Width, Height);
            var stillWorld = World(_origin);
            if (moving == _stillWithout || PaintStill(record, toScreen, skip, placeNote, opacity, placeStroke, stillWorld, moving))
            {
                _inkDrawn = false;
                _inkRevision++;
                var target = Begin(_ink!);
                target.PushAxisAlignedClip(new RawRectF(0, 0, Width, Height), AntialiasMode.Aliased);
                target.DrawImage(_still!, Vector2.Zero, new RawRectF(0, 0, Width, Height), InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
                DrawItems(target, record, toScreen, skip, placeNote, opacity, placeStroke, stillWorld, moving);
                if (adornments is not null)
                    DrawAdornments(target, adornments, stillWorld);
                target.PopAxisAlignedClip();
                End(target);
                return Bounds;
            }
        }

        if (_stillWithout is not null)
            ReleaseStill();
        bool wantCropped = _room > 1 && moving is null && adornments is null;
        bool strokesMatch = moving is null && adornments is null && _cropped == wantCropped && CanKeepStrokes(record, toScreen, skip, placeStroke);
        if (strokesMatch && CanKeepNotes(record, toScreen, skip, placeNote, opacity))
            return Bounds;

        if (wantCropped)
        {
            var inkBounds = MeasureInkBounds(record, toScreen, skip, placeNote, opacity, placeStroke, strokesMatch);
            int ox = (int)Math.Floor((inkBounds.Left - _origin.X) * _scale);
            int oy = (int)Math.Floor((inkBounds.Top - _origin.Y) * _scale);
            var candidateOrigin = new Point(_origin.X + ox / _scale, _origin.Y + oy / _scale);
            int max = (int)Math.Min(16384, _gpu.D2DContext.MaximumBitmapSize);
            int candidateWidth = Math.Clamp((int)Math.Ceiling((inkBounds.Right - candidateOrigin.X) * _scale), 1, max);
            int candidateHeight = Math.Clamp((int)Math.Ceiling((inkBounds.Bottom - candidateOrigin.Y) * _scale), 1, max);
            double prevDx = (_origin.X - _drawnOrigin.X) * _scale, prevDy = (_origin.Y - _drawnOrigin.Y) * _scale;
            bool prevGridAligned = _cropped && _scale == _drawnScale &&
                Math.Abs(prevDx - Math.Round(prevDx)) <= 1e-2 && Math.Abs(prevDy - Math.Round(prevDy)) <= 1e-2;
            double oldRight = _drawnOrigin.X + _drawnWidth / _scale;
            double oldBottom = _drawnOrigin.Y + _drawnHeight / _scale;
            double newRight = candidateOrigin.X + candidateWidth / _scale;
            double newBottom = candidateOrigin.Y + candidateHeight / _scale;
            if (!prevGridAligned)
            {
                _drawnOrigin = candidateOrigin;
                _drawnWidth = candidateWidth;
                _drawnHeight = candidateHeight;
                _stillHasStrokes = false;
            }
            else if (candidateOrigin.X < _drawnOrigin.X || candidateOrigin.Y < _drawnOrigin.Y || newRight > oldRight || newBottom > oldBottom)
            {
                var unionOrigin = new Point(Math.Min(_drawnOrigin.X, candidateOrigin.X), Math.Min(_drawnOrigin.Y, candidateOrigin.Y));
                _drawnWidth = Math.Clamp((int)Math.Ceiling((Math.Max(oldRight, newRight) - unionOrigin.X) * _scale), 1, max);
                _drawnHeight = Math.Clamp((int)Math.Ceiling((Math.Max(oldBottom, newBottom) - unionOrigin.Y) * _scale), 1, max);
                _drawnOrigin = unionOrigin;
                _stillHasStrokes = false;
            }
            _cropped = true;
            _maskStale = true;
        }
        else
        {
            ReleaseStill();
            _drawnOrigin = _origin;
            _drawnWidth = Width;
            _drawnHeight = Height;
            _cropped = false;
            _maskStale = true;
        }
        if (!strokesMatch)
            _stillHasStrokes = false;
        EnsureCapacity(_drawnWidth, _drawnHeight);
        var world = World(_drawnOrigin);
        _generation++;
        bool useStillStrokes = wantCropped && record.Strokes.Count > 0 && record.Notes.Count > 0;
        if (useStillStrokes && !_stillHasStrokes)
        {
            _still ??= _gpu.D2DContext.CreateBitmap(new SizeI(_capacityWidth, _capacityHeight), IntPtr.Zero, 0, new BitmapProperties1(GpuCompositor.Bgra, 96, 96, BitmapOptions.Target));
            var stillContext = Begin(_still);
            stillContext.PushAxisAlignedClip(new RawRectF(0, 0, _drawnWidth, _drawnHeight), AntialiasMode.Aliased);
            stillContext.Clear(new Color4(0, 0, 0, 0));
            DrawItems(stillContext, record, toScreen, skip, placeNote, opacity, placeStroke, world, static item => item is InkStroke);
            stillContext.PopAxisAlignedClip();
            End(stillContext);
            _stillHasStrokes = true;
        }
        var context = Begin(_ink!);
        context.PushAxisAlignedClip(new RawRectF(0, 0, _drawnWidth, _drawnHeight), AntialiasMode.Aliased);
        bool fading;
        if (useStillStrokes)
        {
            var drawnRect = new RawRectF(0, 0, _drawnWidth, _drawnHeight);
            context.DrawImage(_still!, Vector2.Zero, drawnRect, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
            fading = DrawItems(context, record, toScreen, skip, placeNote, opacity, placeStroke, world, static item => item is TextNote);
            if (!strokesMatch)
            {
                foreach (var stroke in record.Strokes)
                {
                    if (_strokes.TryGetValue(stroke, out var cached))
                        cached.Generation = _generation;
                }
            }
        }
        else
        {
            context.Clear(new Color4(0, 0, 0, 0));
            fading = DrawItems(context, record, toScreen, skip, placeNote, opacity, placeStroke, world, null);
        }
        if (adornments is not null)
            DrawAdornments(context, adornments, world);
        context.PopAxisAlignedClip();
        End(context);
        if (!strokesMatch || record.Notes.Count != _drawnNotes.Count)
            ForgetUnused();
        _inkRevision++;
        if (moving is null && adornments is null && !fading)
            RecordDrawnInk(record, toScreen, skip, placeNote, opacity, placeStroke, strokesMatch);
        else
        {
            _inkDrawn = false;
            _cropped = false;
        }
        return Bounds;
    }

    private Rect MeasureInkBounds(SpaceRecord record, Matrix toScreen, TextNote? skip, Func<TextNote, Point>? placeNote, Func<TextNote, float>? opacity, Func<InkStroke, Matrix>? placeStroke, bool strokesMatch = false)
    {
        var bounds = strokesMatch ? _drawnStrokesBounds : Rect.Empty;
        if (!strokesMatch)
        {
            foreach (var stroke in record.Strokes)
            {
                var points = stroke.Points;
                if (points.Length < 2)
                    continue;
                var m = placeStroke?.Invoke(stroke) ?? stroke.Placed(toScreen, record.Anchor);
                double reach = stroke.Thickness / 2 + 4;
                var box = points.Length < 4
                    ? new Rect(m.Transform(new Point(points[0], points[1])), new Size(0, 0))
                    : Rect.Transform(stroke.Box, m);
                box.Inflate(reach, reach);
                bounds.Union(box);
            }
            _drawnStrokesBounds = bounds;
        }
        foreach (var note in record.Notes)
        {
            if (note == skip || note.Text.Length == 0)
                continue;
            float alpha = opacity?.Invoke(note) ?? 1;
            if (alpha <= 0)
                continue;
            var at = placeNote?.Invoke(note) ?? toScreen.Transform(new Point(note.X, note.Y));
            if (note.File is { } file)
            {
                var document = Document(note);
                double width = document.Width, height = Math.Max(document.HeaderHeight, file.Height);
                double pad = note.FontSize * 0.6 + 4;
                bounds.Union(new Rect(at.X - pad, at.Y - pad, width + 2 * pad, height + 2 * pad));
            }
            else
            {
                var cached = NoteLayout(note);
                var metrics = cached.Metrics;
                double pad = Math.Max(8, note.FontSize * 0.5);
                double width = Math.Max(metrics.WidthIncludingTrailingWhitespace, metrics.Width) + 2 * pad;
                double height = metrics.Height + 2 * pad;
                bounds.Union(new Rect(at.X + metrics.Left - pad, at.Y + metrics.Top - pad, Math.Max(1, width), Math.Max(1, height)));
            }
        }
        if (bounds.IsEmpty)
            return new Rect(_origin.X, _origin.Y, 1 / _scale, 1 / _scale);
        return bounds;
    }

    /// <summary>
    /// Whether the strokes drawn in <see cref="_ink"/> are still at the same screen positions and scale.
    /// </summary>
    private bool CanKeepStrokes(SpaceRecord record, Matrix toScreen, TextNote? skip, Func<InkStroke, Matrix>? placeStroke)
    {
        if (!_inkDrawn || _scale != _drawnScale || ThemeInk != _drawnThemeInk || ThemeAccent != _drawnThemeAccent || skip != _drawnSkip)
            return false;
        double dx = (_origin.X - _drawnOrigin.X) * _scale, dy = (_origin.Y - _drawnOrigin.Y) * _scale;
        int sx = (int)Math.Round(dx), sy = (int)Math.Round(dy);
        if (Math.Abs(dx - sx) > 1e-2 || Math.Abs(dy - sy) > 1e-2)
            return false;
        if (!_cropped && (sx < 0 || sy < 0 || sx + Width > _capacityWidth || sy + Height > _capacityHeight))
            return false;
        if (record.Strokes.Count != _drawnStrokes.Count)
            return false;
        for (int i = 0; i < record.Strokes.Count; i++)
        {
            var stroke = record.Strokes[i];
            var drawn = _drawnStrokes[i];
            var m = placeStroke?.Invoke(stroke) ?? stroke.Placed(toScreen, record.Anchor);
            if (!ReferenceEquals(stroke, drawn.Stroke) || !ReferenceEquals(stroke.Points, drawn.Points) || stroke.Revision != drawn.Revision || stroke.Thickness != drawn.Thickness || stroke.Color != drawn.Color || m != drawn.ToScreen)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether the text notes drawn in <see cref="_ink"/> are still at the same screen positions and state.
    /// </summary>
    private bool CanKeepNotes(SpaceRecord record, Matrix toScreen, TextNote? skip, Func<TextNote, Point>? placeNote, Func<TextNote, float>? opacity)
    {
        int noteIndex = 0;
        foreach (var note in record.Notes)
        {
            if (note == skip || note.Text.Length == 0)
                continue;
            float alpha = opacity?.Invoke(note) ?? 1;
            if (alpha <= 0)
                continue;
            if (noteIndex >= _drawnNotes.Count)
                return false;
            var drawn = _drawnNotes[noteIndex++];
            var at = placeNote?.Invoke(note) ?? toScreen.Transform(new Point(note.X, note.Y));
            var file = note.File;
            if (!ReferenceEquals(note, drawn.Note) || note.Text != drawn.Text || note.FontSize != drawn.FontSize || note.Width != drawn.Width || note.Bold != drawn.Bold || note.Italic != drawn.Italic || note.Color != drawn.Color || (file?.Scroll ?? 0) != drawn.Scroll || (file?.Height ?? 0) != drawn.FileHeight || file?.Markdown != drawn.Markdown || at != drawn.At || alpha != drawn.Opacity)
                return false;
        }
        return noteIndex == _drawnNotes.Count;
    }

    private void RecordDrawnInk(SpaceRecord record, Matrix toScreen, TextNote? skip, Func<TextNote, Point>? placeNote, Func<TextNote, float>? opacity, Func<InkStroke, Matrix>? placeStroke, bool strokesMatch = false)
    {
        _inkDrawn = true;
        _drawnScale = _scale;
        _drawnThemeInk = ThemeInk;
        _drawnThemeAccent = ThemeAccent;
        _drawnSkip = skip;
        if (!strokesMatch)
        {
            _drawnStrokes.Clear();
            foreach (var stroke in record.Strokes)
            {
                var m = placeStroke?.Invoke(stroke) ?? stroke.Placed(toScreen, record.Anchor);
                _drawnStrokes.Add(new DrawnStroke(stroke, stroke.Points, stroke.Revision, stroke.Thickness, stroke.Color, m));
            }
        }
        _drawnNotes.Clear();
        foreach (var note in record.Notes)
        {
            if (note == skip || note.Text.Length == 0)
                continue;
            float alpha = opacity?.Invoke(note) ?? 1;
            if (alpha <= 0)
                continue;
            var at = placeNote?.Invoke(note) ?? toScreen.Transform(new Point(note.X, note.Y));
            var file = note.File;
            _drawnNotes.Add(new DrawnNote(note, note.Text, note.FontSize, note.Width, note.Bold, note.Italic, note.Color, file?.Scroll ?? 0, file?.Height ?? 0, file?.Markdown, at, alpha));
        }
    }

    /// <summary>
    /// Paints the ink that stays put while <paramref name="moving"/> is dragged, to be copied every frame of the drag.
    /// </summary>
    /// <returns>false when it can't be kept (a file is fading in): every frame then draws everything.</returns>
    private bool PaintStill(SpaceRecord record, Matrix toScreen, TextNote? skip, Func<TextNote, Point>? placeNote, Func<TextNote, float>? opacity, Func<InkStroke, Matrix>? placeStroke, Matrix3x2 world, Func<IInk, bool> moving)
    {
        _stillWithout = null;
        _stillHasStrokes = false;
        _still ??= _gpu.D2DContext.CreateBitmap(new SizeI(_capacityWidth, _capacityHeight), IntPtr.Zero, 0, new BitmapProperties1(GpuCompositor.Bgra, 96, 96, BitmapOptions.Target));
        _generation++; // this frame draws all of the ink, still and moving: what isn't drawn is let go
        var context = Begin(_still);
        context.PushAxisAlignedClip(new RawRectF(0, 0, Width, Height), AntialiasMode.Aliased);
        context.Clear(new Color4(0, 0, 0, 0));
        bool fading = DrawItems(context, record, toScreen, skip, placeNote, opacity, placeStroke, world, item => !moving(item));
        context.PopAxisAlignedClip();
        End(context);
        foreach (var stroke in record.Strokes)
        {
            if (moving(stroke) && _strokes.TryGetValue(stroke, out var cached))
                cached.Generation = _generation;
        }
        foreach (var note in record.Notes)
        {
            if (moving(note) && _notes.TryGetValue(note, out var cached))
                cached.Generation = _generation;
            if (moving(note) && _documents.TryGetValue(note, out var document))
                document.Generation = _generation;
        }
        ForgetUnused();
        if (fading)
            return false;
        _stillWithout = moving;
        return true;
    }

    /// <summary>Lets go of the still picture of a drag (see <see cref="PaintStill"/>) once the drag is over.</summary>
    private void ReleaseStill()
    {
        _stillWithout = null;
        _stillHasStrokes = false;
        _still?.Dispose();
        _still = null;
    }

    /// <summary>Draws strokes, then notes, of those <paramref name="which"/> takes (all when null).</summary>
    /// <returns>Whether a note was drawn partly see-through (a file fading in).</returns>
    private bool DrawItems(ID2D1DeviceContext context, SpaceRecord record, Matrix toScreen, TextNote? skip, Func<TextNote, Point>? placeNote, Func<TextNote, float>? opacity, Func<InkStroke, Matrix>? placeStroke, Matrix3x2 world, Func<IInk, bool>? which)
    {
        bool fading = false;
        foreach (var stroke in record.Strokes)
        {
            if (which is null || which(stroke))
                DrawStroke(context, stroke, placeStroke?.Invoke(stroke) ?? stroke.Placed(toScreen, record.Anchor), world); // moved, never stretched
        }
        foreach (var note in record.Notes)
        {
            if (note == skip || note.Text.Length == 0 || (which is not null && !which(note))) // an empty note was removed by editing; it is kept only for undo
                continue;
            float alpha = opacity?.Invoke(note) ?? 1;
            if (alpha <= 0)
                continue;
            if (alpha < 1)
            {
                fading = true;
                context.Transform = Matrix3x2.Identity;
                context.PushLayer(new LayerParameters1
                {
                    ContentBounds = new RawRectF(0, 0, _drawnWidth, _drawnHeight),
                    MaskTransform = Matrix3x2.Identity,
                    Opacity = alpha,
                }, null!);
            }
            DrawNote(context, note, placeNote?.Invoke(note) ?? toScreen.Transform(new Point(note.X, note.Y)), world);
            if (alpha < 1)
                context.PopLayer();
        }
        return fading;
    }

    /// <summary>Adds one piece of a stroke being painted (screen DIPs); a zero-length piece is a dot.</summary>
    /// <param name="color">The stroke's colour; null for the theme's ink.</param>
    /// <returns>The changed area, in device pixels.</returns>
    public Int32Rect AddSegment(Point from, Point to, double thickness, Color? color = null)
    {
        var a = new Vector2((float)(from.X - _drawnOrigin.X), (float)(from.Y - _drawnOrigin.Y));
        var b = new Vector2((float)(to.X - _drawnOrigin.X), (float)(to.Y - _drawnOrigin.Y));
        double reach = thickness / 2;
        int x0 = Math.Clamp((int)Math.Floor((Math.Min(from.X, to.X) - _origin.X - reach) * _scale) - Bleed, 0, Width);
        int y0 = Math.Clamp((int)Math.Floor((Math.Min(from.Y, to.Y) - _origin.Y - reach) * _scale) - Bleed, 0, Height);
        int x1 = Math.Clamp((int)Math.Ceiling((Math.Max(from.X, to.X) - _origin.X + reach) * _scale) + Bleed, 0, Width);
        int y1 = Math.Clamp((int)Math.Ceiling((Math.Max(from.Y, to.Y) - _origin.Y + reach) * _scale) + Bleed, 0, Height);
        if (x1 <= x0 || y1 <= y0)
            return Int32Rect.Empty;
        _pending.Add(new Segment(a, b, (float)thickness, color ?? ThemeInk));
        _inkDrawn = false;
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
        int sx = (int)Math.Round((_origin.X - _drawnOrigin.X) * _scale);
        int sy = (int)Math.Round((_origin.Y - _drawnOrigin.Y) * _scale);
        if (_cropped)
        {
            context.Clear(new Color4(0, 0, 0, 0));
            int x0 = Math.Max(area.X, -sx), y0 = Math.Max(area.Y, -sy);
            int x1 = Math.Min(area.X + area.Width, _drawnWidth - sx), y1 = Math.Min(area.Y + area.Height, _drawnHeight - sy);
            if (x1 > x0 && y1 > y0)
            {
                var subCorner = new Vector2(x0, y0);
                var subInk = new RawRectF(x0 + sx, y0 + sy, x1 + sx, y1 + sy);
                context.DrawImage(_ink!, subCorner, subInk, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
                context.DrawImage(_mask!, subCorner, subInk, InterpolationMode.NearestNeighbor, CompositeMode.DestinationIn);
            }
        }
        else
        {
            var corner = new Vector2(area.X, area.Y);
            var inkRect = new RawRectF(area.X + sx, area.Y + sy, area.X + area.Width + sx, area.Y + area.Height + sy);
            context.DrawImage(_ink!, corner, inkRect, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
            context.DrawImage(_mask!, corner, rect, InterpolationMode.NearestNeighbor, CompositeMode.DestinationIn);
        }
        context.PopAxisAlignedClip();
        context.Transform = Matrix3x2.Identity;
    }

    /// <summary>
    /// Where the cropped ink bitmap sits in the window (device pixels), the part of it inside the
    /// space's outline bounds, and whether that part lies wholly inside the space's outline (so no mask is needed).
    /// </summary>
    public void GetCroppedPlacement(out Int2 offset, out Int32Rect visible, out bool unclipped)
    {
        int ox = (int)Math.Round((_drawnOrigin.X - _origin.X) * _scale);
        int oy = (int)Math.Round((_drawnOrigin.Y - _origin.Y) * _scale);
        offset = new Int2(ox, oy);
        var outlineBounds = _outline.Bounds;
        int bx0 = outlineBounds.IsEmpty ? 0 : Math.Clamp((int)Math.Round(outlineBounds.Left * _scale), 0, Width);
        int by0 = outlineBounds.IsEmpty ? 0 : Math.Clamp((int)Math.Round(outlineBounds.Top * _scale), 0, Height);
        int bx1 = outlineBounds.IsEmpty ? 0 : Math.Clamp((int)Math.Round(outlineBounds.Right * _scale), 0, Width);
        int by1 = outlineBounds.IsEmpty ? 0 : Math.Clamp((int)Math.Round(outlineBounds.Bottom * _scale), 0, Height);
        int vx0 = Math.Clamp(bx0 - ox, 0, _drawnWidth);
        int vy0 = Math.Clamp(by0 - oy, 0, _drawnHeight);
        int vx1 = Math.Clamp(bx1 - ox, 0, _drawnWidth);
        int vy1 = Math.Clamp(by1 - oy, 0, _drawnHeight);
        if (vx1 <= vx0 || vy1 <= vy0)
        {
            visible = Int32Rect.Empty;
            unclipped = true;
            return;
        }
        visible = new Int32Rect(vx0, vy0, vx1 - vx0, vy1 - vy0);
        double inset = 0.5 / _scale;
        double dw = Math.Max(0, (vx1 - vx0) / _scale - 2 * inset);
        double dh = Math.Max(0, (vy1 - vy0) / _scale - 2 * inset);
        var visibleDip = new Rect((ox + vx0) / _scale + inset, (oy + vy0) / _scale + inset, dw, dh);
        unclipped = _outline.Covers(visibleDip);
    }

    /// <summary>Copies the cropped ink bitmap into the target at <paramref name="at"/>, masked over <paramref name="visible"/> when <paramref name="unclipped"/> is false.</summary>
    public void ComposeCropped(ID2D1DeviceContext context, Vector2 at, Int32Rect visible, bool unclipped, bool clearFirst = true)
    {
        if (unclipped)
        {
            context.Transform = Matrix3x2.CreateTranslation(at.X, at.Y);
            var rect = new RawRectF(0, 0, _drawnWidth, _drawnHeight);
            context.PushAxisAlignedClip(rect, AntialiasMode.Aliased);
            context.DrawImage(_ink!, Vector2.Zero, rect, InterpolationMode.NearestNeighbor, clearFirst ? CompositeMode.SourceCopy : CompositeMode.SourceOver);
            context.PopAxisAlignedClip();
            context.Transform = Matrix3x2.Identity;
            return;
        }

        context.Transform = Matrix3x2.CreateTranslation(at.X, at.Y);
        var clip = new RawRectF(visible.X, visible.Y, visible.X + visible.Width, visible.Y + visible.Height);
        context.PushAxisAlignedClip(clip, AntialiasMode.Aliased);
        if (clearFirst)
            context.Clear(new Color4(0, 0, 0, 0));
        if (_outline.Figures.Length > 0)
        {
            using var shape = _outline.ToPath(_gpu.D2DFactory);
            _inkBrush ??= context.CreateBitmapBrush(_ink, new BitmapBrushProperties1
            {
                ExtendModeX = ExtendMode.Clamp,
                ExtendModeY = ExtendMode.Clamp,
                InterpolationMode = InterpolationMode.NearestNeighbor,
            });
            _inkBrush.Transform = Matrix3x2.CreateScale((float)(1 / _scale)) *
                Matrix3x2.CreateTranslation((float)(_drawnOrigin.X - _origin.X), (float)(_drawnOrigin.Y - _origin.Y));
            context.Transform = Matrix3x2.CreateTranslation((float)(_origin.X - _drawnOrigin.X), (float)(_origin.Y - _drawnOrigin.Y)) *
                Matrix3x2.CreateScale((float)_scale) *
                Matrix3x2.CreateTranslation(at.X, at.Y);
            context.FillGeometry(shape, _inkBrush);
        }
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
            stroke.Dispose();
        _strokes.Clear();
        foreach (var note in _notes.Values)
            note.Layout.Dispose();
        _notes.Clear();
        foreach (var document in _documents.Values)
            document.Document.Dispose();
        _documents.Clear();
        foreach (var fade in _fadeBrushes.Values)
            fade.Dispose();
        _fadeBrushes.Clear();
        foreach (var fade in _fades.Values)
            fade.Dispose();
        _fades.Clear();
        _context1?.Dispose();
        _dashed?.Dispose();
        _round?.Dispose();
        _inkBrush?.Dispose();
        _inkBrush = null;
        _ink?.Dispose();
        _mask?.Dispose();
        _still?.Dispose();
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
    private Matrix3x2 World(Point origin) =>
        Matrix3x2.CreateTranslation((float)-origin.X, (float)-origin.Y) * Matrix3x2.CreateScale((float)_scale);

    private double _room = 1;
    private bool _expand;
    private bool _trim;

    /// <summary>
    /// Room the bitmaps make beyond the layer's size: more than 1 while the space is picked up to be
    /// reshaped (see <see cref="SpaceSurface.ReshapeRoom"/>). Back to 1, they shrink back at the next shape.
    /// </summary>
    public double ReshapeRoom
    {
        get => _room;
        set
        {
            _expand = value > _room;
            _trim |= value < _room;
            _room = value;
        }
    }

    /// <summary>Makes sure the bitmaps hold the given pixel size; they are only made anew when too small (or far too big).</summary>
    private void EnsureCapacity(int targetWidth, int targetHeight)
    {
        double room = _expand && !_cropped ? _room : 1;
        _expand = false;
        int wantedWidth = Headroom.Wanted(targetWidth, room), wantedHeight = Headroom.Wanted(targetHeight, room);
        bool fits = _ink is not null && _capacityWidth >= wantedWidth && _capacityHeight >= wantedHeight;
        bool wasteful = _room <= 1 && (long)_capacityWidth * _capacityHeight > 4L * Math.Max(Step * Step, (long)wantedWidth * wantedHeight);
        bool oversized = _trim && (_capacityWidth > Headroom.Size(wantedWidth, 0) || _capacityHeight > Headroom.Size(wantedHeight, 0));
        _trim = false;
        if (fits && !wasteful && !oversized)
            return;
        if (!fits && _room > 1 && !_cropped)
        {
            wantedWidth = Headroom.Wanted(targetWidth, _room);
            wantedHeight = Headroom.Wanted(targetHeight, _room);
        }
        int max = (int)Math.Min(16384, _gpu.D2DContext.MaximumBitmapSize);
        _capacityWidth = Math.Min(max, oversized ? Headroom.Size(wantedWidth, 0) : Headroom.Size(wantedWidth, _capacityWidth));
        _capacityHeight = Math.Min(max, oversized ? Headroom.Size(wantedHeight, 0) : Headroom.Size(wantedHeight, _capacityHeight));
        if (!_cropped)
        {
            Width = Math.Min(Width, _capacityWidth);
            Height = Math.Min(Height, _capacityHeight);
        }
        _drawnWidth = Math.Min(_drawnWidth, _capacityWidth);
        _drawnHeight = Math.Min(_drawnHeight, _capacityHeight);
        _inkBrush?.Dispose();
        _inkBrush = null;
        _ink?.Dispose();
        _mask?.Dispose();
        ReleaseStill(); // made again at the new size if a drag goes on
        var size = new SizeI(_capacityWidth, _capacityHeight);
        _ink = _gpu.D2DContext.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(GpuCompositor.Bgra, 96, 96, BitmapOptions.Target));
        _mask = _gpu.D2DContext.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(GpuCompositor.Bgra, 96, 96, BitmapOptions.Target));
        _maskStale = true;
        _inkDrawn = false;
    }

    /// <summary>The outline, filled, in the mask's alpha.</summary>
    private void EnsureMask()
    {
        if (!_maskStale)
            return;
        _maskStale = false;
        int maskWidth = _cropped ? _drawnWidth : Width;
        int maskHeight = _cropped ? _drawnHeight : Height;
        var maskOrigin = _cropped ? _drawnOrigin : _origin;
        var context = Begin(_mask!);
        context.PushAxisAlignedClip(new RawRectF(0, 0, maskWidth, maskHeight), AntialiasMode.Aliased);
        context.Clear(new Color4(0, 0, 0, 0));
        if (_outline.Figures.Length > 0)
        {
            using var shape = _outline.ToPath(_gpu.D2DFactory);
            context.Transform = Matrix3x2.CreateTranslation((float)(_origin.X - maskOrigin.X), (float)(_origin.Y - maskOrigin.Y)) * Matrix3x2.CreateScale((float)_scale);
            context.FillGeometry(shape, Brush(context, Colors.White));
        }
        context.PopAxisAlignedClip();
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

        var cached = Stroke(stroke); // in the record's coordinates
        if (toScreen.M11 == 1 && toScreen.M22 == 1 && toScreen.M12 == 0 && toScreen.M21 == 0)
        {
            var box = stroke.Box;
            double reach = thickness / 2 + 2;
            if (box.Right + toScreen.OffsetX + reach < _drawnOrigin.X ||
                box.Left + toScreen.OffsetX - reach > _drawnOrigin.X + _drawnWidth / _scale ||
                box.Bottom + toScreen.OffsetY + reach < _drawnOrigin.Y ||
                box.Top + toScreen.OffsetY - reach > _drawnOrigin.Y + _drawnHeight / _scale)
                return;

            // Only moved: the cached geometry as it is, tessellated once for this scale and pen width.
            context.Transform = Matrix3x2.CreateTranslation((float)toScreen.OffsetX, (float)toScreen.OffsetY) * world;
            if (Context1(context) is { } context1)
                context1.DrawGeometryRealization(cached.Realize(context1, thickness, _scale, Round()), brush);
            else
                context.DrawGeometry(cached.Geometry, brush, thickness, Round());
            return;
        }
        // Stretched: the points go through the mapping, the pen keeps its width.
        var mapping = new Matrix3x2((float)toScreen.M11, (float)toScreen.M12, (float)toScreen.M21, (float)toScreen.M22, (float)toScreen.OffsetX, (float)toScreen.OffsetY);
        using var mapped = _gpu.D2DFactory.CreateTransformedGeometry(cached.Geometry, mapping);
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
        var cached = NoteLayout(note);
        context.Transform = Matrix3x2.CreateTranslation((float)at.X, (float)at.Y) * world;
        context.DrawTextLayout(Vector2.Zero, cached.Layout, Brush(context, InkColors.Resolve(note.Color, ThemeInk)), DrawTextOptions.EnableColorFont);
    }

    private CachedNote NoteLayout(TextNote note)
    {
        if (!_notes.TryGetValue(note, out var cached) || !cached.Matches(note))
        {
            cached?.Layout.Dispose();
            cached = new CachedNote(NoteText.Layout(note), note.Text, note.FontSize, note.Width, note.Bold, note.Italic);
            _notes[note] = cached;
        }
        cached.Generation = _generation;
        return cached;
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
        if (fadeTop || fadeBottom)
        {
            if (!_fadeBrushes.TryGetValue((fadeTop, fadeBottom), out var fade))
            {
                fade = context.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(0, top), new Vector2(0, height)), Fades(context, fadeTop, fadeBottom));
                _fadeBrushes[(fadeTop, fadeBottom)] = fade;
            }
            else
            {
                fade.StartPoint = new Vector2(0, top);
                fade.EndPoint = new Vector2(0, height);
            }
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
        if (fadeTop || fadeBottom)
            context.PopLayer();
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
        if (source.Kind == ParagraphKind.Text)
        {
            var backings = paragraph.CodeBackings ??= CodeBackings(paragraph);
            if (backings.Count > 0)
            {
                var backing = Brush(context, Color.FromArgb(0x24, inkColor.R, inkColor.G, inkColor.B));
                foreach (var rect in backings)
                    context.FillRoundedRectangle(new RoundedRectangle(new RawRectF(textX + (float)rect.Left - 2, textY + (float)rect.Top, textX + (float)rect.Right + 2, textY + (float)rect.Bottom), 3, 3), backing);
            }
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
    private CachedStroke Stroke(InkStroke stroke)
    {
        if (_strokes.TryGetValue(stroke, out var cached) && cached.Points == stroke.Points && cached.Revision == stroke.Revision)
        {
            cached.Generation = _generation;
            return cached;
        }
        cached?.Dispose();
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
        cached = new CachedStroke(path, stroke.Points, stroke.Revision) { Generation = _generation };
        _strokes[stroke] = cached;
        return cached;
    }

    private ID2D1DeviceContext1? Context1(ID2D1DeviceContext context)
    {
        if (_context1Of != context.NativePointer)
        {
            _context1?.Dispose();
            _context1 = context.QueryInterfaceOrNull<ID2D1DeviceContext1>();
            _context1Of = context.NativePointer;
            foreach (var stroke in _strokes.Values)
                stroke.ClearRealization();
        }
        return _context1;
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
            _strokes[stroke].Dispose();
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

    private readonly record struct DrawnStroke(InkStroke Stroke, double[] Points, int Revision, double Thickness, string? Color, Matrix ToScreen);

    private readonly record struct DrawnNote(TextNote Note, string Text, double FontSize, double? Width, bool Bold, bool Italic, string? Color, double Scroll, double FileHeight, bool? Markdown, Point At, float Opacity);

    private sealed class CachedDocument(NoteDocument document, Color accent)
    {
        public NoteDocument Document { get; } = document;

        public Color Accent { get; } = accent;

        public int Generation { get; set; }
    }

    private sealed class CachedStroke(ID2D1PathGeometry geometry, double[] points, int revision) : IDisposable
    {
        public ID2D1PathGeometry Geometry { get; } = geometry;

        public double[] Points { get; } = points;

        public int Revision { get; } = revision;

        public int Generation { get; set; }

        public ID2D1GeometryRealization? Realization { get; private set; }

        private float _realizedThickness;
        private double _realizedScale;

        public ID2D1GeometryRealization Realize(ID2D1DeviceContext1 context, float thickness, double scale, ID2D1StrokeStyle style)
        {
            if (Realization is null || _realizedThickness != thickness || _realizedScale != scale)
            {
                Realization?.Dispose();
                Realization = context.CreateStrokedGeometryRealization(Geometry, 0.25f / (float)scale, thickness, style);
                _realizedThickness = thickness;
                _realizedScale = scale;
            }
            return Realization;
        }

        public void ClearRealization()
        {
            Realization?.Dispose();
            Realization = null;
        }

        public void Dispose()
        {
            Realization?.Dispose();
            Geometry.Dispose();
        }
    }

    private sealed class CachedNote(Vortice.DirectWrite.IDWriteTextLayout layout, string text, double fontSize, double? width, bool bold, bool italic)
    {
        public Vortice.DirectWrite.IDWriteTextLayout Layout { get; } = layout;

        public Vortice.DirectWrite.TextMetrics Metrics { get; } = layout.Metrics;

        public int Generation { get; set; }

        public bool Matches(TextNote note) =>
            note.Text == text && note.FontSize == fontSize && note.Width == width && note.Bold == bold && note.Italic == italic;
    }
}
