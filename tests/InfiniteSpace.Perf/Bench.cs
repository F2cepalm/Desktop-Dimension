using System.IO;
using System.Windows;
using System.Windows.Interop;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfiniteSpace.Perf;

/// <summary>
/// Real spaces on the real GPU, in a throwaway data folder, their windows far off screen so nothing
/// shows on the desktop while they are timed.
/// </summary>
internal sealed class Bench : IDisposable
{
    /// <summary>Top-left of the area the spaces go in (screen DIPs), well off every screen.</summary>
    public const double X = -8000, Y = -8000;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Perf", Guid.NewGuid().ToString("N"));
    private readonly List<Window> _windows = [];

    public Bench(string theme, IEnumerable<SpaceRecord>? records = null)
    {
        Theme = theme;
        Store = new SpaceStore(_directory, NullLogger<SpaceStore>.Instance);
        if (records is not null)
            Store.Save(records);
        Gpu = new GpuCompositor();
        Universe = new Universe(Gpu);
        Spaces = new SpaceManager(Store, Universe, NullLogger<SpaceManager>.Instance);
        Spaces.ApplyTheme(theme);
        Spaces.RestoreAll();
        Ui.Pump(100);
    }

    public string Theme { get; }

    public GpuCompositor Gpu { get; }

    public Universe Universe { get; }

    public SpaceManager Spaces { get; }

    public SpaceStore Store { get; }

    public IReadOnlyList<SpaceWindow> Windows => [.. Spaces.Windows];

