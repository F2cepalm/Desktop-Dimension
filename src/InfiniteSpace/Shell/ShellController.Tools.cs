using InfiniteSpace.Native;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>Where a tool was picked: it decides whether the tool works only in spaces or on the whole screen.</summary>
internal enum ToolSource
{
    SpaceKey,
    OverlayKey,
    Wheel,
    Toolbar,
}

/// <summary>Picking tools: the keys over a space, and the screen-wide tools they start.</summary>
internal sealed partial class ShellController
{
    private HoverKey[] _toolKeys = [];
    private HoverKey[] _groupKeys = [];

    /// <summary>Picks a tool (see the plan's decisions D2–D5).</summary>
    private void SelectTool(Tool tool, ToolSource source)
    {
        switch (tool)
        {
            case Tool.Hand or Tool.Pen or Tool.Text:
                spaces.SetTool(tool); // Phase 4: Pen/Text from the wheel or toolbar open the screen overlay
                break;
            case Tool.Cube:
                StartBlock(source == ToolSource.SpaceKey ? spaces.HostAtCursor() : Foreground.CaptureHost());
                break;
        }
        UpdateHoverKeys(); // an overlay may have opened: the letters go back to the apps
    }

    /// <summary>The Space cube: a block to place, as Ctrl+Alt+B spawns; <paramref name="host"/> is the layer the new space goes on.</summary>
    private void StartBlock(IntPtr host)
    {
        FadeInkTools();
        IntPtr focus = Foreground.CaptureHost(); // gets the keyboard back afterwards
        ShowOverlay(new BlockOverlay(spaces, universe, host), focus, endsInHand: true);
        spaces.SetTool(Tool.Cube); // after ShowOverlay: closing an overlay it replaced set the hand
    }
}
