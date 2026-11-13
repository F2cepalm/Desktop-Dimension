using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging;

namespace InfiniteSpace.Shell;

/// <summary>Wires hotkeys and the tray icon to the draw/erase overlay and the spaces.</summary>
internal sealed partial class ShellController(SpaceManager spaces, SpaceStore store, Universe universe, ILogger<ShellController> logger) : IDisposable
{
    /// <summary>How long after the mouse last moved T and E still belong to the space under it.</summary>
    internal static readonly TimeSpan LetterKeysAfterMouseMove = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _hoverTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly CursorActivity _cursor = new(LetterKeysAfterMouseMove);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private GlobalHotkeys? _hotkeys;
    private WindowEvents? _windowEvents;
    private TrayIcon? _tray;
    private GpuOverlay? _overlay;
    private InkPaletteWindow? _palette;
    private FrameStatsWindow? _frameStats;
    private AppSettings _settings = new();
    private HoverKey[] _undoKeys = [];

    public void Start()
    {
        _settings = AppSettings.Load(store.DataDirectory);
        ApplyAutoStart();
        spaces.ApplyTheme(Themes.Resolve(_settings.Theme)); // before restoring, so spaces open in it
        spaces.SetPen(_settings.Pen);
        spaces.PenChanged += pen =>
        {
            _settings.Pen = pen;
            SaveSettings();
        };
        spaces.PaletteRequested += ShowPalette;
        spaces.Notice += (title, text) => _tray?.Notify(title, text);
        spaces.PaintingStarted += () => _palette?.FadeOut();
        spaces.RestoreAll();

        _tray = new TrayIcon(new TrayActions(
            Wheel: ToggleWheel,
            WheelHotkey: () => _wheelHotkey,
            Draw: () => { IntPtr host = Foreground.CaptureHost(); ShowOverlay(new CanvasOverlay(OverlayMode.Draw, spaces, universe, host), host); },
            Block: () => { IntPtr host = Foreground.CaptureHost(); ShowOverlay(new BlockOverlay(spaces, universe, host), host); },
            Move: () => ShowOverlay(new MoveOverlay(spaces, universe), IntPtr.Zero),
            Remove: RemoveSpace,
            Ink: () => ShowPalette(CursorOnScreen()),
            EraseWithBrush: () => ShowOverlay(new CanvasOverlay(OverlayMode.Erase, spaces, universe, IntPtr.Zero), IntPtr.Zero),
            EraseAll: ConfirmEraseAll,
            DrawOnScreen: () => SelectTool(Tool.Pen, ToolSource.Toolbar),
            ClearScreenNotes: ConfirmClearScreenNotes,
            Themes: Themes.All,
            CurrentTheme: () => Themes.Resolve(_settings.Theme),
            SetTheme: SetTheme,
            IsAutoStartEnabled: () => AutoStart.IsEnabled,
            SetAutoStart: SetAutoStart,
            IsToolbarShown: () => _settings.ShowToolbar,
            ShowToolbar: SetToolbarShown,
            OpenDataFolder: OpenDataFolder,
            Exit: () => Application.Current.Shutdown()));

        if (_settings.ShowToolbar)
            OpenToolbar();
        spaces.ToolChanged += tool => _toolbar?.ShowTool(tool);

        _hotkeys = new GlobalHotkeys();
        var unavailable = new List<string>();
        if (_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.S, ToggleDraw) is null)
            unavailable.Add("Ctrl+Alt+S");
        if (_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.B, ToggleBlock) is null)
            unavailable.Add("Ctrl+Alt+B");
        if (_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.M, ToggleMove) is null)
            unavailable.Add("Ctrl+Alt+M");
        if (_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.E, RemoveSpace) is null)
            unavailable.Add("Ctrl+Alt+E");
        if (_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.F, SaveSpaceUnderMouse) is null)
            unavailable.Add("Ctrl+Alt+F");
        RegisterWheelHotkey(unavailable);

        _undoKeys =
        [
            new(ModifierKeys.Control, Key.Z, () => spaces.Undo(spaces.SpaceUnderCursor()), repeat: true),
            new(ModifierKeys.Control, Key.Y, () => spaces.Redo(spaces.SpaceUnderCursor()), repeat: true),
        ];
        _toolKeys = [.. ToolCatalog.All
            .Where(t => t.Key is not null && t.Tool != Tool.TextWrap) // W is text wrap's only after a selection
            .Select(t => new HoverKey(ModifierKeys.None, t.Key!.Value, () => SelectTool(t.Tool, ToolSource.SpaceKey)))];
        _wrapKey = new(ModifierKeys.None, Key.W, () => WrapSelection(fromKey: true));
        _groupKeys =
        [
            new(ModifierKeys.Control, Key.G, () => spaces.GroupUnderCursor(group: true)),
            new(ModifierKeys.Control | ModifierKeys.Shift, Key.G, () => spaces.GroupUnderCursor(group: false)),
        ];
        _hoverTimer.Tick += (_, _) => UpdateHoverKeys();
        _hoverTimer.Start();
        _windowEvents = new WindowEvents(spaces.OnWindowEvent);
        if (_settings.ShowFrameStats)
            _frameStats = new FrameStatsWindow(universe);

        if (unavailable.Count > 0)
        {
            logger.LogWarning("Hotkeys already taken by another app: {Hotkeys}", unavailable);
            _tray.Notify("Hotkey unavailable", $"{string.Join(" and ", unavailable)} is taken by another app. Use the tray menu instead.");
        }
        else if (spaces.Count == 0)
        {
            _tray.Notify("Desktop_Dimension is running", $"Press {_wheelHotkey ?? "Ctrl+Alt+S"} for tools, or Ctrl+Alt+S and draw any shape to open a space.");
        }
    }

    /// <summary>Ctrl+Alt+S: draw a space over the app in front, or close the drawing overlay if it is open.</summary>
    private void ToggleDraw()
    {
        FadeInkTools(); // a text box hands focus back to its app first
        // Read before the overlay takes focus: this is the app the space will sit on.
        IntPtr host = Foreground.CaptureHost();
        if (CloseOverlay() is CanvasOverlay { Mode: OverlayMode.Draw })
            return;
        ShowOverlay(new CanvasOverlay(OverlayMode.Draw, spaces, universe, host), host);
    }

    /// <summary>Ctrl+Alt+B: spawn a block under the mouse over the app in front, or cancel it if it is open.</summary>
    private void ToggleBlock()
    {
        FadeInkTools(); // a text box hands focus back to its app first
        IntPtr host = Foreground.CaptureHost();
        if (CloseOverlay() is BlockOverlay)
            return;
        ShowOverlay(new BlockOverlay(spaces, universe, host), host);
    }

    /// <summary>Ctrl+Alt+M: pick up the space under the mouse to move it, or put it back if it is being moved.</summary>
    private void ToggleMove()
    {
        FadeInkTools(); // a text box hands focus back to its app first
        IntPtr host = Foreground.CaptureHost();
        if (CloseOverlay() is MoveOverlay)
            return;
        ShowOverlay(new MoveOverlay(spaces, universe), host);
    }

    /// <returns>The overlay that was open, if any.</returns>
    private GpuOverlay? CloseOverlay()
    {
        var open = _overlay;
        open?.Dismiss();
        if (open is not null)
            ToolCursors.RefreshUnderMouse(); // its cursor would stay until the mouse moved
        return open;
    }

    /// <param name="host">Gets focus back when the overlay closes.</param>
    /// <param name="endsInHand">Closing it returns to the Hand (a screen-wide tool).</param>
    private void ShowOverlay(GpuOverlay overlay, IntPtr host, bool endsInHand = false)
    {
        _wheel?.Dismiss();
        _overlay?.Dismiss();
        FadeInkTools(); // drawing, erasing or moving now
        overlay.Closed += (_, _) =>
        {
            if (_overlay == overlay)
            {
                _overlay = null;
                if (endsInHand)
                    spaces.SetTool(Tool.Hand); // every screen-wide tool ends in the hand
            }
            Foreground.RestoreTo(host);
            spaces.Restack(); // the app just came to the front: its spaces go straight back on top of it
        };
        _overlay = overlay;
        overlay.Show(); // on top, with the keyboard
        _toolbar?.BringToFront(); // above the overlay, so a tool can still be picked
    }

    /// <summary>
    /// Keys that belong to a space while the mouse is on it, and to the focused app the rest of the
    /// time: they are registered as hotkeys only while hovering. Over a space: Ctrl+Z / Ctrl+Y (undo,
    /// redo), H, P, T, C, E (pick a tool), and Ctrl+G / Ctrl+Shift+G while it has a selection. Over screen
    /// notes' ink, Ctrl+Z / Ctrl+Y and the group keys. None while an overlay or a text box is open, so the
    /// keys type as usual there.
    /// </summary>
    /// <remarks>
    /// The letters, the group keys, and anything over screen notes are only taken while the mouse moved
    /// lately (see <see cref="LetterKeysAfterMouseMove"/>): pointing and pressing works, but typing into
    /// the app underneath with the mouse resting there keeps its keys.
    /// </remarks>
    private void UpdateHoverKeys()
    {
        if (_hotkeys is null)
            return;
        bool moved = Win32.GetCursorPos(out var p) && _cursor.Sample(new Point(p.X, p.Y), _clock.Elapsed);
        _selection.Sample((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) != 0, new Point(p.X, p.Y), _clock.Elapsed, () => OnAppText(p));
        _wrapKey?.Hold(_hotkeys, !_wrapping && _overlay is null && _wheel is null && !spaces.IsTyping && _selection.IsRecent(_clock.Elapsed));
        var hover = _overlay is null && _wheel is null && !spaces.IsTyping ? spaces.HoverAtCursor() : SpaceManager.Hover.None;
        bool onSpace = hover is SpaceManager.Hover.Space or SpaceManager.Hover.Note;
        // Over screen notes' ink the mouse may only be resting there: the app keeps its keys then, as for the letters.
        bool onInk = onSpace || (moved && hover == SpaceManager.Hover.Sheet);
        foreach (var key in _undoKeys)
            key.Hold(_hotkeys, onInk);
        foreach (var key in _toolKeys)
            key.Hold(_hotkeys, moved && onSpace);
        bool selection = moved && hover != SpaceManager.Hover.None && spaces.HasSelectionUnderCursor();
        foreach (var key in _groupKeys)
            key.Hold(_hotkeys, selection);
    }

    /// <summary>
    /// The user got on with painting, erasing or drawing: the ink palette fades away, and a text box
    /// keeps what was typed and fades away too. Both hand focus back to the app at once.
    /// </summary>
    private void FadeInkTools()
    {
        _palette?.FadeOut();
        spaces.CommitTyping(fade: true);
    }

    /// <summary>The ink palette (colour, paint width, text size) at a screen DIP point; its picks apply at once.</summary>
    private void ShowPalette(Point at)
    {
        _palette?.Dismiss();
        spaces.CommitTyping();
        IntPtr previousApp = Foreground.CaptureHost(); // not the taskbar, when opened from the tray
        // On a space, the palette also opens files there.
        Action? openFile = spaces.HasSpaceAt(at) ? () => OpenFilesAt(at) : null;
        var palette = new InkPaletteWindow(at, spaces.Pen, Themes.InkOf(spaces.Theme), spaces.SetPen, openFile);
        palette.Gone += () => // closed, or fading away: done with either way
        {
            if (_palette == palette)
                _palette = null;
            Foreground.WhenMouseUp(() => // mid-stroke, focus stays put until the stroke is done
            {
                Foreground.RestoreTo(previousApp);
                spaces.Restack(); // the app just came to the front: its spaces go straight back on top of it
            });
        };
        _palette = palette;
        palette.Show();
        palette.Activate();
    }

    /// <summary>"Open a file…" on the palette: text and Markdown files picked in the file dialog go on the space at <paramref name="at"/>.</summary>
    private void OpenFilesAt(Point at)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open on the space",
            Filter = "Text and Markdown files|*.txt;*.md;*.markdown;*.mdown;*.mkd;*.text;*.log|All files|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() == true)
            spaces.OpenFiles(dialog.FileNames, at);
    }

    private static Point CursorOnScreen() =>
        Win32.GetCursorPos(out var p)
            ? new Point(p.X / Dpi.Scale, p.Y / Dpi.Scale)
            : new Point(SystemParameters.PrimaryScreenWidth / 2, SystemParameters.PrimaryScreenHeight / 2);

    /// <summary>One press, one whole space gone: the one under the cursor, else the newest.</summary>
    private void RemoveSpace()
    {
        _overlay?.Dismiss();
        FadeInkTools();
        if (!Win32.GetCursorPos(out var cursor))
            return;
        spaces.RemoveAt(new Point(cursor.X / Dpi.Scale, cursor.Y / Dpi.Scale));
    }

    /// <summary>Re-dresses every space in the chosen theme and remembers it.</summary>
    private void SetTheme(string name)
    {
        _settings.Theme = Themes.Resolve(name);
        spaces.ApplyTheme(_settings.Theme);
        SaveSettings();
        _toolbar?.ApplyChrome(Themes.ChromeOf(_settings.Theme));
    }

    /// <summary>Start with Windows is on unless the user turned it off.</summary>
    private void ApplyAutoStart()
    {
        if (_settings.StartWithWindows == false)
            return;
        // Re-registering every start keeps the entry pointing at this exe if it moved.
        if (TrySetAutoStart(true) && _settings.StartWithWindows is null)
        {
            _settings.StartWithWindows = true;
            SaveSettings();
        }
    }

    private void SetAutoStart(bool enabled)
    {
        if (!TrySetAutoStart(enabled))
            return;
        _settings.StartWithWindows = enabled;
        SaveSettings();
    }

    private bool TrySetAutoStart(bool enabled)
    {
        try
        {
            AutoStart.Set(enabled);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            logger.LogWarning(ex, "Could not change Start with Windows");
            return false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(store.DataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save settings");
        }
    }

    private void ConfirmEraseAll()
    {
        if (spaces.Count == 0)
            return;

        FadeInkTools();
        var answer = MessageBox.Show(
            $"Erase all {spaces.Count} spaces and screen notes? This cannot be undone.",
            "Desktop_Dimension",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
            spaces.EraseAll();
    }

    private void ConfirmClearScreenNotes()
    {
        if (spaces.SheetCount == 0)
            return;

        FadeInkTools();
        var answer = MessageBox.Show(
            "Clear all screen notes? This cannot be undone.",
            "Desktop_Dimension",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
            spaces.ClearSheets();
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(store.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{store.DataDirectory}\"") { UseShellExecute = true });
    }

    public void Dispose()
    {
        _hoverTimer.Stop();
        _frameStats?.Dispose();
        _windowEvents?.Dispose();
        _wheel?.Dismiss();
        _overlay?.Dismiss();
        _palette?.Dismiss();
        _toolbar?.Close();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        // While the UI thread still runs: windows must be destroyed on it.
        spaces.Dispose();
        universe.Dispose();
    }

    /// <summary>A key combination that is a hotkey only while held (see <see cref="UpdateHoverKeys"/>).</summary>
    private sealed class HoverKey(ModifierKeys modifiers, Key key, Action handler, bool repeat = false)
    {
        private bool _held;
        private int? _id;

        /// <summary>Registers or gives back the combination; a combination another app owns is simply not held.</summary>
        public void Hold(GlobalHotkeys hotkeys, bool hold)
        {
            if (hold == _held)
                return;
            _held = hold;
            if (hold)
            {
                _id = hotkeys.Register(modifiers, key, handler, repeat);
            }
            else if (_id is { } id)
            {
                hotkeys.Unregister(id);
                _id = null;
            }
        }
    }
}
