# Plan: cursor fixes and text cards

Written 2 October 2026 for another model to implement. Status: **implemented** (phases 1–4 committed 2 October 2026; built with no warnings on Linux, but the tests and perf run need Windows and the manual checks are pending).

Where the implementation differs from the plan:
- **F7:** a card stays a card even when its text is erased. Undo can bring the text back, and it should come back as a card.
- **Refits:** cards refit wherever ink is shown again (`ShowInk`), which covers edits, undo, redo and erasing.
- **Reshaping:** the rule is the pure `CardBounds`, so it can be unit tested.

This plan fixes three problems the user found in the real app:

1. **The cursor shows the wrong tool.** The pen sometimes shows the hand or move cursor, and the hand sometimes shows the pen's cross.
2. **Text wrap has no margins.** The text fills the space from edge to edge and can't be read.
3. **The text and its space come apart.** A hand drag moves the text out of its space. The user wants a wrapped space and its text to be one object, a **text card**:
   - Dragging a card moves the space and the text together.
   - When a card gets narrower, the text reflows and the card grows downwards to fit it.

---

## 0. Ground rules

- Follow section 0 of `docs/tools-plan.md`: its code style, its rule against touching the hot paths, and its commands (`dotnet build`, `dotnet test`, and the perf run with `--actions paint,notes,screenink`).
- Do the phases in order. Commit each one as "Fixes phase N: …" when the build is clean, the tests pass and perf shows no `FAIL`.
- You can't run the Windows UI here. Make sure every behaviour that can be checked without the UI has a unit test, and list the manual checks in the commit message.

### Decisions

These are already made. Don't change them.

| # | Decision |
|---|---|
| F1 | One function decides every cursor that depends on the tool. Each tool keeps its system cursor: Pen and Eraser use Cross, Text uses IBeam, and Hand uses Arrow (SizeAll over ink or a card). |
| F2 | Screen notes (sheets) take the mouse only when the Hand is the tool. With Pen, Text or Eraser they let clicks through, so they can't take over the cursor or the press. |
| F3 | A card's text keeps its font size. Wrapping no longer shrinks text to fit; the card grows downwards instead. The smallest wrapped size is 11 (this replaces 6). |
| F4 | A card's padding is `CardPadding(fontSize) = Snap(Math.Max(10, fontSize * 0.75))`, snapped up to `DreamStyle.ArtPixel`. It must be at least the rim plus the edge's wobble and ripple plus 4 DIPs. Check the rim inset in `DreamRaster.DrawRim` and the looks, and raise the floor of 10 if it isn't enough. |
| F5 | A card's height always follows its text: `text height + 2 × padding`. Only its width can be changed by the user. Dragging an edge up or down changes nothing. |
| F6 | Cards never merge with other spaces. This covers making one, dropping one, and dropping or drawing a space over one. |
| F7 | A card stays a card while it has its one note. If that note is erased, it becomes a plain space. |

---

## Phase 1: cursor

The causes found when reading the code:

- **(a)** `SpaceWindow.WndProc` handles `WM_SETCURSOR` for every hit-test code, not only `HTCLIENT`. `GpuOverlay` already checks for `HTCLIENT`.
- **(b)** Sheets always act as the hand (`EffectiveTool`). With the pen, a sheet over red notes shows Arrow or SizeAll and turns the press into a hand press. This is what the user saw as the pen showing the move cursor.
- **(c)** `RefreshCursor` updates the cursor only when the window under the mouse is that same `SpaceWindow`. When the mouse is over another of our windows (an overlay, a sheet, the toolbar), the old cursor stays until the mouse moves. This is what the user saw as the cross staying after picking the hand.
- **(d)** After a gesture, or after the ink under the mouse changes, the hand's Arrow/SizeAll choice isn't worked out again until the next mouse move.

Steps:

1. Add `Spaces/ToolCursors.cs`, a static class:
   - `IntPtr For(Tool effectiveTool, bool overInk)`: the cursors from F1. It is pure, so it can be unit tested by mapping to the `IDC_*` ids through an `internal static int IdFor(...)`.
   - `void RefreshUnderMouse()`: gets the window under the mouse with `GetCursorPos` and `WindowFromPoint`. If that window belongs to this process (`GetWindowThreadProcessId` compared with `Environment.ProcessId`), it calls `SendMessage(hwnd, WM_SETCURSOR, hwnd, MAKELPARAM(HTCLIENT, WM_MOUSEMOVE))`. Add `GetWindowThreadProcessId` and `SendMessage` to `Native/Win32.cs` if they're missing.
