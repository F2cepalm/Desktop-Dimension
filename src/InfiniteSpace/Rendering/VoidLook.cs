using System.Windows;
using System.Windows.Media;
using Vortice.Direct2D1;
using Ellipse = Vortice.Direct2D1.Ellipse;
using Matrix3x2 = System.Numerics.Matrix3x2;

namespace InfiniteSpace.Rendering;

/// <summary>
/// The Void theme: pure black inside a crisp white outline. Nothing moves, so nothing is drawn
/// between clicks; a click sends out one thin white ring.
/// </summary>
internal sealed class VoidLook : SceneLook
{
    public static readonly Color Ink = Colors.White;

    private static readonly FrameStyle Style = new() { Line = Colors.White, LineWidth = 2 };

    private ID2D1SolidColorBrush? _ring;

    protected override FrameStyle Frame => Style;

    protected override bool HasMotionLayer => false;

    protected override double BurstLife => 0.7;

    protected override bool Advance() => false;

    protected override void DrawBackdrop(ID2D1DeviceContext context, ID2D1Factory1 factory)
    {
        using var black = Solid(context, Colors.Black);
        context.FillRectangle(R(new Rect(0, 0, World.Width, World.Height)), black);
    }

    protected override void DrawMotion(ID2D1DeviceContext context, ID2D1Factory1 factory, Matrix3x2 origin)
    {
        _ring ??= Solid(context, Colors.White);
        foreach (var burst in Bursts)
        {
            double age = Time - burst.Start;
            double life = age / BurstLife;
            float radius = (float)(4 + 50 * (1 - Math.Exp(-age * 4)));
            _ring.Opacity = (float)(0.9 * (1 - life) * (1 - life));
            context.DrawEllipse(new Ellipse(V(burst.Origin), radius, radius), _ring, 1.5f);
        }
    }

    protected override void DisposeResources() => _ring?.Dispose();
}
