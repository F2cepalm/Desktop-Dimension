using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;

namespace InfiniteSpace.Rendering;

/// <summary>The Dreamy theme: pixel art from a <see cref="DreamRaster"/>, scaled up on the GPU.</summary>
internal sealed class DreamyLook : ISpaceLook
{
    private DreamRaster _raster = null!;
    private Rect _world;
    private double _renderedAt = double.NaN; // the frame last drawn, and whether it changed (see Render)
    private bool _renderedChanged;

    public bool HasBursts => _raster.HasBursts;

    public void SetShape(Geometry outline, Rect world)
    {
        _world = world;
        _raster = new DreamRaster(outline, world, recycle: _raster, ReshapeRoom);
        _renderedAt = double.NaN;
    }

    public double ReshapeRoom { get; set; } = 1;

    public void Move(Geometry outline, Rect world)
    {
        _world = world;
        _raster.Move(world); // the stars are in screen space: a moved window sees other ones
        _renderedAt = double.NaN;
    }

    /// <summary>The raster is plain memory: spaces' frames are drawn side by side (see <see cref="Universe"/>).</summary>
    public bool RendersOnAnyThread => true;

    public bool Render(FrameState frame)
    {
        if (frame.Time == _renderedAt)
        {
            // Drawn ahead for this very frame: whether it changed is told once.
            bool changed = _renderedChanged;
            _renderedChanged = false;
            return changed;
        }
        _renderedAt = frame.Time;
        return _renderedChanged = _raster.Render(frame);
    }

    public void Present(ISpaceCanvas canvas) =>
        canvas.ShowArt(_raster.Pixels, _raster.Width, _raster.Height, (float)(DreamStyle.ArtPixel * Dpi.Scale));

    public bool IsInside(Point screen) => _raster.IsInside(ToArt(screen));

    public void AddBurst(Point screen)
    {
        _raster.AddBurst(ToArt(screen));
        _renderedAt = double.NaN; // the splash starts with the next frame drawn
    }

    public void Dispose()
    {
    }

    private Point ToArt(Point screen) =>
        new((screen.X - _world.X) / DreamStyle.ArtPixel, (screen.Y - _world.Y) / DreamStyle.ArtPixel);
}
