using System.Runtime.InteropServices;
using InfiniteSpace.Rendering;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace InfiniteSpace.Tests;

/// <summary>
/// A space's layers drawn into GPU bitmaps instead of on screen, so tests can read the picture back.
/// </summary>
internal sealed class OffscreenCanvas(GpuCompositor gpu) : ISpaceCanvas, IDisposable
{
    private ID2D1Bitmap1? _backdrop;
    private ID2D1Bitmap1? _art;
    private ID2D1Bitmap1? _frame;
    private (int[] Pixels, int Width, int Height, float Scale)? _pixelArt;

    public void ShowArt(int[] pixels, int width, int height, float scale)
    {
        HideArt();
        _pixelArt = ((int[])pixels.Clone(), width, height, scale);
    }

    public void DrawVector(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        HideArt();
        _art = gpu.Paint(width, height, scale, draw);
    }

    public void DrawBackdrop(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        _backdrop?.Dispose();
        _backdrop = gpu.Paint(width, height, scale, draw);
    }

    public void DrawFrame(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        _frame?.Dispose();
        _frame = gpu.Paint(width, height, scale, draw);
    }

    public void HideArt()
    {
        _art?.Dispose();
        _art = null;
        _pixelArt = null;
    }

    public ID2D1Bitmap1 Paint(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw) =>
        gpu.Paint(width, height, scale, draw);

    /// <summary>The layers stacked as the compositor would: premultiplied BGRA, row-major, <paramref name="width"/> per row.</summary>
    public int[] Snapshot(int width, int height, float scale)
    {
        using var picture = gpu.Paint(width, height, scale, (context, _) =>
        {
            if (_backdrop is not null)
                context.DrawImage(_backdrop, InterpolationMode.NearestNeighbor, CompositeMode.SourceOver);
            if (_art is not null)
                context.DrawImage(_art, InterpolationMode.NearestNeighbor, CompositeMode.SourceOver);
            else if (_pixelArt is { } art)
                DrawPixelArt(context, art.Pixels, art.Width, art.Height, art.Scale / scale);
            if (_frame is not null)
                context.DrawImage(_frame, InterpolationMode.NearestNeighbor, CompositeMode.SourceOver);
        });
        return Read(picture);
    }

    public void Dispose()
    {
        HideArt();
        _backdrop?.Dispose();
        _frame?.Dispose();
    }

    private static void DrawPixelArt(ID2D1DeviceContext context, int[] pixels, int width, int height, float dipsPerPixel)
    {
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var bitmap = context.CreateBitmap(new SizeI(width, height), handle.AddrOfPinnedObject(), (uint)(width * 4),
                new BitmapProperties1(GpuCompositor.Bgra, 96, 96));
            context.DrawBitmap(bitmap, new Vortice.RawRectF(0, 0, width * dipsPerPixel, height * dipsPerPixel), 1, InterpolationMode.NearestNeighbor, null, null);
        }
        finally
        {
            handle.Free();
        }
    }

    private int[] Read(ID2D1Bitmap1 source)
    {
        var size = source.PixelSize;
        using var readable = gpu.D2DContext.CreateBitmap(size, IntPtr.Zero, 0,
            new BitmapProperties1(GpuCompositor.Bgra, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        readable.CopyFromBitmap(source).CheckError();
        var map = readable.Map(MapOptions.Read);
        var pixels = new int[size.Width * size.Height];
        try
        {
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(map.Bits + y * (int)map.Pitch, pixels, y * size.Width, size.Width);
        }
        finally
        {
            readable.Unmap();
        }
        return pixels;
    }
}
