using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Rendering;


/// <summary>State shared by every space in one animation frame.</summary>
/// <param name="Time">Seconds since start.</param>
/// <param name="Look">Smoothed cursor offset from the centre of the virtual screen, in DIPs.</param>
/// <param name="AmbientDue">
/// Whether the ambient animation moves on in this frame (it runs at 60 frames a second, see
/// <see cref="Universe"/>); other frames only show what the user is doing (paint, a reshape, a click burst).
/// </param>
internal readonly record struct FrameState(double Time, Vector Look, bool AmbientDue = true)
{
    /// <summary>Where a layer's star field sits. All spaces share it, so every space is a window into the same universe.</summary>
    public Vector LayerOffset(StarLayer layer) =>
        (DreamStyle.Drift * Time - Look * DreamStyle.LookParallax) * layer.Depth;
}

/// <summary>Something besides spaces that draws on the frame clock (see <see cref="Universe.Attach(IFramePart)"/>).</summary>
internal interface IFramePart
{
    /// <summary>Whether something it shows is changing under the user's hand: it wants the next refresh.</summary>
    bool WantsFrame { get; }

    /// <summary>Draws what changed since the last frame (it goes out with this frame's commit).</summary>
    void RenderFrame(FrameState frame);

    /// <summary>The GPU went away and came back as new devices: whatever it made on the old ones is gone.</summary>
    void DeviceLost();
}

/// <summary>
/// Drives all spaces from the display's refresh (120/144 Hz displays get 120/144 fps while anything
/// moves under the user's hand) and sends their changes to the compositor in one commit per frame.
/// Idle (no ticks at all) when there are no spaces.
/// </summary>
/// <remarks>
/// Every presented frame has a fixed cost in the graphics stack, so frames are only made when they
/// matter: at the full refresh rate for motion the user drives, and at <see cref="AmbientRate"/> for
/// the ambient pixel-art animation (rim wobble, twinkle, drift), which moves in whole art pixels and
/// looks the same at 60 as at 144.
/// </remarks>
internal sealed class Universe : IDisposable
{
    /// <summary>How long a replaced window stays up, so its replacement is on screen before it goes.</summary>
    private const int RetireAfterFrames = 3;

    /// <summary>Frames per second for the ambient animation, at most (see <see cref="AmbientRate"/>).</summary>
    private const double FullAmbientRate = 60;

    /// <summary>The ambient rates there are: whole fractions of 60, so the animation stays even.</summary>
    private static readonly double[] AmbientRates = [60, 30, 20, 15];

    /// <summary>The share of the UI thread the ambient animation may take; the rest stays free for the user.</summary>
    private const double AmbientShare = 0.25;

    /// <summary>How long the full refresh rate holds after the last thing the user moved, or an app window moved.</summary>
    private const double HotHold = 0.3;

    /// <summary>How often it looks again at which spaces can be seen (see <see cref="CheckSight"/>).</summary>
    private const double SightInterval = 0.25;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<SpaceWindow> _spaces = [];
    private readonly List<(SpaceWindow Window, int FramesLeft)> _retiring = [];
    private readonly List<IFramePart> _parts = [];
    private readonly FrameClock _frames;
    private double _lastTick = double.NegativeInfinity;
    private double _lastFrame = double.NegativeInfinity;
    private double _lastInteractive = double.NegativeInfinity;
    private Vector _look;
    private bool _hasLook;
    private bool _disposed;
    private double _hotUntil = double.NegativeInfinity;
    private double _ambientCost = double.NaN; // ms per ambient frame, smoothed
    private double _nextPowerCheck = double.NegativeInfinity;
    private bool _batterySaver;
    private double _nextSightCheck = double.NegativeInfinity;
    private bool _nothingSeen;
    private readonly ScreenWatch _screen;

    public Universe(GpuCompositor gpu)
    {
        Gpu = gpu;
        _frames = new FrameClock(Dispatcher.CurrentDispatcher, Tick);
        _screen = new ScreenWatch();
    }

    public GpuCompositor Gpu { get; }

    /// <summary>Frames rendered so far (diagnostics).</summary>
    internal long FramesRendered { get; private set; }

    /// <summary>Display refreshes seen so far, drawn or not (diagnostics).</summary>
    internal long Ticks { get; private set; }

    /// <summary>What paces the frames (diagnostics; see <see cref="FrameClock"/>).</summary>
    internal string ClockSource => _frames.Source;

    /// <summary>
    /// Frames per second for the ambient animation now: 60, halved while Windows' battery saver is on, and
    /// lowered further whenever drawing it would take more than a quarter of the UI thread (a slow or
    /// throttled CPU, many large spaces), so what the user does always keeps the display's full rate.
    /// </summary>
    internal double AmbientRate { get; private set; } = FullAmbientRate;

