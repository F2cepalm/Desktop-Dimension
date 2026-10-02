using System.Windows;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging;

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
    private static readonly string[] WheelHotkeys = ["Ctrl+Alt+`", "Ctrl+Alt+W", "Win+Alt+W"];
    private ToolWheelWindow? _wheel;
    private string? _wheelHotkey;
    private ToolbarWindow? _toolbar;

    /// <summary>Picks a tool (see the plan's decisions D2–D5).</summary>
    private void SelectTool(Tool tool, ToolSource source)
    {
        _wheel?.Dismiss(); // a toolbar pick while the wheel is open
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

    /// <summary>Registers the wheel's hotkey: the one in settings, else the first default no other app holds.</summary>
    private void RegisterWheelHotkey(List<string> unavailable)
    {
        string[] wanted = _settings.ToolWheelHotkey is { Length: > 0 } chosen ? [chosen, .. WheelHotkeys] : WheelHotkeys;
        foreach (string text in wanted.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!HotkeyText.TryParse(text, out var modifiers, out var key))
            {
                logger.LogWarning("Can't read the hotkey {Hotkey}", text);
                continue;
            }
            if (_hotkeys!.Register(modifiers, key, ToggleWheel) is null)
                continue;
            _wheelHotkey = HotkeyText.Format(modifiers, key);
            if (!string.Equals(text, wanted[0], StringComparison.OrdinalIgnoreCase))
                _tray?.Notify("Tool wheel", $"{wanted[0]} is taken by another app, so the tool wheel opens with {_wheelHotkey}.");
            return;
        }
        unavailable.Add("the tool wheel's hotkey");
    }

    /// <summary>The wheel hotkey: opens the wheel at the mouse, or closes it.</summary>
    private void ToggleWheel()
    {
        if (_wheel is not null)
        {
            _wheel.Dismiss();
            return;
        }
        CloseOverlay(); // a screen-wide tool ends (and hands focus back) before the wheel takes it
        FadeInkTools();
        IntPtr previousApp = Foreground.CaptureHost();
        var wheel = new ToolWheelWindow(CursorOnScreen(), Themes.ChromeOf(spaces.Theme), spaces.Tool);
        wheel.Gone += () =>
        {
            if (_wheel == wheel)
                _wheel = null;
            Foreground.RestoreTo(previousApp);
            spaces.Restack();
            UpdateHoverKeys();
        };
        wheel.Picked += tool => SelectTool(tool, ToolSource.Wheel);
        _wheel = wheel;
        UpdateHoverKeys();
        wheel.Show();
        wheel.Activate();
    }

    /// <summary>Puts the toolbar on screen, where it was left (or at the primary screen's right edge).</summary>
    private void OpenToolbar()
    {
        if (_toolbar is not null)
            return;
        Point? at = _settings.ToolbarLeft is { } x && _settings.ToolbarTop is { } y ? new Point(x, y) : null;
        var toolbar = new ToolbarWindow(Themes.ChromeOf(spaces.Theme), spaces.Tool, at, () => _wheelHotkey);
        toolbar.Picked += tool => SelectTool(tool, ToolSource.Toolbar);
        toolbar.Moved += square =>
        {
            (_settings.ToolbarLeft, _settings.ToolbarTop) = (square.X, square.Y);
            SaveSettings();
        };
        _toolbar = toolbar;
        toolbar.Show();
    }

    /// <summary>Tray, Show toolbar: on or off, remembered.</summary>
    private void SetToolbarShown(bool shown)
    {
        _settings.ShowToolbar = shown;
        SaveSettings();
        if (shown)
        {
            OpenToolbar();
        }
        else
        {
            _toolbar?.Close();
            _toolbar = null;
        }
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
