using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>Screen notes: paint and text on an app window (or the desktop) with no space under them.</summary>
internal sealed partial class SpaceManager
{
    /// <summary>How many sheets of screen notes there are, kept ones included.</summary>
    public int SheetCount => _spaces.Count(s => s.IsSheet) + _kept.Count(r => r.Kind == SpaceKind.Sheet);

    /// <summary>The colour ink takes by default at a screen point: a space's theme ink, or screen notes' red.</summary>
    public Color InkColorAt(Point at) => SpaceAt(at) is null ? Themes.SheetInk : Themes.InkOf(Theme);

    /// <summary>
    /// A stroke drawn on the screen (screen DIPs): it goes onto the space it starts on, else onto the
    /// screen notes of the app window under its start (or the desktop's).
    /// </summary>
    /// <returns>The window it went to (for undo), or zero when it was too short to keep.</returns>
    public IntPtr AddInk(IReadOnlyList<Point> points, InkPen pen)
    {
        if (points.Count < 2)
            return IntPtr.Zero;
        var space = SpaceAt(points[0]) ?? SheetAt(points[0]);
        space.Record.Strokes.Add(InkStroke.FromScreen(points, space.ToScreen, pen));
        space.Redo.Clear();
        ShowInk(space);
        Persist();
        return space.Window.Handle;
    }

    /// <summary>The text tool on the screen: a text box on the space at the point, else on the screen notes there.</summary>
    public void TypeAt(Point at) => BeginText(SpaceAt(at) ?? SheetAt(at), at, editing: null);

    /// <summary>Takes away every sheet of screen notes, including those waiting for their app.</summary>
    public void ClearSheets()
    {
        foreach (var sheet in _spaces.Where(s => s.IsSheet).ToList())
        {
            CloseWindow(sheet);
            _spaces.Remove(sheet);
        }
        _kept.RemoveAll(r => r.Kind == SpaceKind.Sheet);
        Persist();
    }

    /// <summary>The screen notes of the app window at a point (made if there are none yet).</summary>
    private Space SheetAt(Point at)
    {
        IntPtr host = AppWindows.WindowAt(at); // skips this app's own windows, the overlay included
        return _spaces.FirstOrDefault(s => s.IsSheet && s.Window.Host == host) ?? CreateSheet(host, at);
    }

    private Space CreateSheet(IntPtr host, Point at)
    {
        // A speck of a shape for now; it grows to cover its ink (FitSheet).
        var record = new SpaceRecord
        {
            Kind = SpaceKind.Sheet,
            Host = Describe(host),
            Figures = [[at.X - 1, at.Y - 1, at.X + 1, at.Y - 1, at.X + 1, at.Y + 1, at.X - 1, at.Y + 1]],
        };
        return Open(record, host, Matrix.Identity);
    }

    /// <summary>The space's ink changed: drawn again (screen notes first grow or shrink to cover their ink).</summary>
    private void ShowInk(Space space)
    {
        if (space.IsSheet)
            FitSheet(space);
        else if (space.Record.Card)
            FitCard(space);
        else
            space.Window.RedrawInk();
    }

    /// <summary>Screen notes take the shape of their ink (plus a margin), drawn again at once.</summary>
    private static void FitSheet(Space sheet)
    {
        RefitSheet(sheet);
        sheet.Stretched = false;
        sheet.Shown = sheet.ToScreen;
        sheet.Window.SetShape(sheet.Outline, sheet.ToScreen); // also redraws the ink and remakes where it takes clicks
    }

    /// <summary>
    /// Rewrites a sheet's outline to cover its ink as shown now. Its paint keeps its size when its app
    /// is resized, so the outline is worked out on screen and mapped back, not scaled.
    /// </summary>
    private static void RefitSheet(Space sheet)
    {
        if (Sheets.Bounds(sheet.Record, sheet.ToScreen) is not { } screen)
            return; // no ink: it keeps its shape, shows nothing and takes no clicks
        var toRecord = sheet.ToScreen;
        if (toRecord.HasInverse)
            toRecord.Invert();
        var r = Rect.Transform(screen, toRecord);
        sheet.Record.Figures = [[Math.Round(r.Left, 2), Math.Round(r.Top, 2), Math.Round(r.Right, 2), Math.Round(r.Top, 2), Math.Round(r.Right, 2), Math.Round(r.Bottom, 2), Math.Round(r.Left, 2), Math.Round(r.Bottom, 2)]];
        sheet.Record.FillRule = FillRule.Nonzero;
        sheet.Shape = SpaceGeometry.Decode(sheet.Record);
    }

    /// <summary>Screen notes made for a text box that was cancelled: nothing to keep.</summary>
    private void DropEmptySheet(Space space)
    {
        if (!space.IsSheet || !_spaces.Contains(space) || space.Record.Strokes.Count > 0 || space.Record.Notes.Count > 0 || space.Redo.Count > 0)
            return;
        CloseWindow(space);
        _spaces.Remove(space);
    }
}
