using System.Windows;
using System.Windows.Media;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Ellipse = Vortice.Direct2D1.Ellipse;
using Matrix3x2 = System.Numerics.Matrix3x2;
using RawRectF = Vortice.RawRectF;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Rendering;

/// <summary>
/// The Parallax theme: a textured relief in greens and grey, like a plaster painting. A sage band of
/// folded fabric sweeps across a grey plaster wall, a rough forest-green mass fills the lower corner,
/// and each layer casts a soft shadow on the one behind. Layers shift against the cursor by depth.
/// </summary>
/// <remarks>
/// Each layer is painted once per shape; a frame just places them, and frames are only made while
/// the cursor moves.
/// </remarks>
internal sealed class ParallaxLook : SceneLook
{
    public static readonly Color Ink = Color.FromRgb(0x1F, 0x2A, 0x1F);

    /// <summary>Room around the window for the layers to shift into, in DIPs.</summary>
    private const double Margin = 36;

    private static readonly FrameStyle Style = new()
    {
        Line = Rgb(0x2F302C),
        LineWidth = 2.2,
        Cast = Rgb(0x000000, 0.35),
        CastOffset = new Vector(0, 3),
        CastBlur = 4,
        Shade = Rgb(0x1E2A1A, 0.3),
        ShadeBlur = 7,
    };

    private static readonly Band[] Bands =
    [
        // Sage fabric, from the top right corner down to the left.
        new(From: new Point(1.08, -0.08), To: new Point(-0.08, 0.78), Parallax: 0.012, Seed: 1,
            Colors: [(0, Rgb(0xADC994)), (0.3f, Rgb(0x8FB078)), (1, Rgb(0x7B9F66))]),
        // Forest green, the lower right.
        new(From: new Point(1.08, 0.36), To: new Point(0.12, 1.08), Parallax: 0.026, Seed: 2,
            Colors: [(0, Rgb(0x587D50)), (0.35f, Rgb(0x42633E)), (1, Rgb(0x334F30))]),
    ];

    private readonly ID2D1Bitmap1?[] _layers = new ID2D1Bitmap1?[Bands.Length];
    private ID2D1Bitmap1? _flat; // a band before its shadow (see PaintBand)
    private Rect _bandArea = Rect.Empty; // the area the bands were painted for
    private Shadow? _bandShadow;
    private ID2D1Bitmap1? _plaster;
    private ID2D1Bitmap1? _moss;
    private ID2D1SolidColorBrush? _ring;

    protected override FrameStyle Frame => Style;

    protected override double BurstLife => 0.8;

    protected override bool Advance() => LookMoved(Bands[^1].Parallax);

    protected override void PrepareStills(ISpaceCanvas canvas, float scale)
    {
        _plaster ??= PaintTile(canvas, scale, 180, PlasterStrokes);
        _moss ??= PaintTile(canvas, scale, 120, MossSpeckles);
        // While the shape changes under the hand, the bands it has are stretched to it (see DrawMotion):
        // painting them takes far longer than a frame. They are painted afresh once it rests.
        if (Draft && _layers.All(l => l is not null))
            return;
        for (int i = 0; i < Bands.Length; i++)
            PaintBand(canvas, scale, Bands[i], ref _layers[i]);
        _bandArea = Area;
    }

    protected override void DrawBackdrop(ID2D1DeviceContext context, ID2D1Factory1 factory)
    {
        var a = Area;
        var all = R(new Rect(0, 0, World.Width, World.Height));
        using (var wall = Linear(context, a.TopLeft, a.BottomRight, (0, Rgb(0xE4E2DC)), (1, Rgb(0xC8C6BF))))
            context.FillRectangle(all, wall);
        using (var plaster = Tiled(context, _plaster!))
            context.FillRectangle(all, plaster);
    }

    protected override void DrawMotion(ID2D1DeviceContext context, ID2D1Factory1 factory, Matrix3x2 origin)
    {
        double reach = Margin * 0.9;
        // Bands painted for another shape (a draft, see PrepareStills) are stretched onto this one.
        var a = Area;
        context.Transform = a == _bandArea || _bandArea.IsEmpty
            ? origin
            : Matrix3x2.CreateTranslation((float)-_bandArea.X, (float)-_bandArea.Y) *
              Matrix3x2.CreateScale((float)(a.Width / _bandArea.Width), (float)(a.Height / _bandArea.Height)) *
              Matrix3x2.CreateTranslation((float)a.X, (float)a.Y) * origin;
        for (int i = 0; i < Bands.Length; i++)
        {
            var shift = -Look * Bands[i].Parallax;
            var at = new Vector2(
                (float)(Math.Clamp(shift.X, -reach, reach) - Margin),
                (float)(Math.Clamp(shift.Y, -reach, reach) - Margin));
            context.DrawImage(_layers[i]!, at, null, InterpolationMode.Linear, CompositeMode.SourceOver);
        }
        context.Transform = origin;

        // A click: a soft ring, as if pressed into the plaster.
        _ring ??= Solid(context, Colors.White);
        foreach (var burst in Bursts)
        {
            double life = (Time - burst.Start) / BurstLife;
            float radius = (float)(4 + 44 * Math.Sqrt(life));
            _ring.Opacity = (float)(0.55 * (1 - life));
            context.DrawEllipse(new Ellipse(V(burst.Origin), radius, radius), _ring, 1.6f);
        }
    }

