using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>
/// The on-screen window of one space: a borderless popup that never takes focus. Its window region is
/// the space's outline (plus a small margin), so clicks elsewhere reach whatever is below. Dragging
/// inside it paints with the current <see cref="Pen"/>; a click only splashes; a right-click asks for
/// the ink palette.
/// </summary>
/// <remarks>
/// <para>
/// Z-order: a space drawn over an app sits directly above that app's window and follows it (it comes to
/// the front with the app, is covered with it, hides while the app is minimised). Without an app, or
/// once the app closes, it lives at desktop level, just above the wallpaper. A preview (a block being
/// placed) floats topmost, just under the placement overlay.
/// </para>
/// <para>
/// Pixels reach the screen through DirectComposition (<see cref="SpaceSurface"/>): each frame uploads
/// only the small art-resolution picture and the GPU scales it up, which is what makes 120+ fps cheap.
/// </para>
/// </remarks>
internal sealed partial class SpaceWindow : IDisposable
{
    /// <summary>Room around the outline for the rim wobble and click ripples, in DIPs.</summary>
    private const double RimRoom = 14;

    /// <summary>How far outside the outline the window takes the mouse: rim + wobble + ripple, in DIPs.</summary>
    private const double HitMargin = 10;

    /// <summary>How far (DIPs) a press must move before it becomes a paint stroke rather than a click.</summary>
    private const double DragThreshold = 3;

    /// <summary>Longest straight piece (DIPs) of a wobbling outline, so its sides can bend.</summary>
    private const double JellyStep = 6;

    /// <summary>
    /// For moving and sizing the window: nothing else needs redrawing or repainting (the desktop
    /// compositor draws every window from its own surface), which makes moves several times cheaper.
    /// </summary>
    private const uint PlaceFlags = Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOREDRAW | Win32.SWP_NOCOPYBITS | Win32.SWP_DEFERERASE;

