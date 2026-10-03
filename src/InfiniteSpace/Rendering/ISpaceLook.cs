using System.Windows;
using System.Windows.Media;

namespace InfiniteSpace.Rendering;

/// <summary>How one space is drawn in a theme: its animated picture, its shape for the mouse, and click reactions.</summary>
internal interface ISpaceLook : IDisposable
{
    /// <summary>Whether a click burst (or other user-driven motion) is still playing.</summary>
    bool HasBursts { get; }

    /// <param name="outline">Outline in screen DIPs.</param>
    /// <param name="world">Screen rectangle (DIPs) the window covers, aligned to the art-pixel grid.</param>
    void SetShape(Geometry outline, Rect world);

    /// <summary>
    /// A new shape while it is changing under the user's hand (a reshape, every frame): a look may
    /// take shortcuts that only show while it moves. The shape is set again, not as a draft, once it rests.
    /// </summary>
    void SetShape(Geometry outline, Rect world, bool draft) => SetShape(outline, world);

    /// <summary>
    /// The same shape, moved: <paramref name="outline"/> and <paramref name="world"/> are the last ones
    /// shifted by one offset. Nothing drawn relative to the window is redone.
    /// </summary>
    void Move(Geometry outline, Rect world);

    /// <summary>Advances to this frame.</summary>
    /// <returns>Whether it looks different from the last frame (unchanged frames are not presented).</returns>
    bool Render(FrameState frame);

    /// <summary>
    /// Whether <see cref="Render"/> only works in the look's own memory (no GPU, nothing shared), so looks
    /// of several spaces can render a frame side by side on other threads first; asked again for the same
    /// frame, such a look then answers from what it drew.
    /// </summary>
    bool RendersOnAnyThread => false;

    /// <summary>
    /// Room its own buffers make beyond the shape's size while the space is picked up to be reshaped (see
    /// <see cref="SpaceSurface.ReshapeRoom"/>); a look without buffers of its own ignores it.
    /// </summary>
    double ReshapeRoom { get => 1; set { } }

    /// <summary>Hands the current frame to the compositor.</summary>
    void Present(ISpaceCanvas canvas);

    /// <summary>Whether a screen DIP point is on the space itself (not its transparent surroundings).</summary>
    bool IsInside(Point screen);

    /// <summary>Starts a click reaction at a screen DIP point.</summary>
    void AddBurst(Point screen);
}
