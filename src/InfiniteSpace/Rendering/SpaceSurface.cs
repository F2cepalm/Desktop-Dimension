using System.Numerics;
using System.Windows;
using Vortice;
using Vortice.Direct3D11;
using AntialiasMode = Vortice.Direct2D1.AntialiasMode;
using BitmapOptions = Vortice.Direct2D1.BitmapOptions;
using BitmapProperties1 = Vortice.Direct2D1.BitmapProperties1;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using ID2D1Bitmap1 = Vortice.Direct2D1.ID2D1Bitmap1;
using ID2D1DeviceContext = Vortice.Direct2D1.ID2D1DeviceContext;
using ID2D1Factory1 = Vortice.Direct2D1.ID2D1Factory1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace InfiniteSpace.Rendering;

/// <summary>
/// What the desktop compositor shows in one space window, bottom to top: a still backdrop, the moving
/// picture (pixel art uploaded at art resolution and scaled up on the GPU, or Direct2D vectors), a still
/// frame (shadows, outline), and the ink (paint and text) at device resolution.
/// </summary>
/// <remarks>
/// Themes that draw the backdrop and frame once per shape only redraw the moving picture each frame;
/// the compositor stacks the layers.
/// </remarks>
internal sealed class SpaceSurface : ISpaceCanvas, IDisposable
{
    private static readonly D2DPixelFormat Bgra = GpuCompositor.Bgra;

    private readonly GpuCompositor _gpu;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _root;
    private readonly Layer _backdrop;
    private readonly Layer _art;
    private readonly Layer _frame;
    private readonly Layer _ink;
    private float _artScale = float.NaN;
    private bool _artIsVector;

    public SpaceSurface(GpuCompositor gpu, IntPtr hwnd)
    {
        _gpu = gpu;
        gpu.Composition.CreateTargetForHwnd(hwnd, true, out _target!).CheckError();
        _root = gpu.Composition.CreateVisual();
        _backdrop = new Layer(gpu.Composition.CreateVisual());
        _art = new Layer(gpu.Composition.CreateVisual());
        _frame = new Layer(gpu.Composition.CreateVisual());
        _ink = new Layer(gpu.Composition.CreateVisual());
        _art.Visual.SetBitmapInterpolationMode(BitmapInterpolationMode.NearestNeighbor);
        _root.AddVisual(_backdrop.Visual, false, null);
        _root.AddVisual(_art.Visual, true, _backdrop.Visual);
        _root.AddVisual(_frame.Visual, true, _art.Visual);
        _root.AddVisual(_ink.Visual, true, _frame.Visual);
        _target.SetRoot(_root);
        gpu.Invalidate();
    }

    /// <summary>Shows a whole art-pixel frame (premultiplied BGRA), each art pixel <paramref name="scale"/> device pixels wide.</summary>
    public void ShowArt(int[] pixels, int width, int height, float scale)
    {
        if (_artIsVector)
        {
            // Switching from a vector theme: pixel art is scaled up with hard pixel edges.
            _art.Clear();
            _art.Visual.SetBitmapInterpolationMode(BitmapInterpolationMode.NearestNeighbor);
            _artScale = float.NaN;
            _artIsVector = false;
        }
        _art.Ensure(_gpu, width, height);
        if (scale != _artScale)
        {
            _art.Visual.SetTransform(Matrix3x2.CreateScale(scale));
            _artScale = scale;
        }
        Upload(_art.Surface!, pixels, width, new Int32Rect(0, 0, width, height));
    }

    /// <summary>
    /// Draws the whole moving picture with Direct2D at device resolution: <paramref name="draw"/> works in
    /// DIPs from the window's top-left, on a cleared (transparent) canvas.
    /// </summary>
    public void DrawVector(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        if (!_artIsVector)
        {
            _art.Clear();
            _art.Visual.SetBitmapInterpolationMode(BitmapInterpolationMode.Linear);
            _art.Visual.SetTransform(Matrix3x2.Identity);
            _artScale = float.NaN;
            _artIsVector = true;
        }
        Draw(_art, width, height, scale, draw);
    }

    /// <summary>Draws the still layer under the moving picture, like <see cref="DrawVector"/>.</summary>
    public void DrawBackdrop(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw) =>
        Draw(_backdrop, width, height, scale, draw);

    /// <summary>Draws the still layer over the moving picture (and under the ink), like <see cref="DrawVector"/>.</summary>
    public void DrawFrame(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw) =>
        Draw(_frame, width, height, scale, draw);

    /// <summary>
    /// Places everything shown (all layers) elsewhere in the window, on the GPU and without redrawing:
    /// scaled from the picture's top-left, then shifted (device pixels). For following an app that is
    /// moving or resizing; (1, 1, 0, 0) puts the picture back at the window's top-left.
    /// </summary>
    public void SetPlacement(float scaleX, float scaleY, float offsetX, float offsetY)
    {
        _root.SetTransform(Matrix3x2.CreateScale(scaleX, scaleY) * Matrix3x2.CreateTranslation(offsetX, offsetY));
        _gpu.Invalidate();
    }

