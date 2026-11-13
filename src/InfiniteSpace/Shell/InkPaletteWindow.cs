using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>
/// The ink palette on its own, opened by right-clicking a space (or from the tray menu): the colour,
/// paint width and text size of what is painted and typed next. Every pick applies at once. Esc or
/// Enter closes it; clicking elsewhere, or starting to paint or erase, fades it away (<see cref="FadeOut"/>).
/// </summary>
internal sealed class InkPaletteWindow : Window
{
    private InkPen _pen;
    private bool _closing;
    private bool _fading;
    private bool _gone;
    private bool _openingFile;

    /// <param name="at">Screen DIP position of its top-left corner (it is kept on screen).</param>
    /// <param name="changed">Called with the pen after each pick.</param>
    /// <param name="openFile">
    /// Offers "Open a file…" (on a space): called once the palette is out of the way. None when null.
    /// </param>
    public InkPaletteWindow(Point at, InkPen pen, Color themeInk, Action<InkPen> changed, Action? openFile = null)
    {
        _pen = pen;

        Title = "Desktop_Dimension – ink";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = at.X;
        Top = at.Y;

        var palette = new InkPalette(themeInk, pen.Color, pen.Thickness, pen.FontSize, stacked: true, paint: true, bold: pen.Bold, italic: pen.Italic);
        palette.ColorPicked += color => changed(_pen = _pen with { Color = color });
        palette.ThicknessPicked += thickness => changed(_pen = _pen with { Thickness = thickness });
        palette.FontSizePicked += fontSize => changed(_pen = _pen with { FontSize = fontSize });
        palette.StylePicked += (bold, italic) => changed(_pen = _pen with { Bold = bold, Italic = italic });
        Content = openFile is null ? palette : new StackPanel { Children = { palette, OpenFileButton(openFile) } };

        Loaded += (_, _) => KeepOnScreen();
        Deactivated += (_, _) =>
        {
            if (!_openingFile) // the file dialog takes over: not a click elsewhere
                FadeOut();
        };
        Closed += (_, _) => Leave();
    }

    /// <summary>
    /// Raised once, as soon as the palette is done with: when it starts fading out, or closes. Focus
    /// can go back to the app then, before the fade is over.
    /// </summary>
    public event Action? Gone;

    /// <summary>Closes it at once.</summary>
    public void Dismiss()
    {
        if (!_closing)
            Close();
    }

    /// <summary>
    /// Fades it away (the user got on with painting or erasing, or clicked elsewhere): gone within
    /// <see cref="WindowFade.Duration"/>, and clicks go through it meanwhile.
    /// </summary>
    public void FadeOut()
    {
        if (_closing || _fading)
            return;
        _fading = true;
        Leave();
        WindowFade.Out(this, () => Native.Foreground.WhenMouseUp(Dismiss)); // not mid-stroke
    }

    private void Leave()
    {
        if (_gone)
            return;
        _gone = true;
        Gone?.Invoke();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Dismiss();
        }
    }

    /// <summary>"Open a file…" under the palette, in its chrome: the palette closes, then the file dialog opens.</summary>
    private Border OpenFileButton(Action openFile)
    {
        var chrome = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E));
        var hover = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));
        var button = new Border
        {
            Background = chrome,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(10, 5, 10, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Put a .txt or .md file on the space here (you can also drop files on a space)",
            Child = new TextBlock { Text = "Open a file…", Foreground = Brushes.White, FontSize = 12 },
        };
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = chrome;
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            // Out of the way, and the file dialog opens while this app still has the keyboard; the
            // palette closes after it, handing the keyboard back.
            _openingFile = true;
            Hide();
            try
            {
                openFile();
            }
            finally
            {
                Dismiss();
            }
        };
        return button;
    }

    private void KeepOnScreen()
    {
        double right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        double bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(Left, right - ActualWidth));
        Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(Top, bottom - ActualHeight));
    }
}
