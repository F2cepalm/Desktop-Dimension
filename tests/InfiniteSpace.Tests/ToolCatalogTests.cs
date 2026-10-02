using System.Windows.Input;
using System.Windows.Media;
using InfiniteSpace.Shell;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class ToolCatalogTests
{
    [Fact]
    public void EveryTool_IsListed_InTheEnumsOrder()
    {
        Assert.Equal(Enum.GetValues<Tool>(), ToolCatalog.All.Select(t => t.Tool));
        Assert.Equal(10, ToolCatalog.All.Count);
    }

    [Fact]
    public void ProjectMeterAndList_ArePlaceholders()
    {
        Assert.Equal([false, false, false, false, true, false, false, true, false, false], ToolCatalog.All.Select(t => t.IsStub));
    }

    [Fact]
    public void TheRealTools_HaveKeys_HPTCWE()
    {
        Assert.Equal<Key?>([Key.H, Key.P, Key.T, Key.C, null, Key.W, Key.E, null, null, null], ToolCatalog.All.Select(t => t.Key));
    }

    [Fact]
    public void EveryIcon_Parses()
    {
        Gpu.RunOnSta(() =>
        {
            foreach (var info in ToolCatalog.All)
                Assert.False(Geometry.Parse(info.Icon).IsEmpty(), info.Name);
        });
    }

    [Fact]
    public void Keys_PickTheirTools()
    {
        Assert.Equal(Tool.Pen, ToolCatalog.ForKey(Key.P));
        Assert.Equal(Tool.Eraser, ToolCatalog.ForKey(Key.E));
        Assert.Null(ToolCatalog.ForKey(Key.Q));
    }
}
