using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfiniteSpace.Tests;

/// <summary>Spaces following their app window: moving and stretching with it, and coming back with it.</summary>
public sealed class HostFollowingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AMovedWindow_MovesItsSpaces()
    {
        var m = HostFrames.Map(new Rect(100, 100, 800, 600), new Rect(300, 50, 800, 600));

        Assert.Equal(new Point(350, 100), m.Transform(new Point(150, 150)));
        Assert.True(HostFrames.SameScale(Matrix.Identity, m));
    }

    [Fact]
    public void AResizedWindow_StretchesItsSpaces_InProportion()
    {
        // Twice as wide, half as tall, and moved.
        var m = HostFrames.Map(new Rect(100, 100, 400, 400), new Rect(0, 0, 800, 200));

        Assert.Equal(new Point(0, 0), m.Transform(new Point(100, 100)));          // top-left corner stays the corner
        Assert.Equal(new Point(800, 200), m.Transform(new Point(500, 500)));      // bottom-right too
        Assert.Equal(new Point(400, 100), m.Transform(new Point(300, 300)));      // the middle stays the middle
        Assert.False(HostFrames.SameScale(Matrix.Identity, m));
    }

    [Fact]
    public void Stretching_ThereAndBack_IsLossless()
    {
        var small = new Rect(100, 100, 300, 200);
        var big = new Rect(0, 0, 1920, 1040);
        var there = HostFrames.Map(small, big);
        var back = HostFrames.Map(big, small);
        var p = new Point(123.45, 234.56);

        var round = back.Transform(there.Transform(p));

        Assert.Equal(p.X, round.X, 9);
        Assert.Equal(p.Y, round.Y, 9);
    }

    [Fact]
    public void MovingAPickedUpSpace_OnlyShiftsIt()
    {
        var before = HostFrames.Map(new Rect(0, 0, 100, 100), new Rect(50, 50, 200, 200)); // on a stretched app
        var picked = new Rect(100, 100, 300, 200);
        var moved = picked;
        moved.Offset(40, -10);

        var m = SpaceManager.Reshaped(before, picked, moved);

        Assert.True(HostFrames.SameScale(before, m)); // just a move: nothing is redrawn
        Assert.Equal(before.Transform(new Point(30, 30)) + new Vector(40, -10), m.Transform(new Point(30, 30)));
    }

    [Fact]
    public void ReshapingAPickedUpSpace_StretchesIt_FromTheOppositeSide()
    {
        var picked = new Rect(100, 100, 200, 100);
        // The right side pushed out by 200: twice as wide, the left side stays put.
        var reshaped = Shell.BlockEdit.Grow(picked, Shell.BlockGrip.Right, 200, min: 24);

        var m = SpaceManager.Reshaped(Matrix.Identity, picked, reshaped);

        Assert.Equal(new Point(100, 100), m.Transform(new Point(100, 100)));
        Assert.Equal(new Point(500, 200), m.Transform(new Point(300, 200)));
        Assert.Equal(new Point(300, 150), m.Transform(new Point(200, 150)));
    }

    [Fact]
    public void ReshapingBackToTheStart_IsWhereItBegan()
    {
        var before = new Matrix(1.5, 0, 0, 0.75, 20, 30);
        var picked = new Rect(100, 100, 200, 100);

        Assert.Equal(before, SpaceManager.Reshaped(before, picked, picked));
    }

    [Fact]
    public void ARecord_MovesAndStretches_WithEverythingOnIt_ButInkKeepsItsSize()
    {
        var record = new SpaceRecord
        {
            Figures = [[0, 0, 10, 0, 10, 10, 0, 10]],
            Strokes = [new InkStroke { Points = [1, 1, 5, 5] }],
            Notes = [new TextNote { X = 2, Y = 3, Text = "hi" }],
        };

        record.Transform(new Matrix(2, 0, 0, 3, 100, 200));

        Assert.Equal([100, 200, 120, 200, 120, 230, 100, 230], record.Figures[0]);
        Assert.Equal([102, 203, 106, 207], record.Strokes[0].Points); // its corner (1,1) → (102,203), as long as before
        Assert.Equal((104, 209), (record.Notes[0].X, record.Notes[0].Y));
    }

    [Fact]
    public void AnOutline_IsMappedPointForPoint()
    {
        var outline = SpaceGeometry.FromStroke([new(0, 0), new(10, 0), new(10, 10)], new Vector(0, 0));

        var moved = SpaceGeometry.Transform(outline, new Matrix(2, 0, 0, 2, 5, 5));

        Assert.Equal(new Rect(5, 5, 20, 20), moved.Bounds);
        Assert.True(moved.FillContains(new Point(20, 10)));
        Assert.False(moved.FillContains(new Point(8, 20)));
    }

    [Fact]
    public void AMappedOutline_IsTheSameShapeMoved_WithoutCopyingItsPoints()
    {
        var outline = SpaceGeometry.FromStroke([new(0, 0), new(10, 0), new(10, 10)], new Vector(0, 0));
        outline.Freeze();

        var moved = SpaceGeometry.Mapped(outline, new Matrix(1, 0, 0, 1, 100, 50));

        Assert.Equal(new Rect(100, 50, 10, 10), moved.Bounds);
        Assert.True(moved.FillContains(new Point(108, 52)));
        Assert.False(moved.FillContains(new Point(8, 2)));
        Assert.Same(outline.Figures[0], ((PathGeometry)moved).Figures[0]);
        Assert.Same(outline, SpaceGeometry.Mapped(outline, Matrix.Identity));
    }

    [Fact]
    public void Ink_IsClippedToAMovedOutline()
    {
        Gpu.Run(gpu =>
        {
            // The outline arrives already moved by a transform (as while its app is being dragged).
            var square = SpaceGeometry.FromStroke([new(0, 0), new(100, 0), new(100, 100), new(0, 100)], new Vector(0, 0));
            square.Freeze();
            var moved = SpaceGeometry.Mapped(square, new Matrix(1, 0, 0, 1, 100, 0));
            using var ink = new InkLayer(gpu, 200, 100, 1, new Point(0, 0), moved, Colors.White);

            ink.Redraw(new SpaceRecord { Strokes = [new InkStroke { Points = [10, 50, 190, 50] }] });
            var pixels = ink.ReadPixels();

            Assert.Equal(0, pixels[50 * 200 + 50]);      // left of the moved square: clipped
            Assert.NotEqual(0, pixels[50 * 200 + 150]);  // on it
        });
    }

    [Fact]
    public void TheApp_IsSavedWithTheSpace()
    {
        var store = new SpaceStore(_directory, NullLogger<SpaceStore>.Instance);
        var group = Guid.NewGuid();
        store.Save([new SpaceRecord
        {
            Figures = [[0, 0, 10, 0, 10, 10]],
            Host = new HostInfo { App = @"C:\Apps\notes.exe", WindowClass = "Notes", Title = "a.txt", Window = group, Frame = new Rect(10, 20, 800, 600) },
        }]);

        var host = store.Load().Single().Host!;

        Assert.Equal((@"C:\Apps\notes.exe", "Notes", "a.txt", group), (host.App, host.WindowClass, host.Title, host.Window));
        Assert.Equal(new Rect(10, 20, 800, 600), host.Frame);
    }

    [Fact]
    public void SpacesSavedBeforeApps_WereRemembered_LoadAtDesktopLevel()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "spaces.json"), """{"Version":1,"Spaces":[{"Figures":[[0,0,10,0,10,10]]}]}""");

        Assert.Null(new SpaceStore(_directory, NullLogger<SpaceStore>.Instance).Load().Single().Host);
    }

    [Theory]
    [InlineData("Dreamy")]
    [InlineData("Claudish")]
    [InlineData("Glory")]
    [InlineData("Void")]
    public void AMovedLook_IsWhereTheSpaceIsNow(string theme)
    {
        var outline = SpaceGeometry.FromStroke([new(100, 100), new(300, 100), new(300, 250), new(100, 250)], new Vector(0, 0));
        using var look = Themes.CreateLook(theme);
        look.SetShape(outline, new Rect(86, 86, 228, 178));
        look.Render(new FrameState(1, new Vector()));

        var moved = SpaceGeometry.Transform((PathGeometry)outline, new Matrix(1, 0, 0, 1, 400, 30));
        look.Move(moved, new Rect(486, 116, 228, 178));

        Assert.True(look.IsInside(new Point(600, 205)));
        Assert.False(look.IsInside(new Point(200, 175)));
        Assert.True(look.Render(new FrameState(1.001, new Vector())) || theme == Themes.Claudish, "screen-space scenery is redrawn");
    }

    [Fact]
    public void Ink_GoesWhereItsAppPutsIt_AtItsOwnSize()
    {
        Gpu.Run(gpu =>
        {
            var outline = SpaceGeometry.FromStroke([new(0, 0), new(200, 0), new(200, 200), new(0, 200)], new Vector(0, 0));
            using var ink = new InkLayer(gpu, 200, 200, 1, new Point(0, 0), outline, Colors.White);
            var record = new SpaceRecord { Strokes = [new InkStroke { Points = [10, 20, 40, 20], Thickness = 4 }] };

            ink.Redraw(record, new Matrix(2, 0, 0, 2, 0, 0)); // the app doubled in size
            var pixels = ink.ReadPixels();

            Assert.NotEqual(0, pixels[40 * 200 + 35]);  // its start (10,20) → (20,40); 30 long from there
            Assert.Equal(0, pixels[40 * 200 + 65]);     // not stretched to twice its length
            Assert.Equal(0, pixels[20 * 200 + 25]);     // where it was
            Assert.Equal(0, pixels[36 * 200 + 35]);     // the pen kept its width: 4 across, not 8
        });
    }

    [Fact]
    public void ShrinkingTheApp_MovesInkButKeepsItsSize()
    {
        var stroke = new InkStroke { Points = [100, 100, 160, 140] };
        var record = new SpaceRecord { Strokes = [stroke] };

        record.Transform(new Matrix(0.5, 0, 0, 0.5, 0, 0)); // the app at half its size

        Assert.Equal([50, 50, 110, 90], stroke.Points);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
