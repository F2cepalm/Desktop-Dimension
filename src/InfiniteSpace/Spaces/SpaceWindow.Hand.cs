using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Spaces;

/// <summary>The hand on a space: selecting, moving and editing its strokes and notes.</summary>
internal sealed partial class SpaceWindow
{
    private readonly HashSet<IInk> _selected = [];
    private HashSet<IInk>? _selectedBefore; // what was selected when a Shift+rectangle started
    private IInk? _pressedInk;
    private Vector _selectionDrag;          // how far the selection is being dragged
    private Rect? _marquee;
    private Func<TextNote, Point>? _placeDraggedNote;
    private Func<InkStroke, Matrix>? _placeDraggedStroke;

    /// <summary>Ink was dragged to a new place by this much on screen (DIPs). Not raised for a drag that went nowhere.</summary>
    public event Action<SpaceWindow, IReadOnlyList<IInk>, Vector>? SelectionMoved;

    /// <summary>Text was double-clicked with the hand (at this screen DIP point): it should open for editing.</summary>
    public event Action<SpaceWindow, TextNote, Point>? EditRequested;

    /// <summary>The strokes and notes selected with the hand.</summary>
    public IReadOnlyCollection<IInk> Selected => _selected;

    /// <summary>Whether anything here is selected.</summary>
    public bool HasSelection => _selected.Count > 0;

    /// <summary>Makes these the selection (the new versions of moved ink).</summary>
    public void Select(IEnumerable<IInk> items)
    {
        _selected.Clear();
        _selected.UnionWith(items);
        RedrawInk();
    }

    /// <summary>Selects nothing.</summary>
    public void ClearSelection()
    {
        if (_selected.Count == 0)
            return;
        _selected.Clear();
        RedrawInk();
    }

    private void HandDown(Point at)
    {
        _pressedInk = InkHit.At(_record, _toScreen, at, _editing);
        _gesture = _pressedInk is null ? Gesture.HandOnEmpty : Gesture.HandOnInk;
        // An unselected item is selected on the press, so dragging it moves it at once.
        if (_pressedInk is not null && !_selected.Contains(_pressedInk) && !IsShiftDown())
            Select(InkHit.WithGroup(_record, _pressedInk));
    }

    private void HandDrag(Point at)
    {
        var travel = at - _pressedAt;
        if (_gesture is Gesture.HandOnInk or Gesture.HandOnEmpty)
        {
            if (travel.Length < DragThreshold)
                return;
            if (_gesture == Gesture.HandOnInk)
            {
                _selected.UnionWith(InkHit.WithGroup(_record, _pressedInk!)); // a Shift+press on something new adds it
                _gesture = Gesture.MovingSelection;
            }
            else
            {
                _selectedBefore = IsShiftDown() ? [.. _selected] : null;
                _gesture = Gesture.Marquee;
            }
            PaintingStarted?.Invoke(this); // the palette and a text box get out of the way, as for paint
        }
        if (_gesture == Gesture.MovingSelection)
        {
            _selectionDrag = travel;
        }
        else
        {
            var area = new Rect(_pressedAt, at);
            _marquee = area;
            _selected.Clear();
            if (_selectedBefore is not null)
                _selected.UnionWith(_selectedBefore);
            foreach (var item in InkHit.Touching(_record, _toScreen, area, _editing))
                _selected.UnionWith(InkHit.WithGroup(_record, item));
        }
        RedrawInk();
    }

    private void HandUp(Gesture gesture)
    {
        switch (gesture)
        {
            case Gesture.MovingSelection:
                var drag = _selectionDrag;
                _selectionDrag = default;
                if (drag.Length >= 0.01 && SelectionMoved is not null)
                    SelectionMoved(this, [.. _selected], drag); // the new versions are drawn in their places
                else
                    RedrawInk();
                break;
            case Gesture.Marquee:
                _marquee = null;
                _selectedBefore = null;
                RedrawInk();
                break;
            case Gesture.HandOnInk:
                if (IsCtrlDown() && NoteClicked?.Invoke(this, _pressedAt, NoteClick.CtrlClick) == true)
                    break; // a link in an opened file
                var group = InkHit.WithGroup(_record, _pressedInk!);
                if (IsShiftDown())
                    Toggle(group);
                else
                    Select(group); // a click on part of a bigger selection selects just that
                break;
            case Gesture.HandOnEmpty:
                if (!IsShiftDown())
                    ClearSelection();
                _look.AddBurst(_pressedAt); // a click splashes, as it always has
                break;
        }
        _pressedInk = null;
    }

    private void CancelHandDrag()
    {
        _selectionDrag = default;
        _marquee = null;
        _selectedBefore = null;
        _pressedInk = null;
        RedrawInk();
    }

    /// <returns>Whether the double-click meant something (it then isn't also a press).</returns>
    private bool DoubleClick(Point at)
    {
        if (NoteClicked?.Invoke(this, at, NoteClick.DoubleClick) == true)
            return true; // an opened file's name opens the file
        if (EffectiveTool != Tool.Hand || InkHit.At(_record, _toScreen, at, _editing) is not TextNote note)
            return false;
        EditRequested?.Invoke(this, note, at);
        return true;
    }

    private IInk? InkUnderCursor() =>
        Win32.GetCursorPos(out var p) ? InkHit.At(_record, _toScreen, new Point(p.X / Dpi.Scale, p.Y / Dpi.Scale), _editing) : null;

    private void Toggle(List<IInk> items)
    {
        if (items.All(_selected.Contains))
            _selected.ExceptWith(items);
        else
            _selected.UnionWith(items);
        RedrawInk();
    }

    private static bool IsShiftDown() => (Win32.GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT, as of this message

    /// <summary>Where notes are drawn: <see cref="NotePlacement"/>'s, with the selection along by the drag.</summary>
    private Func<TextNote, Point>? PlaceNotes => _selectionDrag == default ? NotePlacement : _placeDraggedNote ??= note =>
    {
        var at = NotePlacement?.Invoke(note) ?? _toScreen.Transform(new Point(note.X, note.Y));
        return _selected.Contains(note) ? at + _selectionDrag : at;
    };

    /// <summary>How strokes are drawn: <see cref="StrokePlacement"/>'s, with the selection along by the drag.</summary>
    private Func<InkStroke, Matrix>? PlaceStrokes => _selectionDrag == default ? StrokePlacement : _placeDraggedStroke ??= stroke =>
    {
        var m = StrokePlacement?.Invoke(stroke) ?? stroke.Placed(_toScreen, _record.Anchor);
        if (_selected.Contains(stroke))
            m.Translate(_selectionDrag.X, _selectionDrag.Y);
        return m;
    };

    /// <summary>The selection's frames (one per group, or per item outside groups) and the rectangle being dragged.</summary>
    private InkAdornments? Adornments()
    {
        if (_selected.Count == 0 && _marquee is null)
            return null;
        var frames = new List<Rect>();
        foreach (var group in _selected.GroupBy(i => i.Group ?? (object)i))
        {
            var box = Rect.Empty;
            foreach (var item in group)
                box.Union(InkHit.Bounds(item, _toScreen, _record.Anchor));
            if (box.IsEmpty)
                continue;
            box.Offset(_selectionDrag);
            box.Inflate(3, 3);
            frames.Add(box);
        }
        return new InkAdornments(frames, _marquee, Themes.AccentOf(_theme));
    }
}
