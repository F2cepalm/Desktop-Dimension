using System.Windows;
using System.Windows.Media;
using Vortice.Direct2D1;
using Matrix3x2 = System.Numerics.Matrix3x2;

namespace InfiniteSpace.Rendering;

/// <summary>
/// The Glory theme: a dark golden hall lit from above. Soft shafts of light sway and breathe over
/// curtain folds, golden motes and out-of-focus discs drift upwards, and the edges fall into shadow
/// under a glowing gilt outline.
/// </summary>
internal sealed class GloryLook : SceneLook
{
    public static readonly Color Ink = Color.FromRgb(0xFF, 0xF1, 0xD6);

    private static readonly FrameStyle Style = new()
    {
        Line = Rgb(0xFFF2CF),
        LineWidth = 1.3,
        Glow = Rgb(0xFFC65C, 0.95),
        Shade = Rgb(0x000000, 0.8),
        ShadeBlur = 16,
    };

    /// <summary>Far to near: small sharp motes, bigger ones, then large out-of-focus discs.</summary>
    private static readonly Field[] Motes =
    [
        new(Cell: 42, Density: 0.42, Drift: new Vector(1.5, -5), Parallax: 0.010),
        new(Cell: 76, Density: 0.40, Drift: new Vector(2.5, -9), Parallax: 0.020),
        new(Cell: 150, Density: 0.32, Drift: new Vector(4, -14), Parallax: 0.035),
    ];

    private ID2D1Bitmap1? _mote;
    private ID2D1Bitmap1? _bokeh;
    private ID2D1Bitmap1? _beam;

    protected override FrameStyle Frame => Style;

    protected override double BurstLife => 1.4;

    protected override bool Advance() => true;

    protected override void PrepareStills(ISpaceCanvas canvas, float scale)
    {
        _mote ??= PaintGlow(canvas, scale, 16,
            (0, Rgb(0xFFF8E0)), (0.18f, Rgb(0xFFE39A, 0.95)), (0.45f, Rgb(0xFFC24D, 0.35)), (1, Rgb(0xFFB030, 0)));
        _bokeh ??= PaintGlow(canvas, scale, 64,
            (0, Rgb(0xFFE7A6, 0.55)), (0.55f, Rgb(0xFFD47A, 0.45)), (0.8f, Rgb(0xF2B550, 0.22)), (1, Rgb(0xF2B550, 0)));
        // A shaft of light: the lower half of a tall, thin radial glow, brightest where it enters.
        _beam ??= canvas.Paint((int)Math.Ceiling(64 * scale), (int)Math.Ceiling(512 * scale), scale, (context, _) =>
        {
            using var brush = Radial(context, new Point(32, 0), 32, 512,
                (0, Rgb(0xFFF0C8)), (0.45f, Rgb(0xFFD88E, 0.45)), (1, Rgb(0xFFD88E, 0)));
            context.FillRectangle(new Vortice.RawRectF(0, 0, 64, 512), brush);
        });
    }

    protected override void DrawBackdrop(ID2D1DeviceContext context, ID2D1Factory1 factory)
    {
        var a = Area;
        var all = R(new Rect(0, 0, World.Width, World.Height));
        using (var fill = Linear(context, new Point(0, a.Top), new Point(0, a.Bottom),
            (0, Rgb(0x75593A)), (0.45f, Rgb(0x3B2B1A)), (1, Rgb(0x110C07))))
            context.FillRectangle(all, fill);

        // Curtain folds: two soft shadings across the width, at pitches that don't line up.
        using (var folds = Linear(context, new Point(a.Left, 0), new Point(a.Left + 29, 0), ExtendMode.Mirror,
            (0, Rgb(0x000000, 0.22)), (1, Rgb(0xFFE2A0, 0.05))))
            context.FillRectangle(all, folds);
        using (var folds = Linear(context, new Point(a.Left + 7, 0), new Point(a.Left + 54, 0), ExtendMode.Mirror,
            (0, Rgb(0x000000, 0)), (1, Rgb(0x000000, 0.18))))
            context.FillRectangle(all, folds);

        // Light from above.
        using (var light = Radial(context, new Point(a.Left + a.Width / 2, a.Top - a.Height * 0.05),
            Math.Max(a.Width * 0.8, 80), Math.Max(a.Height, 80),
            (0, Rgb(0xFFD98E, 0.55)), (0.5f, Rgb(0xE3A650, 0.18)), (1, Rgb(0xE3A650, 0))))
            context.FillRectangle(all, light);
    }

