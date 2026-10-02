# Plan: save a space to a file (.isf)

Written 2 October 2026. Status: **implemented**. The decisions on 2 October 2026 change parts of the plan below:
- Save is on **Ctrl+Alt+F**, and Ctrl+Alt+S still draws a space.
- The wheel and toolbar grow to **10 tools**: **Save** and a new **Open saved space** tool, which shows an Open dialog and places the space where the wheel opened.
- The extension is **`.spsf`**, not `.isf`. This removes the clash with Microsoft's ink format.
- There is no drag-and-drop opening and no file association.
- Clicking the "Saved" notice doesn't open the folder: tray notices can't take clicks yet.

You point at a space and press the save hotkey, or pick **Save** on the wheel or the toolbar. A Save dialog opens in a folder that only this app uses, with a file name already filled in. The space is written to one self-contained `.isf` file.

---

## 1. Conflicts found in the code

| # | What | Why it matters | Recommendation |
|---|---|---|---|
| C1 | **Ctrl+Alt+S is already taken** by the app's main feature, "draw a space" (`ShellController.cs:76`, `ToggleDraw`). The first-run tray tip mentions it too. | It can't do both. | Give **Ctrl+Alt+S** to Save, as you asked, and move drawing to **Ctrl+Alt+D** ("D" for draw), which is free. Update the tray tip, README and hotkey list. |
| C2 | The **wheel has exactly 8 slots and all are used** (`WheelGeometry.Count = 8`, with tests that expect 8). | Save needs a slot. | Grow the wheel and the toolbar to **9 tools**, with Save added last. Both placeholders (Project meter, List) stay. |
| C3 | **`.isf` is also Microsoft's "Ink Serialized Format"**, used by Windows ink apps and older OneNote and Journal. | If the file type were registered for double-click, it could take over those files. | Keep `.isf` as you asked. The file is JSON with a header naming this app, so it can never be confused with Microsoft's binary format. Register a double-click handler only for the current user, and only if `.isf` has no handler yet (see Q3). |

## 2. Questions

- **Q1:** OK to move "draw a space" to **Ctrl+Alt+D** (C1)? *Recommended: yes.*
- **Q2:** OK to grow the wheel to **9** (C2)? The alternative is to replace the **List** placeholder with Save. *Recommended: 9.*
- **Q3:** Should saved files also **open** in this phase? A save you can't load back isn't much use. Three ways to open one:
  - drag an `.isf` onto the screen
  - tray menu → **Open saved space…**
  - double-click the file in Explorer
  
  *Recommended: yes to all three, in phase 3.*

---

## 3. Behaviour

- **Starting a save.**
  - **Ctrl+Alt+S**, or the wheel or toolbar's **Save**, saves the space under the mouse. With the wheel, that is the space under the point where the wheel opened.
  - Plain spaces, text cards and blocks can all be saved. Screen notes can't.
  - If the mouse isn't over a space, nothing opens and a tray notice says "Point at a space to save it".
