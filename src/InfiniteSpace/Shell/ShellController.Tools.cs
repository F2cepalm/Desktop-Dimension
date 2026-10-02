using System.Windows;
using System.Windows.Input;
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
    private readonly SelectionWatch _selection = new(LetterKeysAfterMouseMove, minDrag: 6, TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime));
    private HoverKey? _wrapKey;
    private bool _wrapping;

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
        if (tool == Tool.TextWrap)
        {
            WrapSelection(fromKey: false); // an action, not a tool: the tool stays
            return;
        }
        switch (tool)
        {
            case Tool.Hand:
                CloseOverlay(); // a screen-wide tool, if one is open (its closing sets the hand too)
                spaces.SetTool(Tool.Hand);
                break;
            case Tool.Pen or Tool.Text or Tool.Eraser when source == ToolSource.SpaceKey:
                spaces.SetTool(tool); // only inside spaces: elsewhere the mouse stays the system's
                break;
            case Tool.Pen or Tool.Text or Tool.Eraser:
                FadeInkTools();
                IntPtr focus = Foreground.CaptureHost();
                var mode = tool switch { Tool.Pen => ScreenInkMode.Pen, Tool.Text => ScreenInkMode.Text, _ => ScreenInkMode.Eraser };
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
            if (_hotkeys!.Register(modifiers, key == Key.Oem3 ? KeyLeftOfOne() : key, ToggleWheel) is null)
                continue;
            _wheelHotkey = HotkeyText.Format(modifiers, key);
            if (!string.Equals(text, wanted[0], StringComparison.OrdinalIgnoreCase))
                _tray?.Notify("Tool wheel", $"{wanted[0]} is taken by another app, so the tool wheel opens with {_wheelHotkey}.");
            return;
        }
        unavailable.Add("the tool wheel's hotkey");
    }

    /// <summary>
    /// The key left of 1 on the keyboard in use (` on US keyboards, Ё on Russian ones, ^ on German ones):
    /// "`" in a hotkey means that key wherever the layout puts it.
    /// </summary>
    private static Key KeyLeftOfOne()
    {
        const uint LeftOfOne = 0x29, MAPVK_VSC_TO_VK = 1; // its scan code is the same on every layout
        uint vk = Win32.MapVirtualKey(LeftOfOne, MAPVK_VSC_TO_VK);
        return vk == 0 ? Key.Oem3 : KeyInterop.KeyFromVirtualKey((int)vk);
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
            ToolCursors.RefreshUnderMouse();
        };
        wheel.Picked += tool => SelectTool(tool, ToolSource.Wheel);
        _wheel = wheel;
        UpdateHoverKeys();
        wheel.Show();
        wheel.Activate();
    }

    /// <summary>
    /// Text wrap: the text selected in the app in front becomes a space of the selection's size on that
    /// app, with the same text inside in its size, colour, weight and slant (as far as the app tells).
    /// </summary>
    /// <param name="fromKey">W pressed after a selection: if nothing turns out to be selected, the W is typed into the app after all.</param>
    private async void WrapSelection(bool fromKey)
    {
        if (_wrapping)
            return;
        _wrapping = true;
        var dragged = _selection.Area;
        _selection.Forget();
        UpdateHoverKeys(); // W goes back to the apps
        try
        {
            if (!fromKey)
                await Task.Delay(120); // the wheel just handed focus back to the app: let it settle
            var found = await Task.Run(TextSelection.FromAutomation);
            if (found is null && fromKey && dragged is { } drag && await TextSelection.CopyAsync() is { } copied)
            {
                // The app doesn't tell where its text is: the area the mouse dragged over, a line tall at least.
                var area = new Rect(drag.X / Dpi.Scale, drag.Y / Dpi.Scale, drag.Width / Dpi.Scale, drag.Height / Dpi.Scale);
                double line = spaces.Pen.FontSize * NoteText.LineSpacing;
                area.Inflate(0, Math.Max(0, (line - area.Height) / 2) + line / 2);
                found = new SelectedText(copied, area, null, null, spaces.Pen.Bold, spaces.Pen.Italic, null);
            }
            if (found is not { Bounds: { } bounds })
            {
                if (fromKey)
                    TextSelection.SendKey(TextSelection.VK_W);
                else
                    _tray?.Notify("Text wrap", "Select some text in an app first, then pick Text wrap (or press W).");
                return;
            }
            // The size it shows at (zoom included) is its line height; the app's own point size otherwise.
            double size = found.LineHeight is { } height ? height / NoteText.LineSpacing
                : found.PointSize is { } points ? points * 96 / 72
                : spaces.Pen.FontSize;
            IntPtr host = AppWindows.WindowAt(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
            if (!spaces.WrapText(found.Text, bounds, host, size, found.Bold, found.Italic, found.Color))
                _tray?.Notify("Text wrap", "That selection is too small for a space.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Text wrap failed");
        }
        finally
        {
            _wrapping = false;
        }
    }

    /// <summary>Whether a screen point (device pixels) is in an app's client area, where a drag selects text (not this app, nor a title bar or frame).</summary>
    private static bool OnAppText(Win32.POINT p)
    {
        IntPtr window = Win32.WindowFromPoint(p);
        if (window == IntPtr.Zero || Foreground.IsOwn(window))
            return false;
        const int WM_NCHITTEST = 0x0084, HTCLIENT = 1;
        const uint SMTO_ABORTIFHUNG = 2;
        var lParam = new IntPtr(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
        return SendMessageTimeout(window, WM_NCHITTEST, IntPtr.Zero, lParam, SMTO_ABORTIFHUNG, 30, out var hit) != IntPtr.Zero && hit == HTCLIENT;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

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