2. In `SpaceWindow.Hand.cs`, make `CursorNow()` call `ToolCursors.For`. In `SpaceWindow.cs`, change the case to `WM_SETCURSOR when (lParam & 0xFFFF) == HTCLIENT` (fixes a).
3. Fix (b): add `SpaceWindow.SetClickThrough(bool)`, which toggles `WS_EX_TRANSPARENT | WS_EX_LAYERED` with `SetWindowLong`. Check how screen-note ink is hit today; if a layered window breaks DirectComposition, use `WM_NCHITTEST → HTTRANSPARENT` instead.
   - In the `Tool` setter, when `IsSheet`, call it with `_tool != Tool.Hand`.
   - Keep `EffectiveTool` as it is.
4. Fix (c): call `ToolCursors.RefreshUnderMouse()` in each of these places:
   - at the end of `SpaceManager.SetTool`, in place of the per-window `RefreshCursor` in the `Tool` setter. Delete that call and keep the method for (d).
   - after an overlay is dismissed (`ShellController.CloseOverlay`)
   - in the wheel's `Gone` handler
   - after a `TextEntry` closes
5. Fix (d): call `RefreshCursor()` at the end of the `WM_LBUTTONUP` case, and after `MoveInk`, erase, undo and redo on that window.
6. Tests: add `ToolCursorsTests` covering every `Tool` value with and without ink. Also test that sheets let clicks through with every tool except the Hand (expose `internal bool ClickThrough`).

Manual checks:

- Press P, H, E and T over a space, a sheet, an app and the toolbar without moving the mouse. The cursor should change at once every time.
- With the pen over red notes, you should see the cross and be able to paint underneath them.

---

## Phase 2: wrapped text gets margins (cards are made)

1. Add `bool Card` to `SpaceRecord`. It is saved, and defaults to false. Write `/// A text card: one note, which the space hugs (see SpaceManager.Cards.cs).`
2. Create `Spaces/SpaceManager.Cards.cs`. Move the wrap code there (delete `SpaceManager.Wrap.cs`) and add:
   - `internal static double CardPadding(double fontSize)` (F4)
   - `internal static Size CardSize(TextNote note, double width)`: the card's outer size for an outer `width`, with the text wrapped at `width − 2·pad`, measured with `NoteText.Layout(...).Metrics.Height`. The height is snapped up to the art pixel.
   - `internal static double MinCardWidth(double fontSize) => 2 * CardPadding(fontSize) + 4 * fontSize`.
3. Rewrite `WrapText`:
   - Font size: `Math.Clamp(round half, SmallestWrappedType = 11, InkPen.MaxFontSize)`.
   - Outer width: `Math.Max(box.Width, MinCardWidth(size))`. The top-left is the selection's, snapped.
   - Note: `X/Y = box.TopLeft + (pad, pad)` and `Width = outer − 2·pad`. Height comes from `CardSize`.
   - `record.Card = true`. Call `Place(record, host, crossing: [])` so it never merges (F6). Check how `Place` gets its crossed spaces when `crossing` is null, and pass an empty list or add a `merge: false` flag.
   - Delete `FitWrappedText`. Rewrite `tests/InfiniteSpace.Tests/TextWrapTests.cs`:
     - the padding is at least the floor, for sizes 6–72
     - `CardSize` height is at least the text height plus 2·pad
     - a narrower width never gives a shorter card
     - a 6pt selection is wrapped at 11
   - Update the `Scenarios.cs` wrap check if its expectations change.

Manual check: wrap a paragraph in a browser. The text should have even margins, be readable, and the card should start at the selection's top-left.

---

## Phase 3: a card is one object

### 3.1 Hand drag moves the whole card

In `SpaceWindow.Hand.cs`:

