using System.Windows;
using InfiniteSpace.Rendering;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;
using Int2 = Vortice.Mathematics.Int2;
using AlphaMode = Vortice.DXGI.AlphaMode;
using Matrix3x2 = System.Numerics.Matrix3x2;
using Vector2 = System.Numerics.Vector2;

namespace InfiniteSpace.Shell;

/// <summary>Drawing with Direct2D into DirectComposition surfaces, in DIPs.</summary>
internal static class SurfaceDrawing
{
    /// <summary>
    /// Redraws a rectangle of a surface (device pixels): it is cleared, then <paramref name="draw"/>
    /// draws in DIPs from the surface's top-left, clipped to it. All of the rectangle must be drawn:
    /// DirectComposition doesn't keep what was there.
    /// </summary>
    public static void Draw(GpuCompositor gpu, IDCompositionSurface surface, RawRect update, float scale, Action<ID2D1DeviceContext> draw)
    {
        var context = gpu.D2DContext;
        float dpi = 96 * scale;
        using (var target = surface.BeginDraw<IDXGISurface>(update, out Int2 offset))
        using (var bitmap = context.CreateBitmapFromDxgiSurface(target, new BitmapProperties1(GpuCompositor.Bgra, dpi, dpi, BitmapOptions.Target | BitmapOptions.CannotDraw)))
        {
            context.Target = bitmap;
            context.SetDpi(dpi, dpi);
            context.BeginDraw();
            context.AntialiasMode = AntialiasMode.PerPrimitive;
            context.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
            // The offset is where the update's top-left sits in DirectComposition's atlas; the clip
            // keeps the drawing out of whatever else shares it.
            var shift = Matrix3x2.CreateTranslation((offset.X - update.Left) / scale, (offset.Y - update.Top) / scale);
            context.Transform = shift;
            context.PushAxisAlignedClip(new RawRectF(update.Left / scale, update.Top / scale, update.Right / scale, update.Bottom / scale), AntialiasMode.Aliased);
            context.Clear(new Color4(0, 0, 0, 0));
            draw(context);
            context.Transform = shift;
            context.PopAxisAlignedClip();
            context.Transform = Matrix3x2.Identity;
            context.EndDraw().CheckError();
            context.Target = null;
        }
        surface.EndDraw();
        gpu.Invalidate();
    }
}

/// <summary>A small picture drawn once with Direct2D, shown by any number of <see cref="OverlaySprite"/>s.</summary>
internal sealed class OverlayPicture : IDisposable
{
    private OverlayPicture(IDCompositionSurface surface, Size size)
    {
        Surface = surface;
        Size = size;
    }

    public IDCompositionSurface Surface { get; }

    /// <summary>Its size in DIPs.</summary>
    public Size Size { get; }

    /// <param name="width">DIPs.</param>
    /// <param name="height">DIPs.</param>
    /// <param name="draw">Draws in DIPs from the picture's top-left.</param>
    public static OverlayPicture Paint(GpuCompositor gpu, double width, double height, Action<ID2D1DeviceContext> draw)
    {
        float scale = (float)Native.Dpi.Scale;
        int w = Math.Max(1, (int)Math.Ceiling(width * scale)), h = Math.Max(1, (int)Math.Ceiling(height * scale));
        gpu.Composition.CreateSurface((uint)w, (uint)h, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out var surface).CheckError();
        var picture = new OverlayPicture(surface, new Size(w / scale, h / scale));
        // A surface's first drawing covers it all (an atlas may place it among others: stay inside it).
        SurfaceDrawing.Draw(gpu, surface, new RawRect(0, 0, w, h), scale, draw);
        return picture;
    }

    public void Dispose() => Surface.Dispose();
}

/// <summary>A visual showing an <see cref="OverlayPicture"/> somewhere on an overlay: moving it redraws nothing.</summary>
internal sealed class OverlaySprite : IDisposable
{
    private readonly GpuCompositor _gpu;
    private OverlayPicture? _picture;
    private bool _shown;
    private (float X, float Y) _at = (float.NaN, float.NaN);

    public OverlaySprite(GpuCompositor gpu, IDCompositionVisual parent, OverlayPicture? picture = null)
    {
        _gpu = gpu;
        Visual = gpu.Composition.CreateVisual();
        parent.AddVisual(Visual, true, null);
        _picture = picture;
    }

    public IDCompositionVisual Visual { get; }

    public OverlayPicture? Picture
    {
        get => _picture;
        set
        {
            if (_picture == value)
                return;
            _picture = value;
            if (_shown)
                Visual.SetContent(value?.Surface);
            _gpu.Invalidate();
        }
    }

    public bool Shown
    {
        get => _shown;
        set
        {
            if (_shown == value)
                return;
            _shown = value;
            Visual.SetContent(value ? _picture?.Surface : null);
            _gpu.Invalidate();
        }
    }

