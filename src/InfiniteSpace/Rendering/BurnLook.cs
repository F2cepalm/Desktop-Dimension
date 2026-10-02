using System.Windows;
using System.Windows.Media;
using Vortice.Direct2D1;
using Matrix3x2 = System.Numerics.Matrix3x2;

namespace InfiniteSpace.Rendering;

/// <summary>
/// The Burn theme: a deep red dark lit by a fire somewhere below. Embers stream upwards as glowing
/// streaks, flicker and die out, the firelight wavers, and the edges sink into shadow under an
/// ember-hot outline.
/// </summary>
internal sealed class BurnLook : SceneLook
{
    public static readonly Color Ink = Color.FromRgb(0xFF, 0xDB, 0xBE);

    private static readonly FrameStyle Style = new()
    {
        Line = Rgb(0xFF9A5C),
        LineWidth = 1.2,
        Glow = Rgb(0xFF4A12, 0.9),
        GlowBlur = 3,
        Shade = Rgb(0x000000, 0.85),
        ShadeBlur = 18,
    };

    /// <summary>Far to near: faster, bigger and longer streaks, all carried up and to the right.</summary>
    private static readonly Field[] Embers =
    [
        new(Cell: 34, Density: 0.40, Drift: new Vector(8, -26), Parallax: 0.012),
        new(Cell: 60, Density: 0.36, Drift: new Vector(14, -44), Parallax: 0.022),
        new(Cell: 112, Density: 0.30, Drift: new Vector(22, -70), Parallax: 0.036),
    ];

    private static readonly (double Size, double Spread, double Streak)[] EmberSizes = [(1.6, 0.8, 1.6), (2.4, 1.2, 2.2), (3.6, 1.6, 3.0)];

    private ID2D1Bitmap1? _ember;
    private ID2D1Bitmap1? _fire;

    protected override FrameStyle Frame => Style;

    protected override double BurstLife => 1.2;

    protected override bool Advance() => true;

    protected override void PrepareStills(ISpaceCanvas canvas, float scale)
    {
        _ember ??= PaintGlow(canvas, scale, 16,
            (0, Rgb(0xFFF4C0)), (0.2f, Rgb(0xFFC45A)), (0.5f, Rgb(0xFF6A22, 0.55)), (1, Rgb(0xC8200A, 0)));
        _fire ??= PaintGlow(canvas, scale, 256,
            (0, Rgb(0xFF7A2E, 0.55)), (0.5f, Rgb(0xE0401A, 0.2)), (1, Rgb(0xE0401A, 0)));
    }

    protected override void DrawBackdrop(ID2D1DeviceContext context, ID2D1Factory1 factory)
    {
        var a = Area;
        var all = R(new Rect(0, 0, World.Width, World.Height));
        using (var fill = Linear(context, new Point(0, a.Top), new Point(0, a.Bottom),
            (0, Rgb(0x150506)), (0.5f, Rgb(0x300A0A)), (0.85f, Rgb(0x571610)), (1, Rgb(0x6E2012))))
            context.FillRectangle(all, fill);
        using (var glow = Radial(context, new Point(a.Left + a.Width / 2, a.Bottom + a.Height * 0.1),
            Math.Max(a.Width * 0.85, 60), Math.Max(a.Height * 0.6, 60),
            (0, Rgb(0xFF6A2A, 0.35)), (0.55f, Rgb(0xC8321A, 0.12)), (1, Rgb(0xC8321A, 0))))
            context.FillRectangle(all, glow);
    }

    protected override void DrawMotion(ID2D1DeviceContext context, ID2D1Factory1 factory, Matrix3x2 origin)
    {
        var a = Area;

        // The unseen fire wavers.
        double flicker = 0.55 + 0.2 * Math.Sin(Time * 2.3) + 0.15 * Math.Sin(Time * 5.9 + 1.3) + 0.1 * Math.Sin(Time * 12.7 + 0.4);
        DrawSprite(context, origin, _fire!, new Point(a.Left + a.Width / 2, a.Bottom + a.Height * 0.05),
            a.Width * 1.3 + 40, a.Height * 0.9 + 40, 0, 0.45 * flicker);

        for (int layer = 0; layer < Embers.Length; layer++)
        {
            var field = Embers[layer];
            var (baseSize, spread, streak) = EmberSizes[layer];
            double heading = Math.Atan2(field.Drift.Y, field.Drift.X);
            foreach (var mote in Scatter(field, layer, 24))
            {
                uint s = mote.Seed;
                // Each ember lives a few seconds: it catches, glows, and goes out, then comes round again.
                double period = 2.5 + 3.5 * Unit(s, 0);
                double phase = Unit(s, 8);
                double u = Frac(Time / period + phase);
                double life = Smooth(0, 0.12, u) * (1 - Smooth(0.55, 1, u));
                if (life <= 0)
                    continue;
                double glow = life * (0.7 + 0.3 * Math.Sin(Time * (8 + 7 * Unit(s, 16)) + phase * 20));
                double sway = Math.Sin(Time * (0.7 + Unit(s, 24)) + phase * 6);
                var at = mote.At + new Vector(5 * sway, 0);
                double size = baseSize + spread * Unit(s, 16);
                DrawSprite(context, origin, _ember!, at, 4 * size * streak, 4 * size, heading + 0.15 * sway, glow);
            }
        }

        // A click: a shower of sparks that fly out, then drift up as they cool.
        foreach (var burst in Bursts)
        {
            double age = Time - burst.Start;
            double fade = 1 - age / BurstLife;
            double flash = 40 + 60 * Math.Sqrt(age / BurstLife);
            DrawSprite(context, origin, _fire!, burst.Origin, flash, flash, 0, 0.6 * fade * fade);
            double travel = (1 - Math.Exp(-age * 3)) / 3;
            for (int i = 0; i < 18; i++)
            {
                uint s = Mix(burst.Seed + (uint)i);
                double angle = Unit(s, 0) * Math.Tau;
                double speed = 50 + 90 * Unit(s, 8);
                var at = burst.Origin + new Vector(Math.Cos(angle), Math.Sin(angle)) * (speed * travel) + new Vector(0, -25 * age * age);
                double size = 2.5 + 1.5 * Unit(s, 16);
                double glow = fade * (0.75 + 0.25 * Math.Sin(Time * 14 + i));
                DrawSprite(context, origin, _ember!, at, 4 * size * 2, 4 * size, angle, glow);
            }
        }
    }

    protected override void DisposeResources()
    {
        _ember?.Dispose();
        _fire?.Dispose();
    }
}
