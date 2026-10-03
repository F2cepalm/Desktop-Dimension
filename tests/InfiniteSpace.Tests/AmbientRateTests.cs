using InfiniteSpace.Rendering;

namespace InfiniteSpace.Tests;

public class AmbientRateTests
{
    [Theory]
    [InlineData(0.5, 60)] // a fast CPU: the full rate
    [InlineData(4.0, 60)] // a quarter of the UI thread exactly
    [InlineData(6.0, 30)] // a throttled laptop: half
    [InlineData(10.0, 20)]
    [InlineData(40.0, 15)] // never slower than 15
    public void TheRate_KeepsTheAnimationToAQuarterOfTheUiThread(double cost, double rate)
    {
        Assert.Equal(rate, Universe.AmbientRateFor(cost, batterySaver: false, current: 60));
    }

    [Fact]
    public void BatterySaver_HalvesIt()
    {
        Assert.Equal(30, Universe.AmbientRateFor(0.5, batterySaver: true, current: 60));
        Assert.Equal(60, Universe.AmbientRateFor(0.5, batterySaver: false, current: 30));
    }

    [Fact]
    public void ItGoesBackUp_OnlyWithRoomToSpare()
    {
        Assert.Equal(30, Universe.AmbientRateFor(3.5, batterySaver: false, current: 30)); // 60 would just fit: stays
        Assert.Equal(60, Universe.AmbientRateFor(3.0, batterySaver: false, current: 30));
    }

    [Fact]
    public void BeforeAnyFrame_ItKeepsTheRateItHas()
    {
        Assert.Equal(60, Universe.AmbientRateFor(double.NaN, batterySaver: false, current: 60));
        Assert.Equal(30, Universe.AmbientRateFor(double.NaN, batterySaver: true, current: 60));
    }
}
