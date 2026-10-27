using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using Microsoft.Extensions.Logging;

namespace InfiniteSpace.Spaces;

/// <summary>
/// Owns the spaces: their records, outlines and windows. A space on an app window moves and
/// stretches with that window; when the window closes the space is kept, hidden, and comes back when
/// the app opens a matching window again (also after a restart). Every change is saved immediately.
/// </summary>
internal sealed partial class SpaceManager(SpaceStore store, Universe universe, ILogger<SpaceManager> logger) : IDisposable
{
    /// <summary>A live resize is over once the app's window keeps its size this long; the space is then drawn properly.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Changes of size closer together than this are one live resize (dragging an edge), not a one-off jump.</summary>
    private static readonly TimeSpan LiveResizeGap = TimeSpan.FromMilliseconds(300);

    /// <summary>How often kept spaces look for their windows even without a hint that one opened.</summary>
    private static readonly TimeSpan LookoutInterval = TimeSpan.FromSeconds(2);

    /// <summary>How far around a note's text (DIPs) the mouse still counts as on it, for E.</summary>
    private const double NoteReach = 4;

    private readonly List<Space> _spaces = [];
    private readonly List<SpaceRecord> _kept = [];
    private readonly Dictionary<IntPtr, Guid> _windowIds = [];
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private SaveQueue? _saves;
    private DispatcherTimer? _settle;
    private DispatcherTimer? _lookout;
    private DispatcherTimer? _lookSoon;
    private TextEntry? _textEntry;

    /// <summary>All spaces, including those kept for apps whose windows are closed.</summary>
    public int Count => _spaces.Count + _kept.Count;

    /// <summary>Spaces waiting for their app's window to open again.</summary>
    public int KeptCount => _kept.Count;

    /// <summary>How spaces look (see <see cref="Themes"/>); new spaces and previews use it too.</summary>
    public string Theme { get; private set; } = Themes.Dreamy;

    /// <summary>Re-dresses every space in a theme, straight away.</summary>
    public void ApplyTheme(string theme)
    {
        Theme = Themes.Resolve(theme);
        foreach (var space in _spaces)
            space.Window.SetTheme(Theme);
        universe.Gpu.Commit();
    }

    /// <summary>Colour and size of what is painted and typed next.</summary>
    public InkPen Pen { get; private set; } = InkPen.Default;

    /// <summary>The pen changed (a pick on a palette or a text box's toolbar).</summary>
    public event Action<InkPen>? PenChanged;

    /// <summary>A space was right-clicked at this screen DIP position: the ink palette should open there.</summary>
    public event Action<Point>? PaletteRequested;

    /// <summary>The user started painting on a space: the ink palette should get out of the way (a text box already does).</summary>
    public event Action? PaintingStarted;

    /// <summary>Makes <paramref name="pen"/> what every space paints and types with from now on.</summary>
    public void SetPen(InkPen pen)
    {
        if (pen == Pen)
            return;
        Pen = pen;
        foreach (var space in _spaces)
            space.Window.Pen = pen;
        PenChanged?.Invoke(pen);
    }

    /// <summary>What the mouse does on spaces now (the hand to start with).</summary>
    public Tool Tool { get; private set; } = Tool.Hand;

    /// <summary>The tool changed (a key over a space, the wheel, the toolbar, or a screen-wide tool ending).</summary>
    public event Action<Tool>? ToolChanged;

    /// <summary>Makes <paramref name="tool"/> what the mouse does on every space from now on.</summary>
    public void SetTool(Tool tool)
    {
        if (tool == Tool)
            return;
        Tool = tool;
        foreach (var space in _spaces)
            space.Window.Tool = tool;
        ToolCursors.RefreshUnderMouse(); // over a space, a screen note or any window of ours alike
        ToolChanged?.Invoke(tool);
    }

    /// <summary>The windows of the spaces that are open (diagnostics and tests).</summary>
    internal IEnumerable<SpaceWindow> Windows => _spaces.Select(s => s.Window);

    /// <summary>The open spaces' records, as they are now (for tests).</summary>
    internal IEnumerable<SpaceRecord> Records => _spaces.Select(s => s.Record);

    /// <summary>Current outlines on screen, in screen DIPs.</summary>
    public IEnumerable<Geometry> Outlines => _spaces.Where(s => !s.IsSheet).Select(s => (Geometry)s.Outline);

    /// <summary>
    /// Opens the saved spaces: desktop-level ones where they were, app ones on their app's window if
    /// it is open (else they wait for it). Spaces from before app windows were remembered go to the desktop.
    /// </summary>
    public void RestoreAll()
    {
        _saves ??= new SaveQueue(store, logger, _dispatcher, SavedRecords);
        foreach (var record in store.Load())
        {
            if (SpaceGeometry.Decode(record).Figures.Count == 0)
                continue;
            if (record.Host is { App.Length: > 0 })
                _kept.Add(record);
            else
                Open(record, IntPtr.Zero, Matrix.Identity);
        }
        ReturnKept();
        logger.LogInformation("Restored {Count} space(s) from {File}; {Kept} wait for their app's window", Count, store.FilePath, _kept.Count);

        _lookout = new DispatcherTimer(LookoutInterval, DispatcherPriority.Background, (_, _) => Lookout(), _dispatcher);
        _lookout.Start();
    }

    /// <summary>
    /// Adds a space. If it crosses spaces on the same layer (the same app, or the desktop), they all
    /// become one space: the union of the outlines, carrying everyone's paint and text. Spaces on
    /// other apps are other layers and are left alone.
    /// </summary>
    /// <param name="outline">Outline in screen DIPs.</param>
    /// <param name="host">The app window to sit on, or zero for desktop level.</param>
    /// <returns>false when the outline is too small to become a space.</returns>
    public bool Create(Geometry outline, IntPtr host)
    {
        if (SpaceGeometry.Area(outline) < SpaceGeometry.MinArea)
            return false;

        var drawn = new SpaceRecord { Figures = SpaceGeometry.Encode(outline, out var fillRule), Host = Describe(host) };
        drawn.FillRule = fillRule;
        Place(drawn, host);
        Persist();
        return true;
    }

