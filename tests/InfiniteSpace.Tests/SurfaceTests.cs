using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using Vortice.Mathematics;

namespace InfiniteSpace.Tests;

/// <summary>
/// What the desktop compositor really shows of a <see cref="SpaceSurface"/>, read back from a window
/// kept off screen.
/// </summary>
[Collection(nameof(SurfaceTests))]
public class SurfaceTests
{
    [Fact]
    public void ASurfaceThatShrank_ShowsNothingOfWhatWasDrawnBefore_PastItsNewSize()
    {
        Gpu.Run(gpu =>
        {
            using var window = new TestWindow(400, 300);
            using var surface = new SpaceSurface(gpu, window.Handle);

            // Big and red, then small and blue: the surface keeps its room, the red stays in it.
            surface.DrawBackdrop(380, 280, 1, (context, _) => Fill(context, 380, 280, new Color4(1, 0, 0, 1)));
            surface.DrawBackdrop(100, 80, 1, (context, _) => Fill(context, 100, 80, new Color4(0, 0, 1, 1)));
            gpu.Commit();
            var pixels = window.Capture();

            Assert.Equal(0xFF0000FFu, Rgb(pixels, window.Width, 50, 40));  // blue
            Assert.Equal(0xFF000000u, Rgb(pixels, window.Width, 200, 40)); // nothing: no red left over
            Assert.Equal(0xFF000000u, Rgb(pixels, window.Width, 50, 200));
        });
    }

    [Fact]
    public void PixelArtThatShrank_IsCutAtItsNewSize_ScaledUp()
    {
        Gpu.Run(gpu =>
        {
            using var window = new TestWindow(400, 300);
            using var surface = new SpaceSurface(gpu, window.Handle);

            surface.ShowArt(Solid(90, 70, unchecked((int)0xFFFF0000)), 90, 70, 4);
            surface.ShowArt(Solid(20, 10, unchecked((int)0xFF00FF00)), 20, 10, 4); // 80 × 40 on screen
            gpu.Commit();
            var pixels = window.Capture();

            Assert.Equal(0xFF00FF00u, Rgb(pixels, window.Width, 70, 30));
            Assert.Equal(0xFF000000u, Rgb(pixels, window.Width, 90, 30));  // past 80: cut off
            Assert.Equal(0xFF000000u, Rgb(pixels, window.Width, 30, 50));  // past 40: cut off
        });
    }

    [Fact]
    public void InkThatShrank_ShowsOnlyWhatIsInUse()
    {
        Gpu.Run(gpu =>
        {
            using var window = new TestWindow(400, 300);
            using var surface = new SpaceSurface(gpu, window.Handle);
            var square = Spaces.SpaceGeometry.FromStroke([new(0, 0), new(390, 0), new(390, 290), new(0, 290)], new Vector());
            using var ink = new InkLayer(gpu, 390, 290, 1, new Point(0, 0), square, System.Windows.Media.Colors.White);
            var record = new Spaces.SpaceRecord { Strokes = [new Spaces.InkStroke { Points = [10, 150, 380, 150], Thickness = 20, Color = "#FF0000" }] };
            surface.ShowInk(ink, ink.Redraw(record));

            var small = Spaces.SpaceGeometry.FromStroke([new(0, 0), new(100, 0), new(100, 290), new(0, 290)], new Vector());
            ink.SetShape(100, 290, 1, new Point(0, 0), small);
            surface.ShowInk(ink, ink.Redraw(record));
            gpu.Commit();
            var pixels = window.Capture();

            Assert.Equal(0xFFFF0000u, Rgb(pixels, window.Width, 50, 150));
            Assert.Equal(0xFF000000u, Rgb(pixels, window.Width, 200, 150));
        });
    }

    [Fact]
    public void AClickThroughWindow_StaysSoWhenResized_AndStillShowsItsPicture()
    {
        Gpu.Run(gpu =>
        {
            using var window = new TestWindow(400, 300);
            using var surface = new SpaceSurface(gpu, window.Handle);
            surface.DrawBackdrop(400, 300, 1, (context, _) => Fill(context, 400, 300, new Color4(1, 0, 0, 1)));
            gpu.Commit();
            bool Hit()
            {
                Win32.GetWindowRect(window.Handle, out var r);
                return Win32.WindowFromPoint(new Win32.POINT { X = r.Left + 50, Y = r.Top + 50 }) == window.Handle;
            }
            Assert.True(Hit());

            // WPF takes the layered style off its windows as they're resized; without it, clicks don't go through.
            Win32.SetClickThrough(window.Handle, true);
            Win32.SetWindowPos(window.Handle, IntPtr.Zero, -20000, -20000, 500, 350, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            Assert.False(Hit());
            Assert.Equal(0xFFFF0000u, Rgb(window.Capture(), window.Width, 50, 50));

            Win32.SetClickThrough(window.Handle, false);
            Win32.SetWindowPos(window.Handle, IntPtr.Zero, -20000, -20000, 400, 300, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            Assert.True(Hit());
        });
    }

    private static void Fill(Vortice.Direct2D1.ID2D1DeviceContext context, float width, float height, Color4 color)
    {
        using var brush = context.CreateSolidColorBrush(color);
        context.FillRectangle(new Vortice.RawRectF(0, 0, width, height), brush);
    }

    private static int[] Solid(int width, int height, int pixel) => Enumerable.Repeat(pixel, width * height).ToArray();

    private static uint Rgb(int[] pixels, int width, int x, int y) => (uint)pixels[y * width + x] | 0xFF000000u;

    /// <summary>
    /// A window far off screen that shows only what DirectComposition puts in it (like a space's),
    /// sized in device pixels, and read back as the compositor has it.
    /// </summary>
    private sealed class TestWindow : IDisposable
    {
        private readonly HwndSource _source;

        public TestWindow(int width, int height)
        {
            Width = width;
            Height = height;
            _source = new HwndSource(new HwndSourceParameters("Infinite Space test")
            {
                WindowStyle = unchecked((int)Win32.WS_POPUP),
                ExtendedWindowStyle = (int)(Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE),
                PositionX = -20000,
                PositionY = -20000,
                Width = width,
                Height = height,
            });
            Win32.SetWindowPos(Handle, IntPtr.Zero, -20000, -20000, width, height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            Win32.ShowWindow(Handle, Win32.SW_SHOWNOACTIVATE);
        }

        public IntPtr Handle => _source.Handle;

        public int Width { get; }

        public int Height { get; }

        /// <summary>The window as the compositor shows it (transparent parts black), once what was committed has landed.</summary>
        public int[] Capture()
        {
            for (int i = 0; i < 5; i++)
            {
                Win32.DwmFlush();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr dc = Win32.CreateCompatibleDC(screen);
            IntPtr bitmap = CreateCompatibleBitmap(screen, Width, Height);
            IntPtr old = Win32.SelectObject(dc, bitmap);
            try
            {
                PrintWindow(Handle, dc, 2); // PW_RENDERFULLCONTENT: what the compositor has, DirectComposition included
                Win32.SelectObject(dc, old);
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var pixels = new int[Width * Height];
                source.CopyPixels(pixels, Width * 4, 0);
                return pixels;
            }
            finally
            {
                Win32.DeleteObject(bitmap);
                Win32.DeleteDC(dc);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        public void Dispose() => _source.Dispose();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    }
}

/// <summary>Window hit tests at a fixed off-screen spot: run alone, so no other test's windows or styles change under them.</summary>
[CollectionDefinition(nameof(SurfaceTests), DisableParallelization = true)]
public sealed class SurfaceTestsCollection;
