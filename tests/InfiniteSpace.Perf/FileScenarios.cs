using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using InfiniteSpace.Native;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Perf;

/// <summary>Text and Markdown files dropped on a space from Explorer, then scrolled with the wheel.</summary>
internal static class FileScenarios
{
    private const int WM_DROPFILES = 0x0233, WM_MOUSEWHEEL = 0x020A;

    public const string Sample = """
        # Plans for the week

        Some **bold** words, some *italic* ones, a bit of `inline code`, ~~a crossed-out idea~~ and a
        [link to the web](https://example.com). A <b>raw tag</b> stays text.

        ## Lists

        - First thing to do
        - Second thing, with a longer line that wraps onto the next one when the note is narrow
          - A nested point
          - Another one
        - [x] A task that is done
        - [ ] A task still open

        1. Numbered
        2. Items

        > A quote, set off with a bar at its side.

        ```
        var answer = 42;
        Console.WriteLine(answer);
        ```

        ---

        ### The end

        One more paragraph so that there is something to scroll to, and then another line or two
        to make it longer than the note shows at first.
        """;

    /// <summary>Two files dropped at once (one Markdown, one long plain text), then the first scrolled down and back.</summary>
    public static IEnumerable<Result> DropAndScroll(string theme, Options options)
    {
        string folder = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Perf", "files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string markdown = Path.Combine(folder, "plans.md");
        string plain = Path.Combine(folder, "log.txt");
        File.WriteAllText(markdown, Sample);
        File.WriteAllText(plain, string.Join("\n", Enumerable.Range(1, 4000).Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog.")));

        var bounds = new Rect(Bench.X, Bench.Y, 900, 620);
        using var bench = new Bench(theme, [Bench.Record(bounds, ink: false)]);
        var window = bench.Windows[0];
        var at = new Point(bounds.X + 30, bounds.Y + 30);

        var drop = new FrameTimer("files: dropped (two)", theme, bench.Gpu);
        drop.Frame(() =>
        {
            Drop(window.Handle, [markdown, plain], at);
            bench.Universe.Tick();
        });
        var arrive = new FrameTimer("files: fading in", theme, bench.Gpu);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 700)
            arrive.Frame(bench.Universe.Tick);
        options.Picture(window.Handle, $"files-{theme}");

        // Scroll the Markdown file: a notch per 40 ms, down then back up, a frame every refresh.
        var scroll = new FrameTimer("files: wheel scrolling", theme, bench.Gpu);
        var over = at + new Vector(150, 200);
        for (int notch = 0; notch < 16; notch++)
        {
            Wheel(window.Handle, over, notch < 8 ? -120 : 120);
            var step = System.Diagnostics.Stopwatch.StartNew();
            while (step.ElapsedMilliseconds < 40)
                scroll.Frame(bench.Universe.Tick);
            if (notch == 4)
                options.Picture(window.Handle, $"files-scrolled-{theme}");
        }
        Ui.Pump(300);

        var saved = bench.Store.Load().Single();
        Console.WriteLine($"  {(saved.Notes.Count == 2 && saved.Notes.All(n => n.File is not null) ? "ok  " : "FAIL")} both files opened and saved ({saved.Notes.Count} notes)");
        Console.WriteLine($"  {(saved.Notes.All(n => n.Text.Length > 100) ? "ok  " : "FAIL")} their text came back from the notes folder");
        Directory.Delete(folder, recursive: true);
        return [drop.Finish(), arrive.Finish(), scroll.Finish()];
    }

    /// <summary>A file of nearly a megabyte (the most that opens), dropped and then scrolled a long way.</summary>
    public static IEnumerable<Result> BigFile(string theme, Options options)
    {
        string folder = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Perf", "big-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "big.md");
        var text = new StringBuilder();
        for (int i = 0; text.Length < TextFiles.MaxBytes - 4096; i++)
            text.Append(i % 12 == 0 ? $"\n## Part {i / 12}\n\n" : $"Paragraph {i} with **some bold**, *some italic* and `code`, long enough to wrap over two or three lines at the note's width.\n\n");
        File.WriteAllText(path, text.ToString());

        var bounds = new Rect(Bench.X, Bench.Y, 700, 620);
        using var bench = new Bench(theme, [Bench.Record(bounds, ink: false)]);
        var window = bench.Windows[0];
        var at = new Point(bounds.X + 30, bounds.Y + 30);
        var drop = new FrameTimer("big file: dropped (1 MB)", theme, bench.Gpu);
        drop.Frame(() =>
        {
            Drop(window.Handle, [path], at);
            bench.Universe.Tick();
        });
        Ui.Pump(500);

        var scroll = new FrameTimer("big file: wheel scrolling", theme, bench.Gpu);
        var over = at + new Vector(150, 200);
        for (int notch = 0; notch < 40; notch++)
        {
            Wheel(window.Handle, over, notch < 30 ? -600 : 360); // five notches at a time: far down, then partly back
            var step = System.Diagnostics.Stopwatch.StartNew();
            while (step.ElapsedMilliseconds < 30)
                scroll.Frame(bench.Universe.Tick);
        }
        options.Picture(window.Handle, $"bigfile-{theme}");
        Directory.Delete(folder, recursive: true);
        return [drop.Finish(), scroll.Finish()];
    }

