using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>
/// The toolbar: a small square in the theme's chrome with three dots, always on top. A click unfolds the
/// tools, named, with their keys; a drag moves it. It never takes the keyboard from the app (or from a
/// screen-wide tool), and it hides while a full-screen app is in front.
/// </summary>
internal sealed class ToolbarWindow : Window
{
    /// <summary>The side of the folded square, in DIPs.</summary>
    public const double Square = 30;

    private const double MenuWidth = 184;
    private const double RowHeight = 28;
    private const double Gap = 4;

    /// <summary>How far (DIPs) a press on the square must move to become a drag.</summary>
    private const double DragThreshold = 4;

    private readonly Func<string?> _wheelHotkey;
    private readonly StackPanel _panel = new();
    private readonly Border _squareView = new() { Width = Square, Height = Square, Cursor = Cursors.Hand };
    private readonly Border _menu = new() { Width = MenuWidth, Padding = new Thickness(4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _header = new() { FontSize = 10.5, Opacity = 0.7, Margin = new Thickness(8, 2, 8, 4) };
    private readonly Dictionary<Tool, (Border Row, TextBlock Name)> _rows = [];
    private readonly DispatcherTimer _outsideWatch = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _fullScreenWatch = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private ToolChrome _chrome;
    private Tool _current;
    private Tool? _hovered;
    private Point _square;           // the square's top-left, screen DIPs
    private Point? _pressedAt;       // the cursor on screen when the square was pressed
    private Point _squareAtPress;
    private bool _dragging;

    /// <param name="chrome">How it is dressed (the theme's).</param>
    /// <param name="current">The tool in use, marked in the menu.</param>
    /// <param name="square">Where its square goes (screen DIPs); null for the primary screen's right edge.</param>
    /// <param name="wheelHotkey">The wheel's hotkey now, shown at the top of the menu.</param>
    public ToolbarWindow(ToolChrome chrome, Tool current, Point? square, Func<string?> wheelHotkey)
    {
        _chrome = chrome;
        _current = current;
        _wheelHotkey = wheelHotkey;
        _square = square is { } at && OnScreen(at) ? at : DefaultPlace();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = _square.X;
        Top = _square.Y;

        // Never taking focus is what keeps a click here from closing the screen overlays or the app's caret.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
            {
                if (msg != Win32.WM_MOUSEACTIVATE)
                    return IntPtr.Zero;
                handled = true;
                return new IntPtr(Win32.MA_NOACTIVATE);
            });
        };

        var rows = new StackPanel();
        rows.Children.Add(_header);
        foreach (var info in ToolCatalog.All)
            rows.Children.Add(Row(info));
        _menu.Child = rows;
        _panel.Children.Add(_squareView);
        _panel.Children.Add(_menu);
        Content = _panel;
        ApplyChrome(chrome);

        _squareView.MouseLeftButtonDown += (_, e) =>
        {
            _pressedAt = CursorOnScreen();
            _squareAtPress = _square;
            _dragging = false;
            _squareView.CaptureMouse();
            e.Handled = true;
        };
        _squareView.MouseMove += (_, _) =>
        {
            if (_pressedAt is not { } pressed || !_squareView.IsMouseCaptured)
                return;
            var travel = CursorOnScreen() - pressed;
            if (!_dragging && travel.Length < DragThreshold)
                return;
            if (!_dragging)
            {
                _dragging = true;
                Collapse();
            }
            _square = Clamp(_squareAtPress + travel);
            Left = _square.X;
            Top = _square.Y;
        };
        _squareView.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool wasPressed = _pressedAt is not null;
            _pressedAt = null;
            _squareView.ReleaseMouseCapture();
            if (!wasPressed)
                return;
            if (_dragging)
                Moved?.Invoke(_square);
            else if (_menu.Visibility == Visibility.Visible)
                Collapse();
            else
                Expand();
            _dragging = false;
        };
        _squareView.LostMouseCapture += (_, _) => _pressedAt = null;

