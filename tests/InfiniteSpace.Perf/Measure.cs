using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Perf;

/// <summary>What one scripted action cost, frame by frame.</summary>
/// <remarks>
/// Frame times leave out handing the frame to the compositor (<see cref="CommitP50"/>): off a real
/// high-refresh display that call waits for the display, which says nothing about the work.
/// </remarks>
internal sealed record Result(
    string Action,
    string Theme,
    int Frames,
    double First,
    double P50,
    double P99,
    double Max,
    double CommitP50,
    double AllocatedKbPerFrame,
    int Gen0,
    int Gen2,
    int OverBudget,
    int Missed)
{
    /// <summary>Frame budget at 360 Hz: 60% of the 2.8 ms refresh period, the rest is the compositor's.</summary>
    public const double Budget = 1.7;

    /// <summary>One refresh at 360 Hz: a frame longer than this is a missed frame.</summary>
    public const double Period = 1000.0 / 360;

    public static string Header =>
        "| Action | Theme | Frames | First ms | p50 ms | p99 ms | Max ms | Commit p50 ms | KB alloc/frame | Gen0 | Gen2 | Over 1.7 ms | Missed at 360 Hz |\n" +
        "|---|---|---|---|---|---|---|---|---|---|---|---|---|";

    public string Row => string.Create(CultureInfo.InvariantCulture,
        $"| {Action} | {Theme} | {Frames} | {First:0.00} | {P50:0.00} | {P99:0.00} | {Max:0.00} | {CommitP50:0.00} | {AllocatedKbPerFrame:0.0} | {Gen0} | {Gen2} | {OverBudget} | {Missed} |");
}

/// <summary>Times frames of one action: CPU time on this thread, allocations and collections.</summary>
/// <param name="gpu">Whose compositor commits are timed apart from the frame (none when null).</param>
internal sealed class FrameTimer(string action, string theme, GpuCompositor? gpu = null)
{
    /// <summary>How often frames start (the display rate simulated), in stopwatch ticks; see <see cref="Options"/>.</summary>
    public static long PeriodTicks { get; set; } = (long)(Stopwatch.Frequency / 360.0);

    private readonly List<double> _times = [];
    private readonly List<double> _commits = [];
    private long _allocated;
    private int _gen0 = GC.CollectionCount(0);
    private int _gen2 = GC.CollectionCount(2);
    private bool _started;
    private long _next;

    /// <summary>Frames timed so far.</summary>
    public int Count => _times.Count;

    /// <summary>Runs one frame's work and times it.</summary>
    /// <remarks>
    /// Frames start a 360 Hz refresh apart, as on such a display (a frame that runs over starts the
    /// next one late).
    /// </remarks>
    public void Frame(Action work)
    {
        if (!_started)
        {
            _started = true;
            _gen0 = GC.CollectionCount(0);
            _gen2 = GC.CollectionCount(2);
            _next = Stopwatch.GetTimestamp();
        }
        while (Stopwatch.GetTimestamp() < _next)
            Thread.SpinWait(20);
        var committed = gpu?.CommitTime ?? TimeSpan.Zero;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        work();
        long ended = Stopwatch.GetTimestamp();
        double commit = ((gpu?.CommitTime ?? TimeSpan.Zero) - committed).TotalMilliseconds;
        _times.Add(Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds - commit);
        _commits.Add(commit);
        _allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        _next = Math.Max(_next + PeriodTicks, ended);
    }

    /// <summary>Where every frame's time is written, one file per action (none when null).</summary>
    public static string? DumpFolder { get; set; }

    public Result Finish()
    {
        if (DumpFolder is { } folder && _times.Count > 0)
        {
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(folder, $"{action}-{theme}.txt".Replace(':', '_').Replace(' ', '_')),
                _times.Select((t, i) => string.Create(CultureInfo.InvariantCulture, $"{t:0.000} {_commits[i]:0.000}")));
        }
        if (_times.Count == 0)
            return new Result(action, theme, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var sorted = _times.Order().ToArray();
        double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
        return new Result(action, theme, _times.Count, _times[0], Percentile(0.5), Percentile(0.99), sorted[^1],
            _commits.Order().ElementAt(_commits.Count / 2),
            _allocated / 1024.0 / _times.Count,
            GC.CollectionCount(0) - _gen0, GC.CollectionCount(2) - _gen2,
            _times.Count(t => t > Result.Budget), _times.Count(t => t > Result.Period));
    }
}

internal static class Ui
{
    /// <summary>Lets the dispatcher run (queued frames, timers, window messages) for a while.</summary>
    /// <summary>Lets the UI thread run until <paramref name="done"/> holds (or the time is up): work that lands from another thread.</summary>
    public static void PumpUntil(Func<bool> done, int milliseconds = 3000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < milliseconds)
            Pump(5);
    }

    public static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>A window's rectangle on screen, in DIPs.</summary>
    public static Rect RectOf(IntPtr window)
    {
        Win32.GetWindowRect(window, out var r);
        return new Rect(r.Left / Dpi.Scale, r.Top / Dpi.Scale, (r.Right - r.Left) / Dpi.Scale, (r.Bottom - r.Top) / Dpi.Scale);
    }

    /// <summary>A key pressed and let go on one of our windows (as the keyboard sends it, without modifiers).</summary>
    public static void Key(IntPtr window, int virtualKey)
    {
        SendMessage(window, 0x0100, virtualKey, 1);                // WM_KEYDOWN
        SendMessage(window, 0x0101, virtualKey, unchecked((int)0xC0000001)); // WM_KEYUP
    }

    /// <summary>A mouse message to one of our windows, at a screen DIP point.</summary>
    public static void Mouse(IntPtr window, int message, Point screen, bool leftDown = false)
    {
        Win32.GetWindowRect(window, out var r);
        int x = (int)Math.Round(screen.X * Dpi.Scale) - r.Left, y = (int)Math.Round(screen.Y * Dpi.Scale) - r.Top;
        SendMessage(window, message, leftDown ? 1 : 0, (y << 16) | (x & 0xFFFF));
    }

    /// <summary>
    /// What a window shows on screen, as the desktop compositor has it (its DirectComposition content
    /// too), saved as a PNG; works while the window is off screen. Transparent parts come out black.
    /// </summary>
    public static void Capture(IntPtr window, string path)
    {
        for (int i = 0; i < 3; i++)
            Win32.DwmFlush(); // what was just committed is composed first
        Win32.GetWindowRect(window, out var r);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr dc = Win32.CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, width, height);
        IntPtr old = Win32.SelectObject(dc, bitmap);
        try
        {
            PrintWindow(window, dc, PW_RENDERFULLCONTENT);
            Win32.SelectObject(dc, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var file = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(file);
        }
        finally
        {
            Win32.DeleteObject(bitmap);
            Win32.DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private const uint PW_RENDERFULLCONTENT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
}
