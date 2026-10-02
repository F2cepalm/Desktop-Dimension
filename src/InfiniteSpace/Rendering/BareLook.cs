using System.Windows;
using System.Windows.Media;

namespace InfiniteSpace.Rendering;

/// <summary>Screen notes' look: nothing at all, so only their paint and text show over the app.</summary>
internal sealed class BareLook : ISpaceLook
{
    private Geometry _outline = Geometry.Empty;

    /// <inheritdoc/>
    public bool HasBursts => false;

    /// <inheritdoc/>
    public void SetShape(Geometry outline, Rect world) => _outline = outline;

    /// <inheritdoc/>
    public void Move(Geometry outline, Rect world) => _outline = outline;

    /// <inheritdoc/>
    public bool Render(FrameState frame) => false;

    /// <inheritdoc/>
    public void Present(ISpaceCanvas canvas) => canvas.HideArt();

    /// <inheritdoc/>
    public bool IsInside(Point screen) => _outline.FillContains(screen);

    /// <inheritdoc/>
    public void AddBurst(Point screen)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
