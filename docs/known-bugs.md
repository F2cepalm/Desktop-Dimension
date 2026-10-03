# Known bugs

Reported by the user on 3 October 2026, after the freeze and battery work (master `fa23dc1`). Not fixed yet.

## 1. Reshaping a space with ink on it drops to 1–10 fps

- **What happens:** reshaping a space (Ctrl+Alt+M arrows) that has text or paint on it lags badly.
  - Dragging a corner (diagonally) is the worst: 1–10 fps.
  - Dragging a side (horizontally or vertically) is a little better: 3–10 fps.
- **Where to look first:**
  - The reshape path: `SpaceManager.Fit` → `SpaceWindow.ReshapeLive` / `DrawLive`.
  - The ink redraw on each shape: `InkLayer.SetShape` → `EnsureCapacity` → `Redraw`. Check for a full redraw, or a mask and bitmap rebuild, on every frame.
  - The new reshape room: `MakeReshapeRoom`, which calls `Lay` on lift, and `ReshapeRoom` in `SpaceSurface`, `InkLayer` and `DreamRaster`. Check whether surfaces are being reallocated every frame. The `oversized` and `wasteful` rules in `Layer.Ensure` and `InkLayer.EnsureCapacity` might fight each other.
  - The idle pacing (`FrameClock.Pace`, `Universe.Hurry`). Ctrl+Alt+M drags come through the overlay's `WM_MOUSEMOVE` → `Hurry`. Check that ticks stay at the full rate during the drag.
  - The ambient-rate governor (`Universe.Govern`). Check whether it is starving frames.
  - **Not a new bug.** The user confirmed that the version before the performance work (`1d085fb`) lags too. It got worse after that work, so look for both the original cause (in the reshape and ink redraw path) and what the new changes made worse.

## 2. A text card's text drifts when the card is moved right after it is made, and Ctrl+Z misbehaves

- **What happens:**
  - The user makes a text card with text wrap (W) and moves it at once. The text drifts away from the card.
  - Ctrl+Z on the card then misbehaves.
- **Where to look first:**
  - The card drag: `SpaceWindow.Hand` with `Gesture.MovingCard`, then `SpaceManager.Cards.WireCard` → `PickUp`, `Fit`, `Drop`.
  - Note placement while moving (`MovingSpace.PlaceNote` for cards), and `Bake` on drop.
  - The interaction with `FitCard` and `ShapeCard`. These change the note's X, Y and Width in place, without making a new version.
  - Undo of a card: the note's versions (`Previous`), and `TrimHistory`/`UndoStop`.
  - Moving a space can't be undone. Card refits change the note in place, so undo may bring back an older note version at an old position, outside the card.