    /// <summary>A space at desktop level over <paramref name="bounds"/> (screen DIPs), with drawn shapes and text on it.</summary>
    public static SpaceRecord Record(Rect bounds, bool ink = true, bool drawnOutline = false)
    {
        double[] outline;
        if (drawnOutline)
        {
            var loop = new List<double>(240);
            double cx = bounds.Left + bounds.Width / 2, cy = bounds.Top + bounds.Height / 2;
            double rx = bounds.Width / 2, ry = bounds.Height / 2;
            for (int i = 0; i < 120; i++)
            {
                double a = i / 120.0 * Math.Tau;
                double r = 0.94 + 0.05 * Math.Cos(a * 4) + 0.02 * Math.Sin(a * 9);
                loop.Add(Math.Round(cx + rx * r * Math.Cos(a), 2));
                loop.Add(Math.Round(cy + ry * r * Math.Sin(a), 2));
            }
            outline = [.. loop];
        }
        else
        {
            outline = [bounds.Left, bounds.Top, bounds.Right, bounds.Top, bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom];
        }

        var record = new SpaceRecord
        {
            Figures = [outline],
        };
        if (!ink)
            return record;

        // 1. Wavy freehand strokes across the space.
        for (int s = 0; s < 6; s++)
        {
            var points = new List<double>();
            for (int i = 0; i < 160; i++)
            {
                double u = i / 159.0;
                points.Add(Math.Round(bounds.Left + 20 + u * (bounds.Width - 40), 2));
                points.Add(Math.Round(bounds.Top + 30 + s * (bounds.Height - 60) / 5 + 12 * Math.Sin(u * 18 + s), 2));
            }
            record.Strokes.Add(new InkStroke { Points = [.. points], Thickness = 2 + s, Color = s % 2 == 0 ? "#FF5A5F" : null, Order = s + 1 });
        }

        // 2. Drawn shapes (freehand box, oval loop, and connected diagram).
        var boxPoints = new List<double>(162);
        double bx = bounds.Left + bounds.Width * 0.12, by = bounds.Top + bounds.Height * 0.18;
        double bw = bounds.Width * 0.36, bh = bounds.Height * 0.34;
        for (int i = 0; i <= 80; i++)
        {
            double t = (i % 80) / 20.0;
            int side = (i % 80) / 20;
            double f = t - side;
            double px = side switch { 0 => bx + f * bw, 1 => bx + bw, 2 => bx + (1 - f) * bw, _ => bx };
            double py = side switch { 0 => by, 1 => by + f * bh, 2 => by + bh, _ => by + (1 - f) * bh };
            boxPoints.Add(Math.Round(px + 1.5 * Math.Sin(i * 0.5), 2));
            boxPoints.Add(Math.Round(py + 1.5 * Math.Cos(i * 0.5), 2));
        }
        record.Strokes.Add(new InkStroke { Points = [.. boxPoints], Thickness = 3, Color = "#4ECDC4", Order = 7 });

        var ovalPoints = new List<double>(162);
        double ox = bounds.Left + bounds.Width * 0.68, oy = bounds.Top + bounds.Height * 0.55;
        double orx = bounds.Width * 0.18, ory = bounds.Height * 0.22;
        for (int i = 0; i <= 80; i++)
        {
            double a = i / 80.0 * Math.Tau;
            ovalPoints.Add(Math.Round(ox + orx * Math.Cos(a) + 2 * Math.Sin(a * 5), 2));
            ovalPoints.Add(Math.Round(oy + ory * Math.Sin(a) + 2 * Math.Cos(a * 5), 2));
        }
        record.Strokes.Add(new InkStroke { Points = [.. ovalPoints], Thickness = 3.5, Color = "#FFE66D", Order = 8 });

        var starPoints = new List<double>(122);
        for (int i = 0; i <= 60; i++)
        {
            double a = i / 60.0 * Math.Tau * 2;
            double r = (i % 6 < 3 ? 0.14 : 0.07) * Math.Min(bounds.Width, bounds.Height);
            starPoints.Add(Math.Round(bounds.Left + bounds.Width * 0.52 + r * Math.Cos(a), 2));
            starPoints.Add(Math.Round(bounds.Top + bounds.Height * 0.35 + r * Math.Sin(a), 2));
        }
        record.Strokes.Add(new InkStroke { Points = [.. starPoints], Thickness = 2.5, Color = null, Order = 9 });

        // 3. Text notes (headings, wrapped paragraphs, opened Markdown, and edge notes pushed during resize).
        record.Notes.Add(new TextNote { X = bounds.Left + 24, Y = bounds.Top + 18, Text = "Stay here — sketches & notes", FontSize = 20, Bold = true, Order = 10 });
        record.Notes.Add(new TextNote
        {
            X = bounds.Left + 28,
            Y = bounds.Top + bounds.Height * 0.46,
            Width = Math.Min(240, bounds.Width - 60),
            Text = "A longer note that wraps over a few lines, the way notes typed on a space usually do when sketching and writing together.",
            Order = 11,
        });
        record.Notes.Add(new TextNote
        {
            X = bounds.Left + Math.Max(36, bounds.Width - 165),
            Y = bounds.Top + 26,
            Width = Math.Min(140, Math.Max(80, bounds.Width - 60)),
            Italic = true,
            Text = "Edge note: pushed inward when the right side shrinks across the drawn shapes.",
            Order = 12,
        });
        record.Notes.Add(new TextNote
        {
            X = bounds.Left + Math.Max(36, bounds.Width - 155),
            Y = bounds.Top + Math.Max(40, bounds.Height - 58),
            Text = "Corner callout (v2.4)",
            FontSize = 15,
            Color = "#FFE66D",
            Order = 13,
        });
        record.Notes.Add(new TextNote
        {
            X = bounds.Left + 32,
            Y = bounds.Top + Math.Max(60, bounds.Height * 0.68),
            Width = Math.Min(230, Math.Max(100, bounds.Width - 64)),
            Text = "# Quick spec\n- Keep **drawn shapes** crisp\n- Push *notes* smoothly on `reshape`\n- [x] Zero-copy crop",
            File = new NoteFile { Path = "spec.md", Scroll = 0, Height = Math.Min(96, Math.Max(48, bounds.Height * 0.26)), Markdown = true },
            Order = 14,
        });
        return record;
    }

    /// <summary>A plain window off screen, for spaces to sit on (an "app") or to lift spaces under.</summary>
    public IntPtr HostWindow(Rect bounds, bool topmost = false)
    {
        var window = new Window
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = topmost,
            Title = "Infinite Space perf host",
        };
        window.Show();
        _windows.Add(window);
        return new WindowInteropHelper(window).Handle;
    }

    public void Dispose()
    {
        Spaces.Dispose();
        foreach (var window in _windows)
            window.Close();
        Ui.Pump(60); // queued frames finish before the GPU goes
        Universe.Dispose();
        Gpu.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Moves or sizes a window, in screen DIPs.</summary>
    public static void Place(IntPtr window, Rect bounds) =>
        Win32.SetWindowPos(window, IntPtr.Zero, (int)Math.Round(bounds.X * Dpi.Scale), (int)Math.Round(bounds.Y * Dpi.Scale),
            (int)Math.Round(bounds.Width * Dpi.Scale), (int)Math.Round(bounds.Height * Dpi.Scale), Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
}
