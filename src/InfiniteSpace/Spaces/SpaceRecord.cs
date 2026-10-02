using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace InfiniteSpace.Spaces;

/// <summary>
/// A persisted space: an arbitrary outline, in screen DIPs. A space on an app window is in DIPs as
/// the screen was when that window had the rectangle in <see cref="Host"/>; it is mapped from there
/// onto wherever the window is now.
/// </summary>
internal sealed class SpaceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public FillRule FillRule { get; set; } = FillRule.Nonzero;

    /// <summary>Closed figures, each flattened to x0,y0,x1,y1,… (holes are figures too).</summary>
    public List<double[]> Figures { get; set; } = [];

    /// <summary>Paint strokes on the space, in screen DIPs.</summary>
    public List<InkStroke> Strokes { get; set; } = [];

    /// <summary>Text typed on the space.</summary>
    public List<TextNote> Notes { get; set; } = [];

    /// <summary>A text card: one note, which the space hugs (see SpaceManager.Cards.cs).</summary>
    public bool Card { get; set; }

    /// <summary>Whether this is a space or screen notes; spaces from before screen notes existed are spaces.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SpaceKind Kind { get; set; }

    /// <summary>The app window the space sits on; null for a space at desktop level.</summary>
    public HostInfo? Host { get; set; }

    /// <summary>What paint keeps its place relative to when the space is resized: its app window's top-left (null on the desktop).</summary>
    [JsonIgnore]
    public Point? Anchor => Host?.Frame.TopLeft;

    /// <summary>Maps everything on the space (outline, paint, text) through a transform, in place.</summary>
    /// <param name="placeNote">Where notes go instead, if not through the transform (from their current place).</param>
    /// <param name="placeStroke">How strokes are mapped instead, if not through the transform (from their current points).</param>
    public void Transform(Matrix m, Func<TextNote, Point>? placeNote = null, Func<InkStroke, Matrix>? placeStroke = null)
    {
        if (m.IsIdentity && placeNote is null && placeStroke is null)
            return;
        foreach (var figure in Figures)
            TransformPoints(figure, m);
        foreach (var stroke in Strokes)
            TransformInk(stroke, m, placeStroke: placeStroke, anchor: Anchor);
        foreach (var note in Notes)
            TransformInk(note, m, placeNote);
    }

    /// <summary>
    /// Maps one stroke's place, or one note's position (and its earlier versions'), through a transform.
    /// Strokes, like notes, keep their size (see <see cref="InkStroke.Placed"/>).
    /// </summary>
    /// <param name="placeNote">Where a note goes instead, if not through the transform (from its current place).</param>
    /// <param name="placeStroke">How a stroke is mapped instead, if not through the transform (from its current points).</param>
    /// <param name="anchor">What strokes keep their place relative to (see <see cref="Anchor"/>).</param>
    public static void TransformInk(IInk ink, Matrix m, Func<TextNote, Point>? placeNote = null, Func<InkStroke, Matrix>? placeStroke = null, Point? anchor = null)
    {
        switch (ink)
        {
            case InkStroke stroke:
                // Every version: undoing a move after the app was resized puts the stroke back in the right place.
                for (var version = stroke; version is not null; version = version.Previous)
                {
                    TransformPoints(version.Points, placeStroke?.Invoke(version) ?? version.Placed(m, anchor)); // moved, never stretched
                    version.Revision++;
                }
                break;
            case TextNote note:
                for (var version = note; version is not null; version = version.Previous)
                {
                    var p = placeNote?.Invoke(version) ?? m.Transform(new Point(version.X, version.Y));
                    version.X = Math.Round(p.X, 2);
                    version.Y = Math.Round(p.Y, 2);
                }
                break;
        }
    }

    private static void TransformPoints(double[] xy, Matrix m)
    {
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            var p = m.Transform(new Point(xy[i], xy[i + 1]));
            xy[i] = Math.Round(p.X, 2);
            xy[i + 1] = Math.Round(p.Y, 2);
        }
    }

    /// <summary>
    /// Removes the most recently added stroke or note (for undo). A note that replaced an earlier
    /// version of itself (it was edited) gives that version back.
    /// </summary>
    /// <returns>null when the space has no ink.</returns>
    public IInk? TakeNewestInk()
    {
        int stroke = IndexOfNewest(Strokes), note = IndexOfNewest(Notes);
        if (stroke < 0 && note < 0)
            return null;

        if (note >= 0 && (stroke < 0 || Notes[note].Order > Strokes[stroke].Order))
        {
            var taken = Notes[note];
            Notes.RemoveAt(note);
            if (taken.Previous is { } previous)
                Notes.Add(previous);
            return taken;
        }
        var takenStroke = Strokes[stroke];
        Strokes.RemoveAt(stroke);
        if (takenStroke.Previous is { } before)
            Strokes.Add(before);
        return takenStroke;
    }

    /// <summary>Puts ink taken by <see cref="TakeNewestInk"/> back (for redo); an edit replaces its earlier version again.</summary>
    public void PutBack(IInk ink)
    {
        switch (ink)
        {
            case InkStroke stroke:
                if (stroke.Previous is { } before)
                    Strokes.Remove(before);
                Strokes.Add(stroke);
                break;
            case TextNote note:
                if (note.Previous is { } previous)
                    Notes.Remove(previous);
                Notes.Add(note);
                break;
        }
    }

    /// <summary>
    /// Replaces a note with its edited version, which remembers the note so that undo can bring it
    /// back. An edited version with no text removes the note (and undo still brings it back).
    /// </summary>
    public void Replace(TextNote note, TextNote edited)
    {
        Notes.Remove(note);
        edited.Previous = note;
        Notes.Add(edited);
    }

    /// <summary>Replaces a stroke with its moved version, which remembers the stroke so that undo can bring it back.</summary>
    public void Replace(InkStroke stroke, InkStroke moved)
    {
        Strokes.Remove(stroke);
        moved.Previous = stroke;
        Strokes.Add(moved);
    }

    /// <summary>
    /// Takes back the newest change: every stroke and note that shares the newest order (a move of
    /// several things is one change). Ink from before ordering existed (order 0) goes one at a time.
    /// </summary>
    /// <returns>Empty when the space has no ink.</returns>
    public IInk[] TakeNewestBatch()
    {
        if (TakeNewestInk() is not { } first)
            return [];
        var batch = new List<IInk> { first };
        while (first.Order != 0 && NewestOrder() == first.Order)
            batch.Add(TakeNewestInk()!);
        return [.. batch];
    }

    private long NewestOrder()
    {
        long newest = -1;
        foreach (var stroke in Strokes)
            newest = Math.Max(newest, stroke.Order);
        foreach (var note in Notes)
            newest = Math.Max(newest, note.Order);
        return newest;
    }

    /// <summary>Newest by <see cref="IInk.Order"/>; on a tie (ink saved before ordering) the later one.</summary>
    private static int IndexOfNewest<T>(List<T> items) where T : IInk
    {
        int newest = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (newest < 0 || items[i].Order >= items[newest].Order)
                newest = i;
        }
        return newest;
    }
}

