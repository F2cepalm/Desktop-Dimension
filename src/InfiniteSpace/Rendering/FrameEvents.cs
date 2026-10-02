using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace InfiniteSpace.Rendering;

/// <summary>
/// Per-frame timing for outside tools: <c>dotnet-counters monitor -n InfiniteSpace --counters
/// InfiniteSpace-Frames</c> shows the frame time and rate; PerfView and WPA get one event per frame at
/// the verbose level. Costs nothing while no one listens.
/// </summary>
[EventSource(Name = "InfiniteSpace-Frames")]
internal sealed class FrameEvents : EventSource
{
    public static readonly FrameEvents Log = new();

    private EventCounter? _frameTime;
    private IncrementingEventCounter? _frames;

    private FrameEvents()
    {
    }

    /// <summary>One frame's work on the UI thread.</summary>
    /// <param name="milliseconds">CPU time the frame took.</param>
    /// <param name="spaces">Spaces drawn.</param>
    /// <param name="interactive">Whether something under the user's hand was moving (full refresh rate).</param>
    [Event(1, Level = EventLevel.Verbose, Message = "Frame {0} ms")]
    public void Frame(double milliseconds, int spaces, bool interactive) => WriteEvent(1, milliseconds, spaces, interactive);

    [NonEvent]
    public void Record(double milliseconds, int spaces, bool interactive)
    {
        FrameStats.Add(milliseconds, interactive);
        if (!IsEnabled())
            return;
        _frameTime?.WriteMetric(milliseconds);
        _frames?.Increment();
        if (IsEnabled(EventLevel.Verbose, EventKeywords.All))
            Frame(milliseconds, spaces, interactive);
    }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        if (command.Command != EventCommand.Enable)
            return;
        _frameTime ??= new EventCounter("frame-time", this) { DisplayName = "Frame time", DisplayUnits = "ms" };
        _frames ??= new IncrementingEventCounter("frames", this) { DisplayName = "Frames", DisplayRateTimeScale = TimeSpan.FromSeconds(1) };
    }
}

/// <summary>The most recent frames' times, for the frame-time graph (see <see cref="Shell.FrameStatsWindow"/>).</summary>
internal static class FrameStats
{
    /// <summary>How many frames are remembered.</summary>
    public const int Capacity = 240;

    private static readonly double[] Times = new double[Capacity];
    private static readonly bool[] Interactive = new bool[Capacity];
    private static readonly long[] Stamps = new long[Capacity];
    private static int _next;

    /// <summary>Frames recorded so far.</summary>
    public static long Count { get; private set; }

    public static void Add(double milliseconds, bool interactive)
    {
        Times[_next] = milliseconds;
        Interactive[_next] = interactive;
        Stamps[_next] = Stopwatch.GetTimestamp();
        _next = (_next + 1) % Capacity;
        Count++;
    }

    /// <summary>
    /// Copies the last frames, oldest first, into <paramref name="times"/> (and whether each was
    /// interactive, and its time since the one before in milliseconds).
    /// </summary>
    /// <returns>How many were copied.</returns>
    public static int Copy(Span<double> times, Span<bool> interactive, Span<double> intervals)
    {
        int count = (int)Math.Min(Math.Min(Count, Capacity), times.Length);
        for (int i = 0; i < count; i++)
        {
            int at = ((_next - count + i) % Capacity + Capacity) % Capacity;
            int before = (at - 1 + Capacity) % Capacity;
            times[i] = Times[at];
            interactive[i] = Interactive[at];
            intervals[i] = i == 0 && Count <= count ? 0 : Stopwatch.GetElapsedTime(Stamps[before], Stamps[at]).TotalMilliseconds;
        }
        return count;
    }
}