    /// <summary>
    /// Sends everything pending to the compositor now, rather than with the next frame: for when the
    /// window itself was just moved, so that the picture lands in the same frame.
    /// </summary>
    public void CommitNow() => _gpu.Commit();

    /// <summary>Takes the moving picture off screen (until it is drawn again).</summary>
    public void HideArt()
    {
        if (_art.Clear())
            _gpu.Invalidate();
    }

    /// <summary>Takes the still backdrop and frame off screen (another theme is taking over).</summary>
    public void HideStills()
    {
        if (_backdrop.Clear() | _frame.Clear())
            _gpu.Invalidate();
    }

    public ID2D1Bitmap1 Paint(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw) =>
        _gpu.Paint(width, height, scale, draw);

    public void Repaint(ref ID2D1Bitmap1? bitmap, int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw) =>
        _gpu.Repaint(ref bitmap, width, height, scale, draw, _room);

    private double _room = 1;

    /// <summary>
    /// Room surfaces make beyond their size: more than 1 while the space is picked up to be reshaped, so
    /// they are made big enough once, when it is picked up, instead of growing during its first frames.
    /// Back to 1, each surface shrinks back to its size the next time it is drawn.
    /// </summary>
    public double ReshapeRoom
    {
        get => _room;
        set
        {
            if (value == _room)
                return;
            _room = value;
            foreach (var layer in new[] { _backdrop, _art, _frame, _ink })
                layer.Room = value;
        }
    }

    /// <summary>Shows the ink; only <paramref name="dirty"/> (device pixels) is copied over, on the GPU.</summary>
    /// <remarks>A new size copies everything, whatever <paramref name="dirty"/> says.</remarks>
    public void ShowInk(InkLayer ink, Int32Rect dirty)
    {
        if (_ink.Ensure(_gpu, ink.Width, ink.Height))
            dirty = ink.Bounds; // a new surface must be drawn whole first
        int x0 = Math.Max(0, dirty.X), y0 = Math.Max(0, dirty.Y);
        int x1 = Math.Min(ink.Width, dirty.X + dirty.Width), y1 = Math.Min(ink.Height, dirty.Y + dirty.Height);
        if (x1 <= x0 || y1 <= y0)
            return;
        var area = new Int32Rect(x0, y0, x1 - x0, y1 - y0);

        ink.Prepare(); // its own drawing first: not inside the surface's
        var context = _gpu.D2DContext;
        bool whole = area == ink.Bounds;
        using (var target = _ink.Surface!.BeginDraw<IDXGISurface>(whole ? null : new RawRect(x0, y0, x1, y1), out Int2 offset))
        using (var bitmap = context.CreateBitmapFromDxgiSurface(target, new BitmapProperties1(Bgra, 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw)))
        {
            context.Target = bitmap;
            context.SetDpi(96, 96);
            context.BeginDraw();
            // A whole update takes the whole surface (see Layer): nothing needs keeping, so nothing is
            // copied. Either way the offset is where the area's top-left goes.
            ink.Compose(context, area, new Vector2(offset.X, offset.Y));
            context.EndDraw().CheckError();
            context.Target = null;
        }
        _ink.Surface.EndDraw();
        _gpu.Invalidate();
    }

    public void HideInk()
    {
        if (_ink.Clear())
            _gpu.Invalidate();
    }

    /// <summary>Draws a layer with Direct2D in DIPs (the context runs at the device's DPI).</summary>
    private void Draw(Layer layer, int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        layer.Ensure(_gpu, width, height);
        var context = _gpu.D2DContext;
        float dpi = 96 * scale;
        using (var target = layer.Surface!.BeginDraw<IDXGISurface>(null, out Int2 offset)) // whole: see Layer
        using (var bitmap = context.CreateBitmapFromDxgiSurface(target, new BitmapProperties1(Bgra, dpi, dpi, BitmapOptions.Target | BitmapOptions.CannotDraw)))
        {
            context.Target = bitmap;
            context.SetDpi(dpi, dpi);
            context.BeginDraw();
            // The surface may sit inside a larger atlas: stay inside our part of it.
            var origin = new Vector2(offset.X / scale, offset.Y / scale);
            context.Transform = Matrix3x2.Identity;
            context.PushAxisAlignedClip(new RawRectF(origin.X, origin.Y, origin.X + width / scale, origin.Y + height / scale), AntialiasMode.Aliased);
            context.Clear(new Color4(0, 0, 0, 0));
            context.Transform = Matrix3x2.CreateTranslation(origin);
            draw(context, _gpu.D2DFactory);
            context.Transform = Matrix3x2.Identity;
            context.PopAxisAlignedClip();
            context.EndDraw().CheckError();
            context.Target = null;
        }
        layer.Surface.EndDraw();
        _gpu.Invalidate();
    }