    /// <summary>
    /// Text dragged in from another app (through the space's OLE drop target), then that note and an
    /// opened file each dragged across the space with the mouse, as a user moves them.
    /// </summary>
    public static IEnumerable<Result> DragNotes(string theme, Options options)
    {
        const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        string folder = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Perf", "notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string markdown = Path.Combine(folder, "plans.md");
        File.WriteAllText(markdown, Sample);

        var bounds = new Rect(Bench.X, Bench.Y, 900, 620);
        using var bench = new Bench(theme, [Bench.Record(bounds, ink: false)]);
        var window = bench.Windows[0];
        var record = bench.Spaces.Records.Single();
        Check(window.Drops is not null, "the space takes drags from other apps");

        // Text from a browser or an editor.
        const string Dragged = "Dragged in from another app: a sentence long enough to wrap once it is on the space.";
        var textAt = new Point(bounds.X + 480, bounds.Y + 60);
        int effect = 3; // copy or move allowed
        var data = new DataObject(DataFormats.UnicodeText, Dragged);
        var screen = new Win32.POINT { X = (int)Math.Round(textAt.X * Dpi.Scale), Y = (int)Math.Round(textAt.Y * Dpi.Scale) };
        window.Drops!.DragEnter(data, 0, screen, ref effect);
        Check(effect == 1, "dragged text is taken as a copy");
        effect = 3;
        window.Drops.Drop(data, 0, screen, ref effect);
        Ui.Pump(700); // past the Dreamy wobble: the space is back in its own window
        var typed = record.Notes.SingleOrDefault(n => n.File is null);
        Check(typed?.Text == Dragged && typed.Width is not null, "dropped text became a wrapped note");

        Drop(window.Handle, [markdown], new Point(bounds.X + 30, bounds.Y + 30));
        bench.Universe.Tick();
        Ui.Pump(700);

        var results = new List<Result>();
        foreach (var (name, pick, grab) in new (string, Func<TextNote, bool>, Point)[]
        {
            ("text", n => n.File is null, textAt),
            ("file", n => n.File is not null, new Point(bounds.X + 150, bounds.Y + 120)),
        })
        {
            var before = record.Notes.Single(pick);
            var timer = new FrameTimer($"notes: {name} dragged", theme, bench.Gpu);
            Ui.Mouse(window.Handle, WM_LBUTTONDOWN, grab, leftDown: true);
            var at = grab;
            for (int i = 1; i <= 120; i++)
            {
                at = grab + new Vector(i * 1.5, i * 0.75); // 180 × 90 DIPs in all
                timer.Frame(() =>
                {
                    Ui.Mouse(window.Handle, WM_MOUSEMOVE, at, leftDown: true);
                    bench.Universe.Tick();
                });
            }
            Ui.Mouse(window.Handle, WM_LBUTTONUP, at);
            results.Add(timer.Finish());
            var after = record.Notes.Single(pick);
            // With the hand, opened files move like any note.
            Check(after != before && Math.Abs(after.X - before.X - 180) < 0.5 && Math.Abs(after.Y - before.Y - 90) < 0.5 && after.Text == before.Text,
                $"the {name} moved with the mouse ({after.X - before.X:0.#}, {after.Y - before.Y:0.#})");
            Check(record.Strokes.Count == 0, $"dragging the {name} left no paint");
        }
        options.Picture(window.Handle, $"notes-{theme}");

        var text = record.Notes.Single(n => n.File is null);
        bench.Spaces.Undo(window.Handle); // the file's move
        bench.Spaces.Undo(window.Handle); // the text's move
        Check(record.Strokes.Count == 0 && record.Notes.Single(n => n.File is null) == text.Previous, "undo puts the text back where it was");
        Ui.Pump(300);
        var saved = bench.Store.Load().Single();
        Check(saved.Notes.Count == 2 && saved.Notes.Single(n => n.File is not null).Text.Length > 100, "both notes saved, the file's text with it");
        Directory.Delete(folder, recursive: true);
        return results;
    }

