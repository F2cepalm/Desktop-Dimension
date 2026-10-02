using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using Matrix3x2 = System.Numerics.Matrix3x2;
using SizeI = Vortice.Mathematics.SizeI;

namespace InfiniteSpace.Rendering;

/// <summary>
/// The GPU side shared by all spaces: one Direct3D 11 device (uploads) and one DirectComposition
/// device (what the desktop compositor shows). Changes from every space go out in one commit per frame.
/// </summary>
/// <remarks>
/// <para>Used from the UI thread only.</para>
/// <para>
/// The GPU can go away under it (a driver update, a GPU reset, an external GPU unplugged): then
/// <see cref="IsLost"/> turns true, and <see cref="Recreate"/> makes new devices on whatever GPU there
/// is now. Everything made on the old ones must be made again (see <see cref="Universe"/>).
/// </para>
/// </remarks>
internal sealed class GpuCompositor : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
        [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    private IDXGIDevice _dxgi = null!;
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private bool _changed;
    private bool _lost;
    private bool _disposed;

    public GpuCompositor() => CreateDevices();

    public ID3D11Device Device { get; private set; } = null!;

    public ID3D11DeviceContext Context { get; private set; } = null!;

    public IDCompositionDevice Composition { get; private set; } = null!;

    /// <summary>
    /// Drawing in software (WARP: no usable GPU, remote desktop, some virtual machines): every frame
    /// costs the CPU, so only what matters is drawn at full rate.
    /// </summary>
    public bool IsSoftware { get; private set; }

    /// <summary>How many times the devices were made anew after the GPU went away.</summary>
    public int Generation { get; private set; }

    /// <summary>
    /// Whether the GPU went away (driver update, reset, removed), or a call failed as if it had: the
    /// devices must be made again (<see cref="Recreate"/>).
    /// </summary>
    public bool IsLost
    {
        get
        {
            if (_lost || Device.DeviceRemovedReason.Failure)
                return true;
            if (++_checks % 64 != 0) // the compositor's own check now and then: once a second or so
                return false;
            Composition.CheckDeviceState(out var valid);
            return !valid;
        }
    }

    private int _checks;

    /// <summary>Notes that a call failed the way it does when the GPU went away (see <see cref="IsDeviceLoss"/>).</summary>
    public void MarkLost() => _lost = true;

    /// <summary>Whether an error from Direct3D, Direct2D or DirectComposition means the GPU went away.</summary>
    public static bool IsDeviceLoss(int hresult) => unchecked((uint)hresult) is
        0x887A0005 or // DXGI_ERROR_DEVICE_REMOVED
        0x887A0006 or // DXGI_ERROR_DEVICE_HUNG
        0x887A0007 or // DXGI_ERROR_DEVICE_RESET
        0x887A0020 or // DXGI_ERROR_DRIVER_INTERNAL_ERROR
        0x8899000C;   // D2DERR_RECREATE_TARGET

    /// <summary>
    /// Makes new devices on the GPU there is now, after the old ones were lost. Everything made on the
    /// old ones (surfaces, bitmaps, brushes, effects, visuals) is useless and must be made again.
    /// </summary>
    public void Recreate()
    {
        ReleaseDevices();
        CreateDevices();
        _lost = false;
        _changed = true;
        Generation++;
    }

    private void CreateDevices()
    {
        (Device, IsSoftware) = CreateDevice();
        Context = Device.ImmediateContext;
        _dxgi = Device.QueryInterface<IDXGIDevice>();
        Composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dxgi);
    }

    private void ReleaseDevices()
    {
        _d2dContext?.Dispose();
        _d2dContext = null;
        _d2dDevice?.Dispose();
        _d2dDevice = null;
        Composition.Dispose();
        _dxgi.Dispose();
        Context.Dispose();
        Device.Dispose();
    }

    /// <summary>Direct2D, for themes drawn as vectors; created on first use.</summary>
    public ID2D1Factory1 D2DFactory => _d2dFactory ??= D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded, DebugLevel.None);

    /// <summary>Direct2D drawing on this device's surfaces; created on first use.</summary>
    public ID2D1DeviceContext D2DContext
    {
        get
        {
            if (_d2dContext is null)
            {
                _d2dDevice = D2DFactory.CreateDevice(_dxgi);
                _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
            }
            return _d2dContext;
        }
    }

    /// <summary>Premultiplied BGRA, the format of every surface and bitmap here.</summary>
    public static Vortice.DCommon.PixelFormat Bgra { get; } = new(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);

    /// <summary>
    /// Paints an offscreen picture at device resolution (the bitmap's DPI is the device's, so it draws
    /// at its size in DIPs). <paramref name="draw"/> works in DIPs; the caller owns the bitmap.
    /// </summary>
    /// <remarks>Not from inside another drawing: it has its own target.</remarks>
    public ID2D1Bitmap1 Paint(int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        var context = D2DContext;
        float dpi = 96 * scale;
        var bitmap = context.CreateBitmap(new SizeI(Math.Max(1, width), Math.Max(1, height)), IntPtr.Zero, 0,
            new BitmapProperties1(Bgra, dpi, dpi, BitmapOptions.Target));
        context.Target = bitmap;
        context.SetDpi(dpi, dpi);
        context.BeginDraw();
        context.Transform = Matrix3x2.Identity;
        context.Clear(new Color4(0, 0, 0, 0));
        draw(context, D2DFactory);
        context.Transform = Matrix3x2.Identity;
        context.EndDraw().CheckError();
        context.Target = null;
        return bitmap;
    }

    /// <summary>
    /// Like <see cref="Paint"/>, into <paramref name="bitmap"/> when it is big enough (it is made anew,
    /// with room to spare, when it isn't): for pictures painted again for every new shape. Everything
    /// past <paramref name="width"/> × <paramref name="height"/> is left transparent.
    /// </summary>
    public void Repaint(ref ID2D1Bitmap1? bitmap, int width, int height, float scale, Action<ID2D1DeviceContext, ID2D1Factory1> draw)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        float dpi = 96 * scale;
        var context = D2DContext;
        var size = bitmap?.PixelSize ?? default;
        bool fits = bitmap is not null && size.Width >= width && size.Height >= height && bitmap.Dpi.Width == dpi;
        bool wasteful = (long)size.Width * size.Height > 4L * Math.Max(256 * 256, (long)width * height);
        if (!fits || wasteful)
        {
            bitmap?.Dispose();
            bitmap = context.CreateBitmap(new SizeI(Headroom.Size(width, size.Width), Headroom.Size(height, size.Height)), IntPtr.Zero, 0,
                new BitmapProperties1(Bgra, dpi, dpi, BitmapOptions.Target));
        }
        context.Target = bitmap;
        context.SetDpi(dpi, dpi);
        context.BeginDraw();
        context.Transform = Matrix3x2.Identity;
        context.Clear(new Color4(0, 0, 0, 0));
        context.PushAxisAlignedClip(new Vortice.RawRectF(0, 0, width / scale, height / scale), AntialiasMode.Aliased);
        draw(context, D2DFactory);
        context.Transform = Matrix3x2.Identity;
        context.PopAxisAlignedClip();
        context.EndDraw().CheckError();
        context.Target = null;
    }

    /// <summary>Reads a bitmap's pixels back (premultiplied BGRA, row after row): slow, for tests and diagnostics.</summary>
    public int[] ReadPixels(ID2D1Bitmap1 source)
    {
        var size = source.PixelSize;
        using var readable = D2DContext.CreateBitmap(size, IntPtr.Zero, 0,
            new BitmapProperties1(Bgra, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        readable.CopyFromBitmap(source).CheckError();
        var map = readable.Map(MapOptions.Read);
        var pixels = new int[size.Width * size.Height];
        try
        {
            for (int y = 0; y < size.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(map.Bits + y * (int)map.Pitch, pixels, y * size.Width, size.Width);
        }
        finally
        {
            readable.Unmap();
        }
        return pixels;
    }

    /// <summary>Notes that some surface or visual changed; <see cref="Commit"/> sends it.</summary>
    public void Invalidate() => _changed = true;

    /// <summary>Hands all pending changes to the desktop compositor at once.</summary>
    public void Commit()
    {
        if (!_changed)
            return;
        _changed = false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = Composition.Commit();
        CommitTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
        if (result.Failure)
            _lost = true; // the next frame finds out why, and makes the devices again if the GPU went away
    }

    /// <summary>
    /// Time spent handing changes to the compositor so far (diagnostics). It includes waiting when
    /// changes come faster than the display shows them.
    /// </summary>
    public TimeSpan CommitTime { get; private set; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseDevices();
        _d2dFactory?.Dispose();
    }

    /// <returns>The device, and whether it draws in software (WARP, or an adapter that is really that).</returns>
    private static (ID3D11Device Device, bool Software) CreateDevice()
    {
        // BgraSupport: DirectComposition surfaces are BGRA. WARP keeps things working without a usable GPU.
        foreach (var driver in new[] { DriverType.Hardware, DriverType.Warp })
        {
            if (D3D11.D3D11CreateDevice(IntPtr.Zero, driver, DeviceCreationFlags.BgraSupport, FeatureLevels, out ID3D11Device? device).Success && device is not null)
                return (device, driver == DriverType.Warp || IsBasicRenderDriver(device));
        }
        throw new InvalidOperationException("Could not create a Direct3D 11 device.");
    }

    /// <summary>The "Microsoft Basic Render Driver": what Windows falls back to without a GPU driver, drawing in software.</summary>
    private static bool IsBasicRenderDriver(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        var description = adapter.Description;
        return description.VendorId == 0x1414 && description.DeviceId == 0x8C;
    }
}
