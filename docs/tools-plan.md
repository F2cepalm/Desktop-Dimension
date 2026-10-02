# Plan: tools everywhere (tool wheel, toolbar, hand tool, screen notes)

Written 1 October 2026, for another model to implement. The user approved the decisions on 2 October 2026, changing D2 (the Space cube places a block). Status: **not started**.

Today the app does everything through spaces. You paint by dragging inside a space, and **T** opens a text box. Controls are hidden in hotkeys and right-clicks.

After this work:

- A **tool wheel** opens anywhere with one hotkey. It offers 8 tools: Hand, Pen, Text, Space cube, and 4 placeholders.
- A small themed **toolbar** (a square with three dots) is always on screen. Clicking it unfolds a labelled menu with the same 8 tools.
- Inside a space, **H/P/T/C** switch tools. The **Hand** is the default, and it is the only tool that selects, moves, groups and edits things.
- The **Pen** and **Text** can also be used straight on the screen, outside any space. What they make becomes **screen notes**: paint and text that sit on an app window, follow it, and come back after a restart, like spaces do.

---

## 0. Ground rules for the implementer

1. **Work phase by phase, in order** (sections 5–11). Each phase ends with `dotnet build` (no new warnings) and `dotnet test` green, plus its manual checks. Don't start a phase until the previous one passes.
2. **Match the surrounding code.**
   - Use file-scoped namespaces, `internal sealed` types, primary constructors where the file uses them, and collection expressions `[]`.
   - Put an XML doc comment on every type and member, in one plain sentence with British spelling (colour, centre, behaviour).
   - Comments say *why*, not what.
   - Don't add NuGet packages.
3. **Don't touch the rendering hot paths** (looks, `DreamRaster`, `Jelly`, `GpuCompositor`, `Universe`, `SpaceSurface`) except where a step says so.
4. **The repo is under git**, with a baseline commit made before this work. Once a phase passes, commit it as "Tools phase N: …" so it can be diffed and reverted.
5. **The app is single-instance.** Exit the running Infinite Space (tray → Exit) before `dotnet run --project src/InfiniteSpace`.
6. Commands:
   - Build: `dotnet build`
   - Tests: `dotnet test`
   - Perf and checks: `dotnet run -c Release --project tests/InfiniteSpace.Perf -- --themes Dreamy --actions paint,notes,screenink`

   Lines starting `FAIL` in the perf output are failed checks.
7. If a step's code doesn't compile as written, it's because the plan guessed a small detail (a parameter name, a private helper). Adapt it to the real code, keep the behaviour, and don't redesign.

---

## 1. Decisions taken while planning

These are interpretations of the request. If the user disagrees, change them **here** first.

| # | Topic | Decision | Why |
|---|---|---|---|
| D1 | Wheel hotkey | **Ctrl+Alt+`** (the key left of **1**; **Ё** on a Russian layout). If another app holds it, try **Ctrl+Alt+W**, then **Win+Alt+W**. It can be set in `settings.json` as `"ToolWheelHotkey": "Ctrl+Shift+Alt+T"`. The tray menu and toolbar show the bound one. | The user asked for a combination no other app uses. Ctrl+Alt+Space, Alt+Space and Ctrl+Space are taken by IMEs and launchers. Ctrl+Alt+Q is Visual Studio's QuickWatch, and Ctrl+Alt+W starts its Watch chords, so W is only a fallback. No combination is guaranteed free, so there is a fallback chain plus a setting. |
| D2 | What "Space cube" does | It **places a cube**: the rectangular block that **Ctrl+Alt+B** spawns (`BlockOverlay`), with a dashed outline that stays visible over spaces until it is created. **C** over a space puts the block on *that space's* app layer, so a block crossing it merges into it, as crossing spaces always do. One block, then back to the Hand. **Ctrl+Alt+S** (freehand) and **Ctrl+Alt+B** work exactly as today and don't change the tool. | The user's choice (2 October 2026). |
| D3 | Tool state | One current tool for the whole app, **Hand** at start. **H/P/T** over a space change it, and it stays until changed. **Every screen-wide tool ends in the Hand** when it closes: wheel or toolbar Pen and Text, and the Space cube's block. Ctrl+Alt+S and Ctrl+Alt+B don't touch the tool. | "By default uses hand tool" holds at start and after every screen-wide action, and the rule is easy to predict. |
| D4 | Pen and Text from the **wheel or toolbar** | They work **everywhere**. A screen-wide overlay opens, like Ctrl+Alt+S, with a hint at the top. A stroke that starts on a space paints that space; anywhere else it becomes a screen note on the app under it. Pen stays on until **Esc**, a right-click, **H**, or another tool. Text places **one** note per pick. | This is the "drawing over a screen without spaces" feature. One note per pick avoids focus fights between the overlay and the text box. |
| D5 | **P / T keys over a space** | They change the tool **inside spaces only**. Outside spaces the mouse stays the normal system cursor. | This matches the request: "While not inside the space: Common system cursor". |
| D6 | Screen notes | Kept per app window: one invisible "sheet" per app (or one for the desktop). Saved in `spaces.json`. They follow the window, hide with it, and come back when the app reopens. Their default ink is **red `#FF5A5F`** when the pen colour is "theme ink". | The app's whole model is persistent and app-attached. White theme ink would be invisible on a white document. |
| D7 | Clicking screen notes | Screen notes catch the mouse **only on their ink** (about 4 DIPs around it); everything else goes to the app. On their ink they always act as the **Hand** (select, move, edit, group). Ctrl+Z/Ctrl+Y work while the mouse is on their ink. | The Hand must be able to move drawings outside spaces too, without blocking the app underneath. |
| D8 | Placeholder tools | Project meter, Text wrap, Eraser and List show in the wheel and toolbar, dimmed with "soon". Picking one closes the menu and shows a tray notice: "‹Name›: coming soon." The tool doesn't change. They have no keys. | Stubs stay visible and testable, and there is a single place to plug real code in later. |
| D9 | The Hand | **Click** selects (a whole group if the item is grouped). **Shift+click** adds or removes. **Drag on ink** moves the selection. **Drag on empty space** draws a selection rectangle. **Double-click** text edits it. **Ctrl+click** a link opens it. **Ctrl+G** groups and **Ctrl+Shift+G** ungroups. A move is **one** Ctrl+Z step. Opened files move with the hand too. **E** still edits the text under the mouse, with the Hand only. | This is what "move texts, move drawings, select and group objects, edit text" needs. Double-click is the discoverable way to edit; E is kept for existing users. |
| D10 | **T** key | **T** no longer opens a text box at once. It picks the Text tool, then a click places the box. | Requested: the keys change the tool. |
| D11 | Toolbar "collidable" | It is a real, clickable window (not click-through), always on top. It **never takes keyboard focus**. Drag it to move it; its place is saved. It hides while a full-screen app or game is in front. | "Collided" means it takes clicks. Never taking focus means typing in apps, and the screen overlays, are never interrupted. |
| D12 | Delete key, real eraser | **Not in this plan.** Removing ink stays Ctrl+Z, the erase brush (spaces only), and the new tray item "Clear screen notes…". | The Eraser is explicitly a stub "for now". |

---

## 2. Words used in this plan

- **Space**: as today; a `SpaceRecord` with `Kind = SpaceKind.Space`.
- **Screen notes / sheet**: a `SpaceRecord` with `Kind = SpaceKind.Sheet`. It has no look (it is invisible) and only its paint and text show. It is shown by a normal `SpaceWindow`, so it follows its app like a space does. There is one sheet per app window, plus one for the desktop.
- **Tool**: `Spaces.Tool` enum (Hand, Pen, Text, Cube, ProjectMeter, TextWrap, Eraser, List). The catalog (names, keys, icons) is `Shell.ToolCatalog`.
- **Screen overlay**: a `GpuOverlay` covering every monitor (as Ctrl+Alt+S uses today). New: `ScreenInkOverlay` (Pen or Text on the screen).
- **Hover keys**: keys registered as global hotkeys only while the mouse is over a space (`ShellController.UpdateHoverKeys`). Today: T, E, Ctrl+Z, Ctrl+Y.

---

## 3. Behaviour reference (the acceptance spec)

### 3.1 Tools

Wheel order runs clockwise from the top. Index 0 is up, 2 is right, 4 is down, 6 is left.

| # | Tool (enum) | Shown as | Key over a space | In a space | From the wheel or toolbar |
|---|---|---|---|---|---|
| 0 | `Hand` | Hand | H | Select, move, group, edit (3.3) | Closes any screen overlay; tool = Hand |
| 1 | `Pen` | Pen | P | Drag paints; a click splashes | Screen pen overlay (D4) |
| 2 | `Text` | Text | T | Click opens a text box there | Screen text overlay, one note (D4) |
| 3 | `Cube` | Space cube | C | Block overlay on this space's layer (D2) | Block overlay on the app in front |
| 4 | `ProjectMeter` | Project meter (soon) | — | — | Tray notice "coming soon" |
| 5 | `TextWrap` | Text wrap (soon) | — | — | same |
| 6 | `Eraser` | Eraser (soon) | — | — | same |
| 7 | `List` | List (soon) | — | — | same |

Everything else works under any tool, as today:

- Right-click on a space opens the ink palette.
- The mouse wheel scrolls opened files.
- Files and text can be dropped from other apps.
- Ctrl+Z and Ctrl+Y undo and redo.

### 3.2 Cursor

- **Outside spaces** it is the normal system cursor, unless a screen overlay is open. The pen overlay shows a cross, the text overlay an I-beam, and the block overlay its move and resize arrows (as today).
- **Over a space:**
  - Hand: arrow, or four-way move arrows over ink.
  - Pen: cross.
  - Text: I-beam.
- **Over screen-note ink**: four-way move arrows (always the Hand).

### 3.3 Hand gestures (in a space, and on screen-note ink)

| Gesture | Result |
|---|---|
| Press on ink (stroke or text) | If it isn't selected (and Shift isn't held), it becomes the selection, with its whole group. |
| …release without moving | Shift: add or remove that item and its group. Ctrl on a link: open the link. Otherwise: select just that item and its group. |
| …drag | Moves the whole selection, drawn live. On release it is kept as a move (one undo step). |
| Press on empty space, release | Clears the selection (Shift keeps it) and splashes, as a click does today. |
| Drag on empty space | Selection rectangle. Selects everything it touches (whole groups), added to the selection if Shift was held. |
| Double-click on text | Opens it for editing (the same text box as **E**). On an opened file's **name** it opens the file, as today. |
| Ctrl+G / Ctrl+Shift+G (mouse over that space, with a selection) | Group the selection (at least 2 items) / ungroup it. |

Selected items show a dashed frame in the theme's accent colour: one frame per group, or per item if it isn't grouped. Picking any tool other than the Hand clears the selection.

### 3.4 Keys

| Where | Keys |
|---|---|
| Global, always | **Ctrl+Alt+`** tool wheel (D1). Unchanged: **Ctrl+Alt+S** draw a space, **Ctrl+Alt+B** block, **Ctrl+Alt+M** move, **Ctrl+Alt+E** remove. |
| Over a space (hover keys; letters only if the mouse moved in the last 3 s, as today) | **H P T C** tools. **E** edit text (Hand only). **Ctrl+Z / Ctrl+Y** (also over screen-note ink). **Ctrl+G / Ctrl+Shift+G** while that space has a selection. |
| In the pen or text screen overlay | **Esc** or right-click: done (Hand). **Ctrl+Z**: undo the last stroke drawn in this overlay. **H T C** (and **P** in text mode): switch tool. |
| In the wheel | **Esc**, a right-click, a click in the middle, a click outside, or the hotkey again: close. |

### 3.5 Toolbar

- **Collapsed**: a 30×30 DIP square in the theme's chrome (3.7) with three dots. Dreamy and Void use square, pixel-style dots; other themes use round ones. The default place is the primary screen's right edge, 35% down.
- **Click** unfolds a labelled menu (184 DIPs wide, 8 rows of 28). It opens downwards and rightwards when there's room, otherwise upwards or leftwards. Each row shows an icon, the name, and the key hint (H/P/T/C, or "soon"). The current tool's row is highlighted.
- Clicking a row picks the tool and folds the menu. A click anywhere outside, or on the square, folds it too.
- **Drag** the square to move it. Its place is saved.
- It is always on top of the screen overlays, so tools can be switched while drawing. It never takes focus, and it hides while a full-screen app is in front.
- Tray menu → **Show toolbar** turns it on and off (saved).

### 3.6 Wheel

- A ring centred on the mouse (outer radius 116 DIPs, inner 40). There are 8 sectors with an icon and a short name each; the placeholders are dimmed with "soon".
- The sector in the mouse's **direction** from the centre is highlighted, even past the ring, within the 300×300 window. The centre shows the highlighted tool's name and key.
- A click picks it. A click in the centre closes the wheel.
- The current tool's sector has an accent edge.

### 3.7 Theme chrome

The wheel and toolbar are dressed per theme by `Themes.ChromeOf(theme)` (Phase 9). Dreamy is a black square with a 2-DIP white rim and no rounding, like a dream block. The foreground always contrasts at least 4.5:1 with the background (a test checks this).

---

## 4. Map of the change

**New files**

| Path | What it holds |
|---|---|
| `src/InfiniteSpace/Spaces/Tool.cs` | `enum Tool` |
| `src/InfiniteSpace/Spaces/SpaceKind.cs` | `enum SpaceKind { Space, Sheet }` |
| `src/InfiniteSpace/Spaces/InkHit.cs` | hit testing, bounds, selection-rectangle tests, groups (pure) |
| `src/InfiniteSpace/Spaces/Sheets.cs` | screen-note bounds and click areas (pure) |
| `src/InfiniteSpace/Spaces/SpaceWindow.Hand.cs` | Hand gestures, selection |
| `src/InfiniteSpace/Spaces/SpaceManager.Sheets.cs` | screen notes: add ink, type, fit, clear |
| `src/InfiniteSpace/Rendering/BareLook.cs` | a look that draws nothing (for sheets) |
| `src/InfiniteSpace/Shell/ToolCatalog.cs` | `ToolInfo`, `ToolCatalog` |
| `src/InfiniteSpace/Shell/ToolIcons.cs` | icon path data, `Shape()` |
| `src/InfiniteSpace/Shell/HotkeyText.cs` | parse and format "Ctrl+Alt+`" |
| `src/InfiniteSpace/Shell/ShellController.Tools.cs` | `SelectTool`, wheel, toolbar wiring (`partial`) |
| `src/InfiniteSpace/Shell/OverlayStroke.cs` | a live stroke on an overlay layer |
| `src/InfiniteSpace/Shell/ScreenInkOverlay.cs` | Pen and Text on the screen |
| `src/InfiniteSpace/Shell/WheelGeometry.cs` | sector maths (pure) |
| `src/InfiniteSpace/Shell/ToolWheelWindow.cs` | the wheel (WPF) |
| `src/InfiniteSpace/Shell/ToolbarWindow.cs` | the toolbar (WPF) |
| `tests/InfiniteSpace.Tests/ToolCatalogTests.cs`, `HotkeyTextTests.cs`, `InkHitTests.cs`, `SheetsTests.cs`, `WheelGeometryTests.cs`, `ToolChromeTests.cs` | unit tests |