    /// <summary>Spaces on the same app window, or both at desktop level (the only cases that merge).</summary>
    internal static bool SameLayer(IntPtr hostA, IntPtr hostB) => hostA == hostB;

    /// <summary>
    /// One space from several crossing ones, oldest first: the union of their outlines and all their
    /// ink, under the oldest one's identity.
    /// </summary>
    internal static SpaceRecord Merge(IReadOnlyList<SpacePart> parts)
    {
        if (parts.Count == 1)
            return parts[0].Record;

        var oldest = parts[0].Record;
        var union = SpaceGeometry.Union([.. parts.Select(p => (Geometry)p.Outline)]);
        var merged = new SpaceRecord
        {
            Id = oldest.Id,
            CreatedAt = oldest.CreatedAt,
            Figures = SpaceGeometry.Encode(union, out var fillRule),
            Strokes = [.. parts.SelectMany(p => p.Record.Strokes)],
            Notes = [.. parts.SelectMany(p => p.Record.Notes)],
        };
        merged.FillRule = fillRule;
        return merged;
    }

    /// <summary>Removes a whole space: the one under <paramref name="point"/> (screen DIPs), else the newest.</summary>
    /// <returns>false when there was no space to remove.</returns>
    public bool RemoveAt(Point point)
    {
        var showing = _spaces.Where(s => s.Window.IsShowing && !s.IsSheet).ToList();
        int index = IndexToRemove([.. showing.Select(s => (Geometry)s.Outline)], point);
        if (index < 0)
            return false;

        var space = showing[index];
        CloseWindow(space);
        _spaces.Remove(space);
        Persist();
        return true;
    }

    /// <summary>
    /// Which space a one-press removal targets, given outlines oldest first: the newest one containing
    /// <paramref name="point"/>, else the newest overall, so repeated presses clear newest to oldest.
    /// </summary>
    /// <returns>-1 when there are no spaces.</returns>
    internal static int IndexToRemove(IReadOnlyList<Geometry> outlines, Point point)
    {
        for (int i = outlines.Count - 1; i >= 0; i--)
        {
            if (outlines[i].FillContains(point))
                return i;
        }
        return outlines.Count - 1;
    }

    /// <summary>Cuts the eraser area out of every space it touches; spaces left with nothing are removed.</summary>
    /// <returns>How many spaces changed.</returns>
    public int Erase(Geometry eraser)
    {
        var reach = eraser.Bounds;
        int changed = 0;
        foreach (var space in _spaces.ToList())
        {
            if (space.IsSheet)
                continue;
            if (!space.Window.IsShowing || !space.Outline.Bounds.IntersectsWith(reach))
                continue;

            double before = SpaceGeometry.Area(space.Outline);
            var remaining = SpaceGeometry.Subtract(space.Outline, eraser);
            if (remaining is null)
            {
                CloseWindow(space);
                _spaces.Remove(space);
                changed++;
                continue;
            }
            if (before - SpaceGeometry.Area(remaining) < 1)
                continue;

            Bake(space);
            space.Record.Figures = SpaceGeometry.Encode(remaining, out var fillRule);
            space.Record.FillRule = fillRule;
            space.Shape = SpaceGeometry.Decode(space.Record);
            space.Window.SetShape(space.Outline);
            changed++;
        }

        if (changed > 0)
            Persist();
        return changed;
    }

    /// <summary>Removes every space, including those waiting for their app.</summary>
    public void EraseAll()
    {
        foreach (var space in _spaces)
            CloseWindow(space);
        _spaces.Clear();
        _kept.Clear();
        Persist();
    }

    /// <summary>Saves, then takes the windows off screen (on exit); the saved spaces are untouched.</summary>
    public void Dispose()
    {
        _textEntry?.Commit();
        _settle?.Stop();
        _lookout?.Stop();
        _lookSoon?.Stop();
        Persist(); // titles and the like picked up since the last change
        FlushSaves(); // on the disk before the app goes
        foreach (var space in _spaces)
            CloseWindow(space);
        _spaces.Clear();
    }

    /// <summary>Whether a text box is open (the keyboard, and the keys that belong to spaces, then belong to it).</summary>
    public bool IsTyping => _textEntry is not null;

    /// <summary>
    /// Keeps what is being typed and closes the text box, before another of this app's windows takes
    /// focus (the closing box hands focus back to the app it came from, which would close that one).
    /// </summary>
    /// <param name="fade">Fade the box away (the user got on with painting or erasing) rather than close it at once.</param>
    public void CommitTyping(bool fade = false)
    {
        if (fade)
            _textEntry?.CommitAndFade();
        else
            _textEntry?.Commit();
    }

    /// <summary>The space window the mouse is on (not just near its rim), or zero.</summary>
    public IntPtr SpaceUnderCursor() => HoveredSpace(out _)?.Window.Handle ?? IntPtr.Zero;

    /// <summary>What the mouse is on: nothing, a space, text on a space, or screen notes.</summary>
    public Hover HoverAtCursor()
    {
        var space = HoveredSpace(out var at);
        return space is null ? Hover.None : space.IsSheet ? Hover.Sheet : NoteAt(space, at) is null ? Hover.Space : Hover.Note;
    }

    /// <summary>The app window the space under the mouse sits on (zero: desktop level), else the app in front: where C draws.</summary>
    public IntPtr HostAtCursor() => HoveredSpace(out _) is { } space ? space.Window.Host : Foreground.CaptureHost();

    /// <summary>The space the mouse is on (not just near its rim, nor covered by another window), and where.</summary>
    private Space? HoveredSpace(out Point at)
    {
        at = default;
        if (!Win32.GetCursorPos(out var cursor))
            return null;
        at = new Point(cursor.X / Dpi.Scale, cursor.Y / Dpi.Scale);
        var space = Find(Win32.WindowFromPoint(cursor));
        return space is not null && space.Window.Contains(at) ? space : null;
    }

    /// <summary>The newest note on the space whose text is at a screen DIP point.</summary>
    private static TextNote? NoteAt(Space space, Point at)
    {
        TextNote? found = null;
        foreach (var note in space.Record.Notes)
        {
            if (note.Text.Length == 0 || (found is not null && note.Order < found.Order))
                continue;
            var bounds = NoteText.Bounds(note, space.ToScreen);
            bounds.Inflate(NoteReach, NoteReach);
            if (bounds.Contains(at))
                found = note;
        }
        return found;
    }

