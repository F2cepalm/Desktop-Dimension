using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class TextWrapTests
{
    private const string Paragraph = "A paragraph a little too long for the area it came from, once laid out in our font.";

    [Fact]
    public void Padding_ClearsTheRim_AtEverySize()
    {
        for (double size = 6; size <= 72; size += 0.5)
        {
            double pad = SpaceManager.CardPadding(size);
            Assert.True(pad >= 10 && pad >= size * 0.75, $"{size}: {pad}");
            Assert.Equal(0, pad % DreamStyle.ArtPixel);
        }
    }

    [Fact]
    public void ACard_HoldsItsTextInsideTheMargins()
    {
        Gpu.RunOnSta(() =>
        {
            var note = new TextNote { Text = Paragraph, FontSize = 16 };
            var size = SpaceManager.CardSize(note, 240);
            double pad = SpaceManager.CardPadding(16);

            using var layout = NoteText.Layout(Paragraph, 16, 240 - 2 * pad);
            Assert.Equal(240, size.Width);
            Assert.True(size.Height >= layout.Metrics.Height + 2 * pad);
            Assert.True(layout.Metrics.Width <= 240 - 2 * pad + 0.5);
        });
    }

    [Fact]
    public void ANarrowerCard_IsNeverShorter()
    {
        Gpu.RunOnSta(() =>
        {
            var note = new TextNote { Text = Paragraph, FontSize = 18 };
            double last = 0;
            for (double width = 600; width >= 100; width -= 20)
            {
                double height = SpaceManager.CardSize(note, width).Height;
                Assert.True(height >= last, $"{width}: {height} < {last}");
                last = height;
            }
        });
    }

    [Fact]
    public void ACard_IsNeverNarrowerThanAFewLetters()
    {
        Gpu.RunOnSta(() =>
        {
            var size = SpaceManager.CardSize(new TextNote { Text = "Hi", FontSize = 20 }, 5);

            Assert.True(size.Width >= SpaceManager.MinCardWidth(20));
        });
    }

    [Fact]
    public void ShapeCard_PutsTheNoteInsideTheMargins_AndTheOutlineRoundIt()
    {
        Gpu.RunOnSta(() =>
        {
            var record = new SpaceRecord { Card = true, Notes = [new TextNote { Text = Paragraph, FontSize = 16 }] };

            var box = SpaceManager.ShapeCard(record, new Point(100, 50), 300);

            double pad = SpaceManager.CardPadding(16);
            var note = record.Notes[0];
            Assert.Equal((100 + pad, 50 + pad, 300 - 2 * pad), (note.X, note.Y, note.Width!.Value));
            Assert.Equal(box, SpaceGeometry.Decode(record).Bounds);
            Assert.Equal(SpaceManager.CardSize(note, 300), box.Size);
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