    /// <summary>Puts its top-left at a point (DIPs from the overlay's top-left), on whole device pixels.</summary>
    public void MoveTo(Point at)
    {
        double scale = Native.Dpi.Scale;
        var pixels = ((float)Math.Round(at.X * scale), (float)Math.Round(at.Y * scale));
        if (pixels == _at)
            return;
        _at = pixels;
        Visual.SetOffsetX(pixels.Item1);
        Visual.SetOffsetY(pixels.Item2);
        _gpu.Invalidate();
    }

    /// <summary>Puts its centre at a point (DIPs from the overlay's top-left).</summary>
    public void CentreAt(Point centre)
    {
        var size = _picture?.Size ?? default;
        MoveTo(new Point(centre.X - size.Width / 2, centre.Y - size.Height / 2));
    }

    public void Dispose() => Visual.Dispose();
}

/// <summary>
/// A layer as big as the overlay, drawn a rectangle at a time: only the parts drawn take memory (a
/// virtual surface), and a stroke growing redraws only where it grew.
/// </summary>
internal sealed class OverlayLayer : IDisposable
{
    private readonly GpuCompositor _gpu;
    private readonly IDCompositionVirtualSurface _surface;
    private readonly int _width;
    private readonly int _height;

    /// <param name="width">Device pixels.</param>
    /// <param name="height">Device pixels.</param>
    public OverlayLayer(GpuCompositor gpu, IDCompositionVisual parent, int width, int height)
    {
        _gpu = gpu;
        _width = width;
        _height = height;
        Visual = gpu.Composition.CreateVisual();
        gpu.Composition.CreateVirtualSurface((uint)width, (uint)height, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out _surface).CheckError();
        Visual.SetContent(_surface);
        parent.AddVisual(Visual, true, null);
    }

    public IDCompositionVisual Visual { get; }

    /// <summary>
    /// Redraws everything in <paramref name="area"/> (DIPs from the overlay's top-left): it is cleared,
    /// and <paramref name="draw"/> (in the same DIPs) must draw all that shows there.
    /// </summary>
    public void Draw(Rect area, Action<ID2D1DeviceContext> draw)
    {
        if (area.IsEmpty)
            return;
        float scale = (float)Native.Dpi.Scale;
        int x0 = Math.Clamp((int)Math.Floor(area.Left * scale), 0, _width), y0 = Math.Clamp((int)Math.Floor(area.Top * scale), 0, _height);
        int x1 = Math.Clamp((int)Math.Ceiling(area.Right * scale), 0, _width), y1 = Math.Clamp((int)Math.Ceiling(area.Bottom * scale), 0, _height);
        if (x1 <= x0 || y1 <= y0)
            return;
        SurfaceDrawing.Draw(_gpu, _surface, new RawRect(x0, y0, x1, y1), scale, draw);
    }

    /// <summary>Lets go of everything drawn (and the memory it took).</summary>
    public void Clear()
    {
        _surface.Trim([], 0);
        _gpu.Invalidate();
    }

    public void Dispose()
    {
        Visual.SetContent(null);
        _surface.Dispose();
        Visual.Dispose();
    }
}

/// <summary>Text for overlays (hints, sizes), laid out with DirectWrite in the system UI font.</summary>
internal static class OverlayText
{
    private static readonly Dictionary<float, IDWriteTextFormat> Formats = [];

    /// <summary>A picture of one line of text, white, with a rounded dark backing when <paramref name="backing"/> is given.</summary>
    /// <param name="padding">Room around the text inside the backing, in DIPs (horizontal, vertical).</param>
    public static OverlayPicture Paint(GpuCompositor gpu, string text, float fontSize, Color4? backing = null, Vector2 padding = default, float radius = 0)
    {
        using var layout = NoteText.Factory.CreateTextLayout(text, Format(fontSize), 1e6f, 1e6f);
        layout.WordWrapping = WordWrapping.NoWrap;
        var metrics = layout.Metrics;
        double width = Math.Ceiling(metrics.WidthIncludingTrailingWhitespace + 2 * padding.X);
        double height = Math.Ceiling(metrics.Height + 2 * padding.Y);
        return OverlayPicture.Paint(gpu, width, height, context =>
        {
            if (backing is { } fill)
            {
                using var brush = context.CreateSolidColorBrush(fill);
                context.FillRoundedRectangle(new RoundedRectangle(new RawRectF(0, 0, (float)width, (float)height), radius, radius), brush);
            }
            using var white = context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            context.DrawTextLayout(padding, layout, white, DrawTextOptions.None);
        });
    }

    private static IDWriteTextFormat Format(float fontSize)
    {
        if (Formats.TryGetValue(fontSize, out var format))
            return format;
        var typeface = NoteText.Typeface;
        format = NoteText.Factory.CreateTextFormat(typeface.FontFamily.Source, null,
            (Vortice.DirectWrite.FontWeight)typeface.Weight.ToOpenTypeWeight(), Vortice.DirectWrite.FontStyle.Normal, Vortice.DirectWrite.FontStretch.Normal, fontSize, "");
        Formats[fontSize] = format;
        return format;
    }
}
