using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;

namespace InfiniteSpace.Rendering;

/// <summary>The Dreamy theme: pixel art from a <see cref="DreamRaster"/>, scaled up on the GPU.</summary>
internal sealed class DreamyLook : ISpaceLook
{
    private DreamRaster _raster = null!;
    private Rect _world;

    public bool HasBursts => _raster.HasBursts;

    public void SetShape(Geometry outline, Rect world)
    {
        _world = world;
        _raster = new DreamRaster(outline, world, recycle: _raster);
    }

    public void Move(Geometry outline, Rect world)
    {
        _world = world;
        _raster.Move(world); // the stars are in screen space: a moved window sees other ones
    }

    public bool Render(FrameState frame) => _raster.Render(frame);

    public void Present(ISpaceCanvas canvas) =>
        canvas.ShowArt(_raster.Pixels, _raster.Width, _raster.Height, (float)(DreamStyle.ArtPixel * Dpi.Scale));

    public bool IsInside(Point screen) => _raster.IsInside(ToArt(screen));

    public void AddBurst(Point screen) => _raster.AddBurst(ToArt(screen));

    public void Dispose()
    {
    }

    private Point ToArt(Point screen) =>
        new((screen.X - _world.X) / DreamStyle.ArtPixel, (screen.Y - _world.Y) / DreamStyle.ArtPixel);
}
