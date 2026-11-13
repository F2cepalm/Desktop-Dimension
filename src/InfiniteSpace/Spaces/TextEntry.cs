using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;
using InfiniteSpace.Shell;

namespace InfiniteSpace.Spaces;

/// <summary>
/// A system text box floating on a space, for typing a new note or editing one. Dragging the text
/// moves it (a click still places the caret, a double-click selects a word, Shift+drag selects); so
/// does the grip (✥) on the toolbar above it, next to the text's colour and size. On it, reshaping
/// arrows: ↔ sets the width its lines wrap at (a double-click unwraps them), ⤡ scales the text.
/// Alt+arrows move it and Alt+Shift+arrows reshape it; Ctrl+B and Ctrl+I make it bold and italic. Enter commits (Shift+Enter starts a new line),
/// Esc cancels, clicking elsewhere commits, and painting or erasing commits and fades it away.
/// </summary>
internal sealed class TextEntry : Window
{
    private const double MinWrapWidth = 24;
    private const double Nudge = 10;

    /// <summary>How far (DIPs) a press on the text must travel before it moves the text instead of placing the caret.</summary>
    private const double MoveThreshold = 4;

    /// <summary>Room right of and below the box for its arrows, in DIPs.</summary>
    private const double Reach = 16;

    private static readonly Brush LightBacking = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush DarkBacking = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)));

    private readonly TextBox _box;
    private readonly Border _frame;
    private readonly InkPalette _palette;
    private readonly Color _themeInk;
    private readonly NoteFile? _file;     // set when editing an opened file's text
    private readonly string _originalText;
    private Point _origin;
    private string? _color;
    private double _fontSize;
    private double? _wrapWidth;
    private bool _bold;
    private bool _italic;
    private double _inset = 2; // where the text box puts its first character; it keeps as much free on the right
    private Reshaping? _reshaping;
    private Point? _pressedAt;     // a press on the text (screen DIPs) that may still become a move
    private Point _pressedOrigin;  // where the text was then
    private int _pressedCaret;     // where the press put the caret
    private bool _movingText;
    private bool _done;
    private bool _closing;
    private bool _gone;

    /// <param name="origin">Screen DIP position of the first line's top-left, where the note will be drawn.</param>
    /// <param name="draft">The text, colour, size and wrap width to start with (a new note has no text).</param>
    /// <param name="themeInk">Colour of text that has none of its own (it follows the theme).</param>
    public TextEntry(Point origin, TextNote draft, Color themeInk)
    {
        _origin = origin;
        _themeInk = themeInk;
        _color = draft.Color;
        _fontSize = draft.FontSize;
        _wrapWidth = draft.Width;
        _bold = draft.Bold;
        _italic = draft.Italic;
        _file = draft.File;
        _originalText = draft.Text;
        if (_file is not null)
            _wrapWidth ??= NoteDocument.DefaultWidth;

        Title = "Desktop_Dimension – text";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        var grip = ArrowHandle.Move();
        grip.Cursor = Cursors.SizeAll;
        grip.Margin = new Thickness(0, 2, 4, 2);
        grip.ToolTip = "Drag this, or the text itself, to move the text (Alt+arrows)";
        grip.MouseLeftButtonDown += (_, e) => DragToMove(e);
        _palette = new InkPalette(themeInk, _color, InkStroke.DefaultThickness, _fontSize, stacked: false, paint: false, leading: grip, bold: _bold, italic: _italic)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _palette.ColorPicked += color =>
        {
            _color = color;
            ApplyColor();
            ColorPicked?.Invoke(color);
        };
        _palette.FontSizePicked += size =>
        {
            _fontSize = size;
            ApplyShape();
            FontSizePicked?.Invoke(size);
        };
        _palette.StylePicked += SetStyle;

        var typeface = NoteText.Typeface;
        _box = new TextBox
        {
            FontFamily = typeface.FontFamily,
            FontStyle = typeface.Style,
            FontWeight = typeface.Weight,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            AcceptsTab = false,
            Text = draft.Text,
        };
        if (_file is not null)
        {
            // An opened file can be long: the box is as tall as the file shows, and scrolls.
            _box.MaxHeight = Math.Max(120, _file.Height);
            _box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        _frame = new Border { Child = _box, BorderThickness = new Thickness(1), MinHeight = 22, Margin = new Thickness(0, 0, Reach, Reach) };

        var widthArrow = ArrowHandle.Stretch(0);
        widthArrow.HorizontalAlignment = HorizontalAlignment.Right;
        widthArrow.VerticalAlignment = VerticalAlignment.Center;
        widthArrow.Margin = new Thickness(0, 0, Reach - ArrowHandle.Size / 2 - 2, Reach); // centred just outside the right edge
        widthArrow.Cursor = Cursors.SizeWE;
        widthArrow.ToolTip = "Drag to set where lines wrap (Alt+Shift+←/→) · double-click to unwrap";
        Reshapes(widthArrow, BlockGrip.Right);

        var sizeArrow = ArrowHandle.Stretch(45);
        sizeArrow.HorizontalAlignment = HorizontalAlignment.Right;
        sizeArrow.VerticalAlignment = VerticalAlignment.Bottom;
        sizeArrow.Cursor = Cursors.SizeNWSE;
        sizeArrow.ToolTip = "Drag to resize the text (Alt+Shift+↑/↓)";
        Reshapes(sizeArrow, BlockGrip.Right | BlockGrip.Bottom);

        Content = new StackPanel
        {
            Children =
            {
                _palette,
                new Grid { HorizontalAlignment = HorizontalAlignment.Left, Children = { _frame, widthArrow, sizeArrow } },
            },
        };

        // Roughly in place already (toolbar above, box border and inset); Loaded puts it exactly.
        Left = origin.X - 1 - _inset;
        Top = origin.Y - 1 - 34;

        ApplyColor();
        ApplyShape();
        _box.PreviewKeyDown += OnPreviewKeyDown;
        MovesByDraggingTheText();
        Loaded += (_, _) =>
        {
            var caret = _box.GetRectFromCharacterIndex(0);
            if (!caret.IsEmpty)
                _inset = caret.X;
            ApplyShape();
            AlignTextWithOrigin();
            _box.Focus();
            _box.CaretIndex = _file is null ? _box.Text.Length : 0; // a file is read from its top
        };
        Deactivated += (_, _) => Finish(commit: true);
        Closed += (_, _) => Leave();
    }

    /// <summary>
    /// Raised when the text is committed, with the note as typed: its place on screen (DIPs), text,
    /// colour, size and wrap width. The text may be empty (an edited note whose text was deleted).
    /// </summary>
    public event Action<TextNote>? Committed;

    /// <summary>A colour was picked on the toolbar (null: the theme's ink); it becomes the pen's.</summary>
    public event Action<string?>? ColorPicked;

    /// <summary>A text size was picked on the toolbar; it becomes the pen's.</summary>
    public event Action<double>? FontSizePicked;

    /// <summary>Bold or italic was switched on the toolbar or with Ctrl+B / Ctrl+I (whether each is on now); it becomes the pen's.</summary>
    public event Action<bool, bool>? StylePicked;

    /// <summary>
    /// Raised once, as soon as the text box is done with: when it closes, or starts fading out. Focus
    /// can go back to the app then, before the fade is over.
    /// </summary>
    public event Action? Gone;

    /// <summary>Commits whatever was typed and closes; used when another text box is opened.</summary>
    public void Commit() => Finish(commit: true);

    /// <summary>
    /// Commits whatever was typed and fades away, gone within <see cref="WindowFade.Duration"/> (the
    /// user got on with painting or erasing). The note is on the space at once; the box, its toolbar
    /// and its arrows fade from around it, and let clicks through meanwhile.
    /// </summary>
    public void CommitAndFade() => Finish(commit: true, fade: true);

    protected override void OnClosing(CancelEventArgs e)
    {
        _done = true; // closed from outside (app exit): nothing more to report
        _closing = true;
        base.OnClosing(e);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Alt) && key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            e.Handled = true;
            if (modifiers.HasFlag(ModifierKeys.Shift))
                ReshapeBy(key);
            else
                MoveBy(key switch
                {
                    Key.Left => new Vector(-Nudge, 0),
                    Key.Right => new Vector(Nudge, 0),
                    Key.Up => new Vector(0, -Nudge),
                    _ => new Vector(0, Nudge),
                });
        }
        else if (modifiers == ModifierKeys.Control && key is Key.B or Key.I)
        {
            e.Handled = true;
            SetStyle(key == Key.B ? !_bold : _bold, key == Key.I ? !_italic : _italic);
            _palette.ShowStyle(_bold, _italic);
        }
        else if (e.Key == Key.Enter && !modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            Finish(commit: true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Finish(commit: false);
        }
    }

    private void MoveBy(Vector offset)
    {
        _origin += offset;
        AlignTextWithOrigin();
    }

    /// <summary>Alt+Shift+arrows: ←/→ narrow or widen the lines, ↑/↓ step the text size.</summary>
    private void ReshapeBy(Key key)
    {
        switch (key)
        {
            case Key.Left or Key.Right:
                _wrapWidth = Math.Max(MinWrapWidth, TextWidth() + (key == Key.Right ? Nudge : -Nudge));
                break;
            default:
                _fontSize = InkColors.Step(InkColors.FontSizes, _fontSize, key == Key.Up ? +1 : -1);
                break;
        }
        ApplyShape();
    }

    /// <summary>
    /// A press on the text that travels (rather than a click) moves the text, the way the grip does.
    /// A click still places the caret; double-clicks and Shift+drags are left to the text box, to select.
    /// </summary>
    private void MovesByDraggingTheText()
    {
        _box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // The scroll bar of an opened file's text scrolls it, never moves it.
            bool onScrollBar = e.OriginalSource is DependencyObject source && Ancestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null;
            _pressedAt = e.ClickCount < 2 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !onScrollBar ? CursorOnScreen() : null;
            _pressedOrigin = _origin;
        };
        // After the text box has placed the caret for the press (it marks the press handled).
        _box.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) => _pressedCaret = _box.CaretIndex), handledEventsToo: true);

        // On the window: once the move starts, the mouse is the frame's, not the text box's.
        PreviewMouseMove += (_, e) =>
        {
            if (_pressedAt is not { } from)
                return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndTextMove();
                return;
            }
            var drag = CursorOnScreen() - from;
            if (!_movingText)
            {
                if (drag.Length < MoveThreshold)
                    return; // still a click (the text box may be starting a selection meanwhile)
                _movingText = true;
                // Taking the mouse from the text box ends the selection it started; the caret goes back where the press put it.
                _frame.CaptureMouse();
                _box.Select(_pressedCaret, 0);
                Mouse.OverrideCursor = Cursors.SizeAll;
            }
            e.Handled = true;
            _origin = _pressedOrigin + drag;
            AlignTextWithOrigin();
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_movingText)
                e.Handled = true;
            EndTextMove();
        };
        _frame.LostMouseCapture += (_, _) => EndTextMove(); // switched away mid-move, say
    }

    private void EndTextMove()
    {
        _pressedAt = null;
        if (!_movingText)
            return;
        _movingText = false;
        Mouse.OverrideCursor = null;
        if (_frame.IsMouseCaptured)
            _frame.ReleaseMouseCapture();
    }

    /// <summary>The grip was pressed: the window follows the mouse until release.</summary>
    private void DragToMove(MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return; // the button was already up
        }
        _origin = TextOrigin();
    }

    /// <summary>Wires a reshaping arrow: dragging it changes the wrap width (↔) or scales the text (⤡).</summary>
    private void Reshapes(FrameworkElement arrow, BlockGrip grip)
    {
        arrow.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2 && grip == BlockGrip.Right)
            {
                _wrapWidth = null; // lines as long as they were typed again
                ApplyShape();
                return;
            }
            _reshaping = new Reshaping(grip, CursorOnScreen(), _fontSize, TextWidth(), Math.Max(1, _box.ActualHeight));
            arrow.CaptureMouse();
        };
        arrow.MouseMove += (_, _) =>
        {
            if (_reshaping is not null && arrow.IsMouseCaptured)
                Reshape(_reshaping, CursorOnScreen() - _reshaping.From);
        };
        arrow.MouseLeftButtonUp += (_, e) =>
        {
            if (!arrow.IsMouseCaptured)
                return;
            e.Handled = true;
            arrow.ReleaseMouseCapture();
        };
        arrow.LostMouseCapture += (_, _) => _reshaping = null;
    }

    private void Reshape(Reshaping from, Vector drag)
    {
        if (from.Grip == BlockGrip.Right)
        {
            _wrapWidth = Math.Max(MinWrapWidth, from.Width + drag.X);
        }
        else
        {
            // Scaled evenly, by how far the corner went both ways.
            double factor = ((from.Width + drag.X) / from.Width + (from.Height + drag.Y) / from.Height) / 2;
            _fontSize = Math.Round(Math.Clamp(from.FontSize * factor, InkPen.MinFontSize, InkPen.MaxFontSize) * 2) / 2;
            if (_wrapWidth is not null)
                _wrapWidth = Math.Max(MinWrapWidth, from.Width * _fontSize / from.FontSize);
        }
        ApplyShape();
    }

    /// <summary>Width of the text as shown now: the wrap width, or the box's inside.</summary>
    private double TextWidth() => _wrapWidth ?? Math.Max(1, _box.ActualWidth - 2 * _inset);

    /// <summary>
    /// Sizes a text box for a note's type size and wrap width. A wrapping box is exactly the wrap width
    /// plus its insets across, so its lines break where the note's will once it is drawn (neither
    /// splits a word longer than a line).
    /// </summary>
    /// <param name="inset">How far in from its edge the box puts the first character (it keeps as much free on the right).</param>
    internal static void Shape(TextBox box, double fontSize, double? wrapWidth, double inset)
    {
        box.FontSize = fontSize;
        if (wrapWidth is { } wrap)
        {
            box.TextWrapping = TextWrapping.WrapWithOverflow;
            box.MinWidth = 0;
            box.Width = wrap + 2 * inset;
        }
        else
        {
            box.TextWrapping = TextWrapping.NoWrap;
            box.MinWidth = 80;
            box.Width = double.NaN;
        }
    }

    /// <summary>All of the text bold or not, italic or not.</summary>
    private void SetStyle(bool bold, bool italic)
    {
        (_bold, _italic) = (bold, italic);
        ApplyShape();
        StylePicked?.Invoke(bold, italic);
    }

    private void ApplyShape()
    {
        _box.FontWeight = _bold && _file is null ? FontWeights.Bold : NoteText.Typeface.Weight; // a file is styled by its Markdown
        _box.FontStyle = _italic && _file is null ? FontStyles.Italic : NoteText.Typeface.Style;
        Shape(_box, _fontSize, _wrapWidth, _inset);
        _palette.ShowFontSize(_fontSize);
        if (IsLoaded)
        {
            UpdateLayout();
            AlignTextWithOrigin(); // the first character stays put while the box grows and shrinks around it
        }
    }

    /// <summary>The text in its colour, on a see-through backing it reads well on: light for dark ink, dark for light.</summary>
    private void ApplyColor()
    {
        var ink = InkColors.Resolve(_color, _themeInk);
        var brush = new SolidColorBrush(ink);
        _box.Foreground = brush;
        _box.CaretBrush = brush;
        _box.Background = InkColors.IsDark(ink) ? LightBacking : DarkBacking;
        _frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, ink.R, ink.G, ink.B));
    }

    /// <summary>Moves the window so the text box's first character sits exactly on the note's origin.</summary>
    private void AlignTextWithOrigin()
    {
        var caret = _box.GetRectFromCharacterIndex(0);
        if (caret.IsEmpty)
            return;
        var inWindow = _box.TranslatePoint(caret.TopLeft, this);
        Left = _origin.X - inWindow.X;
        Top = _origin.Y - inWindow.Y;
    }

    /// <summary>Where the first character is on screen now (DIPs), after the window was dragged.</summary>
    private Point TextOrigin()
    {
        var caret = _box.GetRectFromCharacterIndex(0);
        if (caret.IsEmpty)
            return _origin;
        var inWindow = _box.TranslatePoint(caret.TopLeft, this);
        return new Point(Left + inWindow.X, Top + inWindow.Y);
    }

    /// <param name="fade">Fade away (see <see cref="CommitAndFade"/>) rather than close at once.</param>
    private void Finish(bool commit, bool fade = false)
    {
        if (_done)
            return;
        _done = true;
        EndTextMove();
        string text = _box.Text.TrimEnd();
        var note = new TextNote
        {
            X = Math.Round(_origin.X, 2),
            Y = Math.Round(_origin.Y, 2),
            Text = text,
            FontSize = _fontSize,
            Color = _color,
            Width = _wrapWidth is { } wrap ? Math.Round(wrap, 2) : null,
            Bold = _file is null && _bold,
            Italic = _file is null && _italic,
            // Still the same file; its text is stored anew only if it was changed.
            File = _file is null ? null : text == _originalText ? _file.Copy() : _file.ForNewText(),
        };
        try
        {
            // Before closing: the space shows the kept note by the time the box is gone.
            if (commit)
                Committed?.Invoke(note);
        }
        finally
        {
            if (fade && IsLoaded)
            {
                Leave();
                WindowFade.Out(this, () => Native.Foreground.WhenMouseUp(() => // not mid-stroke
                {
                    if (!_closing)
                        Close();
                }));
            }
            else
            {
                Close();
            }
        }
    }

    private void Leave()
    {
        if (_gone)
            return;
        _gone = true;
        Gone?.Invoke();
    }

    private static Point CursorOnScreen() =>
        Win32.GetCursorPos(out var p) ? new Point(p.X / Dpi.Scale, p.Y / Dpi.Scale) : default;

    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var at = element; at is not null; at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is T found)
                return found;
        }
        return null;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>A reshaping arrow being dragged: what it grips, and the text as it was when the drag began.</summary>
    private sealed record Reshaping(BlockGrip Grip, Point From, double FontSize, double Width, double Height);
}
