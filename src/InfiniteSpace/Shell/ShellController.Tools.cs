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
        var info = ToolCatalog.Of(tool);
        if (info.IsStub)
        {
            _tray?.Notify(info.Name, "Coming soon.");
            return; // placeholders don't change the tool
        }
        switch (tool)
        {
            case Tool.Hand:
                CloseOverlay(); // a screen-wide tool, if one is open (its closing sets the hand too)
                spaces.SetTool(Tool.Hand);
                break;
            case Tool.Pen or Tool.Text when source == ToolSource.SpaceKey:
                spaces.SetTool(tool); // only inside spaces: elsewhere the mouse stays the system's
                break;
            case Tool.Pen or Tool.Text:
                FadeInkTools();
                IntPtr focus = Foreground.CaptureHost();
                var mode = tool == Tool.Pen ? ScreenInkMode.Pen : ScreenInkMode.Text;
                ShowOverlay(new ScreenInkOverlay(mode, spaces, universe, t => SelectTool(t, ToolSource.OverlayKey)), focus, endsInHand: true);
                spaces.SetTool(tool); // after ShowOverlay: closing the overlay it replaced set the hand
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