**Changed files**

| Path | What changes |
|---|---|
| `src/InfiniteSpace/Spaces/SpaceRecord.cs` | `Kind`, `IInk.Group`, `InkStroke.Previous`, stroke versions, batch undo, copy helpers, `InkStroke.FromScreen` |
| `src/InfiniteSpace/Spaces/SpaceWindow.cs` | tool-aware input, cursor, sheet behaviour |
| `src/InfiniteSpace/Spaces/SpaceWindow.Files.cs` | old note-drag code removed |
| `src/InfiniteSpace/Spaces/SpaceManager.cs` | tool, wiring, batch undo, `MoveInk`, groups, sheet exclusions |
| `src/InfiniteSpace/Spaces/SpaceManager.Files.cs` | `MoveNote` removed, `SpaceAt` spaces only |
| `src/InfiniteSpace/Rendering/InkLayer.cs` | selection frames and selection rectangle |
| `src/InfiniteSpace/Rendering/Themes.cs` | `SheetInk`, `ToolChrome`, `ChromeOf` |
| `src/InfiniteSpace/Shell/ShellController.cs` | hover keys, `partial`, overlay closing → Hand, tray actions |
| `src/InfiniteSpace/Shell/TrayIcon.cs` | new menu items |
| `src/InfiniteSpace/Shell/AppSettings.cs` | 4 settings |
| `src/InfiniteSpace/Shell/GpuOverlay.cs` | `Later` made protected, I-beam cursor |
| `src/InfiniteSpace/Shell/BlockOverlay.cs` | dashed outline with a dark halo round the block |
| `src/InfiniteSpace/Native/Win32.cs` | `IDC_IBEAM`, `RGN_OR`, `SHQueryUserNotificationState` |
| `tests/InfiniteSpace.Perf/Scenarios.cs`, `FileScenarios.cs` | pen for painting, files move with the hand, new `screenink` |
| `README.md` | Use table, How it works, Known limits |

**How input is routed after the change**

```
GlobalHotkeys ── Ctrl+Alt+` ─→ ShellController.ToggleWheel ─→ ToolWheelWindow ─┐ Picked
               ── Ctrl+Alt+S/B/M/E ─→ as today (the tool is left alone)         │
ToolbarWindow ───────────────────────────────────────────────── Picked ─────────┤
ScreenInkOverlay (H/P/T/C keys) ──────────────────────────────── selectTool ─────┤
Hover keys H/P/T/C over a space ─────────────────────────────────────────────────┴→ ShellController.SelectTool(tool, source)
   Hand → close screen overlay, SpaceManager.SetTool(Hand)
   Pen/Text + SpaceKey → SpaceManager.SetTool(tool)            (inside spaces only)
   Pen/Text otherwise → ShowOverlay(ScreenInkOverlay), then SetTool(tool)
   Cube → ShowOverlay(BlockOverlay, endsInHand), then SetTool(Cube)
   stub → tray notice
SpaceManager.SetTool → every SpaceWindow.Tool → SpaceWindow.WndProc acts on EffectiveTool
An overlay shown with endsInHand: true closing → SpaceManager.SetTool(Hand)
```

---

## 5. Phase 1: tool model, catalog, hotkey text, settings (no visible change)

### 5.1 `Spaces/Tool.cs` (new)

```csharp
namespace InfiniteSpace.Spaces;

/// <summary>What the mouse does on a space (names, keys and icons: <see cref="Shell.ToolCatalog"/>).</summary>
internal enum Tool
{
    Hand,
    Pen,
    Text,
    Cube,
    ProjectMeter,
    TextWrap,
    Eraser,
    List,
}
```

### 5.2 `Shell/ToolIcons.cs` (new)

Every icon is outline path data in a 16×16 box. It is drawn with a 1.5-DIP stroke with round caps and joins, and no fill.

```csharp
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;

namespace InfiniteSpace.Shell;

/// <summary>The tools' icons: outlines in a 16×16 box, drawn with round 1.5-DIP strokes in the chrome's foreground.</summary>
internal static class ToolIcons
{
    public const string Hand = "M5,10.5 L5,3.5 M7.5,8 L7.5,2 M10,8 L10,2.5 M12.5,8.5 L12.5,4.5 M5,10.5 C5,13 6.8,14.5 9,14.5 C11.4,14.5 12.5,13 12.5,11 L12.5,8.5 M5,10.5 L3,8.5";
    public const string Pen = "M2.5,13.5 L3,10.5 L11,2.5 L13.5,5 L5.5,13 Z M9.5,4 L12,6.5";
    public const string Text = "M3,3 L13,3 M8,3 L8,13.5 M6,13.5 L10,13.5";
    public const string Cube = "M8,1.5 L14,4.75 L14,11.25 L8,14.5 L2,11.25 L2,4.75 Z M2,4.75 L8,8 L14,4.75 M8,8 L8,14.5";
    public const string Meter = "M2,12 A6,6 0 0 1 14,12 M8,12 L11,7.5 M2,12 L14,12";
    public const string Wrap = "M2,3.5 L14,3.5 M2,7.5 L11.5,7.5 C13.5,7.5 13.5,11.5 11.5,11.5 L8,11.5 M9.5,10 L8,11.5 L9.5,13 M2,11.5 L5,11.5";
    public const string Eraser = "M6.5,13.5 L2.5,9.5 L9,3 L14,8 L8.5,13.5 Z M5.5,6.5 L10.5,11.5 M8.5,13.5 L14,13.5";
    public const string List = "M6,4 L14,4 M6,8 L14,8 M6,12 L14,12 M2.5,4 L3,4 M2.5,8 L3,8 M2.5,12 L3,12";

    /// <summary>An icon ready to show, 16×16 DIPs.</summary>
    public static Path Shape(string data, Brush ink)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return new Path
        {
            Data = geometry,
            Stroke = ink,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
        };
    }
}
```

### 5.3 `Shell/ToolCatalog.cs` (new)

```csharp
using System.Windows.Input;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Shell;

/// <summary>A tool as menus show it: its name, what it does, its key over a space, and whether it is a placeholder.</summary>
internal sealed record ToolInfo(Tool Tool, string Name, string Description, Key? Key, bool IsStub, string Icon);

/// <summary>Every tool, in the wheel's order (clockwise from the top) and the toolbar's (top to bottom).</summary>
internal static class ToolCatalog
{
    public static IReadOnlyList<ToolInfo> All { get; } =
    [
        new(Tool.Hand, "Hand", "Select, move, group and edit", Key.H, false, ToolIcons.Hand),
        new(Tool.Pen, "Pen", "Draw on spaces and over apps", Key.P, false, ToolIcons.Pen),
        new(Tool.Text, "Text", "Type a note", Key.T, false, ToolIcons.Text),
        new(Tool.Cube, "Space cube", "Place a new rectangular space", Key.C, false, ToolIcons.Cube),
        new(Tool.ProjectMeter, "Project meter", "Coming soon", null, true, ToolIcons.Meter),
        new(Tool.TextWrap, "Text wrap", "Coming soon", null, true, ToolIcons.Wrap),
        new(Tool.Eraser, "Eraser", "Coming soon", null, true, ToolIcons.Eraser),
        new(Tool.List, "List", "Coming soon", null, true, ToolIcons.List),
    ];

    public static ToolInfo Of(Tool tool) => All.First(t => t.Tool == tool);

    /// <summary>The tool a plain key picks (H, P, T, C), if any.</summary>
    public static Tool? ForKey(Key key) => All.FirstOrDefault(t => t.Key == key)?.Tool;
}
```

The four placeholders map to the user's names: `blank_function1_project_meter` → `ProjectMeter`, `blank_function2_text_wrap` → `TextWrap`, `blank_function3_eraser` → `Eraser`, `blank_function4_list` → `List`.

### 5.4 `Shell/HotkeyText.cs` (new)

```csharp
/// <summary>Hotkeys as people write them ("Ctrl+Alt+`"), read from settings and shown in menus.</summary>
internal static class HotkeyText
{
    /// <summary>Reads "Ctrl+Alt+`", "Win+Alt+W", "ctrl + shift + F9"…; needs at least one modifier (a bare key would be taken from every app).</summary>
    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key);

    /// <summary>"Ctrl+Alt+`": modifiers in the order Ctrl, Alt, Shift, Win.</summary>
    public static string Format(ModifierKeys modifiers, Key key);
}
```

- **Modifier tokens** (case-insensitive, trimmed, split on `+`): `ctrl`/`control`, `alt`, `shift`, `win`/`windows`.
- **Key token** (exactly one, the last):
  - `` ` `` or `~` or `oem3` → `Key.Oem3`
  - one letter → `Key.A + (c - 'A')`
  - one digit → `Key.D0 + d`
  - `space` → `Key.Space`
  - otherwise `Enum.TryParse<Key>(token, ignoreCase: true)`
- Fail if there is no modifier, or not exactly one key.
- **Format** writes `Key.Oem3` as `` ` ``, `D0`–`D9` as the digit, letters as upper case, and otherwise `key.ToString()`.

### 5.5 `Shell/AppSettings.cs`

Add these (with doc comments):

```csharp
/// <summary>The tool wheel's hotkey, as in "Ctrl+Alt+`"; null uses the default (others are tried if it is taken).</summary>
public string? ToolWheelHotkey { get; set; }

/// <summary>Whether the toolbar (the square with three dots) is on screen.</summary>
public bool ShowToolbar { get; set; } = true;

/// <summary>Where the toolbar's square was left (screen DIPs); null puts it at the primary screen's right edge.</summary>
public double? ToolbarLeft { get; set; }

public double? ToolbarTop { get; set; }
```

### 5.6 `Spaces/SpaceManager.cs`: the current tool (no behaviour yet)

Add this next to `Pen`, using the same pattern as `SetPen`:

```csharp
/// <summary>What the mouse does on spaces now (the hand to start with).</summary>
public Tool Tool { get; private set; } = Tool.Hand;

/// <summary>The tool changed (a key over a space, the wheel, the toolbar, or a screen-wide tool ending).</summary>
public event Action<Tool>? ToolChanged;