    /// <summary>
    /// The user is doing something (a press, a drag, the wheel, a key in an overlay): ticks go back to
    /// every refresh at once, rather than at the next ambient frame.
    /// </summary>
    public void Hurry()
    {
        _hotUntil = _clock.Elapsed.TotalSeconds + HotHold;
        _frames.Pace = TimeSpan.Zero;
    }

    /// <summary>Starts animating a space; its first frame is shown right away.</summary>
    public void Attach(SpaceWindow space)
    {
        if (_spaces.Contains(space))
            return;
        _spaces.Add(space);
        space.Universe = this;
        _nothingSeen = false; // a new space is in sight until a look says otherwise
        space.Track();
        space.RenderFrame(new FrameState(_clock.Elapsed.TotalSeconds, _look));
        Gpu.Commit();
        _frames.Start();
    }

    /// <summary>Stops animating a space (the caller disposes it).</summary>
    public void Detach(SpaceWindow space)
    {
        _spaces.Remove(space);
        StopWhenIdle();
    }

    /// <summary>
    /// Draws something besides spaces on the display's refresh (an overlay's handles and strokes), in
    /// the same compositor commit as the spaces, so the two never show a frame apart.
    /// </summary>
    public void Attach(IFramePart part)
    {
        if (_parts.Contains(part))
            return;
        _parts.Add(part);
        _frames.Start();
    }

    public void Detach(IFramePart part)
    {
        _parts.Remove(part);
        StopWhenIdle();
    }

    /// <summary>
    /// Takes a space off screen a few frames from now: long enough for whatever replaces it (a merged
    /// space, a created block) to be showing, so nothing blinks. Disposes it then.
    /// </summary>
    public void Retire(SpaceWindow space)
    {
        _spaces.Remove(space);
        _retiring.Add((space, RetireAfterFrames));
        _frames.Start();
    }

    /// <summary>One display refresh: follow apps, draw what changed, send it all to the compositor.</summary>
    internal void Tick()
    {
        if (_disposed)
            return; // a tick queued before it was disposed: the GPU may be gone
        try
        {
            if (Gpu.IsLost)
                Recover();
            Frame();
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (GpuCompositor.IsDeviceLoss(ex.HResult))
        {
            Gpu.MarkLost(); // made again on the next frame
        }
    }

    /// <summary>
    /// The GPU went away (a driver update, a reset, an external GPU unplugged): new devices, and every
    /// space and overlay makes what it shows again on them.
    /// </summary>
    private void Recover()
    {
        Gpu.Recreate();
        DeviceRecoveries++;
        foreach (var (window, _) in _retiring)
            window.Dispose();
        _retiring.Clear();
        foreach (var space in _spaces.ToArray())
            space.RecreateGraphics();
        foreach (var part in _parts.ToArray())
            part.DeviceLost();
        Gpu.Commit();
    }

    /// <summary>Times the devices were made anew after the GPU went away (diagnostics).</summary>
    internal long DeviceRecoveries { get; private set; }

    private void Frame()
    {
        long started = Stopwatch.GetTimestamp();
        Ticks++;
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTick, 0.1);
        _lastTick = now;

        UpdateLook(dt);
        RetireDue();

        // Every tick: spaces keep up with their apps (moved windows move along). Cheap, no drawing.
        bool hostsMoved = false;
        for (int i = 0; i < _spaces.Count; i++)
            hostsMoved |= _spaces[i].Track();
        if (hostsMoved)
            _hotUntil = now + HotHold; // an app window is being dragged: follow it on every refresh

        // Full refresh rate while the user moves something (a dragged block, paint, a click burst),
        // for that alone; the ambient animation (and the cursor's parallax) moves on at its own rate.
        bool interactive = AnyWantsEveryFrame();
        if (interactive)
            _hotUntil = now + HotHold;
        CheckPower(now);
        if (now >= _nextSightCheck)
        {
            _nextSightCheck = now + SightInterval;
            CheckSight();
        }
        // A quarter early still counts, so refresh jitter never drops an ambient frame (60 Hz displays
        // render every refresh, 120 Hz every other one).
        bool ambientDue = now - _lastFrame >= 0.75 / AmbientRate;
        // Drawing in software, every frame costs the CPU: 60 a second is plenty, however fast the display.
        bool drawn = ambientDue || (interactive && (!Gpu.IsSoftware || now - _lastInteractive >= 0.75 / AmbientRate));
        if (drawn)
            _lastInteractive = now;
        if (drawn)
        {
            if (ambientDue)
                _lastFrame = now;
            FramesRendered++;
            var frame = new FrameState(now, _look, ambientDue);
            for (int i = 0; i < _spaces.Count; i++)
                _spaces[i].RenderFrame(frame);
            for (int i = 0; i < _parts.Count; i++)
                _parts[i].RenderFrame(frame);
        }

        Gpu.Commit();
        StopWhenIdle();
        double cost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (drawn)
            FrameEvents.Log.Record(cost, _spaces.Count, interactive);
        if (drawn && ambientDue)
            Govern(cost);
        // Nothing moving but the ambient animation: the refreshes in between needn't wake this thread;
        // and nothing at all while no space can be seen, but a look now and then.
        _frames.Pace = now < _hotUntil ? TimeSpan.Zero : TimeSpan.FromSeconds(_nothingSeen ? SightInterval : 0.75 / AmbientRate);
    }