    /// <summary>
    /// Another app's window came to the front, was minimised or restored, or was shown: spaces on it
    /// follow now, and kept spaces check whether it is theirs.
    /// </summary>
    public void OnWindowEvent(uint eventType, IntPtr window)
    {
        if (eventType == Win32.EVENT_OBJECT_SHOW)
        {
            if (_kept.Count > 0 && Win32.GetAncestor(window, Win32.GA_ROOT) == window)
                LookSoon();
            return;
        }

        foreach (var space in _spaces)
        {
            if (eventType == Win32.EVENT_SYSTEM_MINIMIZESTART && space.Window.Host == window)
                space.Window.HideWithHost();
            else
                space.Window.Restack();
        }
        if (_kept.Count > 0 && eventType != Win32.EVENT_SYSTEM_MINIMIZESTART)
            LookSoon();
    }

    /// <summary>Puts every space back on its app right away (after this app handed focus back to one).</summary>
    public void Restack()
    {
        foreach (var space in _spaces)
            space.Window.Restack();
    }

    /// <summary>Ctrl+Z over a space: takes back its newest change (a stroke, a note, or one move of several).</summary>
    /// <returns>false when the window is not a space or its canvas is empty.</returns>
    public bool Undo(IntPtr window)
    {
        if (Find(window) is not { } space)
            return false;
        var batch = space.Record.TakeNewestBatch();
        if (batch.Length == 0)
            return false;

        space.Redo.Push(batch);
        ShowInk(space);
        Persist();
        return true;
    }

    /// <summary>Ctrl+Y over a space: brings back what Ctrl+Z took, until new ink is added.</summary>
    public bool Redo(IntPtr window)
    {
        var space = Find(window);
        if (space is null || !space.Redo.TryPop(out var batch))
            return false;

        foreach (var ink in batch.Reverse())
            space.Record.PutBack(ink);
        ShowInk(space);
        Persist();
        return true;
    }

    /// <summary>
    /// Strokes and notes dragged across their space by <paramref name="by"/> (screen DIPs): each gets a new
    /// version in its new place, all with one order, so one undo takes the whole move back.
    /// </summary>
    private void MoveInk(Space space, IReadOnlyList<IInk> items, Vector by)
    {
        var live = items.Where(i => i is InkStroke s ? space.Record.Strokes.Contains(s) : space.Record.Notes.Contains((TextNote)i)).ToList();
        if (!_spaces.Contains(space) || live.Count == 0)
        {
            space.Window.RedrawInk();
            return;
        }
        long order = InkOrder.Next();
        var toRecord = space.ToScreen;
        if (toRecord.HasInverse)
            toRecord.Invert();
        var moved = new List<IInk>();
        foreach (var item in live)
        {
            if (item is InkStroke stroke)
            {
                var copy = stroke.MovedBy(by, order);
                space.Record.Replace(stroke, copy);
                moved.Add(copy);
            }
            else if (item is TextNote note)
            {
                // Notes are placed through the whole mapping (strokes only through its shift).
                var at = toRecord.Transform(space.ToScreen.Transform(new Point(note.X, note.Y)) + by);
                var copy = note.MovedTo(at, order);
                space.Window.Adopt(note, copy); // a long file isn't laid out again
                space.Record.Replace(note, copy);
                moved.Add(copy);
            }
        }
        space.Redo.Clear();
        space.Window.Select(moved); // the selection follows the new versions (this redraws)
        if (space.IsSheet)
            FitSheet(space);
        Persist();
    }

    /// <summary>The version a stroke or note replaced (moved, edited or erased), if any.</summary>
    private static IInk? PreviousOf(IInk ink) => ink switch
    {
        InkStroke stroke => stroke.Previous,
        TextNote note => note.Previous,
        _ => null,
    };

    /// <summary>
    /// Strokes and notes the eraser passed over: each is replaced by an empty version of itself, all of a
    /// sweep with one order, so one undo brings the whole sweep back.
    /// </summary>
    private void EraseInk(Space space, IReadOnlyList<IInk> items, long order)
    {
        if (!_spaces.Contains(space))
            return;
        bool erased = false;
        foreach (var item in items)
        {
            if (item is InkStroke stroke && space.Record.Strokes.Contains(stroke))
                space.Record.Replace(stroke, stroke.Erased(order));
            else if (item is TextNote note && space.Record.Notes.Contains(note))
                space.Record.Replace(note, note.Erased(order));
            else
                continue;
            erased = true;
        }
        if (!erased)
            return;
        space.Redo.Clear();
        ShowInk(space);
        Persist();
    }

    /// <summary>
    /// The eraser on the screen at a point (screen DIPs): the topmost ink there, on a space or on screen
    /// notes, is erased with <paramref name="order"/> (one per sweep).
    /// </summary>
    /// <returns>The window it was erased from (for undo), or zero when there was no ink there.</returns>
    public IntPtr EraseAt(Point at, long order)
    {
        for (int i = _spaces.Count - 1; i >= 0; i--)
        {
            var space = _spaces[i];
            if (!space.Window.IsShowing || (!space.IsSheet && !space.Window.Contains(at)))
                continue;
            if (InkHit.At(space.Record, space.ToScreen, at, space.Window.Editing) is not { } ink)
                continue;
            EraseInk(space, [ink], order);
            return space.Window.Handle;
        }
        return IntPtr.Zero;
    }

    /// <summary>Whether the space under the mouse has a selection (Ctrl+G and Ctrl+Shift+G belong to it then).</summary>
    public bool HasSelectionUnderCursor() => HoveredSpace(out _)?.Window.HasSelection == true;

