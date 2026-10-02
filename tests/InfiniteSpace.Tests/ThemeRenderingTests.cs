using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

/// <summary>What each theme actually paints, drawn on the GPU offscreen and read back.</summary>
public sealed class ThemeRenderingTests : IDisposable
{
    private readonly GpuCompositor _gpu = new();

    /// <summary>A wobbly blob about 400×300 DIPs, centred on (300, 250).</summary>
    internal static Geometry Blob { get; } = SpaceGeometry.FromStroke(
        [.. Enumerable.Range(0, 360).Select(i =>
        {
            double a = i / 360.0 * Math.Tau;
            double r = 150 + 35 * Math.Sin(3 * a) + 18 * Math.Cos(5 * a + 1);
            return new Point(1.3 * r * Math.Cos(a), r * Math.Sin(a));
        })],
        new Vector(300, 250));

    [Theory]
    [InlineData("Dreamy")]
    [InlineData("Claudish")]
    [InlineData("Glory")]
    [InlineData("Burn")]
    [InlineData("Sunday")]
    [InlineData("Parallax")]
    [InlineData("Ocean")]
    [InlineData("Void")]
    public void EveryTheme_FillsItsShape_AndLeavesTheRestClear(string theme)
    {
        var picture = Render(_gpu, theme);

        Assert.Equal(255, picture.At(new Point(300, 250)).A);
        Assert.Equal(255, picture.At(new Point(200, 300)).A);
        Assert.Equal(0, picture.At(picture.World.TopLeft + new Vector(1, 1)).A);   // window corner, far from the shape
        Assert.Equal(0, picture.At(new Point(100, 90)).A);                      // well off its upper left edge
    }

    [Fact]
    public void Void_IsBlackInside_WithAWhiteOutline()
    {
        var picture = Render(_gpu, Themes.Void);

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(new Point(300, 250)));
        // The rightmost point of the blob: r(0) = 150 + 18·cos(1).
        var edge = new Point(300 + 1.3 * (150 + 18 * Math.Cos(1)), 250);
        Assert.True(picture.Brightest(edge, 2) > 240, "the outline is white");
    }

    [Fact]
    public void Claudish_IsClayInside()
    {
        var picture = Render(_gpu, Themes.Claudish);
        var c = picture.At(new Point(300, 250));

        Assert.InRange(c.R, ClaudishStyle.Clay.R - 2, ClaudishStyle.Clay.R + 2);
        Assert.InRange(c.G, ClaudishStyle.Clay.G - 2, ClaudishStyle.Clay.G + 2);
        Assert.InRange(c.B, ClaudishStyle.Clay.B - 2, ClaudishStyle.Clay.B + 2);
    }

    [Fact]
    public void ThemesHaveTheirColours()
    {
        var middle = new Rect(250, 200, 100, 100);

        var glory = Render(_gpu, Themes.Glory).Average(middle);
        Assert.True(glory.R > glory.B + 20 && glory.R < 200, $"Glory is dark gold: {glory}");

        var burn = Render(_gpu, Themes.Burn).Average(middle);
        Assert.True(burn.R > burn.G * 2 && burn.R > burn.B * 2 && burn.R < 160, $"Burn is dark red: {burn}");

        var sunday = Render(_gpu, Themes.Sunday).Average(middle);
        Assert.True(sunday.R > sunday.G && sunday.G > sunday.B && sunday.B > 140, $"Sunday is a light warm sky: {sunday}");

        var ocean = Render(_gpu, Themes.Ocean).Average(middle);
        Assert.True(ocean.B > ocean.R + 60 && ocean.B > ocean.G, $"Ocean is blue: {ocean}");

        var parallax = Render(_gpu, Themes.Parallax);
        var wall = parallax.Average(new Rect(205, 160, 20, 20));    // upper left: the grey wall
        Assert.True(Math.Abs(wall.R - wall.G) < 16 && wall.R > 170, $"Parallax has a grey wall: {wall}");
        var moss = parallax.Average(new Rect(400, 360, 30, 30));    // lower right: forest green
        Assert.True(moss.G > moss.R + 10 && moss.G > moss.B + 10 && moss.G < 150, $"and a dark green corner: {moss}");
    }

    [Fact]
    public void Ocean_Rolls()
    {
        var before = Render(_gpu, Themes.Ocean, time: 10);
        var after = Render(_gpu, Themes.Ocean, time: 11);

        Assert.NotEqual(before.Pixels, after.Pixels);
    }

    [Fact]
    public void Parallax_Layers_ShiftWithTheCursor()
    {
        var still = Render(_gpu, Themes.Parallax, look: new Vector());
        var moved = Render(_gpu, Themes.Parallax, look: new Vector(600, 300));

        Assert.NotEqual(still.Pixels, moved.Pixels);
        Assert.Equal(still.At(new Point(215, 170)), moved.At(new Point(215, 170)));  // the wall stays put
    }

    public void Dispose() => _gpu.Dispose();

    /// <summary>One frame of a theme for <see cref="Blob"/>, as <see cref="SpaceWindow"/> would lay it out.</summary>
    internal static Picture Render(GpuCompositor gpu, string theme, double time = 10, Vector look = default)
    {
        var bounds = Blob.Bounds;
        bounds.Inflate(14, 14);
        const double grid = DreamStyle.ArtPixel;
        double left = Math.Floor(bounds.Left / grid) * grid, top = Math.Floor(bounds.Top / grid) * grid;
        var world = new Rect(left, top, Math.Ceiling(bounds.Right / grid) * grid - left, Math.Ceiling(bounds.Bottom / grid) * grid - top);

        float scale = (float)Dpi.Scale;
        int width = (int)Math.Round(world.Width * scale), height = (int)Math.Round(world.Height * scale);
        using var canvas = new OffscreenCanvas(gpu);
        using var space = Themes.CreateLook(theme);
        space.SetShape(Blob, world);
        space.Render(new FrameState(time, look));
        space.Present(canvas);
        return new Picture(canvas.Snapshot(width, height, scale), width, height, world, scale);
    }

    /// <summary>A rendered space: premultiplied BGRA pixels over its window rectangle.</summary>
    internal sealed record Picture(int[] Pixels, int Width, int Height, Rect World, double Scale)
    {
        /// <summary>The pixel under a screen DIP point (premultiplied, as stored).</summary>
        public Color At(Point screen)
        {
            int x = (int)((screen.X - World.X) * Scale), y = (int)((screen.Y - World.Y) * Scale);
            int p = Pixels[y * Width + x];
            return Color.FromArgb((byte)(p >> 24), (byte)(p >> 16), (byte)(p >> 8), (byte)p);
        }

        /// <summary>The brightest red channel within <paramref name="radius"/> DIPs of a point.</summary>
        public int Brightest(Point screen, double radius)
        {
            int best = 0;
            for (double dy = -radius; dy <= radius; dy += 0.5)
                for (double dx = -radius; dx <= radius; dx += 0.5)
                    best = Math.Max(best, At(screen + new Vector(dx, dy)).R);
            return best;
        }

        /// <summary>The mean colour over a screen DIP rectangle (opaque areas).</summary>
        public (int R, int G, int B) Average(Rect screen)
        {
            long r = 0, g = 0, b = 0, n = 0;
            for (double y = screen.Top; y < screen.Bottom; y += 1)
                for (double x = screen.Left; x < screen.Right; x += 1)
                {
                    var c = At(new Point(x, y));
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    n++;
                }
            return ((int)(r / n), (int)(g / n), (int)(b / n));
        }
    }
}