    protected override void DisposeResources()
    {
        foreach (var layer in _layers)
            layer?.Dispose();
        _flat?.Dispose();
        _bandShadow?.Dispose();
        _plaster?.Dispose();
        _moss?.Dispose();
        _ring?.Dispose();
    }

    /// <summary>
    /// One layer for the current shape: its textured band with the shadow it casts, over the window
    /// plus <see cref="Margin"/> on every side. Painted over the layer it had before, when it fits.
    /// </summary>
    private void PaintBand(ISpaceCanvas canvas, float scale, Band band, ref ID2D1Bitmap1? layer)
    {
        var a = Area;
        var from = new Point(a.Left + band.From.X * a.Width, a.Top + band.From.Y * a.Height);
        var to = new Point(a.Left + band.To.X * a.Width, a.Top + band.To.Y * a.Height);
        var along = to - from;
        double length = along.Length;
        along /= length;
        var outward = new Vector(-along.Y, along.X); // towards the layer behind
        double reach = new Vector(World.Width, World.Height).Length + 2 * Margin;

        // A softly wavy edge, running on past both ends so that shifting never uncovers its tips.
        uint h = Mix((uint)band.Seed * 0x9E3779B9u);
        double p1 = Unit(h, 0) * Math.Tau, p2 = Unit(h, 8) * Math.Tau, p3 = Unit(h, 16) * Math.Tau;
        double Swell(double s) => 9 * Math.Sin(s / 170 * Math.Tau + p1) + 4 * Math.Sin(s / 67 * Math.Tau + p2);
        Point At(double s, double offset) => from + along * s + outward * offset;
        double start = -2 * Margin - 20, end = length + 2 * Margin + 20;
        var edge = new List<Vector2>();
        for (double s = start; s <= end; s += 4)
            edge.Add(V(At(s, Swell(s) + 1.2 * Math.Sin(s / 31 * Math.Tau + p3))));
        var region = new List<Vector2>(edge)
        {
            V(At(end, 0) - outward * reach),
            V(At(start, 0) - outward * reach),
        };

        int wide = (int)Math.Round((World.Width + 2 * Margin) * scale);
        int high = (int)Math.Round((World.Height + 2 * Margin) * scale);
        var shift = Matrix3x2.CreateTranslation((float)Margin, (float)Margin);
        canvas.Repaint(ref _flat, wide, high, scale, (context, factory) =>
        {
            context.Transform = shift;
            using var shape = Path(factory, region, closed: true);
            var middle = from + along * (length / 2);
            using (var fill = Linear(context, middle, middle - outward * (reach * 0.35), band.Colors))
                context.FillGeometry(shape, fill);

            context.PushLayer(new LayerParameters1
            {
                ContentBounds = new RawRectF(float.MinValue, float.MinValue, float.MaxValue, float.MaxValue),
                GeometricMask = shape,
                MaskAntialiasMode = AntialiasMode.PerPrimitive,
                MaskTransform = Matrix3x2.Identity,
                Opacity = 1,
            }, null!); // no layer object: Direct2D 1.1 manages it
            if (band.Seed == 1)
            {
                // Folds in the fabric: a lit ridge with a shadow under it, following the edge's swell
                // and relaxing further in.
                using var light = Solid(context, Rgb(0xD6EAC2, 0.4));
                using var dark = Solid(context, Rgb(0x5A7F4C, 0.28));
                var round = RoundStroke(factory);
                double inset = 6;
                for (int k = 0; k < 22; k++)
                {
                    uint hk = Hash(k, band.Seed, 5);
                    inset += 9 + 6 * Unit(hk, 0);
                    double relax = Math.Max(0.2, 1 - inset / 260);
                    double wave = 2 + 3 * Unit(hk, 8), wavelength = 80 + 60 * Unit(hk, 24), phase = Unit(hk, 16) * Math.Tau;
                    float width = (float)(1.2 + 1.8 * Unit(hk, 4));
                    var ridge = new List<Vector2>();
                    var hollow = new List<Vector2>();
                    for (double s = start; s <= end; s += 9)
                    {
                        double offset = Swell(s) * relax - inset + wave * Math.Sin(s / wavelength * Math.Tau + phase);
                        ridge.Add(V(At(s, offset)));
                        hollow.Add(V(At(s, offset - width * 0.9)));
                    }
                    using var under = Path(factory, hollow, closed: false);
                    using var over = Path(factory, ridge, closed: false);
                    context.DrawGeometry(under, dark, width * 1.3f, round);
                    context.DrawGeometry(over, light, width, round);
                }
            }
            else
            {
                context.Transform = Matrix3x2.Identity; // the speckle pattern lines up with the window, not the shift
                using var moss = Tiled(context, _moss!);
                context.FillRectangle(new RawRectF(0, 0, wide / scale, high / scale), moss);
                context.Transform = shift;
            }
            context.PopLayer();
        });

        var flat = _flat!;
        canvas.Repaint(ref layer, wide, high, scale, (context, _) =>
        {
            _bandShadow ??= new Shadow(context) { BlurStandardDeviation = 5, Color = ToVector4(Rgb(0x000000, 0.38)) };
            _bandShadow.SetInput(0, flat, true);
            DrawEffect(context, _bandShadow, V((Point)(outward * 3.5)));
            context.DrawImage(flat, InterpolationMode.NearestNeighbor, CompositeMode.SourceOver);
        });
    }