    /// <summary>Ctrl+G (or Ctrl+Shift+G with <paramref name="group"/> false) over a space: its selection becomes one group, or none.</summary>
    public bool GroupUnderCursor(bool group)
    {
        if (HoveredSpace(out _) is not { } space || space.Window.Selected.Count < (group ? 2 : 1))
            return false;
        Guid? id = group ? Guid.NewGuid() : null;
        var chosen = space.Window.Selected.ToList();
        // Their earlier versions too, and newer ones waiting for redo: undoing or redoing a move keeps the group.
        foreach (var item in chosen)
        {
            for (var version = item; version is not null; version = PreviousOf(version))
                version.Group = id;
        }
        foreach (var batch in space.Redo)
        {
            foreach (var ink in batch)
            {
                for (var version = PreviousOf(ink); version is not null; version = PreviousOf(version))
                {
                    if (chosen.Contains(version))
                    {
                        ink.Group = id;
                        break;
                    }
                }
            }
        }
        space.Window.Select(chosen); // one frame round the group now
        Persist();
        return true;
    }

    /// <summary>
    /// Picks up the space under a screen point to move it (Ctrl+Alt+M): it floats just under
    /// <paramref name="overlay"/> until it is dropped or put back.
    /// </summary>
    /// <returns>null when no space is showing there.</returns>
    public MovingSpace? PickUp(Point at, IntPtr overlay)
    {
        var space = _spaces.LastOrDefault(s => !s.IsSheet && s.Window.IsShowing && s.Window.Contains(at));
        return space is null ? null : PickUp(space, overlay);
    }

    /// <param name="overlay">The window it floats just under; zero leaves it in its place among the windows (a card dragged by hand).</param>
    private MovingSpace PickUp(Space space, IntPtr overlay)
    {
        if (space.Stretched)
            Reshape(space); // drawn properly where it is before it goes anywhere
        space.Following = false; // it lands when it is dropped or put back, not when it keeps still
        space.Moving = true;
        var moving = new MovingSpace(space, space.ToScreen, space.Outline.Bounds);
        space.NotePlacement = space.Window.NotePlacement = moving.PlaceNote;
        space.StrokePlacement = space.Window.StrokePlacement = moving.PlaceStroke;
        if (overlay != IntPtr.Zero)
            space.Window.Lift(overlay);
        return moving;
    }

    /// <summary>
    /// Shows a picked-up space moved and reshaped so that its outline's bounds are
    /// <paramref name="bounds"/> (screen DIPs). Paint and text are carried along by moves only: reshaping
    /// leaves them where they are, unstretched, unless an edge coming in pushes them (see <see cref="PlaceNote"/>).
    /// </summary>
    /// <remarks>
    /// Moves just shift what is drawn. A new shape is drawn properly on the next frame, never stretched,
    /// so the rim, the scenery and the text stay crisp and in place while it changes.
    /// </remarks>
    /// <param name="carried">How far moves (not reshapes) have carried it since it was picked up.</param>
    public void Fit(MovingSpace moving, Rect bounds, Vector carried)
    {
        var space = moving.Space;
        if (!_spaces.Contains(space) || bounds.Width < 1 || bounds.Height < 1)
            return;
        if (space.Record.Card)
            bounds = FitCardLive(moving, bounds);
        moving.Now = bounds;
        moving.Carried = carried;
        var toScreen = Reshaped(moving.Before, moving.Bounds, bounds);
        if (HostFrames.SameScale(space.Shown, toScreen) && !space.Window.IsReshaping)
        {
            if (toScreen == space.Shown)
                return;
            var offset = new Vector(toScreen.OffsetX - space.Shown.OffsetX, toScreen.OffsetY - space.Shown.OffsetY);
            space.ToScreen = space.Shown = toScreen;
            space.Window.Move(moving.LiveOutline(toScreen), offset, toScreen);
            return;
        }
        space.ToScreen = space.Shown = toScreen;
        space.Window.ReshapeLive(moving.LiveOutline(toScreen), toScreen);
    }

    /// <summary>
    /// Where a note is shown while its space is moved and reshaped: carried along by the moves, and
    /// otherwise left where it was, unless an edge coming in pushes it. A note that already stuck out
    /// of a side keeps sticking out as far, but no further.
    /// </summary>
    /// <param name="note">The note's box on screen (DIPs) when the space was picked up.</param>
    /// <param name="from">The space's bounds then.</param>
    /// <param name="to">Its bounds now.</param>
    /// <param name="carried">How far moves (not reshapes) have carried it.</param>
    /// <returns>The note's top-left on screen.</returns>
    internal static Point PlaceNote(Rect note, Rect from, Rect to, Vector carried)
    {
        note.Offset(carried);
        from.Offset(carried);
        return new Point(
            Push(note.X, note.Width, from.Left, from.Right, to.Left, to.Right),
            Push(note.Y, note.Height, from.Top, from.Bottom, to.Top, to.Bottom));

        // Along one axis: the start of a note of that size, kept inside low..high plus what stuck out before.
        static double Push(double start, double size, double fromLow, double fromHigh, double low, double high)
        {
            double overLow = Math.Max(0, fromLow - start), overHigh = Math.Max(0, start + size - fromHigh);
            if (start + size > high + overHigh)
                start = high + overHigh - size;
            if (start < low - overLow)
                start = low - overLow; // the start wins when both edges press
            return start;
        }
    }

    /// <summary>
    /// The mapping of a record onto the screen that takes what <paramref name="before"/> showed within
    /// <paramref name="from"/> to <paramref name="to"/> instead. Always worked out from where the move
    /// began, so reshaping back and forth never wears the shape down.
    /// </summary>
    internal static Matrix Reshaped(Matrix before, Rect from, Rect to)
    {
        var toScreen = before;
        toScreen.Append(HostFrames.Map(from, to));
        return toScreen;
    }

    /// <summary>
    /// Drops a picked-up space where it is now. It goes onto the app window under
    /// <paramref name="at"/> (or the desktop), and merges with spaces it crosses there.
    /// </summary>
    public void Drop(MovingSpace moving, Point at)
    {
        var space = moving.Space;
        space.Moving = false;
        if (!_spaces.Contains(space))
            return;

        space.Window.FinishReshape(); // what is kept is where it comes to rest
        IntPtr host = AppWindows.WindowAt(at);
        Bake(space); // text goes where it is shown
        space.Record.Host = Describe(host);
        List<Space> crossed = space.Record.Card ? [] : _spaces.Where(s => s != space && !s.IsSheet && !s.Record.Card && SameLayer(s.Window.Host, host) && SpaceGeometry.Overlap(s.Outline, space.Outline)).ToList();
        if (crossed.Count == 0)
        {
            Rest(space);
            space.Window.SetHost(host);
            space.Window.Land();
        }
        else
        {
            // It becomes part of the spaces it was dropped on: one new window replaces them all.
            _spaces.Remove(space);
            Place(space.Record, host, crossed);
            universe.Retire(space.Window);
        }
        Persist();
    }