    /// <summary>
    /// Which spaces can be seen: none while the session is locked or the displays are off, and none that
    /// other apps' windows cover entirely (a maximised app over the desktop, a full-screen game). Those
    /// skip the ambient animation; they pick it up where it has got to once they show again.
    /// </summary>
    private void CheckSight()
    {
        bool dark = _screen.Dark;
        var showing = new List<IntPtr>(_spaces.Count);
        foreach (var space in _spaces)
        {
            if (space.IsShowing)
                showing.Add(space.Handle);
        }
        var covered = dark ? null : Occlusion.Covered(showing);
        int next = 0;
        bool anySeen = false;
        foreach (var space in _spaces)
        {
            bool unseen = space.IsShowing ? dark || covered![next++] : dark;
            space.Unseen = unseen;
            anySeen |= space.IsShowing && !unseen;
        }
        _nothingSeen = !anySeen && _parts.Count == 0;
    }

    /// <summary>Whether Windows' battery saver is on, looked up every few seconds.</summary>
    private void CheckPower(double now)
    {
        if (now < _nextPowerCheck)
            return;
        _nextPowerCheck = now + 5;
        bool saver = Win32.GetSystemPowerStatus(out var status) && status.SystemStatusFlag == 1;
        if (saver == _batterySaver)
            return;
        _batterySaver = saver;
        AmbientRate = AmbientRateFor(_ambientCost, saver, AmbientRate);
    }

    /// <summary>Follows what an ambient frame costs, and sets the ambient rate by it (see <see cref="AmbientRate"/>).</summary>
    private void Govern(double cost)
    {
        _ambientCost = double.IsNaN(_ambientCost) ? cost : _ambientCost + (cost - _ambientCost) * 0.05;
        AmbientRate = AmbientRateFor(_ambientCost, _batterySaver, AmbientRate);
    }

    /// <summary>
    /// The ambient rate for frames that cost <paramref name="cost"/> ms: the fastest that keeps the
    /// animation within <see cref="AmbientShare"/> of the UI thread (30 at most on battery saver, 15 at
    /// least). It only goes back up once it would fit with a margin, so it never flickers between two.
    /// </summary>
    internal static double AmbientRateFor(double cost, bool batterySaver, double current)
    {
        double ceiling = batterySaver ? FullAmbientRate / 2 : FullAmbientRate;
        if (double.IsNaN(cost) || cost <= 0)
            return Math.Min(current, ceiling);
        double affordable = AmbientShare * 1000 / cost;
        foreach (double rate in AmbientRates)
        {
            if (rate > ceiling)
                continue;
            // Down as soon as it doesn't fit; up only with a quarter to spare.
            if (rate <= current ? rate <= affordable : rate * 1.25 <= affordable)
                return rate;
        }
        return AmbientRates[^1];
    }

    private bool AnyWantsEveryFrame()
    {
        for (int i = 0; i < _spaces.Count; i++)
        {
            if (_spaces[i].WantsEveryFrame)
                return true;
        }
        for (int i = 0; i < _parts.Count; i++)
        {
            if (_parts[i].WantsFrame)
                return true;
        }
        return false;
    }

    /// <summary>Disposes retired windows whose replacements have been up for long enough.</summary>
    private void RetireDue()
    {
        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            var (window, framesLeft) = _retiring[i];
            if (framesLeft > 1)
            {
                _retiring[i] = (window, framesLeft - 1);
                continue;
            }
            window.Dispose();
            _retiring.RemoveAt(i);
        }
    }

    private void StopWhenIdle()
    {
        if (_spaces.Count == 0 && _retiring.Count == 0 && _parts.Count == 0)
            _frames.Stop();
    }

    private void UpdateLook(double dt)
    {
        if (!Win32.GetCursorPos(out var p))
            return;

        var cursor = new Point(p.X / Dpi.Scale, p.Y / Dpi.Scale);
        var centre = new Point(
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth / 2,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight / 2);
        Vector target = cursor - centre;

        if (!_hasLook)
        {
            _look = target;
            _hasLook = true;
            return;
        }
        _look += (target - _look) * (1 - Math.Exp(-dt * 2.5));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _frames.Dispose();
        _screen.Dispose();
        foreach (var (window, _) in _retiring)
            window.Dispose();
        _retiring.Clear();
    }
}
