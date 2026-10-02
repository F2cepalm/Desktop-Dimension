using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Shell;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

/// <summary>The ink palette and text boxes fade out of the way once the user gets on with painting or erasing.</summary>
public class FadeTests
{
    [Fact]
    public void ThePalette_HandsBackAtOnce_ThenFadesAway_WithinHalfASecond()
    {
        RunOnSta(() =>
        {
            var palette = new InkPaletteWindow(new Point(0, 0), InkPen.Default, Colors.White, _ => { }) { ShowActivated = false };
            palette.Show();
            (palette.Left, palette.Top) = (-10000, -10000); // off screen (it keeps itself on screen when shown)
            bool gone = false, closed = false;
            palette.Gone += () => gone = true;
            palette.Closed += (_, _) => closed = true;

            var clock = Stopwatch.StartNew();
            palette.FadeOut();

            Assert.True(gone, "done with at once: focus can go back to the app");
            Assert.True(ClicksGoThrough(palette), "painting carries on through it");
            Pump(250);
            Assert.False(closed);
            Assert.InRange(palette.Opacity, 0.01, 0.99); // part way
            PumpUntil(() => closed, 2000);
            Assert.True(closed);
            Assert.InRange(clock.Elapsed.TotalSeconds, 0.45, 0.9);
        });
    }

    [Fact]
    public void ThePalette_FadesOnce_AndDismissingItStillClosesAtOnce()
    {
        RunOnSta(() =>
        {
            var palette = new InkPaletteWindow(new Point(0, 0), InkPen.Default, Colors.White, _ => { }) { ShowActivated = false };
            palette.Show();
            (palette.Left, palette.Top) = (-10000, -10000);
            int gone = 0;
            bool closed = false;
            palette.Gone += () => gone++;
            palette.Closed += (_, _) => closed = true;

            palette.FadeOut();
            palette.FadeOut();
            palette.Dismiss();

            Assert.True(closed);
            Assert.Equal(1, gone);
            Pump(700); // the fade finishing later doesn't close it twice
        });
    }

    [Fact]
    public void ATextBox_KeepsWhatWasTyped_AtOnce_ThenFadesAway()
    {
        RunOnSta(() =>
        {
            var entry = new TextEntry(new Point(-10000, -10000), new TextNote { Text = "keep me", FontSize = 20, Color = "#3FA7FF" }, Colors.White) { ShowActivated = false };
            entry.Show();
            TextNote? kept = null;
            bool gone = false, closed = false;
            entry.Committed += note => kept = note;
            entry.Gone += () => gone = true;
            entry.Closed += (_, _) => closed = true;

            entry.CommitAndFade();

            Assert.Equal(("keep me", 20.0, "#3FA7FF"), (kept?.Text, kept?.FontSize, kept?.Color));
            Assert.True(gone);
            Assert.True(ClicksGoThrough(entry));
            Assert.False(closed);
            PumpUntil(() => closed, 2000);
            Assert.True(closed);

            entry.Commit(); // already done: nothing more is kept
            Assert.Equal("keep me", kept!.Text);
        });
    }

    private static bool ClicksGoThrough(Window window) =>
        (Win32.GetWindowLong(new WindowInteropHelper(window).Handle, Win32.GWL_EXSTYLE) & Win32.WS_EX_TRANSPARENT) != 0;

    private static void Pump(int ms) => PumpUntil(() => false, ms);

    private static void PumpUntil(Func<bool> done, int ms)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
