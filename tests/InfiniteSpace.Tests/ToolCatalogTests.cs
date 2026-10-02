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
        Assert.Equal(8, ToolCatalog.All.Count);
    }

    [Fact]
    public void ProjectMeterTextWrapAndList_ArePlaceholders()
    {
        Assert.Equal([false, false, false, false, true, true, false, true], ToolCatalog.All.Select(t => t.IsStub));
    }

    [Fact]
    public void TheRealTools_HaveKeys_HPTCE()
    {
        Assert.Equal<Key?>([Key.H, Key.P, Key.T, Key.C, null, null, Key.E, null], ToolCatalog.All.Select(t => t.Key));
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