public void SetTool(Tool tool)
{
    if (tool == Tool)
        return;
    Tool = tool;
    foreach (var space in _spaces)
        space.Window.Tool = tool;
    ToolChanged?.Invoke(tool);
}
```

`SpaceWindow.Tool` arrives in Phase 2. For now, add an auto-property `public Tool Tool { get; set; } = Tool.Hand;` to `SpaceWindow` so this compiles. Set it in `Open()` too: `{ Pen = Pen, Tool = Tool }`.

### 5.7 Tests

- **`ToolCatalogTests`**:
  - There are 8 tools, in the enum's order.
  - Exactly 4 are stubs (the last 4).
  - The keys are exactly H, P, T, C on the first 4.
  - Every `Icon` parses with `Geometry.Parse` (run inside `Gpu.RunOnSta`).
  - `ForKey(Key.P) == Tool.Pen` and `ForKey(Key.Q) == null`.
- **`HotkeyTextTests`**:
  - `"Ctrl+Alt+`"` → `(Control|Alt, Oem3)`
  - `"win + alt + w"` → `(Windows|Alt, W)`
  - `"Ctrl+Shift+F9"` works
  - `"W"`, `"Ctrl+"`, `"Ctrl+Alt+W+Q"`, `""` and `null` fail
  - Format round-trips each valid one
- **`AppSettingsTests`**: a settings file without the new keys loads with `ShowToolbar == true`, `ToolWheelHotkey == null` and toolbar position null. The new values save and load back.

**Done when** it builds and the tests pass. The app behaves exactly as before.

---

## 6. Phase 2: tools inside spaces (H/P/T/C, cursor, text tool)

### 6.1 Small additions

- `Native/Win32.cs`: `public const int IDC_IBEAM = 32513;`
- `Shell/GpuOverlay.cs`, class `SystemCursors`: `public static readonly IntPtr IBeam = Load(Win32.IDC_IBEAM);`
- `Spaces/SpaceRecord.cs`: move the body of `SpaceWindow.FinishStroke`'s point mapping into a static factory, and use it from `FinishStroke`:

```csharp
/// <summary>
/// A stroke painted on screen (DIPs), kept in the record's coordinates: only its place is mapped back
/// through <paramref name="toScreen"/>, its shape is kept as painted (see <see cref="Placed"/>).
/// </summary>
public static InkStroke FromScreen(IReadOnlyList<Point> points, Matrix toScreen, InkPen pen)
{
    var toRecord = toScreen;
    if (toRecord.HasInverse)
        toRecord.Invert();
    var corner = new Point(points.Min(p => p.X), points.Min(p => p.Y));
    var shift = toRecord.Transform(corner) - corner;
    var xy = new double[points.Count * 2];
    for (int i = 0; i < points.Count; i++)
    {
        var p = points[i] + shift;
        xy[i * 2] = Math.Round(p.X, 2);
        xy[i * 2 + 1] = Math.Round(p.Y, 2);
    }
    return new InkStroke { Points = xy, Thickness = pen.Thickness, Color = pen.Color, Order = InkOrder.Next() };
}
```

`FinishStroke` then becomes:

```csharp
_record.Strokes.Add(InkStroke.FromScreen(_stroke, _toScreen, _strokePen));
_stroke = [];
InkChanged?.Invoke(this);
```

### 6.2 `SpaceWindow.cs`: tool-aware input (the Hand keeps today's note dragging until Phase 3)

1. Replace the auto-property from 5.6 with:

   ```csharp
   private Tool _tool = Tool.Hand;

   /// <summary>What the mouse does here (set for every space by <see cref="SpaceManager.SetTool"/>).</summary>
   public Tool Tool
   {
       get => _tool;
       set => _tool = value; // Phase 3: leaving the hand clears the selection
   }

   /// <summary>The tool as it acts here: the pen and text as picked, everything else as the hand.</summary>
   private Tool EffectiveTool => _tool is Tool.Pen or Tool.Text ? _tool : Tool.Hand;
   ```

   Phase 4 adds `IsSheet ? Tool.Hand : …` in front.

2. Add `private static readonly IntPtr ArrowCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW);` and `IBeamCursor` (with `IDC_IBEAM`) next to `CrossCursor`.
3. Add `TextPressed` to `enum Gesture`, and this event:

   ```csharp
   /// <summary>The text tool clicked here (screen DIPs): a text box should open there.</summary>
   public event Action<SpaceWindow, Point>? TypeRequested;
   ```

4. `WM_SETCURSOR`:

   ```csharp
   Win32.SetCursor(EffectiveTool switch
   {
       Tool.Pen => CrossCursor,
       Tool.Text => IBeamCursor,
       _ => NoteUnderCursor() is null ? ArrowCursor : MoveCursor,
   });
   ```

5. `WM_LBUTTONDOWN`, inside `if (Contains(at))`:

   ```csharp
   _gesture = EffectiveTool == Tool.Text ? Gesture.TextPressed : Gesture.Pressed;
   _pressedAt = at;
   _pressedNote = EffectiveTool == Tool.Hand ? NoteUnder(at) : null; // only the hand moves text
   Win32.SetCapture(hwnd);
   ```

6. `Drag(at)`: at the top, `if (_gesture == Gesture.TextPressed) return;`. Inside the `Pressed` branch, replace `else { _gesture = Gesture.Painting; }` with:

   ```csharp
   else if (EffectiveTool == Tool.Pen)
   {
       _gesture = Gesture.Painting;
   }
   else
   {
       return; // the hand doesn't paint
   }
   ```

7. `WM_LBUTTONUP`: add `else if (gesture == Gesture.TextPressed) TypeRequested?.Invoke(this, _pressedAt);`. The Ctrl+click link case becomes Hand-only:

   ```csharp
   else if (gesture == Gesture.Pressed && !(EffectiveTool == Tool.Hand && IsCtrlDown() && NoteClicked?.Invoke(this, _pressedAt, NoteClick.CtrlClick) == true))
       _look.AddBurst(_pressedAt);
   ```

### 6.3 `SpaceManager.cs`

- In `Open()`, add `window.TypeRequested += (_, at) => BeginText(space, at, editing: null);`.
- **Delete** `TypeAtCursor()`. T no longer types at once.
- Add:

  ```csharp
  /// <summary>The app window the space under the mouse sits on (zero: desktop level), else the app in front: where C draws.</summary>
  public IntPtr HostAtCursor() => HoveredSpace(out _) is { } space ? space.Window.Host : Foreground.CaptureHost();
  ```

### 6.4 `ShellController`: make it `partial`, add `ShellController.Tools.cs`

- `ShellController.cs`: change to `internal sealed partial class ShellController(...)`.
- Remove `_typeKey` and its `Hold` line. Add `private HoverKey[] _toolKeys = [];` in the new file.
- In `Start()`, replacing the `_typeKey` line:

  ```csharp
  _toolKeys = [.. ToolCatalog.All
      .Where(t => t.Key is not null)
      .Select(t => new HoverKey(ModifierKeys.None, t.Key!.Value, () => SelectTool(t.Tool, ToolSource.SpaceKey)))];
  ```

- `UpdateHoverKeys()`:

  ```csharp
  var hover = _overlay is null && !spaces.IsTyping ? spaces.HoverAtCursor() : SpaceManager.Hover.None;
  bool onSpace = hover is SpaceManager.Hover.Space or SpaceManager.Hover.Note;
  foreach (var key in _undoKeys)
      key.Hold(_hotkeys, hover != SpaceManager.Hover.None);
  foreach (var key in _toolKeys)
      key.Hold(_hotkeys, moved && onSpace);
  _editKey?.Hold(_hotkeys, moved && hover == SpaceManager.Hover.Note && spaces.Tool == Tool.Hand);
  ```

  Update the method's doc comment: the keys are H P T C, E, Ctrl+Z and Ctrl+Y.
- `ShowOverlay(GpuOverlay overlay, IntPtr host, bool endsInHand = false)`: the new parameter is documented as "closing it returns to the Hand (a screen-wide tool)". In the `Closed` handler:

  ```csharp
  if (_overlay == overlay)
  {
      _overlay = null;
      if (endsInHand)
          spaces.SetTool(Tool.Hand); // every screen-wide tool ends in the hand
  }
  ```

  Existing callers (Ctrl+Alt+S/B/M, the erase brush, the tray) keep the default `false`.
- New `Shell/ShellController.Tools.cs`:

  ```csharp
  /// <summary>Where a tool was picked: it decides whether the tool works only in spaces or on the whole screen.</summary>
  internal enum ToolSource
  {
      SpaceKey,
      OverlayKey,
      Wheel,
      Toolbar,
  }

  internal sealed partial class ShellController
  {
      private HoverKey[] _toolKeys = [];

      /// <summary>Picks a tool (see the plan's decisions D2–D5).</summary>
      private void SelectTool(Tool tool, ToolSource source)
      {
          switch (tool)
          {
              case Tool.Hand or Tool.Pen or Tool.Text:
                  spaces.SetTool(tool); // Phase 4: Pen/Text from the wheel or toolbar open the screen overlay
                  break;
              case Tool.Cube:
                  StartBlock(source == ToolSource.SpaceKey ? spaces.HostAtCursor() : Foreground.CaptureHost());
                  break;
          }
          UpdateHoverKeys(); // an overlay may have opened: the letters go back to the apps
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
  ```

### 6.5 `BlockOverlay.cs`: the cube's outline shows over spaces

"Outlines should be visible even inside the space while not pasted." The block preview is a live space, so over a space in the same theme its edge can vanish (Dreamy on Dreamy, Sunday on Sunday). Show a dashed frame round the block until it is created:

- Copy `MoveOverlay`'s frame: the `_across` and `_down` pictures, the four `_sides` sprites, `DashedLine` and `Place`. The sides are long dashed lines cut to length with a clip, so moving the block redraws nothing.
- Make the pictures as long as the overlay and 3 DIPs thick: a 3-DIP line of `new Color4(0, 0, 0, 0.55f)`, with `MoveOverlay`'s white dashes (`FrameInk`) along its middle. That reads on light themes and dark ones alike.
- Create the sprites **before** the handles, so the handles stay on top.
- In `Layout()`, place them on `_rect`'s four sides, 2 DIPs outside it, whether or not the block has been dropped.
- Dispose them in `DisposeParts()`.

Ctrl+Alt+B gets the frame too (it's the same overlay); nothing else about it changes.

### 6.6 Perf scenarios

- `tests/InfiniteSpace.Perf/Scenarios.cs`, `Paint`: right after `var window = bench.Windows[0];`, add `bench.Spaces.SetTool(Tool.Pen);` (with `using InfiniteSpace.Spaces;`).
- `FileScenarios.DragNotes`, the `"file"` branch: dragging a file with the Hand no longer paints. Change its check to `Check(record.Strokes.Count == 0, "dragging an opened file with the hand leaves no paint")`. Phase 3 changes it again.

### 6.7 Manual checks

Hover a space, move the mouse a little, then:

- **P**: the cross cursor; dragging paints. Dragging on text paints over it, without moving it.
- **T**: the I-beam; a click opens a text box there.
- **H**: the arrow; dragging text moves it; dragging elsewhere neither paints nor moves anything; a click splashes.
- **C**: a block appears under the mouse, its dashed frame visible over the space (try Dreamy and Sunday). Pressing Enter where it crosses the space merges it into the space. Afterwards the tool is the Hand.
- **Ctrl+Alt+S** and **Ctrl+Alt+B** work as before and leave the tool as it was.
- Outside spaces, the cursor and clicks are the system's.

---

## 7. Phase 3: the Hand (select, move strokes and text, groups, double-click edit, one-step undo)

### 7.1 Model: `Spaces/SpaceRecord.cs`

1. **Groups.** In `IInk`, add:

   ```csharp
   /// <summary>Items with the same group are selected and moved together (Ctrl+G); null for none.</summary>
   Guid? Group { get; set; }
   ```

   Implement it in `InkStroke` and `TextNote` with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? Group { get; set; }`.

2. **Stroke versions**, as notes already have.

   - In `InkStroke`:

     ```csharp
     /// <summary>The stroke as it was before it was moved into this one; undo brings it back.</summary>
     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
     public InkStroke? Previous { get; set; }

     /// <summary>A new version of the stroke, moved by <paramref name="by"/> (strokes are only ever moved on screen, never scaled, so a screen offset is the record's too).</summary>
     public InkStroke MovedBy(Vector by, long order)
     {
         var xy = new double[Points.Length];
         for (int i = 0; i + 1 < xy.Length; i += 2)
         {
             xy[i] = Math.Round(Points[i] + by.X, 2);
             xy[i + 1] = Math.Round(Points[i + 1] + by.Y, 2);
         }
         return new InkStroke { Points = xy, Thickness = Thickness, Color = Color, Group = Group, Order = order };
     }
     ```

   - In `TextNote`, with the field list taken from today's `SpaceManager.MoveNote`:

     ```csharp
     /// <summary>A new version of the note at another place (record coordinates), with the same text, look and group.</summary>
     public TextNote MovedTo(Point at, long order) => new()
     {
         X = Math.Round(at.X, 2),
         Y = Math.Round(at.Y, 2),
         Text = Text,
         FontSize = FontSize,
         Color = Color,
         Width = Width,
         Bold = Bold,
         Italic = Italic,
         File = File?.Copy(), // the same text: kept where it is
         Group = Group,
         Order = order,
     };
     ```

   - In `SpaceRecord`, add `Replace(InkStroke stroke, InkStroke moved)`, the same as the note overload: remove, set `moved.Previous = stroke`, add.
   - `TakeNewestInk()`, stroke branch: after `Strokes.RemoveAt(stroke)`, `if (takenStroke.Previous is { } before) Strokes.Add(before);`.
   - `PutBack(IInk)`, stroke case: `if (stroke.Previous is { } before) Strokes.Remove(before);`, then add.
   - **`TransformInk`**, stroke case: transform **every version**, as the note case does. Without this, undoing a move after the app was resized puts the stroke back in the wrong place.

     ```csharp
     case InkStroke stroke:
         for (var version = stroke; version is not null; version = version.Previous)
         {
             TransformPoints(version.Points, placeStroke?.Invoke(version) ?? version.Placed(m, anchor));
             version.Revision++;
         }
         break;
     ```

3. **One undo step per move.** Every item in one move gets the same `Order`.

   ```csharp
   /// <summary>
   /// Takes back the newest change: every stroke and note that shares the newest order (a move of
   /// several things is one change). Ink from before ordering existed (order 0) goes one at a time.
   /// </summary>
   /// <returns>Empty when the space has no ink.</returns>
   public IInk[] TakeNewestBatch()
   {
       if (TakeNewestInk() is not { } first)
           return [];
       var batch = new List<IInk> { first };
       while (first.Order != 0 && NewestOrder() == first.Order)
           batch.Add(TakeNewestInk()!);
       return [.. batch];
   }

   private long NewestOrder()
   {
       long newest = -1;
       foreach (var stroke in Strokes)
           newest = Math.Max(newest, stroke.Order);
       foreach (var note in Notes)
           newest = Math.Max(newest, note.Order);
       return newest;
   }
   ```

   Keep `TakeNewestInk()` as it is; the existing tests use it.

### 7.2 `Spaces/InkHit.cs` (new, pure, unit-tested)

```csharp
/// <summary>What the hand finds on a space: ink under a point, in a rectangle, and with its group.</summary>
internal static class InkHit
{
    /// <summary>How far around ink (DIPs) the mouse still counts as on it.</summary>
    public const double Reach = 4;

    /// <summary>Where a stroke or note is on screen (DIPs), as shown through <paramref name="toScreen"/>.</summary>
    public static Rect Bounds(IInk ink, Matrix toScreen, Point? anchor);

    /// <summary>The topmost ink at a screen point: notes (drawn over strokes) first, newest first. Skips <paramref name="skip"/> and emptied notes.</summary>
    public static IInk? At(SpaceRecord record, Matrix toScreen, Point at, TextNote? skip = null);

    /// <summary>Strokes with a point inside <paramref name="area"/>, and notes whose box touches it (screen DIPs).</summary>
    public static List<IInk> Touching(SpaceRecord record, Matrix toScreen, Rect area, TextNote? skip = null);

    /// <summary>The item with every other live item of its group (just the item when it has none).</summary>
    public static List<IInk> WithGroup(SpaceRecord record, IInk item);

    /// <summary>Whether a screen point is within <paramref name="reach"/> of a stroke placed by <paramref name="placed"/>.</summary>
    public static bool OnStroke(InkStroke stroke, Matrix placed, Point at, double reach);

    internal static double DistanceToSegment(Point p, Point a, Point b);
}
```

- **Stroke placement** is always `stroke.Placed(toScreen, record.Anchor)`, a translation. **Note bounds** come from `NoteText.Bounds(note, toScreen)`.
- **`Bounds`** for a stroke: the box of its transformed points, inflated by `Thickness / 2`. For a note: `NoteText.Bounds`.
- **`At`**: notes whose `Bounds` inflated by `Reach` contain the point, picking the one with the highest `Order`. If none, strokes where `OnStroke(…, Reach)`, again the highest `Order`.
- **`OnStroke`**:
  - fewer than 2 numbers → false
  - a single point → distance to it ≤ `Thickness / 2 + reach`
  - otherwise → any segment's `DistanceToSegment` ≤ `Thickness / 2 + reach`

  ```csharp
  internal static double DistanceToSegment(Point p, Point a, Point b)
  {
      var ab = b - a;
      double lengthSquared = ab.LengthSquared;
      if (lengthSquared < 1e-12)
          return (p - a).Length;
      double t = Math.Clamp((p - a) * ab / lengthSquared, 0, 1); // Vector * Vector is the dot product
      return (p - (a + t * ab)).Length;
  }
  ```

- **`Touching`**: strokes with any transformed point inside `area` (points are at most about 1 DIP apart, so a rectangle across a stroke always holds one), and notes whose `Bounds` intersect `area`.
- **`WithGroup`**: if `item.Group is null`, return `[item]`. Otherwise every stroke and every non-empty note in the record with the same `Group`.

### 7.3 `Rendering/InkLayer.cs`: selection frames and the selection rectangle

```csharp
/// <summary>What the hand shows over the ink (screen DIPs): a dashed frame round each selected group or item, and the selection rectangle being dragged.</summary>
internal sealed record InkAdornments(IReadOnlyList<Rect> Frames, Rect? Marquee, Color Accent);
```

- Add the parameter `InkAdornments? adornments = null` **last** in `Redraw(...)`.
- After the notes loop, still inside the clip, draw the adornments:
  - Use the same `world` transform the notes use, so rectangles are in screen DIPs.
  - The brush is the accent colour.
  - Each frame is a dashed rectangle, 1.25 DIPs wide. The stroke style is `DashStyle.Custom` with dashes `[4, 3]`; create it once (`_dashed ??= _gpu.D2DFactory.CreateStrokeStyle(...)`, as `CanvasOverlay` does) and dispose it in `Dispose`.
  - The marquee is filled with the accent at opacity 0.12 (a temporary brush, disposed), then edged with the same dashed rectangle.

### 7.4 `Spaces/SpaceWindow.Hand.cs` (new partial), replacing today's note dragging

**Remove from `SpaceWindow.Files.cs`:** `NoteAt` (property), `NoteMoved` (event), `_pressedNote`, `_movingNote`, `_noteDrag`, `_placeMoving`, `NoteUnder`, `NoteUnderCursor`, `PlaceNotes`, `FinishNoteMove`.

**Keep:** `Adopt`, scrolling, arrivals and drops.

**Remove from `SpaceWindow.cs`:** `Gesture.MovingNote`.

New members:

```csharp
private readonly HashSet<IInk> _selected = [];
private HashSet<IInk>? _selectedBefore; // what was selected when a Shift+rectangle started
private IInk? _pressedInk;
private Vector _selectionDrag;          // how far the selection is being dragged
private Rect? _marquee;

/// <summary>Ink was dragged to a new place by this much on screen (DIPs). Not raised for a drag that went nowhere.</summary>
public event Action<SpaceWindow, IReadOnlyList<IInk>, Vector>? SelectionMoved;

/// <summary>Text was double-clicked with the hand (at this screen DIP point): it should open for editing.</summary>
public event Action<SpaceWindow, TextNote, Point>? EditRequested;

public IReadOnlyCollection<IInk> Selected => _selected;

public bool HasSelection => _selected.Count > 0;

/// <summary>Makes these the selection (the new versions of moved ink).</summary>
public void Select(IEnumerable<IInk> items) { _selected.Clear(); _selected.UnionWith(items); RedrawInk(); }

public void ClearSelection() { if (_selected.Count == 0) return; _selected.Clear(); RedrawInk(); }
```

The `Tool` setter (6.2) becomes `set { if (_tool == value) return; _tool = value; if (EffectiveTool != Tool.Hand) ClearSelection(); }`.

`Gesture` gains `HandOnInk`, `HandOnEmpty`, `MovingSelection` and `Marquee`.

**`WndProc` after this phase:**

```csharp
case Win32.WM_SETCURSOR:
    Win32.SetCursor(EffectiveTool switch
    {
        Tool.Pen => CrossCursor,
        Tool.Text => IBeamCursor,
        _ => InkUnderCursor() is null ? ArrowCursor : MoveCursor,
    });
    handled = true;
    return new IntPtr(1);

case Win32.WM_LBUTTONDOWN:
case WM_LBUTTONDBLCLK:
{
    var at = ToScreen(lParam);
    handled = true;
    if (msg == WM_LBUTTONDBLCLK && DoubleClick(at))
        break;
    if (!Contains(at))
        break;
    _pressedAt = at;
    switch (EffectiveTool)
    {
        case Tool.Pen:
            _gesture = Gesture.Pressed;
            break;
        case Tool.Text:
            _gesture = Gesture.TextPressed;
            break;
        default:
            HandDown(at);
            break;
    }
    Win32.SetCapture(hwnd);
    break;
}

case Win32.WM_MOUSEMOVE when _gesture != Gesture.None:
{
    var at = ToScreen(lParam);
    if (_gesture is Gesture.Pressed or Gesture.Painting)
        Drag(at);
    else if (_gesture != Gesture.TextPressed)
        HandDrag(at);
    handled = true;
    break;
}

case Win32.WM_LBUTTONUP:
{
    var gesture = _gesture;
    _gesture = Gesture.None; // before ReleaseCapture, which sends WM_CAPTURECHANGED
    Win32.ReleaseCapture();
    switch (gesture)
    {
        case Gesture.Painting:
            FinishStroke();
            break;
        case Gesture.Pressed:
            _look.AddBurst(_pressedAt); // a click with the pen only splashes
            break;
        case Gesture.TextPressed:
            TypeRequested?.Invoke(this, _pressedAt);
            break;
        case Gesture.None:
            break;
        default:
            HandUp(gesture);
            break;
    }
    handled = true;
    break;
}

case Win32.WM_CAPTURECHANGED:
    if (_gesture == Gesture.Painting)
        FinishStroke();
    else if (_gesture is Gesture.MovingSelection or Gesture.Marquee)
        CancelHandDrag();
    _gesture = Gesture.None;
    break;
```

`Drag(at)` keeps only the pen. Its `Pressed` branch becomes: `if (travel < DragThreshold) return; _gesture = Gesture.Painting;`, followed by today's painting code unchanged.

**Hand methods** (in `SpaceWindow.Hand.cs`):

```csharp
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
```

**Drawing the selection.** Replace the old `PlaceNotes` with two placements that shift selected ink by `_selectionDrag`. Keep the lambdas in fields, as `_placeMoving` was, so a drag doesn't allocate on every move.

```csharp
private Func<TextNote, Point>? _placeDraggedNote;
private Func<InkStroke, Matrix>? _placeDraggedStroke;

private Func<TextNote, Point>? PlaceNotes => _selectionDrag == default ? NotePlacement : _placeDraggedNote ??= note =>
{
    var at = NotePlacement?.Invoke(note) ?? _toScreen.Transform(new Point(note.X, note.Y));
    return _selected.Contains(note) ? at + _selectionDrag : at;
};

private Func<InkStroke, Matrix>? PlaceStrokes => _selectionDrag == default ? StrokePlacement : _placeDraggedStroke ??= stroke =>
{
    var m = StrokePlacement?.Invoke(stroke) ?? stroke.Placed(_toScreen, _record.Anchor);
    if (_selected.Contains(stroke))
        m.Translate(_selectionDrag.X, _selectionDrag.Y);
    return m;
};

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
        box.Offset(_selectionDrag);
        box.Inflate(3, 3);
        frames.Add(box);
    }
    return new InkAdornments(frames, _marquee, Themes.AccentOf(_theme));
}
```

`DrawInk()` becomes:

```csharp
private void DrawInk()
{
    // Ink that is gone (undone, replaced by a move) can't stay selected.
    _selected.RemoveWhere(i => i is InkStroke s ? !_record.Strokes.Contains(s) : !_record.Notes.Contains((TextNote)i));
    _inkDirty = _ink!.Redraw(_record, _toScreen, _editing, PlaceNotes, _arrivals.Count > 0 ? NoteOpacity : null, PlaceStrokes, Adornments());
}
```

### 7.5 `Spaces/SpaceManager.cs`

1. **`Space.Redo`** becomes `Stack<IInk[]>`. Fix every use; the compiler finds them. In `BakeMapping`, use `foreach (var batch in space.Redo) foreach (var ink in batch) SpaceRecord.TransformInk(...)`.
2. **`Undo`**:

   ```csharp
   var batch = space.Record.TakeNewestBatch();
   if (batch.Length == 0)
       return false;
   space.Redo.Push(batch);
   ```

   Then redraw and persist as today. **`Redo`**: `TryPop(out var batch)`, then `foreach (var ink in batch.Reverse()) space.Record.PutBack(ink);`.
3. **`Open()`** wiring: remove `window.NoteAt = …` and `window.NoteMoved += …`. Add:

   ```csharp
   window.EditRequested += (_, note, at) => BeginText(space, at, note);
   window.SelectionMoved += (_, items, by) => MoveInk(space, items, by);
   ```

4. **`MoveInk`** replaces `MoveNote` (delete `MoveNote` from `SpaceManager.Files.cs`):

   ```csharp
   /// <summary>
   /// Strokes and notes dragged across their space by <paramref name="by"/> (screen DIPs): each gets a new
   /// version in its new place, all with one order, so one undo takes the whole move back.
   /// </summary>
   private void MoveInk(Space space, IReadOnlyList<IInk> items, Vector by)
   {
       var live = items.Where(i => i is InkStroke s ? space.Record.Strokes.Contains(s) : space.Record.Notes.Contains((TextNote)i)).ToList();
       if (!_spaces.Contains(space) || live.Count == 0)
       {
           space.Window.RedrawInk();
           return;
       }
       long order = InkOrder.Next();
       var toRecord = space.ToScreen;
       if (toRecord.HasInverse)
           toRecord.Invert();
       var moved = new List<IInk>();
       foreach (var item in live)
       {
           if (item is InkStroke stroke)
           {
               var copy = stroke.MovedBy(by, order);
               space.Record.Replace(stroke, copy);
               moved.Add(copy);
           }
           else if (item is TextNote note)
           {
               // Notes are placed through the whole mapping (strokes only through its shift).
               var at = toRecord.Transform(space.ToScreen.Transform(new Point(note.X, note.Y)) + by);
               var copy = note.MovedTo(at, order);
               space.Window.Adopt(note, copy); // a long file isn't laid out again
               space.Record.Replace(note, copy);
               moved.Add(copy);
           }
       }
       space.Redo.Clear();
       space.Window.Select(moved); // the selection follows the new versions (this redraws)
       Persist();
   }
   ```

5. **`Keep`** (text box committed): before `typed.Order = …`, add `typed.Group = editing?.Group;` so editing doesn't drop a note from its group.
6. **Groups**:

   ```csharp
   /// <summary>Whether the space under the mouse has a selection (Ctrl+G and Ctrl+Shift+G belong to it then).</summary>
   public bool HasSelectionUnderCursor() => HoveredSpace(out _)?.Window.HasSelection == true;

   /// <summary>Ctrl+G (or Ctrl+Shift+G with <paramref name="group"/> false) over a space: its selection becomes one group, or none.</summary>
   public bool GroupUnderCursor(bool group)
   {
       if (HoveredSpace(out _) is not { } space || space.Window.Selected.Count < (group ? 2 : 1))
           return false;
       Guid? id = group ? Guid.NewGuid() : null;
       foreach (var item in space.Window.Selected)
           item.Group = id;
       space.Window.RedrawInk(); // one frame round the group now
       Persist();
       return true;
   }
   ```

   Grouping changes items in place. Ctrl+Z doesn't undo it; note that in the README.

### 7.6 `ShellController`: group keys

In `ShellController.Tools.cs`: `private HoverKey[] _groupKeys = [];`. In `Start()`:

```csharp
_groupKeys =
[
    new(ModifierKeys.Control, Key.G, () => spaces.GroupUnderCursor(group: true)),
    new(ModifierKeys.Control | ModifierKeys.Shift, Key.G, () => spaces.GroupUnderCursor(group: false)),
];
```

In `UpdateHoverKeys()`:

```csharp
bool selection = hover != SpaceManager.Hover.None && spaces.HasSelectionUnderCursor();
foreach (var key in _groupKeys)
    key.Hold(_hotkeys, selection);
```

### 7.7 Tests

- **`InkHitTests`**:
  - A point 3 DIPs from a 3-wide stroke hits, and one 8 DIPs away misses.
  - A one-point stroke (a dot) hits.
  - A note over a stroke wins.
  - Of two overlapping notes, the newer wins.
  - `_editing` is skipped.
  - A rectangle across a stroke, but around none of its corners, selects it.
  - `WithGroup` returns all items sharing a `Group`, and only live ones.
  - `DistanceToSegment` for a point beyond each end, and beside the middle.

  Notes need DirectWrite (`NoteText.Measure`): wrap those tests in `Gpu.RunOnSta`, as other note tests do.
- **`InkHistoryTests`** additions:
  - A move of a stroke and a note with one order is one `TakeNewestBatch`, and both originals are back.
  - `PutBack` of the batch restores the moved versions.
  - Order-0 strokes still come back one at a time.
  - `SpaceRecord.Transform` with a translation moves both the stroke and its `Previous`.
  - `MovedBy` keeps thickness, colour and group.
- **`SpaceStoreTests`**: a stroke with `Previous` and `Group`, and a note with `Group`, save and load back. A file without these properties loads with `Group == null` and `Previous == null`.

### 7.8 Perf

In `FileScenarios.DragNotes`, the `"file"` branch now expects the file **to move**. Use the same check as the `"text"` branch, about +180 and +90 DIPs, with `record.Strokes.Count == 0`. Update the comment above it: with the hand, files move like any note.

### 7.9 Manual checks

In a space with paint and text, using the Hand:

- Clicking a stroke frames it. Dragging it moves it. **Ctrl+Z** puts it back in one step.
- **Shift+click** adds a note. Dragging moves both. **Ctrl+Z** undoes both at once.
- Dragging on empty space draws a rectangle and selects what it touches.
- **Ctrl+G** makes one frame. Clicking one member selects the whole group. **Ctrl+Shift+G** ungroups.
- Double-clicking text opens it for editing; after editing it's still in its group.
- Ctrl+click a link in a Markdown file opens it. Double-clicking the file's name opens the file.
- **P** clears the selection.
- Ctrl+Alt+M still moves and reshapes the space, with its moved ink.
- Close and reopen the app: moves and groups are kept, and Ctrl+Z still undoes the last move.

---

## 8. Phase 4: screen notes and the screen pen and text

### 8.1 Model

- `Spaces/SpaceKind.cs`:

  ```csharp
  /// <summary>A space, or screen notes: paint and text on an app (or the desktop) with no space under them.</summary>
  internal enum SpaceKind
  {
      Space,
      Sheet,
  }
  ```

- `SpaceRecord`:

  ```csharp
  /// <summary>Whether this is a space or screen notes; spaces from before screen notes existed are spaces.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public SpaceKind Kind { get; set; }
  ```

  `SpaceStore` already has `JsonStringEnumConverter`, so it is saved as `"Sheet"`.
- `SpaceManager.Space`: `public bool IsSheet => Record.Kind == SpaceKind.Sheet;`.
- `Rendering/Themes.cs`:

  ```csharp
  /// <summary>Colour of screen notes painted or typed in the theme's ink: the palette's red, which reads on light and dark apps alike.</summary>
  public static readonly Color SheetInk = Color.FromRgb(0xFF, 0x5A, 0x5F);
  ```

### 8.2 `Rendering/BareLook.cs` (new)

```csharp
/// <summary>Screen notes' look: nothing at all, so only their paint and text show over the app.</summary>
internal sealed class BareLook : ISpaceLook
{
    private Geometry _outline = Geometry.Empty;

    public bool HasBursts => false;

    public void SetShape(Geometry outline, Rect world) => _outline = outline;

    public void Move(Geometry outline, Rect world) => _outline = outline;

    public bool Render(FrameState frame) => false;

    public void Present(ISpaceCanvas canvas) => canvas.HideArt();

    public bool IsInside(Point screen) => _outline.FillContains(screen);

    public void AddBurst(Point screen)
    {
    }

    public void Dispose()
    {
    }
}
```

### 8.3 `Spaces/Sheets.cs` (new, pure)

```csharp
/// <summary>Where screen notes are, and where they take the mouse.</summary>
internal static class Sheets
{
    /// <summary>Room (DIPs) kept around screen notes' ink inside their window.</summary>
    public const double Margin = 24;

    /// <summary>Everything on the sheet as shown on screen, plus <see cref="Margin"/>; null when it has no ink.</summary>
    public static Rect? Bounds(SpaceRecord record, Matrix toScreen)
    {
        var area = Rect.Empty;
        foreach (var stroke in record.Strokes)
            area.Union(InkHit.Bounds(stroke, toScreen, record.Anchor));
        foreach (var note in record.Notes)
            if (note.Text.Length > 0)
                area.Union(InkHit.Bounds(note, toScreen, record.Anchor));
        if (area.IsEmpty)
            return null;
        area.Inflate(Margin, Margin);
        return area;
    }

    /// <summary>
    /// Rectangles (screen DIPs) where screen notes take the mouse: each note's box, and each stroke in
    /// short pieces (6 points, overlapping by one), so a diagonal stroke doesn't block a whole box of the app.
    /// </summary>
    public static IEnumerable<Rect> HitRects(SpaceRecord record, Matrix toScreen)
    {
        const int Piece = 6;
        foreach (var stroke in record.Strokes)
        {
            var placed = stroke.Placed(toScreen, record.Anchor);
            double reach = stroke.Thickness / 2 + InkHit.Reach;
            var xy = stroke.Points;
            int count = xy.Length / 2;
            for (int start = 0; start < count; start += Piece - 1)
            {
                var box = Rect.Empty;
                for (int i = start; i < Math.Min(count, start + Piece); i++)
                    box.Union(placed.Transform(new Point(xy[2 * i], xy[2 * i + 1])));
                box.Inflate(reach, reach);
                yield return box;
                if (start + Piece >= count)
                    break;
            }
        }
        foreach (var note in record.Notes)
        {
            if (note.Text.Length == 0)
                continue;
            var box = NoteText.Bounds(note, toScreen);
            box.Inflate(InkHit.Reach, InkHit.Reach);
            yield return box;
        }
    }
}
```

### 8.4 `SpaceWindow`: how a sheet behaves

Add `private bool IsSheet => _record.Kind == SpaceKind.Sheet;`, then:

1. **Look.** Add `private ISpaceLook CreateLook() => IsSheet ? new BareLook() : Themes.CreateLook(_theme);`. Use it in the constructor, `SetTheme` and `RecreateGraphics`, which call `Themes.CreateLook(...)` today.
2. **Ink colour.** Add `private Color ThemeInk => IsSheet ? Themes.SheetInk : Themes.InkOf(_theme);`. Replace `Themes.InkOf(_theme)` in `EnsureInk`, `SetTheme` (`_ink.ThemeInk = …`) and `Drag` (`_strokeColor`).
3. **No drops.** In the constructor, use `if (previewUnder == IntPtr.Zero && record.Kind == SpaceKind.Space) AcceptFiles();`.
4. **No wobble.** In `ReshapeLive`, use `wobble: !IsSheet && _theme == Themes.Dreamy && …`. In `Wobble()`, add `IsSheet ||` to the early-return condition.
5. **Always the Hand.** `EffectiveTool => IsSheet ? Tool.Hand : _tool is Tool.Pen or Tool.Text ? _tool : Tool.Hand;`.
6. **Takes the mouse only on its ink.** In `ApplyRegion()`, after the `_previewUnder` check:

   ```csharp
   if (IsSheet)
   {
       ApplySheetRegion();
       return;
   }
   ```

   ```csharp
   /// <summary>A sheet takes the mouse only on its ink (see <see cref="Sheets.HitRects"/>); everywhere else clicks reach the app.</summary>
   private void ApplySheetRegion()
   {
       double scale = Dpi.Scale;
       IntPtr region = Win32.CreateRectRgn(0, 0, 0, 0);
       foreach (var r in Sheets.HitRects(_record, _toScreen))
       {
           IntPtr part = Win32.CreateRectRgn(
               (int)Math.Floor((r.Left - _world.X) * scale), (int)Math.Floor((r.Top - _world.Y) * scale),
               (int)Math.Ceiling((r.Right - _world.X) * scale), (int)Math.Ceiling((r.Bottom - _world.Y) * scale));
           Win32.CombineRgn(region, region, part, Win32.RGN_OR);
           Win32.DeleteObject(part);
       }
       if (_region != IntPtr.Zero)
           Win32.DeleteObject(_region);
       _region = Copy(region); // put back after floating (see RestoreRegion)
       SetRegion(region);
   }
   ```

   Add `public const int RGN_OR = 2;` to `Win32`. An empty region means the window takes no clicks at all, which is right for a sheet with no ink.
7. **Region follows the ink.** At the start of `RedrawInk(bool present)`, after the `_disposed` check:

   ```csharp
   if (IsSheet && _gesture == Gesture.None) // mid-drag the window has the mouse anyway; it is made once the drag ends
   {
       if (_floating)
           _regionStale = true;
       else
           ApplyRegion();
   }
   ```

### 8.5 `SpaceManager`: keep sheets out of everything that is about spaces

Change each of these:

| Member | Change |
|---|---|
| `Outlines` | `_spaces.Where(s => !s.IsSheet)…` (the erase brush shows only spaces) |
| `RemoveAt` | `showing` = showing **and not sheets** |
| `Erase` | `continue` for sheets |
| `PickUp` | not sheets |
| `Drop` | `crossed`: not sheets |
| `Place` | `crossed`: not sheets |
| `SpaceAt` (Files) | not sheets (so "Open a file…" and file opening never target a sheet) |
| `HoverAtCursor` | `space.IsSheet ? Hover.Sheet : …`; add `Sheet` to `enum Hover` |
| `Persist` | leave out sheets with no strokes and no notes |
| `BeginText` | `new TextEntry(origin, draft, space.IsSheet ? Themes.SheetInk : Themes.InkOf(Theme))` |
| `Undo`, `Redo`, `Keep` (the `owner.Window.RedrawInk()` line) | call `ShowInk(space)` (8.6) instead of `Window.RedrawInk()` |
| `Reshape(space)` (static) | first line: `if (space.IsSheet) RefitSheet(space);` |
| `MoveInk` | after `space.Window.Select(moved)`, add `if (space.IsSheet) FitSheet(space);` |

Also update the doc comment of `Hover` and `HoverAtCursor`.

### 8.6 `Spaces/SpaceManager.Sheets.cs` (new partial)

```csharp
internal sealed partial class SpaceManager
{
    /// <summary>How many sheets of screen notes there are, kept ones included.</summary>
    public int SheetCount => _spaces.Count(s => s.IsSheet) + _kept.Count(r => r.Kind == SpaceKind.Sheet);

    /// <summary>The colour ink takes by default at a screen point: a space's theme ink, or screen notes' red.</summary>
    public Color InkColorAt(Point at) => SpaceAt(at) is null ? Themes.SheetInk : Themes.InkOf(Theme);

    /// <summary>
    /// A stroke drawn on the screen (screen DIPs): it goes onto the space it starts on, else onto the
    /// screen notes of the app window under its start (or the desktop's).
    /// </summary>
    /// <returns>The window it went to (for undo), or zero when it was too short to keep.</returns>
    public IntPtr AddInk(IReadOnlyList<Point> points, InkPen pen)
    {
        if (points.Count < 2)
            return IntPtr.Zero;
        var space = SpaceAt(points[0]) ?? SheetAt(points[0]);
        space.Record.Strokes.Add(InkStroke.FromScreen(points, space.ToScreen, pen));
        space.Redo.Clear();
        ShowInk(space);
        Persist();
        return space.Window.Handle;
    }

    /// <summary>The text tool on the screen: a text box on the space at the point, else on the screen notes there.</summary>
    public void TypeAt(Point at) => BeginText(SpaceAt(at) ?? SheetAt(at), at, editing: null);

    /// <summary>Takes away every sheet of screen notes, including those waiting for their app.</summary>
    public void ClearSheets()
    {
        foreach (var sheet in _spaces.Where(s => s.IsSheet).ToList())
        {
            CloseWindow(sheet);
            _spaces.Remove(sheet);
        }
        _kept.RemoveAll(r => r.Kind == SpaceKind.Sheet);
        Persist();
    }

    /// <summary>The screen notes of the app window at a point (made if there are none yet).</summary>
    private Space SheetAt(Point at)
    {
        IntPtr host = AppWindows.WindowAt(at); // skips this app's own windows, the overlay included
        return _spaces.FirstOrDefault(s => s.IsSheet && s.Window.Host == host) ?? CreateSheet(host, at);
    }

    private Space CreateSheet(IntPtr host, Point at)
    {
        // A speck of a shape for now; it grows to cover its ink (FitSheet).
        var record = new SpaceRecord
        {
            Kind = SpaceKind.Sheet,
            Host = Describe(host),
            Figures = [[at.X - 1, at.Y - 1, at.X + 1, at.Y - 1, at.X + 1, at.Y + 1, at.X - 1, at.Y + 1]],
        };
        return Open(record, host, Matrix.Identity);
    }

    /// <summary>The space's ink changed: drawn again (screen notes first grow or shrink to cover their ink).</summary>
    private void ShowInk(Space space)
    {
        if (space.IsSheet)
            FitSheet(space);
        else
            space.Window.RedrawInk();
    }

    /// <summary>Screen notes take the shape of their ink (plus a margin), drawn again at once.</summary>
    private static void FitSheet(Space sheet)
    {
        RefitSheet(sheet);
        sheet.Stretched = false;
        sheet.Shown = sheet.ToScreen;
        sheet.Window.SetShape(sheet.Outline, sheet.ToScreen); // also redraws the ink and remakes where it takes clicks
    }

    /// <summary>
    /// Rewrites a sheet's outline to cover its ink as shown now. Its paint keeps its size when its app
    /// is resized, so the outline is worked out on screen and mapped back, not scaled.
    /// </summary>
    private static void RefitSheet(Space sheet)
    {
        if (Sheets.Bounds(sheet.Record, sheet.ToScreen) is not { } screen)
            return; // no ink: it keeps its shape, shows nothing and takes no clicks
        var toRecord = sheet.ToScreen;
        if (toRecord.HasInverse)
            toRecord.Invert();
        var r = Rect.Transform(screen, toRecord);
        sheet.Record.Figures = [[Math.Round(r.Left, 2), Math.Round(r.Top, 2), Math.Round(r.Right, 2), Math.Round(r.Top, 2), Math.Round(r.Right, 2), Math.Round(r.Bottom, 2), Math.Round(r.Left, 2), Math.Round(r.Bottom, 2)]];
        sheet.Record.FillRule = FillRule.Nonzero;
        sheet.Shape = SpaceGeometry.Decode(sheet.Record);
    }

    /// <summary>Screen notes made for a text box that was cancelled: nothing to keep.</summary>
    private void DropEmptySheet(Space space)
    {
        if (!space.IsSheet || !_spaces.Contains(space) || space.Record.Strokes.Count > 0 || space.Record.Notes.Count > 0 || space.Redo.Count > 0)
            return;
        CloseWindow(space);
        _spaces.Remove(space);
    }
}
```

- In `BeginText`, at the end of the `entry.Gone` handler (inside it, after the `editing` block), add `DropEmptySheet(space);`.
- `Keep()`, for sheets: `ShowInk(owner)` replaces `owner.Window.RedrawInk()`, as in 8.5.
- In the `editing is not null && owner.Window.Editing == editing` branch, also call `if (owner.IsSheet) FitSheet(owner);` after setting `Editing = null`.

### 8.7 `Shell/OverlayStroke.cs` (new)

This is the live stroke on an overlay. It does what `CanvasOverlay.DrawNewPieces` does, in one colour and width. **Don't refactor `CanvasOverlay` to use it.**

```csharp
/// <summary>A stroke drawn live on an overlay, one colour and width: each frame redraws only the rectangle its newest pieces cover.</summary>
internal sealed class OverlayStroke : IDisposable
{
    public OverlayStroke(GpuCompositor gpu, IDCompositionVisual parent, int pixelWidth, int pixelHeight);

    /// <summary>Its points, in DIPs from the overlay's top-left.</summary>
    public IReadOnlyList<Point> Points { get; }

    public bool HasNewPieces { get; } // _drawn < _points.Count

    /// <summary>Starts a stroke at a point in a colour and width (DIPs).</summary>
    public void Start(Point at, Color ink, double width);

    /// <summary>Adds a point, if it is at least a DIP from the last.</summary>
    public void Add(Point at);

    /// <summary>Draws the pieces added since the last frame (copy of CanvasOverlay.DrawNewPieces, with round caps).</summary>
    public void DrawNewPieces();

    /// <summary>Forgets the stroke and clears what was drawn.</summary>
    public void Reset();

    public void Dispose();
}
```

It owns one `OverlayLayer`. The colour converts to `Color4(ink.R / 255f, ink.G / 255f, ink.B / 255f, 1)`.

### 8.8 `Shell/ScreenInkOverlay.cs` (new)

First, in `GpuOverlay`, make `Later(Action)` **`protected`** (it is private today).

```csharp
internal enum ScreenInkMode
{
    Pen,
    Text,
}

/// <summary>
/// The pen or text tool over the whole screen. A stroke paints the space it starts on, or else becomes
/// screen notes on the app under it; the text tool places one text box where it is clicked. Esc, a
/// right-click or switching away ends it.
/// </summary>
internal sealed class ScreenInkOverlay : GpuOverlay
{
    private const string PenHint = "Draw on spaces or over apps   ·   Ctrl+Z undo   ·   H hand, T text, C cube   ·   Esc or right-click when done";
    private const string TextHint = "Click where the text goes   ·   Esc to cancel";

    private readonly SpaceManager _spaces;
    private readonly Action<Tool> _selectTool;
    private readonly OverlayStroke? _stroke;
    private readonly Stack<IntPtr> _drawnOn = new(); // windows strokes went to, newest on top (Ctrl+Z)
    private bool _drawing;

    /// <param name="selectTool">Picks another tool (H, P, T or C pressed here); called after this message is handled.</param>
    /// <param name="bounds">Where it covers (screen DIPs); the whole virtual screen by default. Tests keep it off screen.</param>
    public ScreenInkOverlay(ScreenInkMode mode, SpaceManager spaces, Universe universe, Action<Tool> selectTool, Rect? bounds = null)
        : base(universe, mode == ScreenInkMode.Pen ? PenHint : TextHint, bounds)
    {
        Mode = mode;
        _spaces = spaces;
        _selectTool = selectTool;
        Cursor = mode == ScreenInkMode.Pen ? SystemCursors.Cross : SystemCursors.IBeam;
        if (mode == ScreenInkMode.Pen)
            _stroke = new OverlayStroke(Gpu, Layers, PixelWidth, PixelHeight);
    }

    public ScreenInkMode Mode { get; }

    public override bool WantsFrame => _stroke?.HasNewPieces == true;

    protected override void OnMouseDown(Point at, int clickCount)
    {
        if (_stroke is null)
            return;
        var pen = _spaces.Pen;
        _stroke.Start(at, InkColors.Resolve(pen.Color, _spaces.InkColorAt(at + Origin)), pen.Thickness);
        _drawing = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(Point at)
    {
        if (_drawing)
            _stroke!.Add(at);
    }

    protected override void OnMouseUp(Point at)
    {
        if (Mode == ScreenInkMode.Text)
        {
            var screen = at + Origin;
            Dismiss(); // the app gets the keyboard back first; then the text box takes it
            Later(() => _spaces.TypeAt(screen));
            return;
        }
        if (!_drawing)
            return;
        _drawing = false;
        ReleaseMouseCapture();
        var points = _stroke!.Points.Select(p => p + Origin).ToList();
        _stroke.Reset(); // the kept stroke shows on its space or screen notes in the same frame
        IntPtr window = _spaces.AddInk(points, _spaces.Pen);
        if (window != IntPtr.Zero)
            _drawnOn.Push(window);
    }

    protected override void OnCaptureLost()
    {
        if (!_drawing)
            return;
        _drawing = false;
        _stroke!.Reset();
    }

    protected override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key == Key.Z && Mode == ScreenInkMode.Pen)
        {
            while (_drawnOn.TryPop(out var window))
            {
                if (_spaces.Undo(window))
                    break;
            }
            return true;
        }
        if (modifiers == ModifierKeys.None && ToolCatalog.ForKey(key) is { } tool && tool != ToolOfMode)
        {
            Later(() => _selectTool(tool)); // not from inside this overlay's own message: picking may close it
            return true;
        }
        return base.OnKeyDown(key, modifiers); // Esc
    }

    public override void RenderFrame(FrameState frame) => _stroke?.DrawNewPieces();

    protected override void DisposeParts() => _stroke?.Dispose();

    private Tool ToolOfMode => Mode == ScreenInkMode.Pen ? Tool.Pen : Tool.Text;
}
```

### 8.9 `ShellController`: the final `SelectTool`, tray

- `SelectTool`, final version. `_wheel` arrives in Phase 5; until then, leave out the first line.

  ```csharp
  private void SelectTool(Tool tool, ToolSource source)
  {
      _wheel?.Dismiss(); // a toolbar pick while the wheel is open
      var info = ToolCatalog.Of(tool);
      if (info.IsStub)
      {
          _tray?.Notify(info.Name, "Coming soon.");
          return; // placeholders don't change the tool
      }
      switch (tool)
      {
          case Tool.Hand:
              CloseOverlay(); // a screen-wide tool, if one is open (its closing sets the hand too)
              spaces.SetTool(Tool.Hand);
              break;
          case Tool.Pen or Tool.Text when source == ToolSource.SpaceKey:
              spaces.SetTool(tool); // only inside spaces: elsewhere the mouse stays the system's
              break;
          case Tool.Pen or Tool.Text:
              FadeInkTools();
              IntPtr focus = Foreground.CaptureHost();
              var mode = tool == Tool.Pen ? ScreenInkMode.Pen : ScreenInkMode.Text;
              ShowOverlay(new ScreenInkOverlay(mode, spaces, universe, t => SelectTool(t, ToolSource.OverlayKey)), focus, endsInHand: true);
              spaces.SetTool(tool); // after ShowOverlay: closing the overlay it replaced set the hand
              break;
          case Tool.Cube:
              StartBlock(source == ToolSource.SpaceKey ? spaces.HostAtCursor() : Foreground.CaptureHost());
              break;
      }
      UpdateHoverKeys();
  }
  ```

- **Hover keys:** `onSpace` already leaves out `Hover.Sheet`, so tool letters don't fire over screen notes. Undo and group keys do, because they check `hover != None`.
- **Tray.**
  - `TrayActions` gains `Action DrawOnScreen` and `Action ClearScreenNotes`.
  - Menu: after "Spawn a block", add `Item("Draw on the screen", null, actions.DrawOnScreen)`. After "Erase all spaces…", add `Item("Clear screen notes…", null, actions.ClearScreenNotes)`.
  - `DrawOnScreen = () => SelectTool(Tool.Pen, ToolSource.Toolbar)`.
  - `ClearScreenNotes = ConfirmClearScreenNotes`, mirroring `ConfirmEraseAll`: a Yes/No `MessageBox` with "Clear all screen notes? This cannot be undone." → `spaces.ClearSheets()`. Return if `SheetCount == 0`.
  - `ConfirmEraseAll`: change the text to "Erase all {n} spaces and screen notes? This cannot be undone." (`EraseAll` already clears everything.)

### 8.10 Tests and perf

- **`SheetsTests`** (in `Gpu.RunOnSta` where notes are used):
  - `Bounds` covers a stroke plus `Thickness / 2 + Margin`.
  - `Bounds` is null with no ink.
  - With a scaled `toScreen`, a stroke's bounds keep its size.
  - `HitRects` covers every point of a long stroke. Check that each stroke point falls in some rectangle.
  - A dot gives one rectangle.
- **`SpaceStoreTests`**: `Kind = Sheet` round-trips. A record without `Kind` loads as `Space`. A `Space` record's JSON doesn't contain `"Kind"`.
- **Perf**, new scenario in `Scenarios.cs`: `new("screenink", [Themes.Dreamy, Themes.Glory], ScreenInk)`.
  - Bench with one `Bench.Record(new Rect(Bench.X, Bench.Y, 300, 200), ink: false)`.
  - Open `new Shell.ScreenInkOverlay(Shell.ScreenInkMode.Pen, bench.Spaces, bench.Universe, _ => { }, OverlayArea)` with `Show(activate: false)`.
  - Draw one wavy stroke starting **beside** the space (`Bench.X + 400`), as `DrawSpace` does, timed with a `FrameTimer("screen pen", …)`.
  - Then draw one stroke starting **on** the space.
  - `Check`:
    - Exactly one record has `Kind == SpaceKind.Sheet` and one stroke: "a stroke beside a space became screen notes".
    - The space's record has one stroke: "a stroke that starts on a space paints it".
    - `bench.Spaces.Undo(sheetWindow.Handle)` leaves the sheet with no strokes: "undo takes it back".
  - `Dismiss()` at the end.

  Off screen, `AppWindows.WindowAt` finds no app, so the sheet is at desktop level.

### 8.11 Manual checks

- Tray → **Draw on the screen**:
  - The hint shows at the top, with a cross cursor.
  - Drawing over a browser leaves red ink after release. Drawing that starts on a space paints the space.
  - **Ctrl+Z** takes back the last stroke. **Esc** ends it, and the tool is the Hand.
- Clicks on the browser work everywhere except on the ink.
- With the Hand, drag the red stroke: it moves, and **Ctrl+Z** (mouse on the ink) moves it back.
- Move and resize the browser: the notes follow, keep their size, and aren't cut off.
- Minimise the browser: the notes hide with it. Close it and reopen the same page: the notes come back. Restart Infinite Space: they're still there.
- Draw on the bare desktop: the notes sit above the wallpaper.
- **Ctrl+Alt+E** over screen notes doesn't remove them. Erase brush outlines show only spaces.
- Tray → **Clear screen notes…** removes them all.
- Change the theme: spaces re-dress, and screen notes stay red.

---

## 9. Phase 5: theme chrome and the tool wheel

### 9.1 `Rendering/Themes.cs`: chrome

```csharp
/// <summary>Colours and shape of the tool wheel and the toolbar in a theme (see <see cref="Themes.ChromeOf"/>).</summary>
internal sealed record ToolChrome(Color Back, Color Edge, Color Fore, Color Accent, double EdgeWidth, double Corner);
```

```csharp
/// <summary>How the tool wheel and the toolbar are dressed in a theme: a small piece of the theme itself.</summary>
public static ToolChrome ChromeOf(string theme) => theme switch
{
    Claudish => new(ClaudishStyle.Clay, ClaudishStyle.Ink, ClaudishStyle.Ink, ClaudishStyle.Paper, 2.5, 7),
    Glory => new(Color.FromRgb(0x24, 0x18, 0x08), Color.FromRgb(0xFF, 0xC6, 0x5C), GloryLook.Ink, Color.FromRgb(0xFF, 0xC6, 0x5C), 1.5, 6),
    Burn => new(Color.FromRgb(0x1E, 0x07, 0x05), Color.FromRgb(0xFF, 0x9A, 0x3C), BurnLook.Ink, Color.FromRgb(0xFF, 0x9A, 0x3C), 1.5, 6),
    Sunday => new(Color.FromRgb(0xF7, 0xD9, 0xCF), Color.FromRgb(0xB8, 0x3B, 0x5B), SundayLook.Ink, Color.FromRgb(0xB8, 0x3B, 0x5B), 1.5, 8),
    Parallax => new(Color.FromRgb(0xB7, 0xC4, 0xAE), Color.FromRgb(0x2E, 0x6B, 0x3A), ParallaxLook.Ink, Color.FromRgb(0x2E, 0x6B, 0x3A), 1.5, 6),
    Ocean => new(Color.FromRgb(0x0E, 0x6F, 0x80), Colors.White, Colors.White, Color.FromRgb(0xFF, 0xE9, 0x8A), 1.5, 8),
    Void => new(Colors.Black, Colors.White, Colors.White, Color.FromRgb(0x9B, 0xD3, 0xFF), 1, 0),
    _ => new(Colors.Black, Colors.White, Colors.White, Color.FromRgb(0x5F, 0xCD, 0xE4), 2, 0), // Dreamy: a dream block, square like its pixels
};
```

**`ToolChromeTests`:** every theme in `Themes.All` has a chrome whose `Fore` against `Back` reaches a WCAG contrast ratio of at least 4.5. Compute the relative luminance from sRGB (linearise: `c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)`, then `0.2126 R + 0.7152 G + 0.0722 B`). The ratio is `(L1 + 0.05) / (L2 + 0.05)`.

### 9.2 `Shell/WheelGeometry.cs` (new, pure)

```csharp
/// <summary>The tool wheel's sectors: which one a direction points at, and their shapes.</summary>
internal static class WheelGeometry
{
    public const int Count = 8;

    /// <summary>The middle of sector <paramref name="index"/>, in degrees clockwise from straight up.</summary>
    public static double MiddleOf(int index) => index * 360.0 / Count;

    /// <summary>The sector a point (relative to the centre, y down) points at; null inside the inner circle.</summary>
    public static int? SegmentAt(Vector fromCentre, double inner)
    {
        if (fromCentre.Length < inner)
            return null;
        double degrees = Math.Atan2(fromCentre.X, -fromCentre.Y) * 180 / Math.PI; // 0 up, 90 right
        double shifted = (degrees + 360 + 180.0 / Count) % 360;
        return (int)(shifted / (360.0 / Count)) % Count;
    }

    /// <summary>A point <paramref name="radius"/> from the centre, <paramref name="degrees"/> clockwise from up.</summary>
    public static Point Polar(Point centre, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Sin(radians), centre.Y - radius * Math.Cos(radians));
    }

    /// <summary>A ring sector's outline, with a small gap (degrees) between neighbours.</summary>
    public static Geometry Sector(int index, double inner, double outer, Point centre, double gap = 1.5)
    {
        double half = 180.0 / Count - gap / 2;
        double from = MiddleOf(index) - half, to = MiddleOf(index) + half;
        var figure = new PathFigure { StartPoint = Polar(centre, outer, from), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(centre, outer, to), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(centre, inner, to), true));
        figure.Segments.Add(new ArcSegment(Polar(centre, inner, from), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
```

**`WheelGeometryTests`** (inner radius 40):

| Point from the centre | Sector |
|---|---|
| (0, −80) | 0 |
| (60, −60) | 1 |
| (80, 0) | 2 |
| (60, 60) | 3 |
| (0, 80) | 4 |
| (−60, 60) | 5 |
| (−80, 0) | 6 |
| (−60, −60) | 7 |
| (10, 10) | null |

`Polar(c, 10, 90)` is `c + (10, 0)` (compare within 1e-9: cos 90° isn't exactly 0).

### 9.3 `Shell/ToolWheelWindow.cs` (new, WPF)

Follow `InkPaletteWindow` for the window setup: `WindowStyle.None`, `AllowsTransparency`, transparent background, `Topmost`, not in the taskbar, no resizing.

```csharp
/// <summary>
/// The tool wheel: eight tools in a ring round the mouse. The sector in the mouse's direction lights up and
/// a click picks it; Esc, a right-click, a click in the middle or elsewhere closes it.
/// </summary>
internal sealed class ToolWheelWindow : Window
{
    public const double Inner = 40;
    public const double Outer = 116;
    private const double Side = 300;

    /// <param name="centre">Screen DIP point the wheel is centred on (it is kept on screen).</param>
    /// <param name="current">The tool in use, marked on the wheel.</param>
    public ToolWheelWindow(Point centre, ToolChrome chrome, Tool current);

    /// <summary>A tool was picked; raised after the wheel has closed and <see cref="Gone"/> was raised.</summary>
    public event Action<Tool>? Picked;

    /// <summary>Raised once when it closes, picked or not.</summary>
    public event Action? Gone;

    /// <summary>Closes it without a pick; safe to call again.</summary>
    public void Dismiss();
}
```

**Building it**

- A `Canvas` of `Side × Side`, with `Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))`. That colour is nearly invisible but still takes the mouse everywhere in the window; a fully transparent pixel of a layered window lets clicks through.
- For `i` in 0–7, with `info = ToolCatalog.All[i]` and `middle = (Side / 2, Side / 2)`:
  - A `System.Windows.Shapes.Path` with `Data = WheelGeometry.Sector(i, Inner, Outer, middle)`, `Fill = Back` (alpha `0xF2`), `Stroke = Edge`, and `StrokeThickness = Math.Max(1, EdgeWidth)`.
  - The current tool gets `Stroke = Accent` and a thickness of +1.
  - The icon `ToolIcons.Shape(info.Icon, foreBrush)`, centred at `WheelGeometry.Polar(middle, (Inner + Outer) / 2 - 7, MiddleOf(i))` (left = x − 8, top = y − 8).
  - A label `TextBlock` with the name (stubs: the name plus "\nsoon"), `FontSize = 10.5`, `Width = 76`, `TextAlignment.Center`, centred 15 DIPs further out.
  - Stub sectors (path, icon and label) get `Opacity = 0.55`.
- A centre `Ellipse` of diameter `2 × (Inner − 4)` (Back fill, Edge stroke), and a centre `TextBlock` 70 wide: the highlighted tool's name and key ("Pen · P"), or "Tools" when none is highlighted.
- Highlight: the sector's `Fill` becomes `Accent` blended 35% over `Back`. Write a small helper `Blend(Color a, Color b, double t)`.

**Input**

- `canvas.MouseMove`: highlight `WheelGeometry.SegmentAt(e.GetPosition(canvas) - middle, Inner)`.
- `canvas.MouseLeftButtonUp`: a sector → `_picked = tool; Dismiss();`. No sector (centre) → `Dismiss()`.
- `MouseRightButtonUp` → `Dismiss()`.
- `KeyDown` → Esc: `Dismiss()`.
- `Deactivated` → `Dismiss()`.
- `Loaded`: keep it on screen (copy `InkPaletteWindow.KeepOnScreen`). If `SystemParameters.ClientAreaAnimation` is on, fade and scale the canvas in from 0.9 over 120 ms (`ScaleTransform` centred, `DoubleAnimation` on opacity and scale).
- `Dismiss()`: guard with a `_closing` flag, then `Close()`. Override `OnClosing` to set `_closing`, as `InkPaletteWindow` does.
- `Closed`:

  ```csharp
  Closed += (_, _) =>
  {
      Gone?.Invoke(); // focus goes back to the app first…
      if (_picked is { } tool)
          Dispatcher.BeginInvoke(() => Picked?.Invoke(tool)); // …then the tool may open an overlay that takes it
  };
  ```

### 9.4 `ShellController.Tools.cs`: hotkey and toggle

```csharp
private static readonly string[] WheelHotkeys = ["Ctrl+Alt+`", "Ctrl+Alt+W", "Win+Alt+W"];
private ToolWheelWindow? _wheel;
private string? _wheelHotkey;

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
        if (_hotkeys!.Register(modifiers, key, ToggleWheel) is null)
            continue;
        _wheelHotkey = HotkeyText.Format(modifiers, key);
        if (!string.Equals(text, wanted[0], StringComparison.OrdinalIgnoreCase))
            _tray?.Notify("Tool wheel", $"{wanted[0]} is taken by another app, so the tool wheel opens with {_wheelHotkey}.");
        return;
    }
    unavailable.Add("the tool wheel's hotkey");
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
    };
    wheel.Picked += tool => SelectTool(tool, ToolSource.Wheel);
    _wheel = wheel;
    UpdateHoverKeys();
    wheel.Show();
    wheel.Activate();
}
```

- In `Start()`, call `RegisterWheelHotkey(unavailable)` right after the four existing registrations. The tray already exists by then, so its notice can show.
- `UpdateHoverKeys()`: the first condition becomes `_overlay is null && _wheel is null && !spaces.IsTyping`.
- `ShowOverlay()`: add `_wheel?.Dismiss();` at the top.
- `Dispose()`: add `_wheel?.Dismiss();`.
- **Tray**:
  - `TrayActions` gains `Action Wheel` and `Func<string?> WheelHotkey`.
  - The first menu item is `Item("Tool wheel", null, actions.Wheel)`; set its `ShortcutKeyDisplayString = actions.WheelHotkey()` in `menu.Opening`.
  - `Wheel = ToggleWheel`.
- The welcome notice (no spaces yet) becomes: "Press {wheel hotkey} for tools, or Ctrl+Alt+S and draw any shape to open a space."

### 9.5 Manual checks

- **Ctrl+Alt+`** anywhere: the wheel opens round the mouse, in the theme's chrome. Moving towards a sector lights it up.
  - Pen opens the screen pen.
  - Text: the next click places a text box.
  - Space cube places a block.
  - Hand closes any screen tool.
  - A placeholder shows "coming soon".
  - Esc, a right-click, a click in the middle, a click outside, or the hotkey again closes it, and focus is back in the app.
