using System.Windows;

namespace InfiniteSpace.Shell;

/// <summary>
/// Notices the user selecting text in an app with the mouse (a drag, or a double-click, on an app's text),
/// from samples of the left button and the cursor: W belongs to text wrap for a little while after that.
/// </summary>
/// <param name="recent">How long after selecting it still counts.</param>
/// <param name="minDrag">How far (the cursor's units) a press must move to be a drag, and two clicks may be apart to be a double-click.</param>
/// <param name="doubleClick">The longest gap between the clicks of a double-click.</param>
internal sealed class SelectionWatch(TimeSpan recent, double minDrag, TimeSpan doubleClick)
{
    private Point? _pressedAt;
    private bool _pressedOnText;
    private (TimeSpan At, Point Where)? _lastClick;
    private TimeSpan? _selectedAt;

    /// <summary>From where the last selecting press started to where it was let go (the cursor's units).</summary>
    public Rect? Area { get; private set; }

    /// <param name="leftDown">Whether the left button is down now.</param>
    /// <param name="onText">Asked once per press: whether it was on an app's text (not this app, not a title bar).</param>
    public void Sample(bool leftDown, Point cursor, TimeSpan now, Func<bool> onText)
    {
        if (leftDown)
        {
            if (_pressedAt is null)
            {
                _pressedAt = cursor;
                _pressedOnText = onText();
            }
            return;
        }
        if (_pressedAt is not { } from)
            return;
        _pressedAt = null;
        if (!_pressedOnText)
        {
            _lastClick = null;
            return;
        }
        bool dragged = (cursor - from).Length >= minDrag;
        bool doubled = !dragged && _lastClick is { } last && now - last.At <= doubleClick && (cursor - last.Where).Length < minDrag;
        if (dragged || doubled)
        {
            _selectedAt = now;
            Area = new Rect(from, cursor);
        }
        _lastClick = dragged || doubled ? null : (now, cursor);
    }

    /// <summary>Whether text was selected lately.</summary>
    public bool IsRecent(TimeSpan now) => _selectedAt is { } at && now - at < recent;

    /// <summary>The selection was used (or turned out not to be one): W goes back to the apps.</summary>
    public void Forget() => _selectedAt = null;
}
