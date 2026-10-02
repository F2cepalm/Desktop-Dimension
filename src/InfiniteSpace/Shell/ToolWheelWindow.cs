using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Path = System.Windows.Shapes.Path;

namespace InfiniteSpace.Shell;

/// <summary>
/// The tool wheel: eight tools in a ring round the mouse. The sector in the mouse's direction lights up and
/// a click picks it; Esc, a right-click, a click in the middle or elsewhere closes it.
/// </summary>
internal sealed class ToolWheelWindow : Window
{
    /// <summary>The inner radius of the ring (DIPs): a click inside it closes the wheel.</summary>
    public const double Inner = 40;

    /// <summary>The outer radius of the ring (DIPs).</summary>
    public const double Outer = 130; // room for ten sectors' labels

    private const double Side = 300;
    private const double LabelWidth = 64; // narrower than a sector at the labels' radius, so neighbours never touch

    private readonly Canvas _canvas;
    private readonly Path[] _sectors = new Path[WheelGeometry.Count];
    private readonly Brush _back;
    private readonly Brush _lit;
    private readonly TextBlock _centreText;
    private readonly Point _middle = new(Side / 2, Side / 2);
    private int? _highlighted;
    private Tool? _picked;
    private bool _closing;

    /// <param name="centre">Screen DIP point the wheel is centred on (it is kept on screen).</param>
    /// <param name="chrome">How it is dressed (the theme's).</param>
    /// <param name="current">The tool in use, marked on the wheel.</param>
    public ToolWheelWindow(Point centre, ToolChrome chrome, Tool current)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = Side;
        Height = Side;
        Left = centre.X - Side / 2;
        Top = centre.Y - Side / 2;

        _back = Frozen(Color.FromArgb(0xF2, chrome.Back.R, chrome.Back.G, chrome.Back.B));
        _lit = Frozen(Blend(chrome.Back, chrome.Accent, 0.35));
        var fore = Frozen(chrome.Fore);
        var edge = Frozen(chrome.Edge);
        var accent = Frozen(chrome.Accent);

        // Nearly invisible, but it takes the mouse everywhere in the window (a fully clear pixel lets clicks through).
        _canvas = new Canvas { Width = Side, Height = Side, Background = Frozen(Color.FromArgb(1, 0, 0, 0)) };
        for (int i = 0; i < WheelGeometry.Count; i++)
        {
            var info = ToolCatalog.All[i];
            bool isCurrent = info.Tool == current;
            var sector = new Path
            {
                Data = WheelGeometry.Sector(i, Inner, Outer, _middle),
                Fill = _back,
                Stroke = isCurrent ? accent : edge,
                StrokeThickness = Math.Max(1, chrome.EdgeWidth) + (isCurrent ? 1 : 0),
            };
            _sectors[i] = sector;
            _canvas.Children.Add(sector);

            var iconAt = WheelGeometry.Polar(_middle, (Inner + Outer) / 2 - 7, WheelGeometry.MiddleOf(i));
            var icon = ToolIcons.Shape(info.Icon, fore);
            Canvas.SetLeft(icon, iconAt.X - 8);
            Canvas.SetTop(icon, iconAt.Y - 8);
            _canvas.Children.Add(icon);

            var labelAt = WheelGeometry.Polar(_middle, (Inner + Outer) / 2 + 8, WheelGeometry.MiddleOf(i));
            var label = new TextBlock
            {
                Text = info.IsStub ? info.Name + "\nsoon" : info.Name,
                FontSize = 10.5,
                Width = LabelWidth,
                TextAlignment = TextAlignment.Center,
                Foreground = fore,
                IsHitTestVisible = false,
            };
            label.Measure(new Size(LabelWidth, double.PositiveInfinity));
            Canvas.SetLeft(label, labelAt.X - LabelWidth / 2);
            Canvas.SetTop(label, labelAt.Y - label.DesiredSize.Height / 2);
            _canvas.Children.Add(label);

            if (info.IsStub)
                sector.Opacity = icon.Opacity = label.Opacity = 0.55;
            icon.IsHitTestVisible = false;
        }

        var centreDisc = new Ellipse { Width = 2 * (Inner - 4), Height = 2 * (Inner - 4), Fill = _back, Stroke = edge, StrokeThickness = Math.Max(1, chrome.EdgeWidth), IsHitTestVisible = false };
        Canvas.SetLeft(centreDisc, _middle.X - (Inner - 4));
        Canvas.SetTop(centreDisc, _middle.Y - (Inner - 4));
        _canvas.Children.Add(centreDisc);
        _centreText = new TextBlock { Width = 70, FontSize = 10.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = fore, IsHitTestVisible = false };
        _canvas.Children.Add(_centreText);
        ShowCentre();
        Content = _canvas;

        _canvas.MouseMove += (_, e) => Highlight(WheelGeometry.SegmentAt(e.GetPosition(_canvas) - _middle, Inner));
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (WheelGeometry.SegmentAt(e.GetPosition(_canvas) - _middle, Inner) is { } index)
                _picked = ToolCatalog.All[index].Tool;
            Dismiss();
        };
        MouseRightButtonUp += (_, _) => Dismiss();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Dismiss();
        };
        Deactivated += (_, _) => Dismiss();
        Loaded += (_, _) =>
        {
            KeepOnScreen();
            if (SystemParameters.ClientAreaAnimation)
                Grow();
        };
        Closed += (_, _) =>
        {
            Gone?.Invoke(); // focus goes back to the app first…
            if (_picked is { } tool)
                Dispatcher.BeginInvoke(() => Picked?.Invoke(tool)); // …then the tool may open an overlay that takes it
        };
    }

    /// <summary>A tool was picked; raised after the wheel has closed and <see cref="Gone"/> was raised.</summary>
    public event Action<Tool>? Picked;

    /// <summary>Raised once when it closes, picked or not.</summary>
    public event Action? Gone;

    /// <summary>Closes it without a pick; safe to call again.</summary>
    public void Dismiss()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    private void Highlight(int? index)
    {
        if (index == _highlighted)
            return;
        if (_highlighted is { } old)
            _sectors[old].Fill = _back;
        _highlighted = index;
        if (index is { } now)
            _sectors[now].Fill = _lit;
        ShowCentre();
    }

    /// <summary>The highlighted tool's name and key in the middle, or "Tools".</summary>
    private void ShowCentre()
    {
        if (_highlighted is { } index)
        {
            var info = ToolCatalog.All[index];
            _centreText.Text = info.Key is { } key ? $"{info.Name} · {key}" : $"{info.Name} · soon";
        }
        else
        {
            _centreText.Text = "Tools";
        }
        _centreText.Measure(new Size(70, double.PositiveInfinity));
        Canvas.SetLeft(_centreText, _middle.X - 35);
        Canvas.SetTop(_centreText, _middle.Y - _centreText.DesiredSize.Height / 2);
    }

    /// <summary>Fades and scales the wheel in from 90% (only while Windows' animations are on).</summary>
    private void Grow()
    {
        var scale = new ScaleTransform(0.9, 0.9, Side / 2, Side / 2);
        _canvas.RenderTransform = scale;
        var duration = TimeSpan.FromMilliseconds(120);
        _canvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, duration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, duration));
    }

    private void KeepOnScreen()
    {
        double right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        double bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(Left, right - ActualWidth));
        Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(Top, bottom - ActualHeight));
    }

    /// <summary><paramref name="b"/> laid over <paramref name="a"/> at <paramref name="t"/> opacity.</summary>
    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