    /// <summary>A heading dragged from a browser with the pen at 72, bold, onto a narrow space: it is shrunk to fit. Then an italic line.</summary>
    public static IEnumerable<Result> DropToFit(string theme, Options options)
    {
        const string Heading = "Контекстные spaces: рисунки и заметки, «приклеенные» к конкретному окну или приложению.";
        var bounds = new Rect(Bench.X, Bench.Y, 240, 380);
        using var bench = new Bench(theme, [Bench.Record(bounds, ink: false)]);
        var window = bench.Windows[0];
        var record = bench.Spaces.Records.Single();
        bench.Spaces.SetPen(bench.Spaces.Pen with { FontSize = 72, Bold = true });

        var drop = new FrameTimer("drop text: fitted to the space", theme, bench.Gpu);
        drop.Frame(() => DropText(window, Heading, new Point(bounds.X + 60, bounds.Y + 40)));
        Ui.Pump(700);
        var note = record.Notes.Single();
        var inside = new Rect(bounds.X + 11.5, bounds.Y + 11.5, bounds.Width - 23, bounds.Height - 23);
        var box = new Rect(new Point(note.X, note.Y), InfiniteSpace.Rendering.NoteText.Measure(note));
        Check(note.Bold && note.FontSize < 72 && inside.Contains(box), $"the heading fits the space, bold, at {note.FontSize} ({box.Width:0}×{box.Height:0} DIPs)");

        bench.Spaces.SetPen(bench.Spaces.Pen with { FontSize = 16, Bold = false, Italic = true });
        DropText(window, "and an italic line", new Point(bounds.X + 30, bounds.Y + 340));
        Ui.Pump(700);
        Check(record.Notes.Count == 2 && record.Notes[1].Italic && record.Notes[1].FontSize == 16, "short italic text keeps the pen's size");
        options.Picture(window.Handle, $"droptext-{theme}");
        return [drop.Finish()];
    }

    private static void DropText(SpaceWindow window, string text, Point at)
    {
        int effect = 1;
        var data = new DataObject(DataFormats.UnicodeText, text);
        var screen = new Win32.POINT { X = (int)Math.Round(at.X * Dpi.Scale), Y = (int)Math.Round(at.Y * Dpi.Scale) };
        window.Drops!.DragEnter(data, 0, screen, ref effect);
        effect = 1;
        window.Drops.Drop(data, 0, screen, ref effect);
        Ui.Pump(30);
    }

    private static void Check(bool ok, string what) => Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");

    /// <summary>What Explorer sends when files are dropped: a DROPFILES block, at a screen DIP point.</summary>
    private static void Drop(IntPtr window, string[] files, Point at)
    {
        Win32.GetWindowRect(window, out var r);
        string list = string.Join('\0', files) + "\0\0";
        int header = Marshal.SizeOf<DROPFILES>();
        IntPtr memory = GlobalAlloc(0x0042, header + list.Length * 2); // moveable, zeroed
        IntPtr block = GlobalLock(memory);
        Marshal.StructureToPtr(new DROPFILES
        {
            Files = header,
            X = (int)Math.Round(at.X * Dpi.Scale) - r.Left,
            Y = (int)Math.Round(at.Y * Dpi.Scale) - r.Top,
            Wide = 1,
        }, block, false);
        var chars = Encoding.Unicode.GetBytes(list);
        Marshal.Copy(chars, 0, block + header, chars.Length);
        GlobalUnlock(memory);
        SendMessage(window, WM_DROPFILES, memory, 0); // the window lets go of it (DragFinish)
    }

    private static void Wheel(IntPtr window, Point at, int delta)
    {
        int x = (int)Math.Round(at.X * Dpi.Scale), y = (int)Math.Round(at.Y * Dpi.Scale);
        SendMessage(window, WM_MOUSEWHEEL, (nint)(delta << 16), (y << 16) | (x & 0xFFFF));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public int Files;
        public int X;
        public int Y;
        public int NonClient;
        public int Wide;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, nint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, nint wParam, nint lParam);
}