/// <summary>Something painted or typed on a space.</summary>
internal interface IInk
{
    /// <summary>When it was added (see <see cref="InkOrder"/>); 0 for ink saved before ordering existed.</summary>
    long Order { get; set; }

    /// <summary>Items with the same group are selected and moved together (Ctrl+G); null for none.</summary>
    Guid? Group { get; set; }
}

internal static class InkOrder
{
    private static long _last;

    /// <summary>Strictly increasing, and wall-clock based so it also orders against ink from earlier runs.</summary>
    public static long Next() => _last = Math.Max(_last + 1, DateTime.UtcNow.Ticks);
}

internal sealed class InkStroke : IInk
{
    public long Order { get; set; }

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Group { get; set; }

    /// <summary>The stroke as it was before it was moved into this one; undo brings it back.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InkStroke? Previous { get; set; }

    public const double DefaultThickness = 3;

    public double Thickness { get; set; } = DefaultThickness;

    /// <summary>"#RRGGBB"; null (and ink saved before colours existed) follows the theme's ink.</summary>
    public string? Color { get; set; }

    /// <summary>x0,y0,x1,y1,… in screen DIPs.</summary>
    public double[] Points { get; set; } = [];

    /// <summary>Goes up each time <see cref="Points"/> are changed in place, so drawings made from them know to redo it.</summary>
    [JsonIgnore]
    public int Revision { get; set; }

    private (double[] Points, int Revision, Point Corner)? _corner;
    private (double[] Points, int Revision, Rect Box)? _box;

