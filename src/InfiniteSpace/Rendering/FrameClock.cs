using System.Diagnostics;
using System.Windows.Threading;
using InfiniteSpace.Native;
using Vortice.DXGI;

namespace InfiniteSpace.Rendering;

/// <summary>
/// Ticks once per display refresh (144 or 360 Hz displays get 144 or 360 ticks a second), on the UI
/// thread. A background thread waits for the next refresh and posts the tick; ticks never pile up.
/// </summary>
/// <remarks>
/// <para>
/// On Windows 11 the wait is for the desktop compositor's clock, which follows the fastest display and
/// variable refresh rates. On Windows 10 it is for the vertical blank of the display the mouse is on,
/// where the user is at work (not always the primary one); failing that, for the compositor's next frame.
/// </para>
/// <para>
/// A sleeping display (or a broken wait) returns at once: waits that keep returning faster than any
/// display refreshes are paced to 60 a second instead, so the thread never spins.
/// </para>
/// </remarks>
internal sealed class FrameClock : IDisposable
{
    /// <summary>A wait shorter than this, several times running, is no display refresh (none runs at 1000 Hz).</summary>
    private static readonly TimeSpan TooFast = TimeSpan.FromMilliseconds(1);

    private const int TooFastInARow = 4;

    private const int WAIT_TIMEOUT = 0x102;

    private readonly Dispatcher _dispatcher;
    private readonly Action _tick;
    private readonly ManualResetEventSlim _running = new(false);
    private readonly Thread _thread;
    private readonly Stopwatch _sinceTick = new();
    private volatile bool _disposed;
    private int _queued;
    private int _tooFast;
    private bool? _compositorClock = PreferCompositorClock ? null : false; // unknown until first tried
    private IDXGIFactory1? _factory;
    private readonly List<IDXGIOutput> _outputs = [];
    private IDXGIOutput? _output;
    private IntPtr _outputMonitor;
    private long _nextOutputCheck;

    public FrameClock(Dispatcher dispatcher, Action tick)
    {
        _dispatcher = dispatcher;
        _tick = tick;
        _thread = new Thread(Run) { IsBackground = true, Name = "Infinite Space frame clock", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>What the ticks follow (diagnostics): the compositor's clock, a display's vertical blank, or the compositor's frames.</summary>
    public string Source => _compositorClock == true ? "compositor clock" : _output is not null ? "vertical blank" : "compositor frames";

    /// <summary>Whether the compositor clock is used where there is one (tests turn it off to try the Windows 10 way).</summary>
    internal static bool PreferCompositorClock { get; set; } = true;

    public void Start() => _running.Set();

    public void Stop() => _running.Reset();

    private void Run()
    {
        _sinceTick.Start();
        while (!_disposed)
        {
            _running.Wait();
            if (_disposed)
                return;

            WaitForRefresh();

            // Faster than any display refreshes, again and again: the display sleeps or the wait is
            // broken. Tick at 60 a second meanwhile.
            var elapsed = _sinceTick.Elapsed;
            _tooFast = elapsed < TooFast ? _tooFast + 1 : 0;
            if (_tooFast >= TooFastInARow && elapsed < TimeSpan.FromMilliseconds(16))
                Thread.Sleep(TimeSpan.FromMilliseconds(16) - elapsed);
            _sinceTick.Restart();

            if (Interlocked.Exchange(ref _queued, 1) == 0)
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    Volatile.Write(ref _queued, 0);
                    _tick();
                });
            }
        }
    }

    private void WaitForRefresh()
    {
        try
        {
            if (_compositorClock != false && WaitForCompositorClock())
                return;
            if (CursorOutput() is { } output)
            {
                output.WaitForVBlank();
                return;
            }
        }
        catch (Exception ex) when (ex is not ThreadAbortException)
        {
            // Display unplugged, device lost: re-find outputs next time, and meanwhile wait for the compositor.
            ForgetOutputs();
        }
        Win32.DwmFlush();
    }

    /// <summary>Windows 11: the compositor's clock (it follows the fastest display, and variable refresh rates).</summary>
    /// <returns>false where there is no such clock (Windows 10), or it didn't tick for a while (displays off).</returns>
    private bool WaitForCompositorClock()
    {
        try
        {
            uint result = Win32.DCompositionWaitForCompositorClock(0, IntPtr.Zero, 100);
            _compositorClock = true;
            return result != WAIT_TIMEOUT;
        }
        catch (EntryPointNotFoundException)
        {
            _compositorClock = false;
            return false;
        }
    }

    /// <summary>The output of the display the mouse is on, looked up a few times a second.</summary>
    private IDXGIOutput? CursorOutput()
    {
        long now = Stopwatch.GetTimestamp();
        if (_output is not null && now < _nextOutputCheck)
            return _output;
        _nextOutputCheck = now + Stopwatch.Frequency / 4;

        IntPtr monitor = Win32.GetCursorPos(out var cursor) ? Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTOPRIMARY) : IntPtr.Zero;
        if (_output is not null && monitor == _outputMonitor)
            return _output;
        if (_outputs.Count == 0 || (_factory is not null && !_factory.IsCurrent))
            FindOutputs();
        _output = _outputs.FirstOrDefault(o => o.Description.Monitor == monitor) ?? _outputs.FirstOrDefault();
        _outputMonitor = monitor;
        return _output;
    }

    /// <summary>Every display output of every adapter (on a hybrid laptop the displays hang off different GPUs).</summary>
    private void FindOutputs()
    {
        ForgetOutputs();
        _factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; _factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                    _outputs.Add(output);
            }
        }
    }

    private void ForgetOutputs()
    {
        foreach (var output in _outputs)
            output.Dispose();
        _outputs.Clear();
        _output = null;
        _outputMonitor = IntPtr.Zero;
        _factory?.Dispose();
        _factory = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _running.Set();
        _thread.Join(TimeSpan.FromSeconds(1));
        _running.Dispose();
        if (!_thread.IsAlive)
            ForgetOutputs();
    }
}
