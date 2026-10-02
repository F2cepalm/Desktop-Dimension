using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Color4 = Vortice.Mathematics.Color4;
using Ellipse = Vortice.Direct2D1.Ellipse;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Shell;

/// <summary>A stroke drawn live on an overlay, one colour and width: each frame redraws only the rectangle its newest pieces cover.</summary>
internal sealed class OverlayStroke : IDisposable
{
    private readonly GpuCompositor _gpu;
    private readonly OverlayLayer _layer;
    private readonly List<Point> _points = [];
    private ID2D1StrokeStyle? _round;
    private Color4 _ink = new(1, 1, 1, 1);
    private double _width = 1;
    private int _drawn; // points whose pieces are on screen

    /// <param name="pixelWidth">The overlay's width, in device pixels.</param>
    /// <param name="pixelHeight">The overlay's height, in device pixels.</param>
    public OverlayStroke(GpuCompositor gpu, IDCompositionVisual parent, int pixelWidth, int pixelHeight)
    {
        _gpu = gpu;
        _layer = new OverlayLayer(gpu, parent, pixelWidth, pixelHeight);
    }

    /// <summary>Its points, in DIPs from the overlay's top-left.</summary>
    public IReadOnlyList<Point> Points => _points;

    /// <summary>Whether points were added since the last frame drew.</summary>
    public bool HasNewPieces => _drawn < _points.Count;

    /// <summary>Starts a stroke at a point in a colour and width (DIPs).</summary>
    public void Start(Point at, Color ink, double width)
    {
        Reset();
        _ink = new Color4(ink.R / 255f, ink.G / 255f, ink.B / 255f, 1);
        _width = width;
        _points.Add(at);
    }

    /// <summary>Adds a point, if it is at least a DIP from the last.</summary>
    public void Add(Point at)
    {
        if (_points.Count > 0 && (at - _points[^1]).Length < 1)
            return;
        _points.Add(at);
    }

    /// <summary>Draws the pieces added since the last frame (as CanvasOverlay does, with round caps).</summary>
    public void DrawNewPieces()
    {
        if (!HasNewPieces)
            return;
        int from = Math.Max(0, _drawn - 1);
        var area = Rect.Empty;
        for (int i = from; i < _points.Count; i++)
            area.Union(_points[i]);
        double reach = _width / 2 + 1;
        area.Inflate(reach, reach);
        _drawn = _points.Count;

        _layer.Draw(area, context =>
        {
            using var brush = context.CreateSolidColorBrush(_ink);
            _round ??= _gpu.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
            float width = (float)_width;
            if (_points.Count == 1)
            {
                context.FillEllipse(new Ellipse(V(_points[0]), width / 2, width / 2), brush);
                return;
            }
            for (int i = 1; i < _points.Count; i++)
            {
                Point a = _points[i - 1], b = _points[i];
                if (Math.Max(a.X, b.X) + reach < area.Left || Math.Min(a.X, b.X) - reach > area.Right ||
                    Math.Max(a.Y, b.Y) + reach < area.Top || Math.Min(a.Y, b.Y) - reach > area.Bottom)
                    continue;
                context.DrawLine(V(a), V(b), brush, width, _round);
            }
        });
    }

    /// <summary>Forgets the stroke and clears what was drawn.</summary>
    public void Reset()
    {
        if (_points.Count == 0)
            return;
        _points.Clear();
        _drawn = 0;
        _layer.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _round?.Dispose();
        _layer.Dispose();
    }

    private static Vector2 V(Point p) => new((float)p.X, (float)p.Y);
}
