using System.Diagnostics;
using System.IO;
using System.Windows;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>Text and Markdown files opened on spaces (see <see cref="NoteFile"/>).</summary>
internal sealed partial class SpaceManager
{
    /// <summary>Type sizes files are opened at: the pen's, kept readable.</summary>
    private const double SmallestFileType = 11, LargestFileType = 20;

    /// <summary>How far each further file dropped at once lands from the one before, in DIPs.</summary>
    private const double FileStep = 28;

    /// <summary>Something the user should hear about: a title and a line or two (a file that couldn't be opened).</summary>
    public event Action<string, string>? Notice;

    /// <summary>Whether a space is showing at a screen DIP point (files can be opened there).</summary>
    public bool HasSpaceAt(Point at) => SpaceAt(at) is not null;

    /// <summary>
    /// Opens text and Markdown files on the space at a screen DIP point, the first with its top-left
    /// there, each further one a step down and to the right.
    /// </summary>
    /// <returns>How many were opened.</returns>
    public int OpenFiles(IReadOnlyList<string> paths, Point at) =>
        SpaceAt(at) is { } space ? OpenFiles(space, paths, at) : 0;

    private Space? SpaceAt(Point at) => _spaces.LastOrDefault(s => s.Window.IsShowing && s.Window.Contains(at));

    private int OpenFiles(Space space, IReadOnlyList<string> paths, Point at)
    {
        var problems = new List<string>();
        var opened = new List<(TextNote Note, Point At)>();
        var bounds = space.Outline.Bounds;
        foreach (string path in paths)
        {
            string name = Path.GetFileName(path);
            if (!TextFiles.IsSupported(path))
            {
                problems.Add($"{name} is not a text (.txt) or Markdown (.md) file.");
                continue;
            }
            string text;
            try
            {
                text = TextFiles.Read(path).TrimEnd(); // as the text box keeps it, so opening it to edit changes nothing
            }
            catch (TextFileException ex)
            {
                problems.Add(ex.Message);
                continue;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                problems.Add($"{name} is empty.");
                continue;
            }

            var place = NextPlace(opened, at, bounds);
            var note = new TextNote
            {
                Text = text,
                FontSize = Math.Clamp(Pen.FontSize, SmallestFileType, LargestFileType),
                Color = Pen.Color,
                // As wide as the space leaves room for, within reason.
                Width = Math.Round(Math.Clamp(bounds.Right - place.X - 8, 200, NoteDocument.DefaultWidth), 2),
                File = new NoteFile { Path = path, Markdown = TextFiles.IsMarkdown(path) },
            };
            double first;
            double least;
            using (var document = NoteDocument.Build(note))
            {
                first = document.FirstHeight;
                least = document.HeaderHeight + 3 * note.FontSize * NoteText.LineSpacing;
                NoteText.Remember(note, document);
            }
            // All of it, or as much as the space leaves room for (the rest scrolls).
            note.File.Height = Math.Round(Math.Min(first, Math.Max(least, bounds.Bottom - place.Y - 8)), 2);

            // Placed on screen; kept in the record's coordinates (they differ while the app is stretched).
            var toRecord = space.ToScreen;
            if (toRecord.HasInverse)
                toRecord.Invert();
            var p = toRecord.Transform(place);
            (note.X, note.Y) = (Math.Round(p.X, 2), Math.Round(p.Y, 2));
            note.Order = InkOrder.Next();
            space.Record.Notes.Add(note);
            opened.Add((note, place));
        }

        if (opened.Count > 0)
        {
            space.Redo.Clear(); // new ink starts a new history branch
            space.Window.RedrawInk();
            foreach (var (note, place) in opened)
                space.Window.Arrive(note, place);
            Persist();
        }
        if (problems.Count > 0)
            Notice?.Invoke(problems.Count == 1 && opened.Count == 0 ? "That file wasn't opened" : "Some files weren't opened", string.Join("\n", problems));
        return opened.Count;
    }

