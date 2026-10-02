using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace InfiniteSpace.Shell;

/// <summary>
/// The reshaping arrows: small round handles with a white double-headed arrow showing which way a
/// drag stretches (↔ ↕ ⤡ ⤢), and a four-way one (✥) for moving. Dark with a light edge, so they
/// read on any theme and any app.
/// </summary>
internal static class ArrowHandle
{
    /// <summary>Diameter in DIPs.</summary>
    public const double Size = 18;

    private const double Art = 16; // the arrows are drawn in a 16×16 box

    private static readonly Brush Backing = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E)));
    private static readonly Brush Edge = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)));

    /// <summary>A horizontal double-headed arrow, 14 long with heads 8 tall, centred in the box.</summary>
    private static readonly Geometry Arrow = Freeze(Geometry.Parse("M1,8 L5,4 L5,6.5 L11,6.5 L11,4 L15,8 L11,12 L11,9.5 L5,9.5 L5,12 Z"));

    /// <summary>The angle of the arrow for a side or corner: 0 ↔, 90 ↕, 45 ⤡ (top-left to bottom-right), 135 ⤢.</summary>
    public static double AngleFor(BlockGrip grip) => grip switch
    {
        BlockGrip.Left or BlockGrip.Right => 0,
        BlockGrip.Top or BlockGrip.Bottom => 90,
        BlockGrip.Left | BlockGrip.Top or BlockGrip.Right | BlockGrip.Bottom => 45,
        _ => 135,
    };

    /// <summary>A handle whose arrow points along <paramref name="angle"/> degrees (clockwise from ↔).</summary>
    public static FrameworkElement Stretch(double angle)
    {
        var arrow = Arrow.Clone();
        arrow.Transform = new RotateTransform(angle, Art / 2, Art / 2);
        arrow.Freeze();
        return Handle(arrow);
    }

    /// <summary>A handle with a four-way arrow, for moving.</summary>
    public static FrameworkElement Move()
    {
        var upright = Arrow.Clone();
        upright.Transform = new RotateTransform(90, Art / 2, Art / 2);
        var both = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { Arrow, upright } };
        both.Freeze();
        return Handle(both);
    }

    private static FrameworkElement Handle(Geometry arrow) => new Grid
    {
        Width = Size,
        Height = Size,
        Children =
        {
            new Ellipse { Fill = Backing, Stroke = Edge, StrokeThickness = 1 },
            new Path
            {
                Data = arrow,
                Fill = Brushes.White,
                Width = Art,
                Height = Art,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new ScaleTransform(0.8, 0.8, Art / 2, Art / 2),
            },
        },
    };

    /// <summary>
    /// The same handle as a picture for the GPU overlays (see <see cref="GpuOverlay"/>), its arrow
    /// along <paramref name="angle"/> degrees (clockwise from ↔).
    /// </summary>
    public static OverlayPicture Picture(Rendering.GpuCompositor gpu, double angle) =>
        OverlayPicture.Paint(gpu, Size, Size, context =>
        {
            var origin = context.Transform;
            using var backing = context.CreateSolidColorBrush(new Vortice.Mathematics.Color4(0x1E / 255f, 0x1E / 255f, 0x1E / 255f, 0xE6 / 255f));
            using var edge = context.CreateSolidColorBrush(new Vortice.Mathematics.Color4(1, 1, 1, 0xCC / 255f));
            using var white = context.CreateSolidColorBrush(new Vortice.Mathematics.Color4(1, 1, 1, 1));
            var centre = new System.Numerics.Vector2((float)(Size / 2), (float)(Size / 2));
            var circle = new Vortice.Direct2D1.Ellipse(centre, (float)(Size / 2 - 0.5), (float)(Size / 2 - 0.5));
            context.FillEllipse(circle, backing);
            context.DrawEllipse(circle, edge, 1);

            // The arrow in its 16×16 box: turned about the box's middle, shrunk to 0.8, and centred.
            var middle = new System.Numerics.Vector2((float)(Art / 2), (float)(Art / 2));
            context.Transform =
                System.Numerics.Matrix3x2.CreateRotation((float)(angle * Math.PI / 180), middle) *
                System.Numerics.Matrix3x2.CreateScale(0.8f, middle) *
                System.Numerics.Matrix3x2.CreateTranslation((float)((Size - Art) / 2), (float)((Size - Art) / 2)) *
                origin;
            using var arrow = gpu.D2DFactory.CreatePathGeometry();
            using (var sink = arrow.Open())
            {
                sink.BeginFigure(new(1, 8), Vortice.Direct2D1.FigureBegin.Filled);
                sink.AddLines([new(5, 4), new(5, 6.5f), new(11, 6.5f), new(11, 4), new(15, 8), new(11, 12), new(11, 9.5f), new(5, 9.5f), new(5, 12)]);
                sink.EndFigure(Vortice.Direct2D1.FigureEnd.Closed);
                sink.Close();
            }
            context.FillGeometry(arrow, white);
            context.Transform = origin;
        });

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
