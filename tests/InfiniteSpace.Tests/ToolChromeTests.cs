using System.Windows.Media;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Tests;

public class ToolChromeTests
{
    [Fact]
    public void EveryTheme_HasChromeWhoseForegroundReads()
    {
        foreach (string theme in Themes.All)
        {
            var chrome = Themes.ChromeOf(theme);
            double ratio = Contrast(chrome.Fore, chrome.Back);
            Assert.True(ratio >= 4.5, $"{theme}: {ratio:0.##}:1");
        }
    }

    [Fact]
    public void Dreamy_IsABlackSquareWithAWhiteRim()
    {
        var chrome = Themes.ChromeOf(Themes.Dreamy);

        Assert.Equal((Colors.Black, Colors.White, 2.0, 0.0), (chrome.Back, chrome.Edge, chrome.EdgeWidth, chrome.Corner));
    }

    [Fact]
    public void TheToolbar_BuildsAndRedresses_InEveryTheme()
    {
        Gpu.RunOnSta(() =>
        {
            var toolbar = new Shell.ToolbarWindow(Themes.ChromeOf(Themes.Dreamy), Spaces.Tool.Hand, null, () => "Ctrl+Alt+`");
            foreach (string theme in Themes.All)
                toolbar.ApplyChrome(Themes.ChromeOf(theme));
            toolbar.ShowTool(Spaces.Tool.Pen);
            Assert.NotNull(toolbar.Content);
        });
    }

    /// <summary>WCAG contrast ratio of two opaque colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
