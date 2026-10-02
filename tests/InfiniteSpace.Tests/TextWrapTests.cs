using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class TextWrapTests
{
    [Fact]
    public void TextThatFits_KeepsItsSize_AndWrapsAtTheSelectionsWidth()
    {
        Gpu.RunOnSta(() =>
        {
            var (size, width) = SpaceManager.FitWrappedText("Hello", new Size(200, 40), 16, bold: false, italic: false);

            Assert.Equal((16.0, 200.0), (size, width));
        });
    }

    [Fact]
    public void TextTooBigForTheSelection_IsMadeSmallerUntilItFits()
    {
        Gpu.RunOnSta(() =>
        {
            const string text = "A paragraph a little too long for the area it came from, once laid out in our font.";
            var box = new Size(180, 60);

            var (size, width) = SpaceManager.FitWrappedText(text, box, 24, bold: true, italic: false);

            Assert.InRange(size, SpaceManager.SmallestWrappedType, 23.5);
            using var layout = NoteText.Layout(text, size, width, bold: true);
            Assert.True(layout.Metrics.Height <= box.Height + 0.5);
        });
    }

    [Fact]
    public void Contrast_FollowsWcag()
    {
        Assert.Equal(21, Themes.Contrast(Colors.Black, Colors.White), 1);
        Assert.Equal(1, Themes.Contrast(Colors.Red, Colors.Red), 6);
    }
}

/// <summary>Reading a selection through UI Automation, from a WPF text box on a thread of its own (as another app's would be).</summary>
[Collection(nameof(SurfaceTests))]
public class TextSelectionTests
{
    [Fact]
    public void ASelection_IsReadWithItsStyle()
    {
        IntPtr hwnd = IntPtr.Zero;
        System.Windows.Threading.Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var ui = new Thread(() =>
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = "Hello wrap world",
                FontWeight = FontWeights.Bold,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Red,
            };
            var window = new Window { Content = box, Left = -20000, Top = -20000, Width = 300, Height = 80, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            box.Select(6, 4);
            hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
            window.Close();
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
            var edit = System.Windows.Automation.AutomationElement.FromHandle(hwnd).FindFirst(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit));

            var read = InfiniteSpace.Shell.TextSelection.Read(edit);

            Assert.NotNull(read);
            Assert.Equal("wrap", read.Text);
            Assert.True(read.Bold);
            Assert.True(read.Italic);
            Assert.Equal(Colors.Red, read.Color);
        }
        finally
        {
            dispatcher?.InvokeShutdown();
            ui.Join();
        }
    }
}