        _outsideWatch.Tick += (_, _) =>
        {
            if (!Win32.IsMouseButtonDown())
                return;
            var cursor = CursorOnScreen();
            if (!new Rect(Left, Top, ActualWidth, ActualHeight).Contains(cursor))
                Collapse(); // the click still reaches whatever it was on
        };
        _fullScreenWatch.Tick += (_, _) => HideForFullScreen();
        Loaded += (_, _) => _fullScreenWatch.Start();
        Closed += (_, _) =>
        {
            _outsideWatch.Stop();
            _fullScreenWatch.Stop();
        };
    }

    /// <summary>A tool was picked in the menu.</summary>
    public event Action<Tool>? Picked;

    /// <summary>The square was dragged here (screen DIPs of its top-left).</summary>
    public event Action<Point>? Moved;

    /// <summary>Marks the current tool's row.</summary>
    public void ShowTool(Tool tool)
    {
        _current = tool;
        RestyleRows();
    }

    /// <summary>A new theme: rebuilds brushes and the dots.</summary>
    public void ApplyChrome(ToolChrome chrome)
    {
        _chrome = chrome;
        var back = Frozen(chrome.Back);
        var edge = Frozen(chrome.Edge);
        var fore = Frozen(chrome.Fore);
        foreach (var frame in new[] { _squareView, _menu })
        {
            frame.Background = back;
            frame.BorderBrush = edge;
            frame.BorderThickness = new Thickness(chrome.EdgeWidth);
            frame.CornerRadius = new CornerRadius(chrome.Corner);
        }
        _squareView.Child = Dots(fore, pixel: chrome.Corner == 0);
        _header.Foreground = fore;
        foreach (var info in ToolCatalog.All)
        {
            var (row, name) = _rows[info.Tool];
            row.CornerRadius = new CornerRadius(Math.Min(chrome.Corner, 4));
            var grid = (Grid)row.Child;
            grid.Children.RemoveAt(0);
            var icon = ToolIcons.Shape(info.Icon, fore);
            icon.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Insert(0, icon);
            name.Foreground = fore;
            ((TextBlock)grid.Children[2]).Foreground = fore;
        }
        RestyleRows();
    }

    /// <summary>Back to the very top (after a screen-wide overlay was shown over it).</summary>
    public void BringToFront()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>Folds the menu back into the square.</summary>
    public void Collapse()
    {
        _outsideWatch.Stop();
        if (_menu.Visibility != Visibility.Visible)
            return;
        _menu.Visibility = Visibility.Collapsed;
        Left = _square.X;
        Top = _square.Y;
    }

    /// <summary>Unfolds the menu downwards and rightwards where there is room, else upwards or leftwards.</summary>
    private void Expand()
    {
        string? hotkey = _wheelHotkey();
        _header.Text = hotkey is null ? "Tools" : $"Tools · {hotkey} opens the wheel";
        _menu.Visibility = Visibility.Visible;
        _menu.Measure(new Size(MenuWidth, double.PositiveInfinity));
        double menuHeight = _menu.DesiredSize.Height;
        double right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        double bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        bool rightwards = _square.X + MenuWidth <= right;
        bool down = _square.Y + Square + Gap + menuHeight <= bottom;

        _panel.Children.Clear();
        if (down)
        {
            _panel.Children.Add(_squareView);
            _panel.Children.Add(_menu);
        }
        else
        {
            _panel.Children.Add(_menu);
            _panel.Children.Add(_squareView);
        }
        var side = rightwards ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _squareView.HorizontalAlignment = _menu.HorizontalAlignment = side;
        _menu.Margin = down ? new Thickness(0, Gap, 0, 0) : new Thickness(0, 0, 0, Gap);
        Left = rightwards ? _square.X : _square.X + Square - MenuWidth;
        Top = down ? _square.Y : _square.Y - menuHeight - Gap;

        if (SystemParameters.ClientAreaAnimation)
        {
            var scale = new ScaleTransform(1, 0);
            _menu.RenderTransformOrigin = new Point(0.5, down ? 0 : 1); // it grows out of the square
            _menu.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        }
        _outsideWatch.Start();
    }

    /// <summary>One tool in the menu: its icon, name and key (or "soon").</summary>
    private Border Row(ToolInfo info)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ToolIcons.Shape(info.Icon, Brushes.White);
        icon.Margin = new Thickness(0, 0, 10, 0);
        var name = new TextBlock { Text = info.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = info.Key?.ToString() ?? "soon", FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(hint, 2);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(hint);

        var row = new Border
        {
            Height = RowHeight,
            Padding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = grid,
        };
        if (info.IsStub)
            row.Opacity = 0.55;
        AutomationProperties.SetName(row, info.Name);
        row.MouseEnter += (_, _) =>
        {
            _hovered = info.Tool;
            RestyleRows();
        };
        row.MouseLeave += (_, _) =>
        {
            if (_hovered == info.Tool)
                _hovered = null;
            RestyleRows();
        };
        // Clicks act on release, as on the ink palette.
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Collapse();
            Picked?.Invoke(info.Tool);
        };
        _rows[info.Tool] = (row, name);
        return row;
    }

    /// <summary>The current tool's row stands out; the hovered one a little.</summary>
    private void RestyleRows()
    {
        var a = _chrome.Accent;
        foreach (var (tool, (row, name)) in _rows)
        {
            row.Background = tool == _current ? Frozen(Color.FromArgb(0x55, a.R, a.G, a.B))
                : tool == _hovered ? Frozen(Color.FromArgb(0x30, a.R, a.G, a.B))
                : Brushes.Transparent;
            name.FontWeight = tool == _current ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    /// <summary>Hidden while a full-screen app, game or presentation is in front (this app's own overlays don't count).</summary>
    private void HideForFullScreen()
    {
        IntPtr front = Win32.GetForegroundWindow();
        bool fullScreen = !Native.Foreground.IsOwn(front)
            && Win32.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;
        var wanted = fullScreen ? Visibility.Hidden : Visibility.Visible;
        if (Visibility != wanted)
            Visibility = wanted;
    }

    /// <summary>Three 4×4 dots, 3 DIPs apart: square ones in the pixel themes, round ones elsewhere.</summary>
    private static UIElement Dots(Brush fore, bool pixel)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        for (int i = 0; i < 3; i++)
        {
            Shape dot = pixel ? new Rectangle() : new Ellipse();
            dot.Width = dot.Height = 4;
            dot.Fill = fore;
            dot.Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0);
            row.Children.Add(dot);
        }
        return row;
    }

    private static Point DefaultPlace() =>
        new(SystemParameters.PrimaryScreenWidth - Square - 12, Math.Round(SystemParameters.PrimaryScreenHeight * 0.35));

    private static bool OnScreen(Point at) =>
        new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth - Square, SystemParameters.VirtualScreenHeight - Square).Contains(at);

    private static Point Clamp(Point at) => new(
        Math.Clamp(at.X, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Square),
        Math.Clamp(at.Y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Square));

    private static Point CursorOnScreen() =>
        Win32.GetCursorPos(out var p) ? new Point(p.X / Dpi.Scale, p.Y / Dpi.Scale) : default;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