- Press the hotkey while the screen pen is on: the pen ends and the wheel opens.
- Press it while a text box is open: the text is kept and the wheel opens.
- Switch themes: the wheel follows.
- Set `"ToolWheelHotkey": "Ctrl+Shift+Alt+T"` in settings.json and restart: the wheel opens on Ctrl+Shift+Alt+T.

---

## 10. Phase 6: the toolbar

### 10.1 `Native/Win32.cs`

```csharp
/// <summary>Whether a full-screen app, game or presentation is in front (QUNS_BUSY 2, QUNS_RUNNING_D3D_FULL_SCREEN 3, QUNS_PRESENTATION_MODE 4).</summary>
[DllImport("shell32.dll")]
public static extern int SHQueryUserNotificationState(out int state);
```

### 10.2 `Shell/ToolbarWindow.cs` (new, WPF)

```csharp
/// <summary>
/// The toolbar: a small square in the theme's chrome with three dots, always on top. A click unfolds the
/// tools, named, with their keys; a drag moves it. It never takes the keyboard from the app (or from a
/// screen-wide tool), and it hides while a full-screen app is in front.
/// </summary>
internal sealed class ToolbarWindow : Window
{
    public const double Square = 30;
    private const double MenuWidth = 184;
    private const double RowHeight = 28;
    private const double Gap = 4;

    /// <param name="square">Where its square goes (screen DIPs); null for the primary screen's right edge.</param>
    /// <param name="wheelHotkey">The wheel's hotkey now, shown at the top of the menu.</param>
    public ToolbarWindow(ToolChrome chrome, Tool current, Point? square, Func<string?> wheelHotkey);

    public event Action<Tool>? Picked;

    /// <summary>The square was dragged here (screen DIPs of its top-left).</summary>
    public event Action<Point>? Moved;

    public void ShowTool(Tool tool);        // marks the current tool's row

    public void ApplyChrome(ToolChrome chrome); // a new theme: rebuilds brushes and the dots

    /// <summary>Back to the very top (after a screen-wide overlay was shown over it).</summary>
    public void BringToFront();

    public void Collapse();
}
```

