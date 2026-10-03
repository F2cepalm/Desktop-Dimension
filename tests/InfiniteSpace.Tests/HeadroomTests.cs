using InfiniteSpace.Rendering;

namespace InfiniteSpace.Tests;

public class HeadroomTests
{
    [Fact]
    public void Sizes_GoInWholeSteps_AndGrowHalfAgain()
    {
        Assert.Equal(256, Headroom.Size(10, 0));
        Assert.Equal(1024, Headroom.Size(1000, 0));
        Assert.Equal(1536, Headroom.Size(1100, 1024)); // grown: half as big again
        Assert.Equal(512, Headroom.Size(300, 0)); // shrunk: back to its size, in steps
    }

    [Fact]
    public void RoomForAReshape_IsMadeOnTopOfTheSize()
    {
        Assert.Equal(1000, Headroom.Wanted(1000, 1));
        Assert.Equal(1500, Headroom.Wanted(1000, 1.5));
        Assert.Equal(1536, Headroom.Size(Headroom.Wanted(1000, 1.5), 0)); // the first reshape frames fit
    }
}