    /// <summary>Paints a square texture that repeats seamlessly: whatever crosses an edge comes back on the other side.</summary>
    private static ID2D1Bitmap1 PaintTile(ISpaceCanvas canvas, float scale, double size, Action<ID2D1DeviceContext, ID2D1Factory1, float, Action<Action>> draw)
    {
        int pixels = (int)Math.Round(size * scale);
        float side = pixels / scale;
        return canvas.Paint(pixels, pixels, scale, (context, factory) =>
            draw(context, factory, side, paint =>
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        context.Transform = Matrix3x2.CreateTranslation(dx * side, dy * side);
                        paint();
                    }
                }
                context.Transform = Matrix3x2.Identity;
            }));
    }

    private static ID2D1BitmapBrush1 Tiled(ID2D1DeviceContext context, ID2D1Bitmap1 tile) =>
        context.CreateBitmapBrush(tile, new BitmapBrushProperties1(ExtendMode.Wrap, ExtendMode.Wrap, InterpolationMode.Linear), null);

    /// <summary>Short diagonal trowel marks in plaster, some catching the light and some in shadow.</summary>
    private void PlasterStrokes(ID2D1DeviceContext context, ID2D1Factory1 factory, float side, Action<Action> wrapped)
    {
        using var light = Solid(context, Rgb(0xFFFFFF, 0.24));
        using var dark = Solid(context, Rgb(0x9E9B93, 0.14));
        var round = RoundStroke(factory);
        for (int i = 0; i < 90; i++)
        {
            uint h = Hash(i, 3, 11), h2 = Mix(h);
            var p = new Point(Unit(h, 0) * side, Unit(h, 8) * side);
            double length = 20 + 40 * Unit(h, 16);
            double angle = -0.45 + 0.25 * (Unit(h, 24) - 0.5);
            var d = new Vector(Math.Cos(angle), Math.Sin(angle)) * (length / 2);
            float width = (float)(0.8 + 1.6 * Unit(h2, 0));
            var brush = (h2 & 0x100) != 0 ? light : dark;
            wrapped(() => context.DrawLine(V(p - d), V(p + d), brush, width, round));
        }
    }

    /// <summary>Rough speckles, for the forest-green mass.</summary>
    private static void MossSpeckles(ID2D1DeviceContext context, ID2D1Factory1 factory, float side, Action<Action> wrapped)
    {
        using var light = Solid(context, Rgb(0x7FA877, 0.45));
        using var dark = Solid(context, Rgb(0x1C301A, 0.4));
        for (int i = 0; i < 260; i++)
        {
            uint h = Hash(i, 5, 13), h2 = Mix(h);
            var p = V(new Point(Unit(h, 0) * side, Unit(h, 8) * side));
            float rx = (float)(0.6 + 1.0 * Unit(h, 16)), ry = (float)(0.6 + 1.0 * Unit(h, 24));
            var brush = (h2 & 0x100) != 0 ? light : dark;
            wrapped(() => context.FillEllipse(new Ellipse(p, rx, ry), brush));
        }
    }

    /// <summary>A layer: the band on the far side of a line across the space (corners as fractions of its bounds).</summary>
    private sealed record Band(Point From, Point To, double Parallax, int Seed, (float At, Color Color)[] Colors);
}