**Never taking focus.** This is essential: otherwise a click on it would make the screen overlays close themselves.

```csharp
ShowActivated = false;
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
```

Never call `DragMove()` (it activates the window) or `Activate()`. `BringToFront()` is `Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE)`.

**Layout.** `SizeToContent = WidthAndHeight`, with a `StackPanel` holding `_squareView` and `_menu`; `_menu` starts collapsed.

- **`_squareView`**:
  - A `Border` of `Square × Square`, with `Background = Back`, `BorderBrush = Edge`, `BorderThickness = EdgeWidth` and `CornerRadius = Corner`.
  - Its child is three 4×4 dots in `Fore`, 3 DIPs apart and centred: `Rectangle`s when `Corner == 0` (pixel style), else `Ellipse`s.
  - `Cursor = Cursors.Hand`.
- **`_menu`**:
  - A `Border` with the same chrome, `Width = MenuWidth`, and `Padding = 4`.
  - A header line "Tools · {wheelHotkey()} opens the wheel" (font 10.5, opacity 0.7).
  - Then one row per `ToolCatalog.All` entry. Each row is a `Border` with `Height = RowHeight`, `CornerRadius = Math.Min(Corner, 4)` and `Padding = 8,0`, holding a `Grid` with three columns:
    - the icon (`ToolIcons.Shape`), 16 wide plus a 10 margin
    - the name (font 12.5; SemiBold for the current tool)
    - the hint (the key letter, or "soon"; opacity 0.6)
  - Hovered row: `Background` = Accent at alpha `0x30`. Current tool's row: Accent at alpha `0x55`. Stub rows: `Opacity = 0.55`.
  - Clicks act on release, as in `InkPalette.Clickable` (handle `MouseLeftButtonDown` too).
  - `AutomationProperties.SetName(row, info.Name)`.

