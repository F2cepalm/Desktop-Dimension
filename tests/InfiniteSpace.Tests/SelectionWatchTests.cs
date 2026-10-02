using System.Windows;
using InfiniteSpace.Shell;

namespace InfiniteSpace.Tests;

public class SelectionWatchTests
{
    private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);

    private static SelectionWatch Watch() => new(TimeSpan.FromSeconds(3), minDrag: 6, doubleClick: 500 * Ms);

    [Fact]
    public void ADragOnText_IsASelection_ForAWhile()
    {
        var watch = Watch();
        watch.Sample(true, new Point(100, 100), 0 * Ms, () => true);
        watch.Sample(false, new Point(180, 120), 400 * Ms, () => true);

        Assert.True(watch.IsRecent(1000 * Ms));
        Assert.Equal(new Rect(100, 100, 80, 20), watch.Area);
        Assert.False(watch.IsRecent(3500 * Ms));
    }

    [Fact]
    public void AClick_OrADragOffText_IsNot()
    {
        var watch = Watch();
        watch.Sample(true, new Point(100, 100), 0 * Ms, () => true);
        watch.Sample(false, new Point(102, 100), 100 * Ms, () => true);
        Assert.False(watch.IsRecent(200 * Ms));

        watch.Sample(true, new Point(100, 100), 2000 * Ms, () => false); // a title bar, or this app
        watch.Sample(false, new Point(300, 100), 2300 * Ms, () => false);
        Assert.False(watch.IsRecent(2400 * Ms));
    }

    [Fact]
    public void ADoubleClick_SelectsAWord_ButTwoSlowClicksDont()
    {
        var watch = Watch();
        watch.Sample(true, new Point(50, 50), 0 * Ms, () => true);
        watch.Sample(false, new Point(50, 50), 80 * Ms, () => true);
        watch.Sample(true, new Point(51, 50), 1000 * Ms, () => true);
        watch.Sample(false, new Point(51, 50), 1080 * Ms, () => true);
        Assert.False(watch.IsRecent(1100 * Ms));

        watch.Sample(true, new Point(51, 50), 1300 * Ms, () => true);
        watch.Sample(false, new Point(51, 50), 1380 * Ms, () => true);
        Assert.True(watch.IsRecent(1400 * Ms));
    }

    [Fact]
    public void Forget_GivesWBack()
    {
        var watch = Watch();
        watch.Sample(true, new Point(0, 0), 0 * Ms, () => true);
        watch.Sample(false, new Point(50, 0), 100 * Ms, () => true);

        watch.Forget();

        Assert.False(watch.IsRecent(200 * Ms));
    }
}