    /// <summary>Longest text taken from a drop, in characters (as much as a file opened on a space).</summary>
    private const int MaxDroppedText = (int)TextFiles.MaxBytes;

    /// <summary>Room left between dropped text and its space's edges, in DIPs.</summary>
    private const double DropMargin = 12;

    /// <summary>The smallest type dropped text is shrunk to so that it fits its space.</summary>
    internal const double SmallestDroppedType = 10;

    /// <summary>Widest dropped text wraps at, in lines of type: about as long as lines read well.</summary>
    private const double DroppedLineEms = 26;

    /// <summary>
    /// Where text dropped at <paramref name="at"/> goes on a space with these bounds, and how big: the
    /// pen's type size, or smaller until every word fits across the space and every line fits down it.
    /// Lines wrap at the space's width (no wider than reads well). Its first line is centred on the
    /// drop point, moved left or up only as far as it takes to stay inside the space.
    /// </summary>
    /// <returns>Its top-left on screen (DIPs), type size and wrap width (null: its lines fit as they are).</returns>
    internal static (Point At, double FontSize, double? Width) FitDroppedText(string text, Rect space, Point at, double fontSize, bool bold, bool italic)
    {
        var area = space;
        area.Inflate(-DropMargin, -DropMargin);
        if (area.Width < 40 || area.Height < 20)
            area = space; // a tiny space: all of it

        // Lays the text out at a size: its wrap width, and the box it takes up.
        (double? Width, Size Box) Lay(double size)
        {
            double widest = Math.Min(area.Width, Math.Max(NoteDocument.DefaultWidth, DroppedLineEms * size));
            using (var unwrapped = NoteText.Layout(text, size, null, bold, italic))
            {
                var m = unwrapped.Metrics;
                if (m.Width <= widest)
                    return (null, new Size(m.Width, m.Height));
            }
            using var wrapped = NoteText.Layout(text, size, widest, bold, italic);
            var metrics = wrapped.Metrics;
            return (widest, new Size(Math.Max(metrics.Width, widest), metrics.Height)); // a word longer than a line runs over
        }
        bool Fits((double? Width, Size Box) laid) => laid.Box.Width <= area.Width + 0.5 && laid.Box.Height <= area.Height + 0.5;

        double best = Math.Max(SmallestDroppedType, Math.Min(fontSize, InkPen.MaxFontSize));
        var laid = Lay(best);
        if (!Fits(laid) && best > SmallestDroppedType)
        {
            // The largest size that fits, in half points, found by halving the range.
            double low = SmallestDroppedType, high = best;
            var lowLaid = Lay(low);
            if (Fits(lowLaid))
            {
                while (high - low > 0.5)
                {
                    double middle = Math.Round(low + high) / 2; // halfway, to a half point
                    if (middle <= low || middle >= high)
                        break;
                    var middleLaid = Lay(middle);
                    if (Fits(middleLaid))
                        (low, lowLaid) = (middle, middleLaid);
                    else
                        high = middle;
                }
            }
            (best, laid) = (low, lowLaid); // at the smallest size, even if it doesn't fit: the rest is cut off at the edge
        }

        var box = laid.Box;
        double line = best * NoteText.LineSpacing;
        double x = Math.Max(area.Left, Math.Min(at.X, area.Right - box.Width));
        double y = Math.Max(area.Top, Math.Min(at.Y - line / 2, area.Bottom - box.Height));
        return (new Point(Math.Round(x, 2), Math.Round(y, 2)), best, laid.Width is { } width ? Math.Round(width, 2) : null);
    }

    /// <summary>
    /// Text dragged from another app was dropped on a space: a note in the pen's colour and style, as
    /// big as the pen's type or as fits the space (see <see cref="FitDroppedText"/>).
    /// </summary>
    private void DropText(Space space, string text, Point at)
    {
        if (!_spaces.Contains(space))
            return;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "").TrimEnd();
        if (text.Trim().Length == 0)
            return;
        if (text.Length > MaxDroppedText)
        {
            Notice?.Invoke("That text wasn't dropped", $"It is {text.Length / 1024} K characters; up to {MaxDroppedText / 1024} K fit on a space.");
            return;
        }