**Where it unfolds.** Keep `_square`, the square's screen top-left in DIPs.

- **Expand:**
  - `right = _square.X + MenuWidth <= VirtualScreenRight`; `down = _square.Y + Square + Gap + menuHeight <= VirtualScreenBottom`, where `menuHeight = 8 * RowHeight + header + padding`. Measure the menu, or use `_menu.DesiredSize` after `Measure`.
  - Order the children `[square, menu]` if `down`, else `[menu, square]`.
  - `HorizontalAlignment` of both is `Left` if `right`, else `Right`.
  - Set `Left = right ? _square.X : _square.X + Square - MenuWidth` and `Top = down ? _square.Y : _square.Y - menuHeight - Gap`.
  - Set the menu's `Margin` to `Gap` on the side facing the square.
  - Show the menu. If `ClientAreaAnimation` is on, animate its `ScaleY` from 0 to 1 over 120 ms, with the origin on the square's side.
  - Start the outside-click watch.
- **Collapse:** hide the menu, set `Left/Top = _square`, and stop the watch.

**Outside-click watch** (only while expanded): a 50 ms `DispatcherTimer`. If `Win32.IsMouseButtonDown()` and the cursor (`Win32.GetCursorPos` / `Dpi.Scale`) is outside `new Rect(Left, Top, ActualWidth, ActualHeight)`, call `Collapse()`. The click still reaches the app.