    private static readonly IntPtr CrossCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_CROSS);
    private static readonly IntPtr MoveCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_SIZEALL);
    private static readonly IntPtr ArrowCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW);
    private static readonly IntPtr IBeamCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_IBEAM);

    private readonly HwndSource _source;
    private SpaceSurface _surface; // made again if the GPU is lost (see RecreateGraphics)
    private readonly GpuCompositor _gpu;
    private readonly SpaceRecord _record;
    private readonly IntPtr _previewUnder;
    private IntPtr _host;
    private Geometry _outline = Geometry.Empty;
    private ISpaceLook _look;
    private string _theme;
    private InkLayer? _ink;
    private Int32Rect _inkDirty = Int32Rect.Empty;
    private Rect _world;
    private Win32.POINT _position;
    private Win32.SIZE _size;
    private bool _placementChanged;
    private bool _needsPresent;
    private bool _lookDirty = true; // the look has a picture the compositor hasn't got yet
    private Gesture _gesture;
    private Point _pressedAt;
    private List<Point> _stroke = [];
    private InkPen _strokePen = InkPen.Default;
    private Color _strokeColor;
    private TextNote? _editing;
    private bool _shown;
    private bool _hidden;
    private bool _disposed;
    private DateTime? _minimizingSince;
    private Matrix _toScreen = Matrix.Identity;
    private Rect _hostFrame = Rect.Empty;
    private bool _following = true;
    private bool _floating;
    private Win32.POINT _floatOrigin;
    private IntPtr _region;
    private bool _regionStale;
    private bool _hostGone;
    private IntPtr _liftedUnder;
    private (Geometry Outline, Matrix ToScreen)? _live; // where a live reshape comes to rest (see ReshapeLive)
    private bool _liveChanged;
    private readonly Jelly _jelly = new();
    private bool _restWhenStill; // wobbling by itself (see Wobble): back in its own window once still
    private bool _draft;          // the look has a draft of the shape (see DrawLive)
    private double _lastLive = double.NaN; // frame time the live shape was last drawn

    /// <summary>How long a live reshape holds still before its draft is drawn properly, in seconds.</summary>
    private const double DraftPause = 0.15;
    private double _jellyTime = double.NaN; // frame time the wobble was last moved on to
    private FrameState _lastFrame;

    /// <param name="record">The space; strokes painted here are added to it.</param>
    /// <param name="outline">Outline in screen DIPs.</param>
    /// <param name="host">Top-level window of the app to sit on, or zero for desktop level.</param>
    /// <param name="gpu">Where the pixels go.</param>
    /// <param name="theme">How it looks (see <see cref="Themes"/>).</param>
    /// <param name="previewUnder">
    /// Makes this a preview, topmost just under this (topmost) window; zero for a real space.
    /// </param>
    /// <param name="toScreen">Maps the record's coordinates onto the screen (see <see cref="SpaceRecord"/>); identity by default.</param>
    public SpaceWindow(SpaceRecord record, Geometry outline, IntPtr host, GpuCompositor gpu, string theme, IntPtr previewUnder = default, Matrix toScreen = default)
    {
        _record = record;
        _host = host;
        _previewUnder = previewUnder;
        _theme = theme;
        _toScreen = toScreen;
        _look = Themes.CreateLook(theme);

        // No redirection bitmap: the window shows only what DirectComposition puts in it.
        _source = new HwndSource(new HwndSourceParameters("Infinite Space")
        {
            WindowStyle = unchecked((int)Win32.WS_POPUP),
            ExtendedWindowStyle = (int)(Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE),
            WindowClassStyle = CS_DBLCLKS, // an opened file's name opens the file on a double-click
            Width = 1,
            Height = 1,
        });
        _source.AddHook(WndProc);
        if (previewUnder == IntPtr.Zero)
            AcceptFiles();
        int noRounding = Win32.DWMWCP_DONOTROUND;
        Win32.DwmSetWindowAttribute(_source.Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRounding, sizeof(int));

        _gpu = gpu;
        _surface = new SpaceSurface(gpu, _source.Handle);
        SetShape(outline);
    }

    private enum Gesture
    {
        None,
        Pressed,
        Painting,
        TextPressed,
        HandOnInk,
        HandOnEmpty,
        MovingSelection,
        Marquee,
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>The app window this space sits on, or zero at desktop level (also once that app has closed).</summary>
    public IntPtr Host => _host;

    /// <summary>A paint stroke was added to the record.</summary>
    public event Action<SpaceWindow>? InkChanged;

    /// <summary>The space was right-clicked at this screen DIP position: show the ink palette.</summary>
    public event Action<SpaceWindow, Point>? PaletteRequested;

    /// <summary>A press on the space just became a paint stroke (the palette and text boxes get out of the way).</summary>
    public event Action<SpaceWindow>? PaintingStarted;

    /// <summary>Colour and width of the next paint stroke (one being painted keeps the pen it started with).</summary>
    public InkPen Pen { get; set; } = InkPen.Default;

    private Tool _tool = Tool.Hand;

    /// <summary>What the mouse does here (set for every space by <see cref="SpaceManager.SetTool"/>).</summary>
    public Tool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
                return;
            _tool = value;
            if (EffectiveTool != Tool.Hand)
                ClearSelection();
        }
    }

    /// <summary>The tool as it acts here: the pen and text as picked, everything else as the hand.</summary>
    private Tool EffectiveTool => _tool is Tool.Pen or Tool.Text ? _tool : Tool.Hand;

    /// <summary>The text tool clicked here (screen DIPs): a text box should open there.</summary>
    public event Action<SpaceWindow, Point>? TypeRequested;

    /// <summary>
    /// Where notes are drawn on screen (DIPs) while the space is being moved and reshaped, when that
    /// is not simply where the record's mapping puts them (text stays put while the space is
    /// reshaped). Null draws them through the mapping. Takes effect on the next redraw.
    /// </summary>
    public Func<TextNote, Point>? NotePlacement { get; set; }

    /// <summary>
    /// How strokes are mapped onto the screen while the space is being moved and reshaped, when that
    /// is not simply the record's mapping (paint, like text, stays put while the space is reshaped).
    /// Null maps them through the mapping. Takes effect on the next redraw.
    /// </summary>
    public Func<InkStroke, Matrix>? StrokePlacement { get; set; }

    /// <summary>
    /// Whether its shape is still changing by itself: a new shape waiting for the next frame, or a
    /// resize still wobbling (see <see cref="ReshapeLive"/>). Moves then go through <see cref="ReshapeLive"/> too.
    /// </summary>
    public bool IsReshaping => _liveChanged || _jelly.IsMoving;

    /// <summary>
    /// A note of this space being edited in a text box: it is left out of the picture meanwhile, so it
    /// doesn't show twice (or at its old place while the box is moved). Null shows every note.
    /// </summary>
    public TextNote? Editing
    {
        get => _editing;
        set
        {
            if (_editing == value)
                return;
            _editing = value;
            RedrawInk();
        }
    }

    /// <summary>
    /// The app window moved or changed size (its new rectangle, in screen DIPs). Also raised once for
    /// the rectangle first seen.
    /// </summary>
    public event Action<SpaceWindow, Rect>? HostMoved;

    /// <summary>The app window closed. The space is hidden until told what to do (<see cref="SetHost"/> or dispose).</summary>
    public event Action<SpaceWindow>? HostClosed;

    /// <summary>
    /// Something the user is doing here is moving (a block being dragged, paint, a click burst): it
    /// wants every display refresh, not just the ambient animation rate. (Following the app's moves
    /// needs no drawing: see <see cref="Track"/>.)
    /// </summary>
    public bool WantsEveryFrame => !_disposed && (_needsPresent || !_shown || _look.HasBursts || _jelly.IsMoving || FilesMoving || (_draft && _live is not null));

    /// <summary>Whether a screen DIP point is on the space itself (not its transparent surroundings).</summary>
    public bool Contains(Point screen) => _look.IsInside(screen);

    /// <summary>On screen now (not hidden with its app, nor waiting to be shown).</summary>
    public bool IsShowing => !_disposed && _shown && !_hidden;

    /// <param name="outline">Outline in screen DIPs.</param>
    /// <param name="toScreen">A new mapping of the record's coordinates onto the screen, if it changed.</param>
    public void SetShape(Geometry outline, Matrix? toScreen = null)
    {
        if (toScreen is { } m)
            _toScreen = m;
        EndLive(); // this one wins
        bool landing = _floating;
        EndFloat(); // drawn properly at the new size below, in its own window again
        Lay(outline);
        _placementChanged = true;
        ApplyRegion();

        // Render now with the latest frame, so a moving block keeps up with the mouse; the next
        // compositor commit (at most one refresh away) puts it on screen.
        _look.Render(_lastFrame);
        _needsPresent = true;
        if (_shown && !_hidden)
        {
            Present();
            if (landing)
                _surface.CommitNow(); // back in its own window just now: the new picture goes with it
        }
    }

    /// <summary>
    /// For a space being reshaped under the user's hand (lifted with Ctrl+Alt+M, or a block being
    /// placed): shows it at a new shape, drawn properly, never stretched. It is drawn on the next frame,
    /// however often it changes meanwhile, in the window spread over the screen: only the picture
    /// changes, in one compositor commit, so nothing jumps or flashes. In the Dreamy theme a resize
    /// wobbles like jelly for up to half a second (see <see cref="Jelly"/>); a move doesn't, and other
    /// themes don't wobble. A lifted space's window takes its
    /// own place and click-through shape again when it lands (<see cref="Rest"/>).
    /// </summary>
    /// <param name="outline">Outline in screen DIPs, where it comes to rest.</param>
    /// <param name="toScreen">The record's mapping onto the screen for that outline.</param>
    public void ReshapeLive(Geometry outline, Matrix toScreen)
    {
        if (_disposed)
            return;
        _live = (outline, toScreen);
        _liveChanged = true;
        // Only Dreamy wobbles; and none while Windows' "Show animations" is off, nor in software drawing.
        _jelly.Retarget(outline.Bounds, wobble: _theme == Themes.Dreamy && SystemParameters.ClientAreaAnimation && !_gpu.IsSoftware);
        _needsPresent = true; // wanted on the very next frame
    }

    /// <summary>
    /// Draws the shape as it ends up, now, with no wobble left (see <see cref="ReshapeLive"/>): before the
    /// space lands, its record is rewritten, or a block becomes a space.
    /// </summary>
    public void FinishReshape()
    {
        if (_disposed || _live is null)
            return;
        bool changing = IsReshaping;
        _jelly.Stop();
        if (changing)
            DrawLive();
    }

    /// <summary>Draws the live shape as the wobble has it now (exactly where it rests once the wobble is over).</summary>
    /// <param name="draft">
    /// The look may take shortcuts while the shape changes every frame (see
    /// <see cref="ISpaceLook.SetShape(Geometry, Rect, bool)"/>); a pause, or landing, draws it properly.
    /// </param>
    private void DrawLive(bool draft = true)
    {
        _lastLive = _lastFrame.Time;
        var (outline, toScreen) = _live!.Value;
        _liveChanged = false;
        if (_jelly.IsMoving)
        {
            var shown = _jelly.Shown;
            toScreen.Append(HostFrames.Map(_jelly.Rest, shown)); // the outline wobbles (paint and text are placed on their own)
            outline = SpaceGeometry.Warp(outline, p => _jelly.Map(p, shown), JellyStep);
        }
        else
        {
            _jellyTime = double.NaN;
        }
        _toScreen = toScreen;
        Lay(outline, draft);
        _regionStale = true; // made once, when it lands; meanwhile the overlay above takes the mouse
        _look.Render(_lastFrame);
        _needsPresent = true;
        if (!_shown || _hidden)
        {
            _placementChanged = true; // placed properly when it next shows
            return;
        }
        Present();
        ShowAt(_world); // the new picture at its place: it lands in the same commit
    }

    /// <summary>Done with live reshaping (it landed, or was given a shape outright): the next one starts afresh.</summary>
    private void EndLive()
    {
        _live = null;
        _liveChanged = false;
        _jelly.Reset();
        _jellyTime = double.NaN;
    }

    /// <summary>
    /// Lays the space out for an outline: the art-pixel-aligned rectangle it covers, its look, its
    /// window's place and size (for when it is in its own window), and its ink redrawn.
    /// </summary>
    /// <param name="draft">A shape changing every frame (a live reshape): the look may take shortcuts.</param>
    private void Lay(Geometry outline, bool draft = false)
    {
        _outline = outline;
        var bounds = outline.Bounds;
        bounds.Inflate(RimRoom, RimRoom);
        // Snap to the art-pixel grid so all spaces share one pixel grid (and one star field).
        const double grid = DreamStyle.ArtPixel;
        double left = Math.Floor(bounds.Left / grid) * grid, top = Math.Floor(bounds.Top / grid) * grid;
        _world = new Rect(left, top, Math.Ceiling(bounds.Right / grid) * grid - left, Math.Ceiling(bounds.Bottom / grid) * grid - top);
        _look.SetShape(outline, _world, draft);
        _draft = draft;
        _lookDirty = true;

        double scale = Dpi.Scale;
        _position = new Win32.POINT { X = (int)Math.Round(_world.X * scale), Y = (int)Math.Round(_world.Y * scale) };
        _size = new Win32.SIZE { Width = Math.Max(1, (int)Math.Round(_world.Width * scale)), Height = Math.Max(1, (int)Math.Round(_world.Height * scale)) };

        if (_record.Strokes.Count == 0 && _record.Notes.Count == 0)
        {
            DropInk();
        }
        else if (_ink is null)
        {
            EnsureInk();
        }
        else
        {
            // A space being reshaped gets a new shape every frame: the same layer draws it.
            _ink.SetShape(_size.Width, _size.Height, Dpi.Scale, _world.TopLeft, outline);
            DrawInk();
        }
    }

    /// <summary>No ink on the space (any more): its layer goes.</summary>
    private void DropInk()
    {
        if (_ink is null)
            return;
        _ink.Dispose();
        _ink = null;
        _inkDirty = Int32Rect.Empty;
        _surface.HideInk();
    }

    /// <summary>Re-dresses the space in another theme, straight away.</summary>
    public void SetTheme(string theme)
    {
        if (_disposed || theme == _theme)
            return;
        _theme = theme;
        _look.Dispose();
        _look = Themes.CreateLook(theme);
        if (_ink is not null)
        {
            _ink.ThemeInk = Themes.InkOf(theme);
            _ink.ThemeAccent = Themes.AccentOf(theme);
        }
        _surface.HideStills(); // the new look draws its own, if it has any
        SetShape(_outline); // new look for the same shape; ink is redrawn in the theme's colour
    }

    /// <summary>
    /// The GPU was lost and its devices made anew: everything shown is made again from the record (a
    /// new surface, look and ink), in the space's own window, at its place.
    /// </summary>
    public void RecreateGraphics()
    {
        if (_disposed)
            return;
        _ink?.Dispose();
        _ink = null;
        _inkDirty = Int32Rect.Empty;
        _surface.Dispose();
        _surface = new SpaceSurface(_gpu, _source.Handle);
        _look.Dispose();
        _look = Themes.CreateLook(_theme);
        EndLive();
        _restWhenStill = false;
        if (_floating)
        {
            EndFloat();
            _placementChanged = true; // back in its own window when it is next shown
        }
        SetShape(_outline);
    }

    /// <summary>Re-rasterises the record's strokes and notes (after text was added or undone).</summary>
    /// <param name="present">Show it now (otherwise the caller presents).</param>
    public void RedrawInk(bool present = true)
    {
        if (_disposed)
            return;
        if (_record.Strokes.Count == 0 && _record.Notes.Count == 0)
        {
            _selected.Clear(); // nothing left to select
            DropInk();
            return;
        }
        if (_ink is null)
            EnsureInk();
        else
            DrawInk();
        if (present && _shown && !_hidden)
            Present();
    }

    /// <summary>All the ink afresh: the note being edited left out, notes and strokes where they are shown, new files fading in.</summary>
    private void DrawInk()
    {
        // Ink that is gone (undone, replaced by a move) can't stay selected.
        _selected.RemoveWhere(i => i is InkStroke s ? !_record.Strokes.Contains(s) : !_record.Notes.Contains((TextNote)i));
        _inkDirty = _ink!.Redraw(_record, _toScreen, _editing, PlaceNotes, _arrivals.Count > 0 ? NoteOpacity : null, PlaceStrokes, Adornments());
    }

    /// <summary>
    /// Moves the space with everything on it, without redrawing anything but what depends on where
    /// it is on screen. <paramref name="outline"/> is the current one shifted by <paramref name="offset"/>.
    /// </summary>
    /// <param name="toScreen">The record's mapping onto the screen, shifted by the same offset.</param>
    public void Move(Geometry outline, Vector offset, Matrix toScreen)
    {
        if (_disposed)
            return;
        _outline = outline;
        _toScreen = toScreen;
        _world.Offset(offset);
        _look.Move(outline, _world);
        _ink?.MoveOrigin(offset);
        _jelly.Shift(offset); // a later resize wobbles from here, not from where it was

        double scale = Dpi.Scale;
        _position = new Win32.POINT { X = (int)Math.Round(_world.X * scale), Y = (int)Math.Round(_world.Y * scale) };
        // What it shows is relative to it, so only its place changes. Whatever is fixed to the screen
        // (stars, motes) catches up on the next animation frame.
        ShowAt(_world);
    }

    /// <summary>
    /// Stretches what is drawn now, on the GPU, without redrawing it: <paramref name="relative"/> maps
    /// the screen as drawn to where it should be (a scale and a shift). For a live resize of the app;
    /// <see cref="SetShape"/> draws it properly once the resize is over.
    /// </summary>
    /// <param name="toScreen">The record's mapping onto the screen after the stretch.</param>
    public void Stretch(Matrix relative, Matrix toScreen)
    {
        if (_disposed)
            return;
        _toScreen = toScreen;
        ShowAt(Rect.Transform(_world, relative));
    }

    /// <summary>
    /// Back into its own window-sized window, where the space is now, once its app has stopped
    /// moving (see <see cref="ShowAt"/>).
    /// </summary>
    public void Rest()
    {
        if (_disposed)
            return;
        FinishReshape(); // the latest shape, at rest, is the one that lands
        if (_draft && _live is not null)
            DrawLive(draft: false); // and it lands drawn properly
        EndLive();
        if (!_floating)
            return;
        EndFloat();
        Win32.SetWindowPos(_source.Handle, IntPtr.Zero, _position.X, _position.Y, _size.Width, _size.Height, PlaceFlags);
        _surface.CommitNow(); // the window moved just now: the picture's placement goes with it
        RestoreRegion();
    }

    /// <summary>
    /// Shows what is drawn at a screen rectangle (DIPs), through the desktop compositor alone. The first
    /// time, the window is spread over the whole screen, see-through and click-through, so that from
    /// then on following an app that moves or resizes costs no window moves at all, only compositor
    /// frames. <see cref="Rest"/> or <see cref="SetShape"/> ends it.
    /// </summary>
    private void ShowAt(Rect target)
    {
        if (!_shown || _hidden || _placementChanged)
        {
            _placementChanged = true; // not on screen: it is placed properly when it next shows
            return;
        }
        bool spreading = !_floating;
        if (spreading)
            Float();

        double scale = Dpi.Scale;
        double sx = target.Width * scale / _size.Width, sy = target.Height * scale / _size.Height;
        // Whole pixels and an exact 1:1 while only moving, so nothing is resampled.
        if (Math.Abs(sx - 1) < 1e-3 && Math.Abs(sy - 1) < 1e-3)
            sx = sy = 1;
        _surface.SetPlacement((float)sx, (float)sy,
            (float)Math.Round(target.X * scale - _floatOrigin.X), (float)Math.Round(target.Y * scale - _floatOrigin.Y));
        if (spreading)
            _surface.CommitNow(); // the window was spread over the screen just now: the picture's placement goes with it
    }

    private void Float()
    {
        var screen = new Win32.RECT
        {
            Left = (int)Math.Floor(SystemParameters.VirtualScreenLeft * Dpi.Scale),
            Top = (int)Math.Floor(SystemParameters.VirtualScreenTop * Dpi.Scale),
            Right = (int)Math.Ceiling((SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth) * Dpi.Scale),
            Bottom = (int)Math.Ceiling((SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight) * Dpi.Scale),
        };
        _floatOrigin = new Win32.POINT { X = screen.Left, Y = screen.Top };
        _floating = true;
        // Click-through while floating: the mouse belongs to the moving app, and then to whatever is below.
        Win32.SetClickThrough(_source.Handle, true);
        Win32.SetWindowRgn(_source.Handle, IntPtr.Zero, redraw: false);
        Win32.SetWindowPos(_source.Handle, IntPtr.Zero, screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top, PlaceFlags);
    }

    /// <summary>Undoes <see cref="Float"/>'s placement and click-through; the caller puts the window and its shape back.</summary>
    private void EndFloat()
    {
        if (!_floating)
            return;
        _floating = false;
        _surface.SetPlacement(1, 1, 0, 0);
        Win32.SetClickThrough(_source.Handle, false);
    }

    /// <summary>
    /// Makes the record's mapping onto the screen this one without redrawing (the record was rewritten
    /// in these coordinates, so it looks the same).
    /// </summary>
    public void Rebase(Matrix toScreen)
    {
        _toScreen = toScreen;
        if (_live is { } live)
            _live = (live.Outline, toScreen);
    }

    /// <summary>Puts the space on another app window, or at desktop level (zero), and shows it there.</summary>
    public void SetHost(IntPtr host)
    {
        if (_disposed)
            return;
        _host = host;
        _hostGone = false;
        _hostFrame = Rect.Empty;
        _minimizingSince = null;
        if (!_shown)
            return;
        if (_hidden && host == IntPtr.Zero)
        {
            Win32.ShowWindow(_source.Handle, Win32.SW_SHOWNOACTIVATE);
            _hidden = false;
        }
        if (_liftedUnder == IntPtr.Zero)
            Settle();
    }

    /// <summary>Floats the space just under a topmost window (the one moving it), above every app.</summary>
    public void Lift(IntPtr under)
    {
        if (_disposed)
            return;
        _liftedUnder = under;
        const uint keep = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE;
        Win32.SetWindowPos(_source.Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, keep);
        Win32.SetWindowPos(_source.Handle, under, 0, 0, 0, 0, keep);
    }

    /// <summary>Back from <see cref="Lift"/>: into the normal band, on its app or at desktop level.</summary>
    public void Land()
    {
        if (_disposed || _liftedUnder == IntPtr.Zero)
            return;
        _liftedUnder = IntPtr.Zero;
        Win32.SetWindowPos(_source.Handle, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        Settle();
    }

    /// <summary>
    /// Keeps up with the app window (z-order, visibility, moves and resizes); cheap, so it runs on
    /// every display refresh, whether or not a frame is drawn.
    /// </summary>
    public void Track()
    {
        if (!_disposed)
            _following = FollowHost();
    }

    /// <summary>Draws the next animation frame, if it looks different (and the space is showing).</summary>
    public void RenderFrame(FrameState frame)
    {
        _lastFrame = frame;
        if (_disposed || !_following)
            return;
        bool filesMoved = AdvanceFiles(frame);
        if (_live is not null && IsReshaping)
        {
            // Renders and presents this frame at the new shape, as far into its wobble as it has got
            // (its ink too, scrolls and fade-ins included).
            _jelly.Step(double.IsNaN(_jellyTime) ? 0 : frame.Time - _jellyTime);
            _jellyTime = frame.Time;
            DrawLive();
            return;
        }
        if (_restWhenStill)
        {
            _restWhenStill = false;
            Rest(); // the drop's wobble is over: back in its own window
        }
        else if (_draft && _live is not null && frame.Time - _lastLive >= DraftPause)
        {
            DrawLive(draft: false); // the reshape paused: drawn properly while it holds still
            return;
        }
        if (filesMoved)
            RedrawInk(present: false);
        // The scenery moves on with the ambient animation (and faster only while a click splashes);
        // frames in between only show what the user is doing, such as paint.
        if ((frame.AmbientDue || _look.HasBursts || !_shown) && _look.Render(frame))
            _lookDirty = true;
        if (_lookDirty || _needsPresent || !_shown)
            Present();
    }

    /// <summary>
    /// Re-checks the z-order against the app right now (the app was just activated, restored or
    /// closed), instead of waiting for the next frame.
    /// </summary>
    public void Restack()
    {
        _minimizingSince = null;
        Track();
    }

    /// <summary>The app is starting to minimise: vanish now rather than after its animation.</summary>
    public void HideWithHost()
    {
        if (_disposed || !_shown || _hidden)
            return;
        Win32.ShowWindow(_source.Handle, Win32.SW_HIDE);
        _hidden = true;
        _minimizingSince = DateTime.UtcNow;
    }

    private void Present()
    {
        bool landed = false;
        if (_placementChanged)
        {
            bool wasFloating = _floating;
            EndFloat();
            Win32.SetWindowPos(_source.Handle, IntPtr.Zero, _position.X, _position.Y, _size.Width, _size.Height, PlaceFlags);
            landed = wasFloating;
            if (wasFloating)
                RestoreRegion();
            _placementChanged = false;
        }

        if (_lookDirty)
        {
            _look.Present(_surface);
            _lookDirty = false;
        }
        if (_ink is not null && !_inkDirty.IsEmpty)
        {
            _surface.ShowInk(_ink, _inkDirty);
            _inkDirty = Int32Rect.Empty;
        }
        _needsPresent = false;
        if (landed)
            _surface.CommitNow(); // back in its own window just now: the new picture goes with it

        if (!_shown)
        {
            Win32.ShowWindow(_source.Handle, Win32.SW_SHOWNOACTIVATE);
            const uint keep = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE;
            if (_previewUnder != IntPtr.Zero)
            {
                // Into the topmost band, then just below the overlay that drives the preview.
                Win32.SetWindowPos(_source.Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, keep);
                Win32.SetWindowPos(_source.Handle, _previewUnder, 0, 0, 0, 0, keep);
            }
            else
            {
                Settle();
            }
            _shown = true;
        }
    }

    /// <summary>
    /// Keeps the space directly above its app and in step with the app's rectangle; hides it while the
    /// app is minimised, hidden or on another virtual desktop, and reports when the app's window closes.
    /// </summary>
    /// <returns>false while the space is hidden with its app.</returns>
    private bool FollowHost()
    {
        if (_host == IntPtr.Zero)
            return true;
        if (_hostGone)
            return false; // hidden, waiting to be kept for later or put on the desktop

        if (!Win32.IsWindow(_host))
        {
            _hostGone = true;
            Hide();
            HostClosed?.Invoke(this);
            return false;
        }

        // Just told the app is minimising, but it may not report "iconic" yet: stay hidden meanwhile.
        bool minimizing = _minimizingSince is { } since && DateTime.UtcNow - since < TimeSpan.FromMilliseconds(500);
        if (!Win32.IsWindowVisible(_host) || Win32.IsIconic(_host) || minimizing || AppWindows.IsCloaked(_host))
        {
            Hide();
            return false;
        }

        // Moved or resized: the owner re-fits the space before it shows again.
        if (AppWindows.TryGetFrame(_host, out var frame) && frame != _hostFrame)
        {
            _hostFrame = frame;
            HostMoved?.Invoke(this, frame);
            if (_disposed)
                return false;
        }

        if (_hidden)
        {
            Win32.ShowWindow(_source.Handle, Win32.SW_SHOWNOACTIVATE);
            _hidden = false;
        }
        if (_shown && _liftedUnder == IntPtr.Zero && Win32.GetWindow(_host, Win32.GW_HWNDPREV) != _source.Handle)
            PlaceAboveHost();
        return true;
    }

    private void Hide()
    {
        if (_shown && !_hidden)
        {
            Win32.ShowWindow(_source.Handle, Win32.SW_HIDE);
            _hidden = true;
        }
    }

    /// <summary>Into its place in the z-order: just above its app, or at the bottom at desktop level.</summary>
    private void Settle()
    {
        if (_host != IntPtr.Zero)
            PlaceAboveHost();
        else
            Win32.SetWindowPos(_source.Handle, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void PlaceAboveHost()
    {
        // SetWindowPos puts us *below* the insert-after window, so insert after whatever is above the app.
        IntPtr above = Win32.GetWindow(_host, Win32.GW_HWNDPREV);
        bool aboveIsTopmost = above != IntPtr.Zero && (Win32.GetWindowLong(above, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) != 0;
        IntPtr insertAfter = above == IntPtr.Zero || aboveIsTopmost ? Win32.HWND_TOP : above;
        Win32.SetWindowPos(_source.Handle, insertAfter, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// The window takes the mouse only on the space (plus <see cref="HitMargin"/> for the rim);
    /// everywhere else clicks go to whatever is underneath.
    /// </summary>
    private void ApplyRegion()
    {
        _regionStale = false;
        if (_previewUnder != IntPtr.Zero)
            return; // the overlay above a preview takes all input anyway

        var pen = new Pen(Brushes.Black, HitMargin * 2) { LineJoin = PenLineJoin.Round };
        var rim = _outline.GetWidenedPathGeometry(pen, 0.5, ToleranceType.Absolute);
        var area = Geometry.Combine(_outline, rim, GeometryCombineMode.Union, null, 0.5, ToleranceType.Absolute)
            .GetFlattenedPathGeometry(0.5, ToleranceType.Absolute);

        var points = new List<Win32.POINT>();
        var counts = new List<int>();
        foreach (var figure in area.Figures)
        {
            int start = points.Count;
            void Add(Point p) => points.Add(new Win32.POINT
            {
                X = (int)Math.Round((p.X - _world.X) * Dpi.Scale),
                Y = (int)Math.Round((p.Y - _world.Y) * Dpi.Scale),
            });

            Add(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                    Add(line.Point);
                else if (segment is PolyLineSegment poly)
                    foreach (var p in poly.Points)
                        Add(p);
            }

            if (points.Count - start >= 3)
                counts.Add(points.Count - start);
            else
                points.RemoveRange(start, points.Count - start);
        }
        if (counts.Count == 0)
            return;

        IntPtr region = Win32.CreatePolyPolygonRgn([.. points], [.. counts], counts.Count, Win32.WINDING);
        if (region == IntPtr.Zero)
            return;
        // Kept, to put back after floating (see Rest): it is relative to the window, so moves don't change it.
        if (_region != IntPtr.Zero)
            Win32.DeleteObject(_region);
        _region = Copy(region);
        SetRegion(region);
    }

    /// <summary>The click-through shape back on the window after floating: made afresh if the shape changed meanwhile.</summary>
    private void RestoreRegion()
    {
        if (_regionStale)
            ApplyRegion();
        else if (_region != IntPtr.Zero)
            SetRegion(Copy(_region));
    }

    /// <summary>Hands a region to the window, which then owns it.</summary>
    private void SetRegion(IntPtr region)
    {
        if (region != IntPtr.Zero && Win32.SetWindowRgn(_source.Handle, region, redraw: true) == 0)
            Win32.DeleteObject(region);
    }

    private static IntPtr Copy(IntPtr region)
    {
        IntPtr copy = Win32.CreateRectRgn(0, 0, 0, 0);
        if (copy != IntPtr.Zero)
            Win32.CombineRgn(copy, region, IntPtr.Zero, Win32.RGN_COPY);
        return copy;
    }

    private InkLayer EnsureInk()
    {
        if (_ink is null)
        {
            _ink = new InkLayer(_gpu, _size.Width, _size.Height, Dpi.Scale, _world.TopLeft, _outline, Themes.InkOf(_theme)) { ThemeAccent = Themes.AccentOf(_theme) };
            DrawInk();
        }
        return _ink;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_WINDOWPOSCHANGING when _host == IntPtr.Zero && _previewUnder == IntPtr.Zero && _liftedUnder == IntPtr.Zero:
                // Desktop-level spaces stay on the bottom, whatever tries to raise them.
                var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
                pos.hwndInsertAfter = Win32.HWND_BOTTOM;
                pos.flags &= ~Win32.SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, fDeleteOld: false);
                break;

            case Win32.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Win32.MA_NOACTIVATE);

            case Win32.WM_SETCURSOR:
                Win32.SetCursor(EffectiveTool switch
                {
                    Tool.Pen => CrossCursor,
                    Tool.Text => IBeamCursor,
                    _ => InkUnderCursor() is null ? ArrowCursor : MoveCursor,
                });
                handled = true;
                return new IntPtr(1);

            case Win32.WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
            {
                var at = ToScreen(lParam);
                handled = true;
                if (msg == WM_LBUTTONDBLCLK && DoubleClick(at))
                    break;
                if (!Contains(at))
                    break;
                _pressedAt = at;
                switch (EffectiveTool)
                {
                    case Tool.Pen:
                        _gesture = Gesture.Pressed;
                        break;
                    case Tool.Text:
                        _gesture = Gesture.TextPressed;
                        break;
                    default:
                        HandDown(at);
                        break;
                }
                Win32.SetCapture(hwnd);
                break;
            }

            case Win32.WM_MOUSEMOVE when _gesture != Gesture.None:
            {
                var at = ToScreen(lParam);
                if (_gesture is Gesture.Pressed or Gesture.Painting)
                    Drag(at);
                else if (_gesture != Gesture.TextPressed)
                    HandDrag(at);
                handled = true;
                break;
            }

            case Win32.WM_LBUTTONUP:
            {
                var gesture = _gesture;
                _gesture = Gesture.None; // before ReleaseCapture, which sends WM_CAPTURECHANGED
                Win32.ReleaseCapture();
                switch (gesture)
                {
                    case Gesture.Painting:
                        FinishStroke();
                        break;
                    case Gesture.Pressed:
                        _look.AddBurst(_pressedAt); // a click with the pen only splashes
                        break;
                    case Gesture.TextPressed:
                        TypeRequested?.Invoke(this, _pressedAt);
                        break;
                    case Gesture.None:
                        break;
                    default:
                        HandUp(gesture);
                        break;
                }
                handled = true;
                break;
            }

            case WM_MOUSEWHEEL:
                Wheel?.Invoke(this, ScreenAt(lParam), (short)((wParam.ToInt64() >> 16) & 0xFFFF));
                handled = true;
                break;

            case WM_DROPFILES:
                OnDropFiles(wParam);
                handled = true;
                break;

            case Win32.WM_RBUTTONDOWN:
                handled = true; // the palette opens on release, like a context menu
                break;

            case Win32.WM_RBUTTONUP:
            {
                var at = ToScreen(lParam);
                if (_gesture == Gesture.None && Contains(at))
                    PaletteRequested?.Invoke(this, at);
                handled = true;
                break;
            }

            case Win32.WM_CAPTURECHANGED:
                if (_gesture == Gesture.Painting)
                    FinishStroke();
                else if (_gesture is Gesture.MovingSelection or Gesture.Marquee)
                    CancelHandDrag();
                _gesture = Gesture.None;
                break;
        }
        return IntPtr.Zero;
    }

    private void Drag(Point at)
    {
        if (_gesture == Gesture.Pressed)
        {
            if ((at - _pressedAt).Length < DragThreshold)
                return;
            _gesture = Gesture.Painting;
        }
        if (_stroke.Count == 0)
        {
            _strokePen = Pen;
            _strokeColor = InkColors.Resolve(_strokePen.Color, Themes.InkOf(_theme));
            _stroke = [_pressedAt];
            _inkDirty = InkLayer.Union(_inkDirty, EnsureInk().AddSegment(_pressedAt, _pressedAt, _strokePen.Thickness, _strokeColor));
            PaintingStarted?.Invoke(this);
        }

        var last = _stroke[^1];
        if ((at - last).Length < 1)
            return;
        _stroke.Add(at);
        _inkDirty = InkLayer.Union(_inkDirty, EnsureInk().AddSegment(last, at, _strokePen.Thickness, _strokeColor));
        _needsPresent = true; // shown on the next frame, at most one refresh away
    }

    private void FinishStroke()
    {
        if (_stroke.Count == 0)
            return;
        // Painted on screen; kept in the record's coordinates (they differ while the app is stretched).
        _record.Strokes.Add(InkStroke.FromScreen(_stroke, _toScreen, _strokePen));
        _stroke = [];
        InkChanged?.Invoke(this);
    }

    /// <summary>Client coordinates (device pixels, signed 16-bit each) to screen DIPs.</summary>
    private Point ToScreen(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        int x = (short)(value & 0xFFFF), y = (short)((value >> 16) & 0xFFFF);
        return new Point(_world.X + x / Dpi.Scale, _world.Y + y / Dpi.Scale);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _look.Dispose();
        _ink?.Dispose();
        _surface.Dispose();
        RefuseDrops();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        if (_region != IntPtr.Zero)
            Win32.DeleteObject(_region);
        _region = IntPtr.Zero;
    }
}
