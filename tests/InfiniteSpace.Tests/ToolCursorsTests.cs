using InfiniteSpace.Native;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

public class ToolCursorsTests
{
    [Theory]
    [InlineData((int)Tool.Pen, false, Win32.IDC_CROSS)]
    [InlineData((int)Tool.Pen, true, Win32.IDC_CROSS)]
    [InlineData((int)Tool.Eraser, true, Win32.IDC_CROSS)]
    [InlineData((int)Tool.Text, false, Win32.IDC_IBEAM)]
    [InlineData((int)Tool.Text, true, Win32.IDC_IBEAM)]
    [InlineData((int)Tool.Hand, false, Win32.IDC_ARROW)]
    [InlineData((int)Tool.Hand, true, Win32.IDC_SIZEALL)]
    public void EachTool_HasItsCursor(int tool, bool overInk, int expected)
    {
        Assert.Equal(expected, ToolCursors.IdFor((Tool)tool, overInk));
    }

    [Fact]
    public void ScreenNotes_TakeTheMouse_OnlyForTheHand()
    {
        foreach (var tool in Enum.GetValues<Tool>())
        {
            Assert.Equal(tool != Tool.Hand, SpaceWindow.LetsClicksThrough(SpaceKind.Sheet, tool));
            Assert.False(SpaceWindow.LetsClicksThrough(SpaceKind.Space, tool));
        }
    }
}