    /// <summary>Puts a picked-up space back where it was, as it was.</summary>
    public void PutBack(MovingSpace moving)
    {
        var space = moving.Space;
        space.Moving = false;
        if (moving.CardWidthBefore is { } width && space.Record.Notes is [var note])
            note.Width = width; // reflowed while reshaping: as it was
        space.NotePlacement = space.Window.NotePlacement = null; // text and paint back where the mapping puts them
        space.StrokePlacement = space.Window.StrokePlacement = null;
        if (!_spaces.Contains(space))
            return;
        space.ToScreen = moving.Before;
        // Its app may have moved meanwhile: back onto it where it is now.
        if (space.Record.Host is { } host && AppWindows.TryGetFrame(space.Window.Host, out var frame))
            space.ToScreen = HostFrames.Map(host.Frame, frame);
        Reshape(space); // drawn properly, in its own window again, in the same commit
        space.Window.Land();
    }

    private Space? Find(IntPtr window) =>
        window == IntPtr.Zero ? null : _spaces.FirstOrDefault(s => s.Window.Handle == window);

    /// <summary>
    /// Opens a space whose record is in screen coordinates on a layer, merged with the spaces it
    /// crosses there (or with <paramref name="crossing"/>, oldest first, when the caller already knows them).
    /// </summary>
    private void Place(SpaceRecord drawn, IntPtr host, List<Space>? crossing = null)
    {
        var outline = SpaceGeometry.Decode(drawn);
        var crossed = crossing
            ?? _spaces.Where(s => !s.IsSheet && !s.Record.Card && SameLayer(s.Window.Host, host) && SpaceGeometry.Overlap(s.Outline, outline)).ToList(); // cards never merge
        foreach (var space in crossed)
            Bake(space);

        var record = crossed.Count == 0
            ? drawn
            : Merge([.. crossed.Select(s => new SpacePart(s.Record, s.Outline)), new SpacePart(drawn, outline)]);
        record.Host = drawn.Host;

        // The new (or merged) space goes on screen first; the spaces it replaces leave a few frames
        // later, so there is never a moment with neither.
        Open(record, host, Matrix.Identity);
        foreach (var space in crossed)
        {
            _spaces.Remove(space);
            universe.Retire(space.Window);
        }
    }

    /// <param name="toScreen">Maps the record's coordinates onto the screen.</param>
    private Space Open(SpaceRecord record, IntPtr host, Matrix toScreen)
    {
        var shape = SpaceGeometry.Decode(record);
        var outline = SpaceGeometry.Transform(shape, toScreen);
        var window = new SpaceWindow(record, outline, host, universe.Gpu, Theme, toScreen: toScreen) { Pen = Pen, Tool = Tool };
        var space = new Space(record, shape, window, toScreen);
        window.InkChanged += _ =>
        {
            space.Redo.Clear(); // new ink starts a new history branch
            Persist();
        };
        window.PaletteRequested += (_, at) => PaletteRequested?.Invoke(at);
        window.TypeRequested += (_, at) => BeginText(space, at, editing: null);
        window.PaintingStarted += _ =>
        {
            CommitTyping(fade: true); // what was being typed stays; the box gets out of the way
            PaintingStarted?.Invoke();
        };
        window.HostMoved += (_, frame) => OnHostMoved(space, frame);
        window.FilesDropped += (_, paths, at) => OpenFiles(space, paths, at);
        window.Wheel += (_, at, delta) => OnWheel(space, at, delta);
        window.ScrollSettled += _ => Persist(); // where it was scrolled to comes back next time
        window.NoteClicked = (_, at, click) => OnNoteClicked(space, at, click);
        window.EditRequested += (_, note, at) => BeginText(space, at, note);
        window.SelectionMoved += (_, items, by) => MoveInk(space, items, by);
        WireCard(space);
        window.InkErased += (_, items, order) => EraseInk(space, items, order);
        window.HandPressed += pressed =>
        {
            foreach (var other in _spaces)
            {
                if (other.Window != pressed)
                    other.Window.ClearSelection(); // one selection at a time, on the space in hand
            }
        };
        window.TextDropped += (_, text, at) => DropText(space, text, at);
        window.HostClosed += _ => _dispatcher.BeginInvoke(DispatcherPriority.Normal, () => OnHostClosed(space)); // not mid-frame
        _spaces.Add(space);
        universe.Attach(window);
        return space;
    }

    /// <summary>What to remember about a space's app window; null at desktop level.</summary>
    private HostInfo? Describe(IntPtr host)
    {
        if (host == IntPtr.Zero || !AppWindows.TryGetFrame(host, out var frame))
            return null;
        var identity = AppWindows.Describe(host);
        if (!_windowIds.TryGetValue(host, out var id))
            _windowIds[host] = id = Guid.NewGuid();
        return new HostInfo
        {
            App = identity?.App ?? "",
            WindowClass = identity?.WindowClass ?? "",
            Title = identity?.Title ?? "",
            Window = id,
            Frame = frame,
        };
    }

    /// <summary>The app window moved or changed size: the space goes along, stretched to match.</summary>
    private void OnHostMoved(Space space, Rect frame)
    {
        if (space.Record.Host is not { } host || space.Moving)
            return;
        ShowAt(space, HostFrames.Map(host.Frame, frame));
    }