        var fit = FitDroppedText(text, space.Outline.Bounds, at, Pen.FontSize, Pen.Bold, Pen.Italic);
        var place = fit.At;
        var note = new TextNote { Text = text, FontSize = fit.FontSize, Color = Pen.Color, Width = fit.Width, Bold = Pen.Bold, Italic = Pen.Italic };

        var toRecord = space.ToScreen;
        if (toRecord.HasInverse)
            toRecord.Invert();
        var p = toRecord.Transform(place);
        (note.X, note.Y) = (Math.Round(p.X, 2), Math.Round(p.Y, 2));
        note.Order = InkOrder.Next();
        space.Record.Notes.Add(note);
        space.Redo.Clear();
        space.Window.RedrawInk();
        space.Window.Arrive(note, at);
        Persist();
    }

    /// <summary>
    /// Where the next of several files dropped at once goes (screen DIPs): beside the one before while
    /// the space has room for it, else a step down and to the right of the first.
    /// </summary>
    private static Point NextPlace(List<(TextNote Note, Point At)> opened, Point first, Rect bounds)
    {
        if (opened.Count == 0)
            return first;
        var (last, lastAt) = opened[^1];
        var beside = new Point(lastAt.X + (last.Width ?? NoteDocument.DefaultWidth) + 2 * FileStep, lastAt.Y);
        if (beside.X + 200 <= bounds.Right)
            return beside;
        return first + new Vector(FileStep, FileStep) * opened.Count;
    }

    /// <summary>The wheel over an opened file scrolls it, a few lines a notch (as the system's setting says).</summary>
    private void OnWheel(Space space, Point at, int delta)
    {
        if (FileAt(space, at) is not { File: { } file } note)
            return;
        double line = note.FontSize * NoteText.LineSpacing;
        int lines = SystemParameters.WheelScrollLines;
        double step = lines > 0 ? lines * line : file.Height * 0.8; // "one screen at a time"
        double max = NoteText.MaxScroll(note);
        double target = Math.Clamp(space.Window.ScrollTarget(note) - delta / 120.0 * step, 0, max);
        space.Window.ScrollTo(note, Math.Round(target, 2));
    }

    /// <summary>
    /// A double-click on an opened file's name opens the file itself (in its own app); a Ctrl+click
    /// on a link in it opens the link in the browser.
    /// </summary>
    /// <returns>Whether it meant something there.</returns>
    private bool OnNoteClicked(Space space, Point at, NoteClick click)
    {
        if (FileAt(space, at) is not { File: { } file } note)
            return false;
        var corner = space.ToScreen.Transform(new Point(note.X, note.Y));
        var inNote = new Point(at.X - corner.X, at.Y - corner.Y);
        using var document = NoteDocument.Build(note);
        if (click == NoteClick.DoubleClick)
        {
            if (!document.IsOnHeader(inNote))
                return false;
            if (!File.Exists(file.Path))
            {
                Notice?.Invoke("The file is gone", $"{file.Path} was moved or deleted. What was opened stays on the space.");
                return true;
            }
            Launch(file.Path);
            return true;
        }

        if (document.LinkAt(inNote, Math.Clamp(file.Scroll, 0, document.MaxScroll(file.Height))) is not { } url)
            return false;
        Launch(url);
        return true;
    }

    /// <summary>The newest opened file on a space whose shown area is at a screen DIP point.</summary>
    private static TextNote? FileAt(Space space, Point at)
    {
        TextNote? found = null;
        foreach (var note in space.Record.Notes)
        {
            if (note.File is null || note.Text.Length == 0 || (found is not null && note.Order < found.Order))
                continue;
            if (NoteText.Bounds(note, space.ToScreen).Contains(at))
                found = note;
        }
        return found;
    }

    /// <summary>Opens a file or web address with whatever Windows opens it with.</summary>
    private void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Notice?.Invoke("Couldn't open it", ex.Message);
        }
    }
}