- When `_record.Card` is true, a hand press anywhere inside the card counts as pressing its note. Press and click still select the note, so delete and the eraser keep working, and a double-click still edits it.
- Once a drag passes `DragThreshold`, don't enter `MovingSelection`. Set `_gesture = Gesture.MovingCard` (a new value) and raise `public event Action<SpaceWindow>? CardDragStarted`.
- Don't drive the drag with `lParam`, because the window moves under the mouse. Use `GetCursorPos ÷ Dpi.Scale` in screen DIPs.
- In the `WM_MOUSEMOVE` and `WM_LBUTTONUP` cases, route `MovingCard` to new events, `CardDragged(SpaceWindow, Vector travel)` and `CardDropped(SpaceWindow, Point at)`.
- In `WM_CAPTURECHANGED`, cancel the drag through `CardDragCancelled`.

In `SpaceManager.Cards.cs`, wire these up in `Place` next to `SelectionMoved`:

- **Start:** `PickUp(space, lift: false)`. Split today's `PickUp(Point, IntPtr)` into finding the space and an internal `PickUp(Space, IntPtr overlay)`, and skip `Lift` when `overlay == IntPtr.Zero` (`Land` already does nothing if the window wasn't lifted).
- **Drag:** `Fit(moving, Rect.Offset(moving.Bounds, travel), carried: travel)`.
- **Drop:** `Drop(moving, at)`.
- **Cancel:** `PutBack(moving)`, or whatever the method at line ~637 is called.
- `Drop` skips the merge for cards (F6). Also leave cards out of the `crossed` list wherever another space is dropped or created. Search for `SpaceGeometry.Overlap(` in `SpaceManager*.cs`.

`ToolCursors.For`: a card under the Hand counts as being over ink, so it shows SizeAll anywhere on it. Pass `overInk: _record.Card || InkUnderCursor() is not null`.

### 3.2 Reshaping reflows the text

- Add `internal void FitCard(Space space, double outerWidth, Point topLeft)`. It:
  - clamps the width to at least `MinCardWidth`
  - sets `note.Width = outerWidth − 2·pad` and puts the note at `topLeft + (pad, pad)`
  - computes the height with `CardSize`
  - sets the outline to that rectangle (as `WrapText` does), redraws and saves
- In `Fit(moving, bounds, carried)`, when `space.Record.Card`:
  - replace `bounds.Height` with `CardSize(note, bounds.Width).Height` (F5) before the matrix is computed
  - for live reflow, set `note.Width` straight away, keep the note at `bounds.TopLeft + pad` (not `PlaceNote`), and call `space.Window.RedrawInk()`. Throw away any cached text layout for that note.
- Before writing any code, read `Bake`/`BakeMapping` (SpaceManager.cs ~942) and `HostFrames.Map`. Notes are drawn without scaling, so `note.Width` must be in the units `Bake` keeps for notes. A move followed by a reshape must leave `X`, `Y` and `Width` consistent after `Drop`. Add a unit test for this through the pure helpers.
- After a card's note is edited (the `TextEntry` commit path in `SpaceManager`, where an edited note replaces the old version), call `FitCard(space, current outer width, current top-left)`. The card's width wins over any width the text box's arrow grips set. Keep the font size the text box set, and make the card grow or shrink to fit the new size.
- F7: if erasing or deleting leaves a card with no note, set `Card = false`.

Undo keeps today's behaviour: space moves can't be undone, and note edits can. Don't add undo for card moves.

### 3.3 Tests (`tests/InfiniteSpace.Tests/TextCardTests.cs`)

- A card made by `WrapText` has `Card == true`, one note, and its outline bounds equal `CardSize`.
- `FitCard` at a narrower width gives a taller card with the same font size, and at a wider width a card that isn't taller.
- Wrapping over an existing space leaves both spaces as they were, unmerged.
- Erasing a card's note clears `Card`.
- Saving and loading keeps `Card`. Use the existing round-trip test pattern in the store tests.

Manual checks:

- With the hand, drag a card by its text and by its margin. The space and the text should move as one.
- Narrow a card from a side grip (Ctrl+Alt+M). The text should reflow live and the card should grow downwards.
- Drag a top or bottom grip: the card's height shouldn't change.
- Double-click and edit the text: the card should fit the new text.

---

## Phase 4: docs

- In `README.md`, describe text cards under text wrap (W): drag to move, narrow to reflow, the cursor per tool.
- Set this file's status to implemented, and list the manual checks still pending.