    /// <summary>
    /// Shows the space through a new mapping of its record onto the screen, as cheaply as it can be:
    /// a move only moves its window; a one-off change of size (maximise, restore, snap) redraws it at
    /// once; a live resize stretches what is drawn on the GPU and redraws it properly once the resize
    /// has settled for <see cref="SettleDelay"/>.
    /// </summary>
    private void ShowAt(Space space, Matrix toScreen)
    {
        if (toScreen == space.ToScreen)
            return;
        space.ToScreen = toScreen;

        var now = DateTime.UtcNow;
        if (!space.Stretched && HostFrames.SameScale(space.Shown, toScreen))
        {
            var offset = new Vector(toScreen.OffsetX - space.Shown.OffsetX, toScreen.OffsetY - space.Shown.OffsetY);
            space.Shown = toScreen;
            space.Window.Move(SpaceGeometry.Mapped(space.Shape, toScreen), offset, toScreen);
            SettleLater(space, now);
            return;
        }

        bool live = space.Stretched || now - space.LastResize < LiveResizeGap;
        space.LastResize = now;
        if (!live)
        {
            Reshape(space);
            return;
        }

        var relative = space.Shown;
        relative.Invert();
        relative.Append(toScreen); // from the screen as drawn to the screen as it should be
        space.Window.Stretch(relative, toScreen);
        space.Stretched = true;
        SettleLater(space, now);
    }

    /// <summary>The space is following its app through the compositor; once the app keeps still, it settles.</summary>
    private void SettleLater(Space space, DateTime now)
    {
        space.Following = true;
        space.LastChange = now;
        _settle ??= new DispatcherTimer(SettleDelay, DispatcherPriority.Background, (_, _) => Settle(), _dispatcher);
        if (!_settle.IsEnabled)
            _settle.Start();
    }

    /// <summary>
    /// Spaces whose app has kept still for <see cref="SettleDelay"/> go back into their own windows,
    /// drawn properly at their new size if it changed.
    /// </summary>
    private void Settle()
    {
        var now = DateTime.UtcNow;
        bool waiting = false;
        foreach (var space in _spaces)
        {
            if (!space.Following || space.Moving)
                continue;
            if (now - space.LastChange < SettleDelay)
            {
                waiting = true;
                continue;
            }
            Rest(space);
        }
        if (!waiting)
            _settle?.Stop();
    }

    /// <summary>Back into its own window now, drawn at its current size.</summary>
    private static void Rest(Space space)
    {
        space.Following = false;
        if (space.Stretched)
            Reshape(space);
        else
            space.Window.Rest();
    }

    private static void Reshape(Space space)
    {
        if (space.IsSheet)
            RefitSheet(space);
        space.Stretched = false;
        space.Following = false;
        space.Shown = space.ToScreen;
        space.Window.SetShape(space.Outline, space.ToScreen);
    }

    /// <summary>
    /// The app closed the space's window. The space is kept, out of sight, until the app opens a
    /// matching window again; a space whose app can't be told apart goes to the desktop instead.
    /// </summary>
    private void OnHostClosed(Space space)
    {
        if (!_spaces.Contains(space))
            return;
        _windowIds.Remove(space.Window.Host);

        if (space.Record.Host is { App.Length: > 0 } host)
        {
            CloseWindow(space);
            _spaces.Remove(space);
            _kept.Add(space.Record);
            logger.LogInformation("Keeping a space until {App} opens \"{Title}\" again", Path.GetFileName(host.App), host.Title);
        }
        else
        {
            Bake(space);
            space.Record.Host = null;
            space.Window.SetHost(IntPtr.Zero);
        }
        Persist();
    }