- **The dialog.** It is the standard Windows Save dialog:
  - **Folder:** `Documents\Infinite Space\Saved spaces\`, created on first use. Once you pick another folder, that one is remembered (`SaveDirectory` in `settings.json`).
  - **File name:** the first line of the space's text (characters a file name can't hold are removed, and it's cut to 40 characters). If there's no text, the name is `Space 2026-10-02 14-30`.
  - **File type:** "Infinite Space (*.isf)".
  - The space flashes briefly so you can see which one is being saved.
- **After saving.** A tray notice says "Saved to …". Clicking it opens the folder with the file selected. Focus goes back to the app you were in.
- **Errors.** A disk or permission error shows a tray notice with the reason and is logged. Nothing else changes.

## 4. File format (`.isf`)

The file is UTF-8 JSON, so it is readable and can be compared between versions:

```json
{
  "format": "InfiniteSpace.Space",
  "version": 1,
  "savedAt": "2026-10-02T14:30:00+05:00",
  "theme": "Dreamy",
  "space": { …SpaceRecord… },
  "texts": { "<id>": "full text of an opened file" }
}
```

- **Self-contained.** The text of opened-file notes normally lives in the app's private `notes\` folder. In a save it goes into `texts`, and each note points to its entry by id.
- **No undo history** (`Previous` versions) is saved. The app window the space sat on is saved for information only.
- **Coordinates are moved** so the space's top-left corner is at (0, 0). A loaded space can then go wherever the mouse is.
- **Checked on reading.** A file without `"format": "InfiniteSpace.Space"`, or from a newer version, is refused with a clear message. That includes Microsoft ink files.

---

## 5. Phases

Each phase ends with a clean `dotnet build` (no new warnings), green `dotnet test`, and its own commit.

### Phase 1: file format (pure code, unit tested)

- New `Spaces/SpaceFile.cs` with:
  - `Write(Stream, SpaceRecord, string theme, Func<NoteFile, string> textOf)`
  - `SpaceRecord Read(Stream)`, which throws `SpaceFileException` with a readable message
  - `string SuggestedName(SpaceRecord)`
  - the coordinate shift
- `SpaceStore` gets a public `TextOf(NoteFile)` (from today's `ReadStored`) and a way to store texts read back from a file.
- Tests (`SpaceFileTests`):
  - a save and read round trip keeps strokes, notes, card flag, styles and opened-file text
  - the top-left becomes (0, 0)
  - history is left out
  - a Microsoft ISF file, random bytes or a version-2 file are refused cleanly
  - suggested names: odd characters removed, cut to length, a date when there's no text

### Phase 2: saving

- `Tool.Save` is added at the end of the `Tool` enum, with a `ToolCatalog` entry ("Save", "Save a space to a file") and a floppy-disk icon in `ToolIcons`. It has no single-key shortcut.
- `WheelGeometry.Count` becomes `ToolCatalog.All.Count`. Update the catalog and wheel tests from 8 to 9.
- `SelectTool`: Save is an action, like Text wrap, so the current tool stays.
- Hotkeys: Ctrl+Alt+S → `SaveSpaceUnderMouse`, Ctrl+Alt+D → `ToggleDraw` (after Q1). Update the tray tip.
- `SpaceManager.SnapshotAt(Point)` returns a copy of the record under the point, ready to save (or null), plus the window to flash.
- New `Shell/ShellController.Saves.cs`:
  - shows the dialog (`Microsoft.Win32.SaveFileDialog`), owned by a hidden helper window brought to the front first, because the app normally never takes focus
  - writes to a temporary file and then moves it into place, as `SpaceStore` does
  - shows the notice and restores focus with `Foreground.RestoreTo`
- `AppSettings.SaveDirectory`.
- README: the Save row, the new hotkeys, and the file layout.
- Manual checks:
  - Save a card, a plain space, and a space with an opened file. Open the `.isf` in Notepad: it is readable JSON.
  - Press Ctrl+Alt+S away from any space: you get the notice.
  - Ctrl+Alt+D draws a space.

### Phase 3: opening (only if Q3 is yes)

- `SpaceManager.Load(SpaceRecord, Point at)` places the space with its top-left at the mouse, on the app window under it, without merging. It gets a new id, and its opened-file texts go into the private store.
- **Drag onto the screen:** spaces already accept dropped files (`WM_DROPFILES`). An `.isf` dropped on a space loads beside it. The tray menu's **Open saved space…** opens the standard Open dialog in the save folder.
- **Double-click in Explorer:** a per-user association under `HKCU\Software\Classes\.isf`, written only if `.isf` has no handler. The app's single-instance hand-off passes the path to the running copy, which loads it at the mouse.
- Tests: load places the space at the given point; ids are new; texts are restored.
- Manual check: save, delete the space (Ctrl+Alt+E), then open the file: the space comes back the same.

## 6. Not in this plan

- Saving several spaces into one file.
- Saving screen notes.
- Cloud sync.
- Thumbnails in Explorer.