    /// <summary>The top-left of the box its points cover (DIPs, in the record's coordinates).</summary>
    [JsonIgnore]
    public Point Corner
    {
        get
        {
            if (_corner is { } c && c.Points == Points && c.Revision == Revision)
                return c.Corner;
            double x = double.MaxValue, y = double.MaxValue;
            for (int i = 0; i + 1 < Points.Length; i += 2)
            {
                x = Math.Min(x, Points[i]);
                y = Math.Min(y, Points[i + 1]);
            }
            var corner = Points.Length < 2 ? new Point() : new Point(x, y);
            _corner = (Points, Revision, corner);
            return corner;
        }
    }

    /// <summary>The box its points cover (DIPs, in the record's coordinates, without its thickness); empty with no points.</summary>
    [JsonIgnore]
    public Rect Box
    {
        get
        {
            if (_box is { } b && b.Points == Points && b.Revision == Revision)
                return b.Box;
            var box = Rect.Empty;
            for (int i = 0; i + 1 < Points.Length; i += 2)
                box.Union(new Point(Points[i], Points[i + 1]));
            _box = (Points, Revision, box);
            return box;
        }
    }

    /// <summary>
    /// How the stroke is mapped onto the screen through <paramref name="toScreen"/>: it keeps its size
    /// and shape, and only moves the way <paramref name="anchor"/> does. On an app, the anchor is the
    /// app window's top-left, so paint keeps its place on the app as the window is resized, as the
    /// app's own content does; without one, the stroke's own top-left.
    /// </summary>
    public Matrix Placed(Matrix toScreen, Point? anchor = null)
    {
        var from = anchor ?? Corner;
        var at = toScreen.Transform(from);
        return new Matrix(1, 0, 0, 1, at.X - from.X, at.Y - from.Y);
    }

    /// <summary>
    /// A stroke painted on screen (DIPs), kept in the record's coordinates: only its place is mapped back
    /// through <paramref name="toScreen"/>, its shape is kept as painted (see <see cref="Placed"/>).
    /// </summary>
    public static InkStroke FromScreen(IReadOnlyList<Point> points, Matrix toScreen, InkPen pen)
    {
        var toRecord = toScreen;
        if (toRecord.HasInverse)
            toRecord.Invert();
        var corner = new Point(points.Min(p => p.X), points.Min(p => p.Y));
        var shift = toRecord.Transform(corner) - corner;
        var xy = new double[points.Count * 2];
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i] + shift;
            xy[i * 2] = Math.Round(p.X, 2);
            xy[i * 2 + 1] = Math.Round(p.Y, 2);
        }
        return new InkStroke { Points = xy, Thickness = pen.Thickness, Color = pen.Color, Order = InkOrder.Next() };
    }

    /// <summary>A new version of the stroke, moved by <paramref name="by"/> (strokes are only ever moved on screen, never scaled, so a screen offset is the record's too).</summary>
    public InkStroke MovedBy(Vector by, long order)
    {
        var xy = new double[Points.Length];
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            xy[i] = Math.Round(Points[i] + by.X, 2);
            xy[i + 1] = Math.Round(Points[i + 1] + by.Y, 2);
        }
        return new InkStroke { Points = xy, Thickness = Thickness, Color = Color, Group = Group, Order = order };
    }

    /// <summary>A version of the stroke with no points: what the eraser leaves, so that undo brings the stroke back.</summary>
    public InkStroke Erased(long order) => new() { Thickness = Thickness, Color = Color, Group = Group, Order = order };
}

internal sealed class TextNote : IInk
{
    public long Order { get; set; }

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Group { get; set; }

    public const double DefaultFontSize = 16;

    /// <summary>Top-left of the first line, in screen DIPs.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Empty only for a note removed by editing its text away (kept so undo can bring the note back).</summary>
    public string Text { get; set; } = "";

    public double FontSize { get; set; } = DefaultFontSize;

    /// <summary>"#RRGGBB"; null (and notes saved before colours existed) follows the theme's ink.</summary>
    public string? Color { get; set; }

    /// <summary>Width (DIPs) the lines wrap at; null keeps each line as long as it was typed.</summary>
    public double? Width { get; set; }

    /// <summary>All of its text in bold (typed notes; an opened file is styled by its Markdown).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Bold { get; set; }

    /// <summary>All of its text in italics (typed notes).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Italic { get; set; }

    /// <summary>The note as it was before it was edited into this one; undo brings it back.</summary>
    public TextNote? Previous { get; set; }

    /// <summary>
    /// For a note opened from a file (a .txt or .md): which, and how it is shown. Its text is a snapshot
    /// of the file's, so it outlives the file. Null for typed notes.
    /// </summary>
    public NoteFile? File { get; set; }

