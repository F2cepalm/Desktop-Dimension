using System.Windows;
using System.Windows.Media;
using Vortice.Direct2D1;
using Matrix3x2 = System.Numerics.Matrix3x2;

namespace InfiniteSpace.Rendering;

/// <summary>
/// The Sunday theme: a hazy late-afternoon sky, rose at the top warming to pale gold, with a low sun
/// glowing through. Soft wisps of haze drift across and the sun shimmers. A light theme, so paint
/// and text are a deep plum.
/// </summary>
/// <remarks>Everything moves slowly, so it only needs 30 frames per second.</remarks>
internal sealed class SundayLook : SceneLook
{
    public static readonly Color Ink = Color.FromRgb(0x5A, 0x26, 0x38);

    private const double FrameRate = 30;

    private static readonly FrameStyle Style = new()
    {
        Line = Rgb(0xFFF9F2),
        LineWidth = 1.5,
        Glow = Rgb(0xE9727E, 0.6),
        GlowWidth = 3,
        GlowBlur = 3,
        Shade = Rgb(0xC45A6A, 0.22),
        ShadeBlur = 12,
    };

    private static readonly Field[] Wisps =
    [
        new(Cell: 130, Density: 0.45, Drift: new Vector(5, 0), Parallax: 0.008),
        new(Cell: 210, Density: 0.40, Drift: new Vector(9, 0), Parallax: 0.018),
    ];

    private ID2D1Bitmap1? _wisp;
    private ID2D1Bitmap1? _sun;

    protected override FrameStyle Frame => Style;

    protected override double BurstLife => 1.1;

    protected override bool Advance() => Stepped(FrameRate) | LookMoved(Wisps[^1].Parallax);

    private Point Sun => new(Area.Left + Area.Width * 0.74, Area.Top + Area.Height * 0.30);

    private double SunRadius => Math.Max(Math.Max(Area.Width, Area.Height) * 0.65, 60);

    protected override void PrepareStills(ISpaceCanvas canvas, float scale)
    {
        _wisp ??= PaintGlow(canvas, scale, 64,
            (0, Rgb(0xFFF6F0, 0.9)), (0.5f, Rgb(0xFFEFE8, 0.45)), (1, Rgb(0xFFEFE8, 0)));
        _sun ??= PaintGlow(canvas, scale, 128,
            (0, Rgb(0xFFFBEA)), (0.15f, Rgb(0xFFF1CC, 0.8)), (0.45f, Rgb(0xFFDDA8, 0.3)), (1, Rgb(0xFFDDA8, 0)));
    }

    protected override void DrawBackdrop(ID2D1DeviceContext context, ID2D1Factory1 factory)
    {
        var a = Area;
        var all = R(new Rect(0, 0, World.Width, World.Height));
        using (var sky = Linear(context, new Point(0, a.Top), new Point(0, a.Bottom),
            (0, Rgb(0xEE9499)), (0.35f, Rgb(0xF4AC9F)), (0.68f, Rgb(0xF8C8A2)), (1, Rgb(0xFBE3AE))))
            context.FillRectangle(all, sky);
        using (var sun = Radial(context, Sun, SunRadius, SunRadius,
            (0, Rgb(0xFFF8E4, 0.9)), (0.12f, Rgb(0xFFEFC8, 0.7)), (0.42f, Rgb(0xFFD9A8, 0.28)), (1, Rgb(0xFFD9A8, 0))))
            context.FillRectangle(all, sun);
        using (var haze = Linear(context, new Point(0, a.Top + a.Height * 0.62), new Point(0, a.Bottom),
            (0, Rgb(0xFFF1DA, 0)), (1, Rgb(0xFFF1DA, 0.5))))
            context.FillRectangle(all, haze);
    }

    protected override void DrawMotion(ID2D1DeviceContext context, ID2D1Factory1 factory, Matrix3x2 origin)
    {
        double shimmer = SunRadius * (0.9 + 0.05 * Math.Sin(Time * 0.5));
        DrawSprite(context, origin, _sun!, Sun, shimmer, shimmer, 0, 0.18 + 0.1 * Math.Sin(Time * 0.3));

        for (int layer = 0; layer < Wisps.Length; layer++)
        {
            foreach (var mote in Scatter(Wisps[layer], layer, 160))
            {
                uint s = mote.Seed;
                double width = (layer == 0 ? 90 : 160) + 80 * Unit(s, 0);
                double height = width * (0.10 + 0.05 * Unit(s, 8));
                double bob = 3 * Math.Sin(Time * 0.15 + Unit(s, 16) * Math.Tau);
                double opacity = (layer == 0 ? 0.32 : 0.26) * (0.7 + 0.3 * Math.Sin(Time * 0.2 + Unit(s, 24) * Math.Tau));
                DrawSprite(context, origin, _wisp!, mote.At + new Vector(0, bob), width, height, 0, opacity);
            }
        }

        // A click: a soft bloom of light.
        foreach (var burst in Bursts)
        {
            double life = (Time - burst.Start) / BurstLife;
            double size = 20 + 140 * Math.Sqrt(life);
            DrawSprite(context, origin, _sun!, burst.Origin, size, size, 0, 0.75 * Math.Pow(1 - life, 1.5));
        }
    }

    protected override void DisposeResources()
    {
        _wisp?.Dispose();
        _sun?.Dispose();
    }
}