    /// <summary>Soon (once windows have settled on their title and size), gives kept spaces back to open windows.</summary>
    private void LookSoon()
    {
        _lookSoon ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            _lookSoon!.Stop();
            ReturnKept();
        }, _dispatcher);
        if (!_lookSoon.IsEnabled)
            _lookSoon.Start();
    }

    /// <summary>Every couple of seconds: remember app windows' current titles, and look for kept spaces' windows.</summary>
    private void Lookout()
    {
        if (_spaces.Any(s => s.Moving || s.Window.IsReshaping))
            return;
        foreach (var space in _spaces)
        {
            if (space.Record.Host is not { } host || space.Window.Host == IntPtr.Zero)
                continue;
            string title = AppWindows.TitleOf(space.Window.Host);
            if (title.Length > 0)
                host.Title = title;
        }
        ReturnKept();
    }

    /// <summary>Puts kept spaces back on open windows of their apps (see <see cref="HostMatcher"/>).</summary>
    private void ReturnKept()
    {
        if (_kept.Count == 0)
            return;

        var waiting = _kept
            .Where(r => r.Host is not null)
            .GroupBy(r => r.Host!.Window)
            .Select(g => g.OrderByDescending(r => r.CreatedAt).First().Host!)
            .Select(h => new HostMatcher.Waiting(h.Window, h.App, h.WindowClass, h.Title))
            .ToList();
        var classes = waiting.Select(w => w.WindowClass).ToHashSet(StringComparer.Ordinal);
        // Screen notes on a window don't keep its spaces away (a second sheet on it is harmless).
        var busy = _spaces.Where(s => !s.IsSheet).Select(s => s.Window.Host).Where(h => h != IntPtr.Zero).ToHashSet();

        var open = new List<HostMatcher.Open>();
        foreach (var hwnd in AppWindows.OpenWindows())
        {
            if (busy.Contains(hwnd) || !classes.Contains(AppWindows.ClassOf(hwnd)))
                continue;
            if (AppWindows.Describe(hwnd) is { } identity)
                open.Add(new HostMatcher.Open(hwnd, identity.App, identity.WindowClass, identity.Title));
        }

        foreach (var (group, window) in HostMatcher.Match(waiting, open, busy))
        {
            if (!AppWindows.TryGetFrame(window, out var frame))
                continue;
            _windowIds[window] = group;
            var records = _kept.Where(r => r.Host?.Window == group).ToList();
            foreach (var record in records)
            {
                _kept.Remove(record);
                Open(record, window, HostFrames.Map(record.Host!.Frame, frame));
            }
            logger.LogInformation("Brought back {Count} space(s) on {App} \"{Title}\"", records.Count, Path.GetFileName(records[0].Host!.App), AppWindows.TitleOf(window));
        }
    }

    /// <summary>
    /// Rewrites the record in the coordinates it is shown in now, so that edits made on screen (erasing,
    /// merging, moving to another app) apply directly. Looks the same afterwards.
    /// </summary>
    private static void Bake(Space space)
    {
        if (space.Stretched)
            Reshape(space); // what is drawn matches the mapping again
        BakeMapping(space);
    }

    private static void BakeMapping(Space space)
    {
        var m = space.ToScreen;
        var placeNote = space.NotePlacement; // while it was being reshaped, text and paint went their own way
        var placeStroke = space.StrokePlacement;
        space.NotePlacement = space.Window.NotePlacement = null;
        space.StrokePlacement = space.Window.StrokePlacement = null;
        if (m.IsIdentity && placeNote is null && placeStroke is null)
            return;
        space.Record.Transform(m, placeNote, placeStroke);
        foreach (var batch in space.Redo)
        {
            foreach (var ink in batch)
                SpaceRecord.TransformInk(ink, m, placeNote, placeStroke, space.Record.Anchor);
        }
        if (space.Record.Host is { } host)
            host.Frame = Rect.Transform(host.Frame, m);
        space.Shape = SpaceGeometry.Decode(space.Record);
        space.ToScreen = space.Shown = Matrix.Identity;
        space.Window.Rebase(Matrix.Identity);
    }

    /// <summary>
    /// Opens a system text box on a space: for a new note in the pen's colour and size where the mouse
    /// is, or on an existing note (<paramref name="editing"/>), which the box shows instead meanwhile.
    /// </summary>
    private void BeginText(Space space, Point at, TextNote? editing)
    {
        _textEntry?.Commit();

        Point origin;
        TextNote draft;
        if (editing is null)
        {
            double lineHeight = Pen.FontSize * NoteText.LineSpacing;
            // The mouse marks the middle of the first line, where the caret appears.
            origin = new Point(Math.Round(at.X, 2), Math.Round(at.Y - lineHeight / 2, 2));
            draft = new TextNote { FontSize = Pen.FontSize, Color = Pen.Color, Bold = Pen.Bold, Italic = Pen.Italic };
        }
        else
        {
            origin = space.ToScreen.Transform(new Point(editing.X, editing.Y));
            draft = editing;
            space.Window.Editing = editing;
        }
        IntPtr previousApp = Win32.GetForegroundWindow();

        var entry = new TextEntry(origin, draft, space.IsSheet ? Themes.SheetInk : Themes.InkOf(Theme));
        entry.ColorPicked += color => SetPen(Pen with { Color = color });
        entry.FontSizePicked += size => SetPen(Pen with { FontSize = size });
        entry.StylePicked += (bold, italic) => SetPen(Pen with { Bold = bold, Italic = italic });
        entry.Committed += typed => Keep(space, editing, origin, typed);
        entry.Gone += () => // closed, or fading away: done with either way
        {
            if (_textEntry == entry)
                _textEntry = null;
            ToolCursors.RefreshUnderMouse(); // the text box's cursor would stay until the mouse moved
            if (editing is not null)
            {
                foreach (var shown in _spaces.Where(s => s.Window.Editing == editing))
                    shown.Window.Editing = null; // cancelled: the note shows again as it was
            }
            DropEmptySheet(space);
            Foreground.WhenMouseUp(() => // mid-stroke, focus stays put until the stroke is done
            {
                Foreground.RestoreTo(previousApp);
                Restack(); // the app just came to the front: its spaces go straight back on top of it
            });
        };
        _textEntry = entry;
        entry.Show();
        entry.Activate();
    }

    /// <summary>
    /// Keeps what was typed: a new note, or the edited version of <paramref name="editing"/>, which
    /// undo turns back into the note as it was. Text that was deleted entirely removes the note.
    /// </summary>
    /// <param name="shownAt">Where the text box started on screen: the edited note's place, unless it was moved.</param>
    /// <param name="typed">The note as typed, placed on screen.</param>
    private void Keep(Space space, TextNote? editing, Point shownAt, TextNote typed)
    {
        // An edited note is still on whichever space holds it, even if its space merged meanwhile.
        var owner = editing is null ? space : _spaces.FirstOrDefault(s => s.Record.Notes.Contains(editing));
        if (owner is null || !_spaces.Contains(owner))
            return; // the space was removed, merged or put away while typing
        if (editing is null && typed.Text.Length == 0)
            return;

        var onScreen = new Point(typed.X, typed.Y);
        if (editing is not null && (onScreen - shownAt).Length < 0.01)
        {
            (typed.X, typed.Y) = (editing.X, editing.Y); // not moved: exactly where it was
        }
        else
        {
            // Typed on screen; kept in the record's coordinates (they differ while the app is stretched).
            var toRecord = owner.ToScreen;
            if (toRecord.HasInverse)
                toRecord.Invert();
            var p = toRecord.Transform(onScreen);
            (typed.X, typed.Y) = (Math.Round(p.X, 2), Math.Round(p.Y, 2));
        }

        if (editing is not null && SameNote(editing, typed))
            return; // closed without changes
        typed.Group = editing?.Group; // editing doesn't take a note out of its group
        typed.Order = InkOrder.Next();
        if (editing is null)
        {
            owner.Record.Notes.Add(typed);
        }
        else
        {
            if (typed.Text == editing.Text)
                owner.Window.Adopt(editing, typed); // moved or recoloured: a long file isn't laid out again
            owner.Record.Replace(editing, typed);
        }

        owner.Redo.Clear();
        if (editing is not null && owner.Window.Editing == editing)
        {
            owner.Window.Editing = null; // shows the kept version in its place
            if (owner.IsSheet)
                FitSheet(owner);
            else if (owner.Record.Card)
                FitCard(owner);
        }
        else
        {
            ShowInk(owner);
        }
        Persist();
    }

    /// <summary>Whether an edit changed nothing about a note.</summary>
    internal static bool SameNote(TextNote a, TextNote b) =>
        a.Text == b.Text && a.X == b.X && a.Y == b.Y && a.FontSize == b.FontSize && a.Color == b.Color && a.Width == b.Width &&
        a.Bold == b.Bold && a.Italic == b.Italic;

    private void CloseWindow(Space space)
    {
        universe.Detach(space.Window);
        space.Window.Dispose();
    }

    /// <summary>Something changed: saved a moment later, off the UI thread (see <see cref="SaveQueue"/>).</summary>
    private void Persist() => (_saves ??= new SaveQueue(store, logger, _dispatcher, SavedRecords)).Request();

    /// <summary>Saves what is waiting and waits for the disk (on exit, and for checks that read the file).</summary>
    internal void FlushSaves() => _saves?.Flush();

    /// <summary>What is saved: every space, and kept ones, but screen notes only with something on them.</summary>
    private IEnumerable<SpaceRecord> SavedRecords() =>
        _spaces.Where(s => !s.IsSheet || s.Record.Strokes.Count > 0 || s.Record.Notes.Count > 0).Select(s => s.Record).Concat(_kept);

    internal sealed record SpacePart(SpaceRecord Record, PathGeometry Outline);

    /// <summary>What the mouse is on (see <see cref="HoverAtCursor"/>).</summary>
    internal enum Hover
    {
        None,
        Space,
        Note,
        Sheet,
    }

    /// <summary>A space picked up with Ctrl+Alt+M (see <see cref="PickUp"/>).</summary>
    internal sealed class MovingSpace
    {
        private readonly MatrixTransform? _liveTransform;
        private readonly PathGeometry? _liveOutline;
        private Dictionary<TextNote, Rect>? _noteBounds;

        internal MovingSpace(Space space, Matrix before, Rect bounds)
        {
            Space = space;
            Before = before;
            Bounds = Now = bounds;
            CardWidthBefore = space?.Record is { Card: true, Notes: [var note] } ? note.Width : null;
            if (space is not null)
            {
                _liveTransform = new MatrixTransform(before);
                _liveOutline = new PathGeometry(space.Shape.Figures, space.Shape.FillRule, _liveTransform);
            }
        }

        internal Geometry LiveOutline(Matrix toScreen)
        {
            if (Space.Record.Card || _liveOutline is null || _liveTransform is null)
                return SpaceGeometry.Mapped(Space.Shape, toScreen);
            _liveTransform.Matrix = toScreen;
            return _liveOutline;
        }

        /// <summary>A card's text width when it was picked up, which reshaping reflows (put back if it is put back).</summary>
        internal double? CardWidthBefore { get; }

        internal Space Space { get; }

        /// <summary>Where it was shown before it was picked up.</summary>
        internal Matrix Before { get; }

        /// <summary>Its outline's bounds on screen (DIPs) when it was picked up.</summary>
        internal Rect Bounds { get; }

        /// <summary>Its outline's bounds on screen now (see <see cref="Fit"/>).</summary>
        internal Rect Now { get; set; }

        /// <summary>How far moves, not reshapes, have carried it (and its text) since it was picked up.</summary>
        internal Vector Carried { get; set; }

        /// <summary>Where a note of the space is shown now (see <see cref="SpaceManager.PlaceNote"/>).</summary>
        internal Point PlaceNote(TextNote note)
        {
            if (Space?.Record.Card == true)
                return Now.TopLeft + new Vector(CardPadding(note.FontSize), CardPadding(note.FontSize)); // a card's text stays inside its margins
            _noteBounds ??= [];
            if (!_noteBounds.TryGetValue(note, out var box))
                _noteBounds[note] = box = NoteText.Bounds(note, Before);
            return SpaceManager.PlaceNote(box, Bounds, Now, Carried);
        }

        /// <summary>
        /// How a stroke of the space is mapped onto the screen now: as it was shown when picked up,
        /// carried along by moves only. Reshaping never stretches or shifts paint, whichever edge moves.
        /// </summary>
        internal Matrix PlaceStroke(InkStroke stroke)
        {
            var m = stroke.Placed(Before, Space?.Record.Anchor);
            m.Translate(Carried.X, Carried.Y);
            return m;
        }
    }

    internal sealed class Space(SpaceRecord record, PathGeometry shape, SpaceWindow window, Matrix toScreen)
    {
        public SpaceRecord Record { get; } = record;

        /// <summary>Whether this is screen notes rather than a space.</summary>
        public bool IsSheet => Record.Kind == SpaceKind.Sheet;

        private PathGeometry? _outline;
        private Matrix _outlineFor;

        /// <summary>The outline in the record's coordinates.</summary>
        public PathGeometry Shape
        {
            get;
            set
            {
                field = value;
                _outline = null;
            }
        } = shape;

        /// <summary>
        /// The outline on screen now: <see cref="Shape"/> through <see cref="ToScreen"/>. Worked out
        /// when asked for, not on every move of the app.
        /// </summary>
        public PathGeometry Outline
        {
            get
            {
                if (_outline is null || _outlineFor != ToScreen)
                {
                    _outline = SpaceGeometry.Transform(Shape, ToScreen);
                    _outlineFor = ToScreen;
                }
                return _outline;
            }
        }

        /// <summary>Maps the record's coordinates onto the screen (identity unless its app moved or resized since).</summary>
        public Matrix ToScreen { get; set; } = toScreen;

        /// <summary>The mapping the window was drawn for; differs from <see cref="ToScreen"/> while it is <see cref="Stretched"/>.</summary>
        public Matrix Shown { get; set; } = toScreen;

        /// <summary>Stretched on the GPU to follow a live resize, waiting to be drawn at its new size.</summary>
        public bool Stretched { get; set; }

        /// <summary>When its app last changed size.</summary>
        public DateTime LastResize { get; set; }

        /// <summary>Following its moving app through the compositor, not yet back in its own window.</summary>
        public bool Following { get; set; }

        /// <summary>When its app last moved or changed size.</summary>
        public DateTime LastChange { get; set; }

        /// <summary>Being moved with Ctrl+Alt+M: its app's moves don't pull it along meanwhile.</summary>
        public bool Moving { get; set; }

        /// <summary>While it is moved and reshaped: where its notes are shown, which is not through <see cref="ToScreen"/>.</summary>
        public Func<TextNote, Point>? NotePlacement { get; set; }

        /// <summary>While it is moved and reshaped: how its strokes are mapped, which is not through <see cref="ToScreen"/>.</summary>
        public Func<InkStroke, Matrix>? StrokePlacement { get; set; }

        public SpaceWindow Window { get; } = window;

        /// <summary>Changes taken back by undo (each one or more items), newest on top; lives for this session only.</summary>
        public Stack<IInk[]> Redo { get; } = new();
    }
}
