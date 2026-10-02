using System.Windows;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Perf;

/// <summary>One scripted action; <see cref="Themes"/> null runs it in every theme.</summary>
internal sealed record Scenario(string Name, IReadOnlyList<string>? Themes, Func<string, Options, IEnumerable<Result>> Run);

/// <summary>The actions timed, each as the user does it: input, then the frame it shows up in.</summary>
internal static class Scenarios
{
    private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;

    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("clock", [Themes.Void], Clock),
        new("ambient", null, Ambient),
        new("paint", null, Paint),
        new("move", null, Move),
        new("reshape", null, Reshape),
        new("jelly", [Themes.Dreamy], Jelly),
        new("follow", null, Follow),
        new("draw", [Themes.Dreamy], DrawSpace),
        new("erase", [Themes.Dreamy], EraseSpaces),
        new("block", [Themes.Dreamy, Themes.Glory], PlaceBlock),
        new("arrows", [Themes.Dreamy, Themes.Glory], DragArrows),
        new("files", [Themes.Dreamy, Themes.Glory, Themes.Claudish, Themes.Sunday], FileScenarios.DropAndScroll),
        new("notes", [Themes.Dreamy, Themes.Glory], FileScenarios.DragNotes),
        new("droptext", [Themes.Dreamy, Themes.Glory], FileScenarios.DropToFit),
        new("bigfile", [Themes.Dreamy, Themes.Glory], FileScenarios.BigFile),
        new("recover", [Themes.Dreamy, Themes.Glory, Themes.Claudish], Recover),
        new("stats", [Themes.Dreamy], Stats),
        new("keys", [Themes.Dreamy], Keys),
        new("clickthrough", [Themes.Dreamy], ClickThrough),
    ];

    /// <summary>Where the overlays go in the tests: off every screen, over the spaces made there.</summary>
    private static readonly Rect OverlayArea = new(Bench.X - 100, Bench.Y - 100, 1000, 700);

    /// <summary>Ctrl+Alt+S: one freehand loop drawn on the overlay, a mouse move per frame, then released into a space.</summary>
    private static IEnumerable<Result> DrawSpace(string theme, Options options)
    {
        using var bench = new Bench(theme);
        var overlay = new Shell.CanvasOverlay(Shell.OverlayMode.Draw, bench.Spaces, bench.Universe, IntPtr.Zero, OverlayArea);
        overlay.Show(activate: false);
        Point At(int i)
        {
            double a = i / 480.0 * Math.Tau;
            return new(Bench.X + 400 + 330 * Math.Cos(a) + 12 * Math.Sin(a * 11), Bench.Y + 250 + 220 * Math.Sin(a));
        }
        var timer = new FrameTimer("draw a space", theme, bench.Gpu);
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, At(0));
        for (int i = 1; i < 480; i++)
        {
            var p = At(i);
            timer.Frame(() =>
            {
                Ui.Mouse(overlay.Handle, WM_MOUSEMOVE, p, leftDown: true);
                bench.Universe.Tick();
            });
        }
        options.Picture(overlay.Handle, $"draw-{theme}");
        var done = new FrameTimer("draw: released, space made", theme, bench.Gpu);
        done.Frame(() =>
        {
            Ui.Mouse(overlay.Handle, WM_LBUTTONUP, At(479));
            bench.Universe.Tick();
        });
        Check(bench.Spaces.Count == 1, "the loop became a space");
        return [timer.Finish(), done.Finish()];
    }

    /// <summary>The erase brush dragged across two spaces, then released.</summary>
    private static IEnumerable<Result> EraseSpaces(string theme, Options options)
    {
        using var bench = new Bench(theme, [Bench.Record(new Rect(Bench.X, Bench.Y, 360, 260)), Bench.Record(new Rect(Bench.X + 420, Bench.Y + 60, 360, 260))]);
        var overlay = new Shell.CanvasOverlay(Shell.OverlayMode.Erase, bench.Spaces, bench.Universe, IntPtr.Zero, OverlayArea);
        overlay.Show(activate: false);
        Point At(int i) => new(Bench.X - 40 + i * 2.2, Bench.Y + 150 + 60 * Math.Sin(i / 30.0));
        var timer = new FrameTimer("erase brush", theme, bench.Gpu);
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, At(0));
        for (int i = 1; i < 400; i++)
        {
            var p = At(i);
            timer.Frame(() =>
            {
                Ui.Mouse(overlay.Handle, WM_MOUSEMOVE, p, leftDown: true);
                bench.Universe.Tick();
            });
        }
        options.Picture(overlay.Handle, $"erase-{theme}");
        var done = new FrameTimer("erase: released, cut", theme, bench.Gpu);
        done.Frame(() =>
        {
            Ui.Mouse(overlay.Handle, WM_LBUTTONUP, At(399));
            bench.Universe.Tick();
        });
        options.Picture(overlay.Handle, $"erased-{theme}");
        overlay.Dismiss();
        double left = bench.Spaces.Outlines.Sum(SpaceGeometry.Area);
        Check(left < 2 * 360 * 260 - 20000, $"the brush cut through both spaces ({left:0} of {2 * 360 * 260} square DIPs left)");
        return [timer.Finish(), done.Finish()];
    }

    /// <summary>Ctrl+Alt+B: the block follows the mouse, is dropped with a click, then resized by a corner.</summary>
    private static IEnumerable<Result> PlaceBlock(string theme, Options options)
    {
        using var bench = new Bench(theme);
        var overlay = new Shell.BlockOverlay(bench.Spaces, bench.Universe, IntPtr.Zero, OverlayArea);
        overlay.Show(activate: false);
        Ui.Pump(30);
        var start = new Point(OverlayArea.X + OverlayArea.Width / 2, OverlayArea.Y + OverlayArea.Height / 2);
        var follow = new FrameTimer("block: follows the mouse", theme, bench.Gpu);
        for (int i = 0; i < 180; i++)
        {
            var p = start + new Vector(120 * Math.Sin(i / 30.0), 60 * Math.Cos(i / 25.0));
            follow.Frame(() =>
            {
                Ui.Mouse(overlay.Handle, WM_MOUSEMOVE, p);
                bench.Universe.Tick();
            });
        }
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, overlay.Block.TopLeft + new Vector(40, 40));
        Ui.Mouse(overlay.Handle, WM_LBUTTONUP, overlay.Block.TopLeft + new Vector(40, 40));
        var corner = overlay.Block.BottomRight;
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, corner);
        var resize = new FrameTimer("block: corner dragged", theme, bench.Gpu);
        for (int i = 1; i < 180; i++)
        {
            var p = corner + new Vector(80 * Math.Sin(i / 20.0), 50 * Math.Sin(i / 33.0));
            resize.Frame(() =>
            {
                Ui.Mouse(overlay.Handle, WM_MOUSEMOVE, p, leftDown: true);
                bench.Universe.Tick();
            });
        }
        Ui.Mouse(overlay.Handle, WM_LBUTTONUP, corner);
        Ui.Pump(600);
        options.Picture(overlay.Handle, $"block-{theme}");
        overlay.Dismiss();
        return [follow.Finish(), resize.Finish()];
    }

    /// <summary>Ctrl+Alt+M: a space picked up with a click, set down, then stretched by its arrows and moved by its body.</summary>
    private static IEnumerable<Result> DragArrows(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 420, 280);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        var overlay = new Shell.MoveOverlay(bench.Spaces, bench.Universe, OverlayArea);
        overlay.Show(activate: false);
        var inside = new Point(bounds.X + 200, bounds.Y + 140);
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, inside); // picks it up
        Ui.Mouse(overlay.Handle, WM_LBUTTONUP, inside);
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, inside); // sets it down
        Ui.Mouse(overlay.Handle, WM_LBUTTONUP, inside);
        Check(overlay.Moving is not null, "picked up with a click");
        var right = new Point(bounds.Right, bounds.Y + bounds.Height / 2);
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, right);
        var stretch = new FrameTimer("arrows: right side dragged", theme, bench.Gpu);
        for (int i = 1; i < 200; i++)
        {
            var p = right + new Vector(100 * Math.Sin(i / 25.0), 0);
            stretch.Frame(() =>
            {
                Ui.Mouse(overlay.Handle, WM_MOUSEMOVE, p, leftDown: true);
                bench.Universe.Tick();
            });
        }
        Ui.Mouse(overlay.Handle, WM_LBUTTONUP, right);
        Ui.Pump(600);
        options.Picture(overlay.Handle, $"arrows-{theme}");
        Ui.Mouse(overlay.Handle, WM_LBUTTONDOWN, inside);
        var move = new FrameTimer("arrows: body dragged", theme, bench.Gpu);
        for (int i = 1; i < 200; i++)
        {
            var p = inside + new Vector(60 * Math.Sin(i / 25.0), 40 * Math.Sin(i / 40.0));
            move.Frame(() =>
            {
                Ui.Mouse(overlay.Handle, WM_MOUSEMOVE, p, leftDown: true);
                bench.Universe.Tick();
            });
        }
        Ui.Mouse(overlay.Handle, WM_LBUTTONUP, inside);
        overlay.Dismiss(); // puts it back
        return [stretch.Finish(), move.Finish()];
    }

    /// <summary>
    /// The GPU goes away (as on a driver update): the next frame makes new devices, and every space
    /// shows again as it was, ink and all.
    /// </summary>
    private static IEnumerable<Result> Recover(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 420, 300);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        var window = bench.Windows[0];
        Ui.Pump(200);
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "InfiniteSpace.Perf", "recover-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        string before = System.IO.Path.Combine(folder, "before.png"), after = System.IO.Path.Combine(folder, "after.png");
        Ui.Capture(window.Handle, before);

        var timer = new FrameTimer("GPU lost: everything made again", theme, bench.Gpu);
        bench.Gpu.MarkLost();
        timer.Frame(bench.Universe.Tick);
        Ui.Pump(300);
        Ui.Capture(window.Handle, after);
        options.Picture(window.Handle, $"recovered-{theme}");

        int Lit(string path)
        {
            var image = new System.Windows.Media.Imaging.PngBitmapDecoder(new Uri(path), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            var pixels = new int[image.PixelWidth * image.PixelHeight];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            return pixels.Count(p => (p & 0xFFFFFF) != 0);
        }
        int litBefore = Lit(before), litAfter = Lit(after);
        Check(bench.Universe.DeviceRecoveries == 1 && bench.Gpu.Generation == 1, "new devices were made");
        Check(litAfter > litBefore * 0.9 && litAfter < litBefore * 1.1, $"the space shows as before ({litAfter} lit pixels, {litBefore} before)");
        System.IO.Directory.Delete(folder, recursive: true);
        return [timer.Finish()];
    }

    /// <summary>The frame-time graph (the hidden ShowFrameStats setting), after a stretch of reshaping: just a picture of it.</summary>
    private static IEnumerable<Result> Stats(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 420, 300);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        using var stats = new Shell.FrameStatsWindow(bench.Universe, new Point(Bench.X, Bench.Y - 200));
        var lift = bench.HostWindow(new Rect(Bench.X - 1000, Bench.Y - 1000, 10, 10), topmost: true);
        var moving = bench.Spaces.PickUp(new Point(bounds.X + 100, bounds.Y + 100), lift)!;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int f = 0; clock.ElapsedMilliseconds < 1500; f++)
        {
            bench.Spaces.Fit(moving, new Rect(bounds.X, bounds.Y, bounds.Width + 60 * Math.Sin(f / 20.0), bounds.Height), new Vector());
            Ui.Pump(4);
        }
        bench.Spaces.PutBack(moving);
        Ui.Pump(300);
        options.Picture(stats.Handle, $"stats-{theme}");
        var middle = new Win32.POINT { X = (int)((Bench.X + 100) * Dpi.Scale), Y = (int)((Bench.Y - 160) * Dpi.Scale) };
        Check(Win32.WindowFromPoint(middle) != stats.Handle, "clicks go through the graph");
        return [];
    }

    /// <summary>A floating space (spread over the screen while it follows an app or is reshaped) must let clicks through.</summary>
    private static IEnumerable<Result> ClickThrough(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 300, 200);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        var window = bench.Windows[0];
        var lift = bench.HostWindow(new Rect(Bench.X - 1000, Bench.Y - 1000, 10, 10), topmost: true);
        var moving = bench.Spaces.PickUp(new Point(bounds.X + 100, bounds.Y + 100), lift)!;
        bench.Spaces.Fit(moving, new Rect(bounds.X, bounds.Y, 340, 200), new Vector());
        bench.Universe.Tick();
        Win32.GetWindowRect(window.Handle, out var r);
        var inside = new Win32.POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        Check(r.Right - r.Left > 1000, $"floating over the screen ({r.Right - r.Left}×{r.Bottom - r.Top} px)");
        Check(Win32.WindowFromPoint(inside) != window.Handle, "a floating space lets clicks through");
        bench.Spaces.PutBack(moving);
        bench.Universe.Tick();
        Win32.GetWindowRect(window.Handle, out r);
        var back = new Win32.POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        Check(Win32.WindowFromPoint(back) == window.Handle, "put back, it takes clicks again");
        return [];
    }

    /// <summary>The overlays' keys, sent as the keyboard would: arrows, Enter and Esc.</summary>
    private static IEnumerable<Result> Keys(string theme, Options options)
    {
        const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_RIGHT = 0x27, VK_DOWN = 0x28;
        using var bench = new Bench(theme);

        var block = new Shell.BlockOverlay(bench.Spaces, bench.Universe, IntPtr.Zero, OverlayArea);
        block.Show(activate: false);
        Ui.Pump(30);
        var before = block.Block;
        Ui.Key(block.Handle, VK_RIGHT);
        Ui.Key(block.Handle, VK_DOWN);
        Check(Math.Abs(block.Block.Width - before.Width - 10) < 0.01 && Math.Abs(block.Block.Height - before.Height - 10) < 0.01, "arrows grow the block's sides");
        for (int i = 0; i < 200; i++)
            Ui.Key(block.Handle, VK_RIGHT);
        Check(block.Block.Right <= OverlayArea.Right + Shell.BlockEdit.Spill + 0.5, $"a block stops at the screen's edge ({block.Block.Right - OverlayArea.Right:0.#} DIPs past it)");
        bool closed = false;
        block.Closed += (_, _) => closed = true;
        Ui.Key(block.Handle, VK_RETURN);
        Ui.Pump(60);
        Check(closed && bench.Spaces.Count == 1, "Enter makes it a space and closes the overlay");

        var bounds = new Rect(Bench.X, Bench.Y, 300, 200);
        using var moved = new Bench(theme, [Bench.Record(bounds)]);
        var move = new Shell.MoveOverlay(moved.Spaces, moved.Universe, OverlayArea);
        move.Show(activate: false);
        var inside = new Point(bounds.X + 100, bounds.Y + 100);
        Ui.Mouse(move.Handle, WM_LBUTTONDOWN, inside);
        Ui.Mouse(move.Handle, WM_LBUTTONUP, inside);
        Ui.Key(move.Handle, VK_RIGHT);
        Ui.Pump(60);
        bool putBack = false;
        move.Closed += (_, _) => putBack = true;
        Ui.Key(move.Handle, VK_ESCAPE);
        Ui.Pump(100);
        var outline = moved.Spaces.Outlines.Single().Bounds;
        Check(putBack && Math.Abs(outline.Width - bounds.Width) < 0.5, $"Esc puts a picked-up space back as it was ({outline.Width:0} wide)");

        var erase = new Shell.CanvasOverlay(Shell.OverlayMode.Erase, moved.Spaces, moved.Universe, IntPtr.Zero, OverlayArea);
        erase.Show(activate: false);
        bool done = false;
        erase.Closed += (_, _) => done = true;
        Ui.Key(erase.Handle, VK_RETURN);
        Ui.Pump(60);
        Check(done, "Enter ends erasing");
        return [];
    }

    private static void Check(bool ok, string what) => Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);

    /// <summary>How often the frame clock really ticks (it should match the display's refresh rate), both ways it can wait.</summary>
    private static IEnumerable<Result> Clock(string theme, Options options)
    {
        foreach (bool compositorClock in new[] { true, false })
        {
            FrameClock.PreferCompositorClock = compositorClock;
            using var bench = new Bench(theme, [Bench.Record(new Rect(Bench.X, Bench.Y, 200, 200), ink: false)]);
            long before = bench.Universe.Ticks;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Ui.Pump(2000);
            double rate = (bench.Universe.Ticks - before) / clock.Elapsed.TotalSeconds;
            Console.WriteLine($"  frame clock: {rate:0.0} ticks a second, from the {bench.Universe.ClockSource}");
        }
        FrameClock.PreferCompositorClock = true;
        return [];
    }

    /// <summary>The ambient animation (rim wobble, twinkle, drift), 60 frames a second, with one and with five spaces.</summary>
    private static IEnumerable<Result> Ambient(string theme, Options options)
    {
        foreach (int count in new[] { 1, 5 })
        {
            var records = Enumerable.Range(0, count).Select(i => Bench.Record(new Rect(Bench.X + i * 520, Bench.Y, 480, 320)));
            using var bench = new Bench(theme, records);
            var windows = bench.Windows;
            var timer = new FrameTimer($"ambient x{count}", theme, bench.Gpu);
            for (int f = -30; f < 300; f++)
            {
                double t = 1000 + f / 60.0;
                var frame = new FrameState(t, new Vector(120 * Math.Sin(t / 3), 60 * Math.Cos(t / 4)));
                void Work()
                {
                    foreach (var window in windows)
                        window.RenderFrame(frame);
                    bench.Gpu.Commit();
                }
                if (f < 0)
                    Work(); // warming up
                else
                    timer.Frame(Work);
            }
            if (count == 1)
                options.Picture(windows[0].Handle, $"ambient-{theme}");
            yield return timer.Finish();
        }
    }

    /// <summary>A paint stroke across a space: one mouse move per frame.</summary>
    private static IEnumerable<Result> Paint(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 600, 400);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        var window = bench.Windows[0];
        var timer = new FrameTimer("paint", theme, bench.Gpu);
        Point At(int i) => new(bounds.Left + 40 + i * 2, bounds.Top + 200 + 120 * Math.Sin(i / 20.0));
        Ui.Mouse(window.Handle, WM_LBUTTONDOWN, At(0));
        for (int i = 1; i < 260; i++)
        {
            var p = At(i);
            timer.Frame(() =>
            {
                Ui.Mouse(window.Handle, WM_MOUSEMOVE, p, leftDown: true);
                bench.Universe.Tick();
            });
        }
        var end = new FrameTimer("paint: stroke done (saved)", theme, bench.Gpu);
        end.Frame(() =>
        {
            Ui.Mouse(window.Handle, WM_LBUTTONUP, At(259));
            bench.Universe.Tick();
        });
        Ui.Pump(50);
        options.Picture(window.Handle, $"paint-{theme}");
        return [timer.Finish(), end.Finish()];
    }

    /// <summary>A space picked up with Ctrl+Alt+M and carried along: moves only.</summary>
    private static IEnumerable<Result> Move(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 480, 320);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        var lift = bench.HostWindow(new Rect(Bench.X - 1000, Bench.Y - 1000, 10, 10), topmost: true);
        var moving = bench.Spaces.PickUp(new Point(bounds.Left + 100, bounds.Top + 100), lift) ?? throw new InvalidOperationException("nothing picked up");
        var timer = new FrameTimer("move", theme, bench.Gpu);
        var rect = moving.Bounds;
        var carried = new Vector();
        for (int f = 0; f < 240; f++)
        {
            var step = new Vector(3, f % 2);
            timer.Frame(() =>
            {
                carried += step;
                rect.Offset(step);
                bench.Spaces.Fit(moving, rect, carried);
                bench.Universe.Tick();
            });
        }
        bench.Spaces.PutBack(moving);
        return [timer.Finish()];
    }

    /// <summary>A space picked up with Ctrl+Alt+M and reshaped by dragging its right arrow back and forth.</summary>
    private static IEnumerable<Result> Reshape(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 480, 320);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        var lift = bench.HostWindow(new Rect(Bench.X - 1000, Bench.Y - 1000, 10, 10), topmost: true);
        var moving = bench.Spaces.PickUp(new Point(bounds.Left + 100, bounds.Top + 100), lift) ?? throw new InvalidOperationException("nothing picked up");
        var timer = new FrameTimer("reshape", theme, bench.Gpu);
        var start = moving.Bounds;
        for (int f = 0; f < 240; f++)
        {
            var rect = new Rect(start.X, start.Y, start.Width + 120 * Math.Sin(f / 25.0) + 60, start.Height + 20 * Math.Sin(f / 40.0));
            timer.Frame(() =>
            {
                bench.Spaces.Fit(moving, rect, new Vector());
                bench.Universe.Tick();
            });
        }
        options.Picture(bench.Windows[0].Handle, $"reshape-{theme}");
        bench.Spaces.PutBack(moving);
        return [timer.Finish()];
    }

    /// <summary>One resize (a wheel notch), then the wobble until it rests: the Dreamy theme's jelly.</summary>
    private static IEnumerable<Result> Jelly(string theme, Options options)
    {
        var bounds = new Rect(Bench.X, Bench.Y, 480, 320);
        using var bench = new Bench(theme, [Bench.Record(bounds)]);
        var lift = bench.HostWindow(new Rect(Bench.X - 1000, Bench.Y - 1000, 10, 10), topmost: true);
        var moving = bench.Spaces.PickUp(new Point(bounds.Left + 100, bounds.Top + 100), lift) ?? throw new InvalidOperationException("nothing picked up");
        var window = bench.Windows[0];
        var timer = new FrameTimer("jelly", theme, bench.Gpu);
        var rect = moving.Bounds;
        for (int round = 0; round < 4; round++)
        {
            rect.Inflate(round % 2 == 0 ? 24 : -24, round % 2 == 0 ? 16 : -16);
            bench.Spaces.Fit(moving, rect, new Vector());
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (window.IsReshaping && clock.ElapsedMilliseconds < 1500)
            {
                timer.Frame(bench.Universe.Tick);
            }
        }
        bench.Spaces.PutBack(moving);
        return [timer.Finish()];
    }

    /// <summary>An app window dragged around with a space on it, then resized live (the app's own move is not timed).</summary>
    private static IEnumerable<Result> Follow(string theme, Options options)
    {
        using var bench = new Bench(theme);
        var frame = new Rect(Bench.X, Bench.Y, 640, 420);
        var host = bench.HostWindow(frame);
        Ui.Pump(50);
        var outline = SpaceGeometry.FromStroke([new(frame.Left + 40, frame.Top + 40), new(frame.Right - 40, frame.Top + 40), new(frame.Right - 40, frame.Bottom - 40), new(frame.Left + 40, frame.Bottom - 40)], new Vector());
        bench.Spaces.Create(outline, host);
        Ui.Pump(100);

        var moves = new FrameTimer("follow: app dragged", theme, bench.Gpu);
        for (int f = 0; f < 240; f++)
        {
            frame.Offset(3, f % 3 - 1);
            Bench.Place(host, frame);
            moves.Frame(bench.Universe.Tick);
        }
        Ui.Pump(300); // settles back into its own window

        var resizes = new FrameTimer("follow: app resized live", theme, bench.Gpu);
        for (int f = 0; f < 120; f++)
        {
            frame.Width += 3;
            frame.Height += f % 2;
            Bench.Place(host, frame);
            resizes.Frame(bench.Universe.Tick);
        }
        Ui.Pump(250);
        return [moves.Finish(), resizes.Finish()];
    }
}