    private void Upload(IDCompositionSurface surface, int[] pixels, int stride, Int32Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // The surface may live inside a larger atlas: write at the offset DirectComposition hands out.
        // The whole surface is taken (see Layer), so nothing outside the rectangle is copied along.
        using (var texture = surface.BeginDraw<ID3D11Texture2D>(null, out Int2 offset))
        {
            var box = new Box(offset.X + rect.X, offset.Y + rect.Y, 0, offset.X + rect.X + rect.Width, offset.Y + rect.Y + rect.Height, 1);
            ReadOnlySpan<int> source = pixels.AsSpan(rect.Y * stride + rect.X, (rect.Height - 1) * stride + rect.Width);
            _gpu.Context.UpdateSubresource(source, texture, 0, (uint)(stride * 4), 0, box);
        }
        surface.EndDraw();
        _gpu.Invalidate();
    }

    public void Dispose()
    {
        _target.SetRoot(null);
        _gpu.Invalidate();
        _ink.Dispose();
        _frame.Dispose();
        _art.Dispose();
        _backdrop.Dispose();
        _root.Dispose();
        _target.Dispose();
    }

    /// <summary>
    /// One visual and the surface it shows. The surface is made with room to spare and the visual is
    /// clipped to the part in use, so a space whose size changes every frame (being reshaped, wobbling)
    /// keeps drawing into the same surface.
    /// </summary>
    /// <remarks>
    /// Whatever the spare room holds never shows, so a drawing that redoes all of the part in use asks
    /// DirectComposition for the whole surface: then it keeps nothing of what was there, where asking
    /// for part of a surface the compositor is still showing copies everything else along first.
    /// </remarks>
    private sealed class Layer(IDCompositionVisual visual) : IDisposable
    {
        /// <summary>Surfaces grow in steps of this many pixels.</summary>
        private const int Step = Headroom.Step;

        public IDCompositionVisual Visual { get; } = visual;

        public IDCompositionSurface? Surface { get; private set; }

        private (int Width, int Height) _size;
        private (int Width, int Height) _capacity;
        private double _room = 1;
        private bool _trim;

        /// <summary>Room to make beyond the size (see <see cref="SpaceSurface.ReshapeRoom"/>); less than before shrinks it next time.</summary>
        public double Room
        {
            get => _room;
            set
            {
                _trim |= value < _room;
                _room = value;
            }
        }

        /// <summary>Makes sure there is a surface this big, showing only this much of it.</summary>
        /// <returns>Whether it is new (and must be drawn whole).</returns>
        public bool Ensure(GpuCompositor gpu, int width, int height)
        {
            int wantedWidth = Headroom.Wanted(width, _room), wantedHeight = Headroom.Wanted(height, _room);
            bool fits = Surface is not null && wantedWidth <= _capacity.Width && wantedHeight <= _capacity.Height;
            bool wasteful = (long)_capacity.Width * _capacity.Height > 4L * Math.Max(Step * Step, (long)wantedWidth * wantedHeight);
            // Room made for a reshape that is over: back to the size, once.
            bool oversized = _trim && (_capacity.Width > Headroom.Size(wantedWidth, 0) || _capacity.Height > Headroom.Size(wantedHeight, 0));
            _trim = false;
            if (fits && !wasteful && !oversized)
            {
                if (_size != (width, height))
                {
                    _size = (width, height);
                    Visual.SetClip(new RawRectF(0, 0, width, height)); // in the visual's own pixels, before its transform
                    gpu.Invalidate();
                }
                return false;
            }
            Surface?.Dispose();
            _capacity = oversized
                ? (Headroom.Size(wantedWidth, 0), Headroom.Size(wantedHeight, 0))
                : (Headroom.Size(wantedWidth, _capacity.Width), Headroom.Size(wantedHeight, _capacity.Height));
            gpu.Composition.CreateSurface((uint)_capacity.Width, (uint)_capacity.Height, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out var surface).CheckError();
            Surface = surface; // its first drawing takes all of it, as a new surface's must
            Visual.SetContent(surface);
            Visual.SetClip(new RawRectF(0, 0, width, height));
            _size = (width, height);
            gpu.Invalidate();
            return true;

        }

        /// <returns>Whether there was anything to take off screen.</returns>
        public bool Clear()
        {
            if (Surface is null)
                return false;
            Visual.SetContent(null);
            Surface.Dispose();
            Surface = null;
            _size = default;
            _capacity = default;
            return true;
        }

        public void Dispose()
        {
            Surface?.Dispose();
            Visual.Dispose();
        }
    }
}
