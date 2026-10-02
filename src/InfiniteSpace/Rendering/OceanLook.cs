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
/// The Ocean theme: aqua water seen from above, row after row of smooth swells rolling across at their
/// own pace and rising and falling, with sheen along the crests, shadow in the troughs and glints of
/// sun. Clicks send out ripples.
/// </summary>
/// <remarks>
/// A swell is a strip painted once (three shapes of it) and repeated sideways by a wrapping bitmap
/// brush, so each frame is one fill per row. The sea is in screen space: neighbouring spaces show the
/// same swells.
/// </remarks>
internal sealed class OceanLook : SceneLook
{
    public static readonly Color Ink = Colors.White;

    /// <summary>A swell's shape repeats across this many DIPs.</summary>
    private const double Period = 256;

    /// <summary>Distance between swells, in DIPs.</summary>
    private const double RowPitch = 30;

    /// <summary>Height of a swell strip; it reaches under the next one, which covers the rest.</summary>
    private const double TileHeight = 76;

    /// <summary>Mean height of the crest in a strip.</summary>
    private const double CrestAt = 26;

    private const int Shapes = 3;

    private static readonly FrameStyle Style = new()
    {
        Line = Rgb(0xF2FBFF),
        LineWidth = 1.4,
        Glow = Rgb(0xBFE8FF, 0.85),
        Shade = Rgb(0x083A7A, 0.5),
        ShadeBlur = 11,
    };

    private static readonly Field Glints = new(Cell: 64, Density: 0.35, Drift: new Vector(12, 0), Parallax: 0.02);

    private readonly ID2D1Bitmap1?[] _tiles = new ID2D1Bitmap1?[Shapes];
    private readonly ID2D1BitmapBrush1?[] _swells = new ID2D1BitmapBrush1?[Shapes];
    private ID2D1Bitmap1? _glint;
    private ID2D1SolidColorBrush? _ring;

    protected override FrameStyle Frame => Style;

    protected override double BurstLife => 1.3;

    protected override bool Advance() => true;

    protected override void PrepareStills(ISpaceCanvas canvas, float scale)
    {
        for (int i = 0; i < Shapes; i++)
            _tiles[i] ??= PaintSwell(canvas, scale, i);
        _glint ??= PaintGlow(canvas, scale, 16, (0, Colors.White), (0.25f, Rgb(0xE8F7FF, 0.8)), (1, Rgb(0xE8F7FF, 0)));
    }

    protected override void DrawBackdrop(ID2D1DeviceContext context, ID2D1Factory1 factory)
    {
        using var deep = Linear(context, new Point(0, Area.Top), new Point(0, Area.Bottom), (0, Rgb(0x5DB0EA)), (1, Rgb(0x1E6EC2)));
        context.FillRectangle(R(new Rect(0, 0, World.Width, World.Height)), deep);
    }

    protected override void DrawMotion(ID2D1DeviceContext context, ID2D1Factory1 factory, Matrix3x2 origin)
    {
        // Swells from the back (top) to the front, each covering the lower part of the one behind.
        int first = (int)Math.Floor((World.Top - TileHeight) / RowPitch) - 1;
        int last = (int)Math.Ceiling((World.Bottom + CrestAt) / RowPitch) + 1;
        float width = (float)World.Width;
        for (int row = first; row <= last; row++)
        {
            uint h = Hash(row, 0, 7);
            int shape = (int)(h % Shapes);
            double speed = 9 + 9 * Unit(h, 8);
            double bob = 3.2 * Math.Sin(Time * 0.9 + row * 0.83) + 1.5 * Math.Sin(Time * 1.7 + row * 2.1);
            double top = row * RowPitch + bob - Look.Y * 0.012 - CrestAt - World.Top;
            if (top > World.Height || top + TileHeight < 0)
                continue;
            double x = (Unit(h, 16) * Period + Time * speed - Look.X * 0.02 - World.Left) % Period;

            var brush = _swells[shape] ??= context.CreateBitmapBrush(_tiles[shape]!,
                new BitmapBrushProperties1(ExtendMode.Wrap, ExtendMode.Clamp, InterpolationMode.Linear), null);
            brush.Transform = Matrix3x2.CreateTranslation((float)x, (float)top);
            context.FillRectangle(new RawRectF(0, (float)top, width, (float)(top + TileHeight)), brush);
        }

        // Glints of sun: each flashes briefly, now and then.
        foreach (var mote in Scatter(Glints, 0, 10))
        {
            uint s = mote.Seed;
            double period = 1.6 + 2 * Unit(s, 0);
            double u = Frac(Time / period + Unit(s, 8));
            if (u >= 0.18)
                continue;
            double size = 5 + 4 * Unit(s, 16);
            DrawSprite(context, origin, _glint!, mote.At, size * 1.6, size, 0, 0.9 * Math.Sin(Math.PI * u / 0.18));
        }
        context.Transform = origin;

        // A click: three rings spreading out.
        _ring ??= Solid(context, Colors.White);
        foreach (var burst in Bursts)
        {
            double age = Time - burst.Start;
            for (int k = 0; k < 3; k++)
            {
                double t = age - k * 0.15;
                double life = t / (BurstLife - k * 0.15);
                if (t <= 0 || life >= 1)
                    continue;
                float radius = (float)(6 + 70 * (1 - Math.Exp(-t * 2.2)));
                _ring.Opacity = (float)(0.7 * (1 - life));
                context.DrawEllipse(new Ellipse(V(burst.Origin), radius, radius * 0.8f), _ring, 1.6f);
            }
        }
    }