**Click or drag on the square:**

- `MouseLeftButtonDown`: remember the cursor's screen position, `CaptureMouse()` on the square, and `e.Handled = true`.
- `MouseMove` while captured: once it has moved 4 DIPs, collapse if expanded and start dragging. Move `_square` by the delta, clamp it to the virtual screen, and set `Left/Top`.
- `MouseLeftButtonUp`: `ReleaseMouseCapture()`. If it was a drag, `Moved?.Invoke(_square)`; otherwise toggle the menu.

**Full-screen apps:** a 1 s `DispatcherTimer`:

```csharp
IntPtr front = Win32.GetForegroundWindow();
bool fullScreen = !Foreground.IsOwn(front) // our own full-screen overlays don't count
    && Win32.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;
Visibility = fullScreen ? Visibility.Hidden : Visibility.Visible;
```

**Default place:** `new Point(SystemParameters.PrimaryScreenWidth - Square - 12, Math.Round(SystemParameters.PrimaryScreenHeight * 0.35))`. Use the same if a saved point isn't on the virtual screen.

### 10.3 `ShellController.Tools.cs`

```csharp
private ToolbarWindow? _toolbar;

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
```

- `Start()`:
  - After the tray is made, `if (_settings.ShowToolbar) OpenToolbar();`.
  - `spaces.ToolChanged += tool => _toolbar?.ShowTool(tool);`.
