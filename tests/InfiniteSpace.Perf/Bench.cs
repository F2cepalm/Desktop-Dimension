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

    /// <summary>A space at desktop level over <paramref name="bounds"/> (screen DIPs), with paint and text on it.</summary>
    public static SpaceRecord Record(Rect bounds, bool ink = true)
    {
        var record = new SpaceRecord
        {
            Figures = [[bounds.Left, bounds.Top, bounds.Right, bounds.Top, bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom]],
        };
        if (!ink)
            return record;
        // A few wavy strokes and two notes, one of them a wrapped paragraph.
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
        record.Notes.Add(new TextNote { X = bounds.Left + 24, Y = bounds.Top + 20, Text = "Stay here", FontSize = 20, Order = 10 });
        record.Notes.Add(new TextNote
        {
            X = bounds.Left + 30,
            Y = bounds.Top + bounds.Height / 2,
            Width = Math.Min(260, bounds.Width - 60),
            Text = "A longer note that wraps over a few lines, the way notes typed on a space usually do.",
            Order = 11,
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