    protected override void DisposeResources()
    {
        foreach (var brush in _swells)
            brush?.Dispose();
        foreach (var tile in _tiles)
            tile?.Dispose();
        _glint?.Dispose();
        _ring?.Dispose();
    }

    /// <summary>One swell strip, <see cref="Period"/> wide and repeating seamlessly sideways.</summary>
    private ID2D1Bitmap1 PaintSwell(ISpaceCanvas canvas, float scale, int shape)
    {
        uint h = Mix((uint)shape * 0x9E3779B9u + 1);
        double p1 = Unit(h, 0) * Math.Tau, p2 = Unit(h, 8) * Math.Tau, p3 = Unit(h, 16) * Math.Tau;
        double a1 = 5 + 2 * Unit(h, 24);
        double Crest(double x) => CrestAt
            + a1 * Math.Sin(Math.Tau * x / Period + p1)
            + 3 * Math.Sin(Math.Tau * 2 * x / Period + p2)
            + 1.3 * Math.Sin(Math.Tau * 4 * x / Period + p3);

        int wide = (int)Math.Round(Period * scale);
        int high = (int)Math.Ceiling(TileHeight * scale);
        float period = wide / scale;
        var crest = new List<Vector2>();
        for (int i = 0; i <= 3 * 64; i++)
        {
            double x = i * 3 * period / (3 * 64);
            crest.Add(new Vector2((float)x, (float)Crest(x)));
        }

        // The water, three periods wide so that the shadow's blur below has no seams.
        using var water = canvas.Paint(3 * wide, high, scale, (context, factory) =>
        {
            var body = new List<Vector2>(crest) { new(3 * period, (float)TileHeight + 1), new(0, (float)TileHeight + 1) };
            using var face = Path(factory, body, closed: true);
            using var line = Path(factory, crest, closed: false);
            double from = CrestAt - 10;
            float At(double y) => (float)((y - from) / (TileHeight - from));
            using (var fill = Linear(context, new Point(0, from), new Point(0, TileHeight),
                (0, Rgb(0xA8DAF7)), (At(CrestAt + 4), Rgb(0x6DB8EC)), (At(CrestAt + 20), Rgb(0x4298DE)), (1, Rgb(0x2C80D1))))
                context.FillGeometry(face, fill);

            // Sheen along the crest, brighter and fainter in turns (a whole number of turns per period).
            using (var sheen = Linear(context, new Point(0, 0), new Point(period / 6, 0), ExtendMode.Mirror,
                (0, Rgb(0xF4FBFF, 0.9)), (1, Rgb(0xF4FBFF, 0.12))))
                context.DrawGeometry(line, sheen, 1.3f);
            context.Transform = Matrix3x2.CreateTranslation(0, 2.5f);
            using (var soft = Solid(context, Rgb(0xDDF1FF, 0.18)))
                context.DrawGeometry(line, soft, 5);
            context.Transform = Matrix3x2.Identity;
        });

        // The middle period, with the trough's shadow thrown up onto the swell behind.
        return canvas.Paint(wide, high, scale, (context, _) =>
        {
            using var shadow = new Shadow(context) { BlurStandardDeviation = 4, Color = ToVector4(Rgb(0x0B4C95, 0.55)) };
            shadow.SetInput(0, water, true);
            DrawEffect(context, shadow, new Vector2(-period, -3));
            context.DrawImage(water, new Vector2(-period, 0), null, InterpolationMode.NearestNeighbor, CompositeMode.SourceOver);
        });
    }
}
