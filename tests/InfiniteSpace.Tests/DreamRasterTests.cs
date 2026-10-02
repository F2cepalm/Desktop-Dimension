using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class DreamRasterTests
{
    private const int Transparent = 0;
    private const int Black = unchecked((int)0xFF000000);
    private const int White = unchecked((int)0xFFFFFFFF);

    /// <summary>Rendered frames are also written here (scaled up), for eyeballing the look.</summary>
    private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "renders");

    private static readonly Point Centre = new(700, 400);

    [Fact]
    public void Frame_IsAStarryVoidWithAWhiteRim_AndTransparentOutside()
    {
        var raster = CreateRaster();

        Assert.True(raster.Render(new FrameState(12.3, new Vector(0, 0))));
        SavePng(raster, "dream-raster.png");

        var pixels = raster.Pixels;
        Assert.Equal(Transparent, pixels[0]);
        Assert.True(raster.IsInside(ToArt(Centre)));
        Assert.False(raster.IsInside(new Point(1, 1)));

        int stars = pixels.Count(p => p is not (Transparent or Black or White));
        int rim = pixels.Count(p => p == White);
        int voids = pixels.Count(p => p == Black);
        Assert.True(stars > 80, $"expected a field of stars, found {stars} star pixels");
        Assert.True(rim > 500, $"expected a rim around the space, found {rim} rim pixels");
        Assert.True(voids > stars * 10, "the void should dominate");
    }

    [Fact]
    public void StarsOnlyLandOnTheVoid()
    {
        var raster = CreateRaster();
        raster.Render(new FrameState(3, new Vector(0, 0)));

        // Every coloured pixel must have the void or other stars/rim around it, never plain transparency
        // on both sides horizontally (which would mean it was drawn outside the space).
        var p = raster.Pixels;
        for (int y = 0; y < raster.Height; y++)
        {
            for (int x = 1; x < raster.Width - 1; x++)
            {
                int c = p[y * raster.Width + x];
                if (c is Transparent or Black or White)
                    continue;
                Assert.False(p[y * raster.Width + x - 1] == Transparent && p[y * raster.Width + x + 1] == Transparent);
            }
        }
    }

    [Fact]
    public void StarField_MovesWithTheLookOffset()
    {
        var raster = CreateRaster();

        raster.Render(new FrameState(5, new Vector(0, 0)));
        var still = raster.Pixels.ToArray();
        Assert.True(raster.Render(new FrameState(5, new Vector(400, -250))));

        Assert.NotEqual(still, raster.Pixels);
    }

    [Fact]
    public void SameMoment_RendersNoChange()
    {
        var raster = CreateRaster();

        Assert.True(raster.Render(new FrameState(7, new Vector(10, 10))));
        Assert.False(raster.Render(new FrameState(7, new Vector(10, 10))));
    }

    [Fact]
    public void Click_SendsSparksAndARipple()
    {
        var raster = CreateRaster();
        raster.Render(new FrameState(20, new Vector(0, 0)));
        var before = raster.Pixels.ToArray();

        raster.AddBurst(ToArt(Centre - new Vector(120, 0)));
        raster.Render(new FrameState(20, new Vector(0, 0)));
        Assert.NotEqual(before, raster.Pixels);

        raster.Render(new FrameState(20.2, new Vector(0, 0)));
        SavePng(raster, "dream-raster-click.png");
    }

    [Fact]
    public void ErasedHole_IsTransparent()
    {
        var outline = SpaceGeometry.FromStroke(Blob(), (Vector)Centre);
        var holed = SpaceGeometry.Subtract(outline, SpaceGeometry.EraserStroke([Centre], new Vector(0, 0), radius: 40))!;
        var raster = new DreamRaster(holed, World(holed));

        raster.Render(new FrameState(1, new Vector(0, 0)));

        Assert.False(raster.IsInside(ToArt(Centre)));
        Assert.True(raster.IsInside(ToArt(Centre + new Vector(90, 0))));
    }

    private static Point ToArt(Point screen) => ToArt(screen, World(SpaceGeometry.FromStroke(Blob(), (Vector)Centre)));

    private static Point ToArt(Point screen, Rect world) =>
        new((screen.X - world.X) / DreamStyle.ArtPixel, (screen.Y - world.Y) / DreamStyle.ArtPixel);

    private static DreamRaster CreateRaster()
    {
        var outline = SpaceGeometry.FromStroke(Blob(), (Vector)Centre);
        return new DreamRaster(outline, World(outline));
    }

    /// <summary>Same art-grid alignment as SpaceWindow.</summary>
    private static Rect World(Geometry outline)
    {
        var bounds = outline.Bounds;
        bounds.Inflate(14, 14);
        double grid = DreamStyle.ArtPixel;
        double left = Math.Floor(bounds.Left / grid) * grid, top = Math.Floor(bounds.Top / grid) * grid;
        return new Rect(left, top, Math.Ceiling(bounds.Right / grid) * grid - left, Math.Ceiling(bounds.Bottom / grid) * grid - top);
    }

    /// <summary>A lopsided freehand-like loop, about 420×300 DIPs.</summary>
    private static List<Point> Blob() =>
        [.. Enumerable.Range(0, 360).Select(i =>
        {
            double a = i / 360.0 * Math.Tau;
            double r = 140 + 35 * Math.Sin(3 * a) + 18 * Math.Cos(5 * a + 1);
            return new Point(1.4 * r * Math.Cos(a), r * Math.Sin(a));
        })];

    private static void SavePng(DreamRaster raster, string fileName)
    {
        const int zoom = 4;
        int width = raster.Width * zoom, height = raster.Height * zoom;
        var scaled = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                scaled[y * width + x] = raster.Pixels[y / zoom * raster.Width + x / zoom];

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, scaled, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(OutputDirectory);
        using var file = File.Create(Path.Combine(OutputDirectory, fileName));
        encoder.Save(file);
    }
}