    /// <summary>A new version of the note at another place (record coordinates), with the same text, look and group.</summary>
    public TextNote MovedTo(Point at, long order) => new()
    {
        X = Math.Round(at.X, 2),
        Y = Math.Round(at.Y, 2),
        Text = Text,
        FontSize = FontSize,
        Color = Color,
        Width = Width,
        Bold = Bold,
        Italic = Italic,
        File = File?.Copy(), // the same text: kept where it is
        Group = Group,
        Order = order,
    };

    /// <summary>A version of the note with no text: what the eraser leaves, so that undo brings the note back.</summary>
    public TextNote Erased(long order) => new()
    {
        X = X,
        Y = Y,
        FontSize = FontSize,
        Color = Color,
        Width = Width,
        Bold = Bold,
        Italic = Italic,
        Group = Group,
        Order = order,
    };
}

/// <summary>
/// A text or Markdown file opened on a space: shown <see cref="TextNote.Width"/> wide and
/// <see cref="Height"/> tall, scrolled to <see cref="Scroll"/>, with its name at the top.
/// </summary>
/// <remarks>
/// The text is saved in a file of its own next to spaces.json (<see cref="Stored"/>), written once:
/// spaces.json is written out whole after every change, a paint stroke included.
/// </remarks>
internal sealed class NoteFile
{
    /// <summary>The file it was opened from (shown by name; double-clicking the name opens it).</summary>
    public string Path { get; set; } = "";

    /// <summary>Styled as Markdown (headings, lists, emphasis…) rather than shown as typed.</summary>
    public bool Markdown { get; set; }

    /// <summary>How much of it shows, in DIPs, its name included; the rest is scrolled to.</summary>
    public double Height { get; set; }

    /// <summary>How far down it is scrolled, in DIPs.</summary>
    public double Scroll { get; set; }

    /// <summary>Where its text is kept, relative to the data folder; null until it is first saved.</summary>
    public string? Stored { get; set; }

    /// <summary>The same file for an edited version of the note: its text is new, so it is stored anew.</summary>
    public NoteFile ForNewText() => new() { Path = Path, Markdown = Markdown, Height = Height, Scroll = Scroll };

    /// <summary>The same file for a version of the note with the same text (moved, resized): it shares where the text is kept.</summary>
    public NoteFile Copy() => new() { Path = Path, Markdown = Markdown, Height = Height, Scroll = Scroll, Stored = Stored };
}

/// <summary>
/// The app window a space sits on, remembered so that the space can follow the window's moves and
/// resizes, and come back when the app opens the window again after it was closed.
/// </summary>
internal sealed class HostInfo
{
    /// <summary>Full path of the app's program.</summary>
    public string App { get; set; } = "";

    public string WindowClass { get; set; } = "";

    /// <summary>The window's title when last seen; a window with the same title is preferred on the way back.</summary>
    public string Title { get; set; } = "";

    /// <summary>Spaces that were on the same window share this, so they come back together on one window.</summary>
    public Guid Window { get; set; }

    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>The window's rectangle (screen DIPs) that the space's coordinates were taken against.</summary>
    [JsonIgnore]
    public Rect Frame
    {
        get => new(Left, Top, Math.Max(1, Width), Math.Max(1, Height));
        set => (Left, Top, Width, Height) = (value.Left, value.Top, value.Width, value.Height);
    }
}

/// <summary>How a space follows its window: stretched and moved with the window's rectangle.</summary>
internal static class HostFrames
{
    /// <summary>
    /// The transform taking positions against the <paramref name="from"/> rectangle to the same
    /// relative positions against <paramref name="to"/>.
    /// </summary>
    public static Matrix Map(Rect from, Rect to)
    {
        double sx = from.Width >= 1 ? to.Width / from.Width : 1;
        double sy = from.Height >= 1 ? to.Height / from.Height : 1;
        return new Matrix(sx, 0, 0, sy, to.X - from.X * sx, to.Y - from.Y * sy);
    }

    /// <summary>Whether two such transforms differ only by where they put things (the size is the same).</summary>
    public static bool SameScale(Matrix a, Matrix b) =>
        Math.Abs(a.M11 - b.M11) < 1e-9 && Math.Abs(a.M22 - b.M22) < 1e-9;
}

internal sealed class SpacesDocument
{
    public int Version { get; set; } = 1;

    public List<SpaceRecord> Spaces { get; set; } = [];
}
