using Vortice.Direct2D1;

namespace InfiniteSpace.Rendering;

/// <summary>
/// Where a look draws one space, bottom to top: a still backdrop, the moving picture, a still frame.
/// On screen that is a <see cref="SpaceSurface"/>; tests draw offscreen to read the pixels back.
/// </summary>
/// <remarks>
/// Draw callbacks work in DIPs from the window's top-left, on a cleared (transparent) canvas at the
/// device's resolution.
/// </remarks>
internal interface ISpaceCanvas
{
    /// <summary>Shows a whole art-pixel frame (premultiplied BGRA), each art pixel <paramref name="scale"/> device pixels wide.</summary>
    void ShowArt(int[] pixels, int width, int height, float scale);

    /// <summary>Draws the whole moving picture with Direct2D.</summary>
    void DrawVector(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw);

    /// <summary>Draws the still layer under the moving picture.</summary>
    void DrawBackdrop(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw);

    /// <summary>Draws the still layer over the moving picture.</summary>
    void DrawFrame(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw);

    /// <summary>Takes the moving picture away (until it is drawn again).</summary>
    void HideArt();

    /// <summary>
    /// Paints an offscreen picture at device resolution, for sprites, textures and effect inputs drawn
    /// later from a draw callback. The caller owns the bitmap.
    /// </summary>
    /// <remarks>Call it outside draw callbacks: it has its own target.</remarks>
    ID2D1Bitmap1 Paint(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw);

    /// <summary>
    /// Like <see cref="Paint"/>, reusing <paramref name="bitmap"/> when it can (it may be bigger than
    /// asked for; the rest is transparent), for pictures painted again for every new shape.
    /// </summary>
    void Repaint(ref ID2D1Bitmap1? bitmap, int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        bitmap?.Dispose();
        bitmap = Paint(width, height, scale, draw);
    }
}
