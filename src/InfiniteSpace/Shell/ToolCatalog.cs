using System.Windows.Input;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>A tool as menus show it: its name, what it does, its key over a space, and whether it is a placeholder.</summary>
internal sealed record ToolInfo(Tool Tool, string Name, string Description, Key? Key, bool IsStub, string Icon);

/// <summary>Every tool, in the wheel's order (clockwise from the top) and the toolbar's (top to bottom).</summary>
internal static class ToolCatalog
{
    /// <summary>The tools, one per <see cref="Tool"/>, in its order.</summary>
    public static IReadOnlyList<ToolInfo> All { get; } =
    [
        new(Tool.Hand, "Hand", "Select, move, group and edit", Key.H, false, ToolIcons.Hand),
        new(Tool.Pen, "Pen", "Draw on spaces and over apps", Key.P, false, ToolIcons.Pen),
        new(Tool.Text, "Text", "Type a note", Key.T, false, ToolIcons.Text),
        new(Tool.Cube, "Space cube", "Place a new rectangular space", Key.C, false, ToolIcons.Cube),
        new(Tool.ProjectMeter, "Project meter", "Coming soon", null, true, ToolIcons.Meter),
        new(Tool.TextWrap, "Text wrap", "Turn text selected in an app into a space", Key.W, false, ToolIcons.Wrap),
        new(Tool.Eraser, "Eraser", "Erase strokes and notes", Key.E, false, ToolIcons.Eraser),
        new(Tool.List, "List", "Coming soon", null, true, ToolIcons.List),
    ];

    /// <summary>How menus show a tool.</summary>
    public static ToolInfo Of(Tool tool) => All.First(t => t.Tool == tool);

    /// <summary>The tool a plain key picks (H, P, T, C, E, and W for text wrap), if any.</summary>
    public static Tool? ForKey(Key key) => All.FirstOrDefault(t => t.Key == key)?.Tool;
}
