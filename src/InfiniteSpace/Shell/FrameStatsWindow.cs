using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using AlphaMode = Vortice.DXGI.AlphaMode;
using Color4 = Vortice.Mathematics.Color4;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Shell;

/// <summary>
/// A small graph of the last few seconds of frames in the bottom-right corner of the primary screen:
/// one bar per frame (how long its work took), lines at the 144 and 360 Hz budgets, and the median
/// and slowest times. Shown when settings.json has <c>"ShowFrameStats": true</c>; clicks go through it.
/// </summary>
internal sealed class FrameStatsWindow : IDisposable
{
    private const double Width = 260, Height = 92;

    /// <summary>The graph's top, in milliseconds: slower frames reach it.</summary>
    private const double Top = 8.4;

    private readonly Universe _universe;
    private readonly HwndSource _source;
    private IDCompositionTarget _target = null!;
    private IDCompositionVisual _visual = null!;
    private IDCompositionSurface _surface = null!;
    private int _generation;
    private readonly DispatcherTimer _timer;
    private readonly IDWriteTextFormat _format;
    private readonly double[] _times = new double[FrameStats.Capacity];
    private readonly bool[] _interactive = new bool[FrameStats.Capacity];
    private readonly double[] _intervals = new double[FrameStats.Capacity];
    private readonly int _pixelWidth, _pixelHeight;
    private long _shown = -1;

    /// <param name="at">Its top-left on screen (DIPs); the bottom-right corner of the primary screen by default.</param>
    public FrameStatsWindow(Universe universe, Point? at = null)
    {
        _universe = universe;
        var gpu = universe.Gpu;
        double scale = Dpi.Scale;
        _pixelWidth = (int)Math.Ceiling(Width * scale);
        _pixelHeight = (int)Math.Ceiling(Height * scale);
        var area = SystemParameters.WorkArea;
        _source = new HwndSource(new HwndSourceParameters("Desktop_Dimension frame stats")
        {
            WindowStyle = unchecked((int)Win32.WS_POPUP),
            ExtendedWindowStyle = (int)(Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_NOREDIRECTIONBITMAP),
            PositionX = (int)Math.Round((at?.X ?? area.Right - Width - 12) * scale),
            PositionY = (int)Math.Round((at?.Y ?? area.Bottom - Height - 12) * scale),
            Width = _pixelWidth,
            Height = _pixelHeight,
        });
        Build();
        _format = NoteText.Factory.CreateTextFormat(NoteText.Typeface.FontFamily.Source, null, Vortice.DirectWrite.FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, Vortice.DirectWrite.FontStretch.Normal, 11, "");
        Draw();
        Win32.SetClickThrough(_source.Handle, true); // clicks go through it
        Win32.ShowWindow(_source.Handle, Win32.SW_SHOWNOACTIVATE);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Draw(), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    /// <summary>Its visual and surface, on the GPU devices there are now.</summary>
    private void Build()
    {
        var gpu = _universe.Gpu;
        _generation = gpu.Generation;
        gpu.Composition.CreateTargetForHwnd(_source.Handle, true, out _target!).CheckError();
        _visual = gpu.Composition.CreateVisual();
        gpu.Composition.CreateSurface((uint)_pixelWidth, (uint)_pixelHeight, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out _surface!).CheckError();
        _visual.SetContent(_surface);
        _target.SetRoot(_visual);
    }

    private void Draw()
    {
        if (_universe.Gpu.Generation != _generation)
        {
            // The GPU was lost and its devices made anew: so are these.
            _surface.Dispose();
            _visual.Dispose();
            _target.Dispose();
            Build();
            _shown = -1;
        }
        if (FrameStats.Count == _shown && _shown >= 0)
            return;
        _shown = FrameStats.Count;
        int count = FrameStats.Copy(_times, _interactive, _intervals);
        var gpu = _universe.Gpu;
        float scale = (float)Dpi.Scale;
        SurfaceDrawing.Draw(gpu, _surface, new RawRect(0, 0, _pixelWidth, _pixelHeight), scale, context =>
        {
            using var backing = context.CreateSolidColorBrush(new Color4(0, 0, 0, 0.72f));
            using var ambient = context.CreateSolidColorBrush(new Color4(0.55f, 0.55f, 0.6f, 1));
            using var interactive = context.CreateSolidColorBrush(new Color4(0.37f, 0.8f, 0.89f, 1));
            using var over = context.CreateSolidColorBrush(new Color4(1, 0.35f, 0.37f, 1));
            using var line = context.CreateSolidColorBrush(new Color4(1, 1, 1, 0.35f));
            using var text = context.CreateSolidColorBrush(new Color4(1, 1, 1, 0.9f));
            context.FillRoundedRectangle(new RoundedRectangle(new RawRectF(0, 0, (float)Width, (float)Height), 6, 6), backing);

            // The graph: time goes left to right, a frame's work up.
            const float left = 8, bottom = (float)Height - 8, graph = 56;
            float Y(double ms) => bottom - (float)(Math.Min(ms, Top) / Top * graph);
            foreach (double budget in new[] { 4.1, 1.7 })
                context.FillRectangle(new RawRectF(left, Y(budget), (float)Width - 8, Y(budget) + 1), line);
            float barWidth = (float)((Width - 16) / FrameStats.Capacity);
            for (int i = 0; i < count; i++)
            {
                float x = left + (FrameStats.Capacity - count + i) * barWidth;
                var brush = _times[i] > 1.7 ? over : _interactive[i] ? interactive : ambient;
                context.FillRectangle(new RawRectF(x, Y(_times[i]), x + Math.Max(1 / scale, barWidth - 0.2f), bottom), brush);
            }

            var sorted = _times.AsSpan(0, count).ToArray();
            Array.Sort(sorted);
            double median = count > 0 ? sorted[count / 2] : 0, slowest = count > 0 ? sorted[^1] : 0;
            double span = 0;
            for (int i = 1; i < count; i++)
                span += _intervals[i];
            double rate = span > 0 ? (count - 1) / (span / 1000) : 0;
            string summary = string.Create(CultureInfo.InvariantCulture,
                $"{median:0.00} ms median · {slowest:0.0} ms slowest · {rate:0} frames/s");
            using var layout = NoteText.Factory.CreateTextLayout(summary, _format, (float)Width - 16, 20);
            context.DrawTextLayout(new Vector2(8, 5), layout, text, DrawTextOptions.None);
        });
        gpu.Commit();
    }

    public IntPtr Handle => _source.Handle;

    public void Dispose()
    {
        _timer.Stop();
        _target.SetRoot(null);
        _universe.Gpu.Commit();
        _surface.Dispose();
        _visual.Dispose();
        _target.Dispose();
        _source.Dispose();
    }
}