    protected override void DrawMotion(ID2D1DeviceContext context, ID2D1Factory1 factory, Matrix3x2 origin)
    {
        var a = Area;

        // Shafts of light fanning out from above, swaying and breathing.
        const int beams = 4;
        for (int i = 0; i < beams; i++)
        {
            double u = (i + 0.5) / beams + 0.05 * Math.Sin(Time * 0.11 + i * 2.3);
            double length = a.Height * 1.25 + 40;
            double width = 38 + 24 * (i * 37 % 5) / 4.0;
            double angle = (0.5 - u) * 0.5 + 0.04 * Math.Sin(Time * 0.17 + i * 1.3);
            double opacity = 0.35 + 0.25 * (0.5 + 0.5 * Math.Sin(Time * 0.45 + i * 1.9));
            var top = new Point(a.Left + a.Width * u, a.Top - 12);
            DrawSprite(context, origin, _beam!, top + Rotate(new Vector(0, length / 2), angle), width, length, angle, opacity);
        }

        for (int layer = 0; layer < Motes.Length; layer++)
        {
            foreach (var mote in Scatter(Motes[layer], layer, 40))
            {
                uint s = mote.Seed;
                double phase = Unit(s, 0) * Math.Tau;
                var at = mote.At + new Vector(5 * Math.Sin(Time * (0.25 + 0.3 * Unit(s, 8)) + phase), 0);
                double twinkle = 0.55 + 0.45 * Math.Sin(Time * (0.8 + 1.2 * Unit(s, 16)) + phase * 3);
                switch (layer)
                {
                    case 0:
                        double small = 8 + 4 * Unit(s, 24);
                        DrawSprite(context, origin, _mote!, at, small, small, 0, 0.6 * twinkle);
                        break;
                    case 1:
                        double mid = 13 + 8 * Unit(s, 24);
                        DrawSprite(context, origin, _mote!, at, mid, mid, 0, 0.8 * twinkle);
                        break;
                    default:
                        double big = 24 + 22 * Unit(s, 24);
                        DrawSprite(context, origin, _bokeh!, at, big, big, 0, 0.2 + 0.2 * twinkle);
                        break;
                }
            }
        }

        // A click: a flash of light and a spray of golden motes that float up as they fade.
        foreach (var burst in Bursts)
        {
            double age = Time - burst.Start;
            double fade = 1 - age / BurstLife;
            double flash = 30 + 90 * Math.Sqrt(age / BurstLife);
            DrawSprite(context, origin, _mote!, burst.Origin, flash, flash, 0, 0.8 * fade * fade);
            double travel = (1 - Math.Exp(-age * 3)) / 3;
            for (int i = 0; i < 16; i++)
            {
                uint s = Mix(burst.Seed + (uint)i);
                double angle = Unit(s, 0) * Math.Tau;
                double speed = 40 + 90 * Unit(s, 8);
                var at = burst.Origin + new Vector(Math.Cos(angle), Math.Sin(angle)) * (speed * travel) + new Vector(0, -14 * age);
                double size = 8 + 6 * Unit(s, 16);
                DrawSprite(context, origin, _mote!, at, size, size, 0, fade);
            }
        }
    }

    protected override void DisposeResources()
    {
        _mote?.Dispose();
        _bokeh?.Dispose();
        _beam?.Dispose();
    }
}