- `SetTheme()`: `_toolbar?.ApplyChrome(Themes.ChromeOf(_settings.Theme));`.
- `ShowOverlay()`: after `overlay.Show();`, add `_toolbar?.BringToFront(); // above the overlay, so a tool can still be picked`.
- `Dispose()`: `_toolbar?.Close();`.
- **Tray:** `TrayActions` gains `Func<bool> IsToolbarShown` and `Action<bool> ShowToolbar`. Add a checkable "Show toolbar" item next to "Start with Windows", and set `Checked` in `menu.Opening`.

### 10.4 Manual checks

- The square shows at the right edge in the theme's chrome; Dreamy has a white rim and square dots.
- Clicking it unfolds the menu leftwards (it's at the right edge), with Hand highlighted. Clicking Pen starts the screen pen, and the toolbar stays clickable above it. Clicking Hand ends it.
- Clicking outside folds the menu.
- Dragging the square moves it, and after a restart it's where it was left.
- While typing in Notepad, clicking the toolbar doesn't take the caret away.
- F11 in a browser hides the toolbar; leaving full screen shows it again.
- Tray → Show toolbar off and on.
- Switch themes: the toolbar follows.

---

## 11. Phase 7: docs and final pass

1. **README.md, Use table.**
   - Add rows for:
     - **Tool wheel** (hotkey and fallbacks, how to pick, placeholders)
     - **Toolbar**
     - **Tools in a space** (H/P/T/C, cursor)
     - **Hand** (gestures table 3.3, condensed)
     - **Screen notes** (wheel, toolbar or tray → Pen/Text; where they go; red default; follow apps; Ctrl+Z; clear)
   - Rewrite **Paint on a space** (needs the Pen: P, wheel or toolbar), **Move text** (the Hand; files too), **Type on a space** (T picks Text; click to type) and **Edit text** (double-click with the Hand, or E).
   - **Hover keys** row: H, P, T, C, E, Ctrl+Z, Ctrl+Y, Ctrl+G, Ctrl+Shift+G.
   - **Everything else** row: the new tray items.
2. **README.md, How it works tree.** Add the new files with one line each, in the style of the existing ones.
3. **README.md, Known limits.**
   - H, P, T, C and E are plain letters (the same 3-second rule as before).
   - Screen notes take clicks on their ink.
   - Grouping isn't undone by Ctrl+Z.
   - Screen notes are only undone while drawing them, or with the mouse on their ink.
   - The four placeholder tools do nothing yet.
4. Run everything:
   - `dotnet build`
   - `dotnet test`
   - `dotnet run -c Release --project tests/InfiniteSpace.Perf -- --themes Dreamy,Glory --actions paint,notes,screenink,keys,draw,block,arrows`

   No `FAIL` lines. Paint and screen-pen medians should be in the README's ranges (about 0.3–0.5 ms).
5. Run the manual checks of every phase once more, in Dreamy and in Sunday (a light theme).

---

## 12. Pitfalls checklist (read before each phase)

1. **Order in `SelectTool`:** show the overlay first, *then* `spaces.SetTool(tool)`. Showing it closes the previous overlay, whose `Closed` sets the Hand. Only overlays shown from `SelectTool` pass `endsInHand: true`; Ctrl+Alt+S/B/M and the erase brush never change the tool.
2. **Close the screen overlay before showing the wheel** (`ToggleWheel` → `CloseOverlay()`). Otherwise the overlay's deferred "deactivated → dismiss" hands focus back *after* the wheel took it.
3. **The wheel's `Picked` runs after `Gone`, via `Dispatcher.BeginInvoke`.** Focus goes back to the app first, then a new overlay takes it.
4. **Overlay key handlers never close the overlay synchronously:** use `Later(...)`.
5. **The toolbar must never activate:** `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`, `ShowActivated = false`, no `DragMove`, no `Activate`. Otherwise every click on it closes the screen overlays.
6. **The full-screen check ignores our own windows.** Otherwise the toolbar hides as soon as the screen pen opens.
7. **Sheets are not spaces.** Every place listed in 8.5 must filter them out. When in doubt, grep for `_spaces.` in `SpaceManager*.cs` and decide for each one.
8. **A sheet's click region is rebuilt when its ink changes.** An empty region is correct (fully click-through).
9. **Stroke `Previous` chains are transformed** in `SpaceRecord.TransformInk` (7.1), and **`Group` is copied** into every new version (`MovedBy`, `MovedTo`, `Keep`).
10. **Batch undo treats `Order == 0` one at a time** (legacy ink all has 0).
11. **`Space.Redo` is now `Stack<IInk[]>`:** fix `BakeMapping`'s loop.
12. **JSON compatibility:** every new property is omitted at its default. Old `spaces.json` and `settings.json` must load unchanged; tests cover this.
13. **Perf scenarios** need the Pen to paint (`Paint`) and expect files to move with the Hand (`DragNotes`).
14. **`System.Windows.Shapes.Path` vs `System.IO.Path`:** use an alias where both are imported.
15. **Don't break the click-through work** in `SpaceWindow.Float` and `EndFloat`. Sheets go through the same `RestoreRegion` path, via `_region`.

## 13. Not in this plan

These are for later, and were not requested now:

- the placeholder tools' real behaviour (project meter, text wrap, eraser, list)
- a Delete key for the selection
- undoing group changes
- bring to front / send to back
- selecting across several spaces
- moving screen notes between apps
- custom (non-system) cursors
- per-monitor DPI
- an optional dashed outline of existing spaces while drawing a new one
