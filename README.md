# Infinite Space

A Windows background app for drawing "spaces" over your apps or on the desktop. Press a hotkey and draw any freehand shape, and it becomes a window into an infinite star field, styled after Celeste's dream blocks, or into one of seven other themes. You can paint and type on a space, open text and Markdown files on it, and it stays, across restarts, until you erase it.

## Use

| Action | How |
|---|---|
| Draw a space | **Ctrl+Alt+S**, then draw one freehand stroke; releasing the mouse closes the shape. The screen doesn't change while you draw. |
| Spawn a block | **Ctrl+Alt+B** spawns a rectangular dream block under the mouse. It's a live space from the start (stars, wobbling rim) and follows the mouse until you click to drop it. Arrow keys push that side out; **Shift**+arrow pulls it in, **Ctrl**+arrow moves in fine steps, and the mouse wheel scales the block; neither stretches it more than about 40 DIPs past the edge of the screen. Once dropped, drag an edge or corner to resize, drag the body to move it, or click elsewhere to move it there. **Enter** or a double-click creates the space; **Esc** or right-click cancels. Like drawn spaces, it sits on the app in front and merges with spaces it crosses. In the Dreamy theme, resizing the block makes it wobble like jelly (see below). |
| Grow a space | Draw a new space that crosses it. Crossing spaces merge into one, with a single rim around the combined shape, keeping all their paint and text. |
| Paint on a space | Drag inside it. Strokes take the pen's colour and width (see below). A click only splashes; it doesn't open a text box. A drag that starts on text moves the text instead (see below). |
| Move text | Drag any text on a space (the cursor turns into a four-way arrow over it); it follows the mouse and stays where you let go. **Ctrl+Z** puts it back. An opened file moves only while it is edited: press **E** on it, then drag its text (its scroll bar only scrolls). |
| Drop text from other apps | Select text in a browser, editor or any app that lets you drag it, and drop it on a space. It becomes a note in the pen's colour, size and style, its first line at the drop point. It is fitted to the space: its lines wrap at the space's width (no wider than reads well), it moves left or up if it would run off the space, and if a word or the whole text still doesn't fit, the type is made smaller until it does (down to 10). The text stays where it came from. |
| Type on a space | Point at a space and press **T**: a text box opens with its first line at the mouse. Type, then press **Enter** to commit. **Shift+Enter** starts a new line and **Esc** cancels. Clicking elsewhere also commits. While typing:<br>• Drag the text itself to move it, or the **✥** grip. A click still places the caret, a double-click selects a word, and **Shift**+drag selects. **Alt**+arrows nudge it.<br>• Drag the **↔** arrow to set where the lines wrap. Double-click it to unwrap them.<br>• Drag the **⤡** arrow to resize the text. **Alt+Shift**+←/→ narrow or widen the lines, and **Alt+Shift**+↑/↓ step the size.<br>• The toolbar sets the text's colour and size, and makes it **B**old or ***I***talic (**Ctrl+B**, **Ctrl+I**), all of it at once.<br>If you start painting, or erase or draw a space, while the text box is open, what you typed is kept and the box fades away within half a second. |
| Edit text | Point at text on a space and press **E**. It opens in the same text box, where you can change, move and reshape it as above. Delete all of its text to remove it. **Ctrl+Z** turns an edit back. |
| Open a text or Markdown file | Drag `.txt` or `.md` files from Explorer and drop them on a space, or right-click a space → **Open a file…**. Each file lands where you dropped it (several side by side), with a splash, fading in; in Dreamy the space wobbles a little as it takes it. It shows as a note with the file's name at the top, on a soft backing:<br>• Markdown is styled in the theme's colours: headings, **bold**, *italic*, `code`, ~~strikethrough~~, links, bullet, numbered and task lists, quotes, code blocks and rules. Raw HTML stays text, and images show only their description (nothing is ever fetched).<br>• Plain text shows as it is.<br>• **Scroll** it with the mouse wheel; edges with more beyond them fade out.<br>• **E** opens its text for editing, and it moves and reshapes like any note there; outside edit mode, dragging on it paints. **Ctrl+Z** takes it off again.<br>• **Double-click its name** to open the original file in its own app. **Ctrl+click a link** to open it in the browser (only `http` and `https` addresses open).<br>What shows is a copy of the file's text, so it stays if the file is moved or deleted. Files up to 1 MB open; the encoding is read from the file's byte-order mark, else UTF-8, else the system's code page. |
| Colour and size | Right-click a space (or tray menu → **Ink colour and size…**). Pick a colour, the paint width, the text size, and whether text is bold or italic. The dashed first swatch is the theme's own ink, which follows the theme. Picks apply to what you paint and type next, and are saved in `settings.json`. Colours picked on a text box's toolbar become the pen's too. Ink keeps the colour and size it was made with. The palette gets out of your way: as soon as you start painting, erase a space, draw one, or click elsewhere, it fades away within half a second, and clicks go through it meanwhile. **Esc** or **Enter** closes it at once. |
| Hover keys | **T**, **E**, **Ctrl+Z** and **Ctrl+Y** belong to a space only while the mouse is on it (**E** only while it's on text) and no text box is open. The rest of the time they go to the focused app as usual. **T** and **E** also need the mouse to have moved within the last 3 seconds. Point at the space and press the key, and it works. If you're typing into the app underneath with the mouse resting on a space, your letters are left alone. |
| Undo / redo on a space | With the mouse over a space, **Ctrl+Z** takes back its newest stroke, note or edit, and **Ctrl+Y** brings it back. Only while the mouse is over the space, the keys go to it instead of the focused app. Undo also reaches ink from earlier sessions; redo lasts until the app closes or you add new ink. |
| Move and reshape a space | Point at a space and press **Ctrl+Alt+M**. It lifts, shows reshaping arrows on its sides and corners, and follows the mouse. A click sets it down, and then you can:<br>• Drag an arrow to stretch it that way, or drag its body to move it.<br>• Click elsewhere to move it there.<br>At any time, arrow keys push a side out (**Shift** pulls it in, **Ctrl** for fine steps), **Alt**+arrows nudge it and the wheel scales it, as for a block. Stretching stops about 40 DIPs past the edge of the screen (a space already past it can still be pulled in or moved). Paint and text go along when the space moves, but stay where they are, at their size, while you reshape it. Paint never moves for a reshape: an edge pulled in over it hides it rather than pushing it. Text is pushed along by an edge that reaches it. Every new shape is drawn properly as it changes, never stretched, so the rim, the scenery and the text stay crisp. In the **Dreamy** theme, a resize makes the space wobble like jelly:<br>• The edges you moved jump to their new place, recoil a little, then spring back out past it and settle, each swing smaller than the last.<br>• While it wobbles it keeps its volume: squashed, it gets a little taller and bulges; stretched, it gets shorter and thins in the middle.<br>• It's all over within half a second. Moves don't wobble, the arrows show where it will come to rest, and the paint and text stay still.<br>• Other themes don't wobble, and neither does Dreamy while Windows' **Show animations in Windows** setting is off. A **double-click** or **Enter** drops it on the app window under the mouse, or on the desktop, and it merges with spaces it lands on there. **Esc**, a right-click or switching away puts it back as it was. If the mouse wasn't on a space, click one to pick it up. |
| Remove a space | **Ctrl+Alt+E** removes the whole space under the mouse in one press. If the mouse isn't over a space, it removes the newest one. |
| Erase part of a space | Tray menu → **Erase with brush…**, then drag over spaces. Press **Esc** or right-click when done. |
| Cancel drawing | **Esc**, right-click, or **Ctrl+Alt+S** again |
| Everything else | Tray icon menu: draw or spawn a block (at desktop level), move a space, remove newest, ink colour and size, erase with brush, erase all (including spaces waiting for their apps), theme, start with Windows, open data folder, exit |
| Theme | Tray menu → **Theme**. Switching re-dresses every space immediately, and the choice is saved in `settings.json`.<br>**Dreamy**: a Celeste-style dream block with a black void, twinkling pixel stars, a wobbling white rim, and white paint and text.<br>**Claudish**: flat clay orange (#DB7859) with a bold, hand-drawn black ink outline that swells and thins like a brush and "boils" about 10 times a second; paint and text are black, and clicks splash ink.<br>**Glory**: a dark golden hall lit from above, with shafts of light that sway and breathe over curtain folds, golden motes and soft out-of-focus lights drifting upwards, edges falling into shadow, and a glowing gilt outline. Clicks flash and scatter motes.<br>**Burn**: a deep red dark lit by an unseen fire below. Embers stream upwards as glowing streaks, flicker and die out, and the firelight wavers. The edges sink into shadow under an ember-hot outline, and clicks throw a shower of sparks.<br>**Sunday**: a hazy late-afternoon sky, rose at the top warming to pale gold, with a low sun glowing through and soft wisps of haze drifting across. Paint and text are deep plum, and clicks bloom with light.<br>**Parallax**: a textured relief in greens and grey: a sage band of folded fabric across a grey plaster wall, with rough forest green in the lower corner. Each layer casts a soft shadow on the one behind, and the layers shift against each other as the cursor moves. Paint and text are dark green.<br>**Ocean**: aqua water seen from above, with rows of smooth swells rolling across at their own pace and rising and falling, sheen on the crests, shadow in the troughs, and glints of sun. Clicks send out ripples.<br>**Void**: pure black inside a crisp white outline. Nothing moves; a click sends out one thin white ring. |

**Where a space sits.** A space drawn with Ctrl+Alt+S or Ctrl+Alt+B sits on the app that was in front when you pressed the hotkey. It stays directly above that app's window:
- It comes to the front with the window, is covered along with it, and hides while the window is minimised or on another virtual desktop.
- It **moves and resizes with the window**. Dragging the window carries the space along on every frame. Resizing or maximising stretches the space in proportion; paint and text move with it but keep their size and shape.
- When the app **closes the window**, the space is kept, out of sight. When the app opens a window again, the space comes back on it, in the same place relative to the window, even if that window opens elsewhere or at another size. A window with the same title (the same document) is preferred; otherwise any window of the same program gets it. Store apps, which all run under one host program, need the same title.
- The same happens across restarts of Infinite Space. Spaces go back onto their apps' open windows at once, and the rest wait for their app.

Spaces drawn from the tray menu, and spaces moved onto the desktop with Ctrl+Alt+M, sit just above the wallpaper. So do spaces from versions that didn't remember apps.

In Dreamy, every space shows the same star field, which drifts slowly and shifts with parallax as the cursor moves. Glory's motes, Burn's embers, Sunday's haze and Ocean's swells are likewise one scene seen through all spaces. The light, sky and layers are fitted to each space's own shape.

## Run

```bash
dotnet run --project src/InfiniteSpace
```

```bash
dotnet test
```

This needs Windows 10 or 11 and the .NET 10 SDK. Only one instance runs at a time. The rendering tests draw every theme offscreen on the GPU (or on WARP, the software rasteriser, when there is none) and check the pixels. Some also read back what the desktop compositor shows of a window kept off screen.

```bash
dotnet run -c Release --project tests/InfiniteSpace.Perf -- --themes Dreamy,Glory --actions paint,reshape
```

The performance project times every interactive action frame by frame, on the real GPU in windows far off screen: the ambient animation, painting, moving, reshaping, the jelly, following an app, drawing and erasing on the overlays, placing a block, dragging the arrows, opening and scrolling files, and recovering from a lost GPU. It prints each action's median, 99th-percentile and slowest frame, allocations and collections, and how many frames would miss a 360 Hz display. `--capture folder` also saves pictures of the windows as the compositor shows them, `--dump folder` writes every frame's time, and `--hz 60` paces frames at another rate. For the running app, `dotnet-counters monitor -n InfiniteSpace --counters InfiniteSpace-Frames` shows the frame time and rate, and `"ShowFrameStats": true` in `settings.json` shows a small frame-time graph in the bottom-right corner.

Spaces, including their paint and text, are saved to `%AppData%\InfiniteSpace\spaces.json`, and settings to `settings.json` in the same folder. The text of files opened on spaces is kept in the `notes` folder next to them. To use another folder, pass `--InfiniteSpace:DataDirectory=D:\somewhere` or set the environment variable `InfiniteSpace__DataDirectory`.

**Start with Windows is on by default.** Each launch registers the running exe in the per-user Run key. If you turn it off in the tray menu, the choice is saved and respected.

## How it works

```
src/InfiniteSpace/
  Program.cs              Generic Host (DI, config, logging) + WPF app, single instance
  Shell/                  tray icon, hotkeys, hover keys (T, E, Ctrl+Z/Y), ink palette, text box arrows,
                          autostart, settings, frame-time graph
    GpuOverlay.cs         base of the overlays: a DirectComposition window over every monitor, input from its messages
    OverlayParts.cs       what overlays show: pictures drawn once, sprites that move, layers redrawn a rectangle at a time
    CanvasOverlay.cs      Ctrl+Alt+S drawing and the erase brush
    BlockOverlay.cs       Ctrl+Alt+B block placement
    MoveOverlay.cs        Ctrl+Alt+M moving and reshaping
  Spaces/
    SpaceManager.cs       spaces: create, remove, erase, text, move; follow apps, keep and bring back; persist
    SpaceManager.Files.cs opening text and Markdown files on spaces, scrolling them, their links
    HostMatcher.cs        which reopened window gets a closed window's spaces
    SpaceWindow.cs        one window per space: region-shaped, paint input, follows its app
    SpaceWindow.Files.cs  file drops, the wheel, scrolling, a new file's arrival
    TextEntry.cs          the text box for typing and editing notes: toolbar, move grip, reshaping arrows
    TextFiles.cs          reading .txt and .md files: size limit, encodings, line breaks
    InkPen.cs             colour and size of new paint and text; the offered colours and sizes
    SpaceGeometry.cs      stroke -> outline, eraser brush, save format
    SpaceStore.cs         JSON file, written atomically; opened files' text in notes\
  Rendering/
    Themes.cs             theme list, per-theme ink and accent colours, Claudish palette
    ISpaceLook.cs         what a theme draws for one space
    ISpaceCanvas.cs       where it draws: backdrop, moving picture, frame (on screen: SpaceSurface)
    DreamRaster.cs        Dreamy: pixel-art renderer (scanline-filled void, stars, 1-px wobbling rim)
    InkyLook.cs           Claudish: Direct2D vectors (clay fill, variable-width ink ribbon, line boil)
    SceneLook.cs          base of the painted themes: stills painted once per shape, sprites, particle fields
    GloryLook.cs … VoidLook.cs   the six painted themes
    InkLayer.cs           paint + text + opened files on the GPU at device resolution, clipped to the space
    NoteText.cs           how notes are laid out (DirectWrite), and measured
    NoteParagraphs.cs     an opened file's text as paragraphs (Markdown through Markdig)
    NoteDocument.cs       an opened file laid out: its name, then its paragraphs, styled
    SpaceSurface.cs       DirectComposition visuals: backdrop, art (GPU-scaled or vectors), frame, ink
    GpuCompositor.cs      shared Direct3D 11 + Direct2D + DirectComposition devices, one commit per frame;
                          made anew if the GPU is lost
    FrameClock.cs         ticks at the display's refresh (compositor clock, or the cursor's display's vblank)
    Universe.cs           frame scheduling, cursor parallax, shared star field, recovery from a lost GPU
    FrameEvents.cs        per-frame timing for dotnet-counters, PerfView and WPA, and for the graph
    DreamStyle.cs         palette, layers, wobble
  Native/WindowEvents.cs  hears app activation / minimise / restore / show as it happens
  Native/AppWindows.cs    other apps' windows: rectangle, program, class, title; finding them again
tests/
  InfiniteSpace.Tests/    unit and rendering tests
  InfiniteSpace.Perf/     every interactive action timed frame by frame (see Run)
```

- **Shapes are exactly what you draw.** The stroke is kept point for point, with no simplification or smoothing. Erasing uses real geometry subtraction, so a space can be split into pieces or have holes punched in it.
- **Dreamy is pixel art, on the GPU.** One art pixel is 2 DIPs. Each frame, only the small art-resolution picture is uploaded (for example 224×188 pixels); DirectComposition scales it up with nearest-neighbour sampling.
- **Paint and text are drawn on the GPU** with Direct2D and DirectWrite, at full device resolution, into a GPU bitmap of their own. A paint stroke draws only its newest piece, and only the rectangle it changed is copied to the screen; the space's outline is applied as a mask during that copy. Everything is redrawn only when the ink or the shape changes (each stroke's path and each note's layout are kept from one redraw to the next). Notes are laid out by DirectWrite exactly as the WPF text box that types them breaks its lines (a test checks every line break at many widths and sizes).
- **The other themes are Direct2D vectors at device resolution.** A space has up to three layers: a still backdrop (sky, wall, gradients), the moving picture, and a still frame (inner shadow, glow, outline). The stills are painted once per shape, with Direct2D's blur and shadow effects. Each frame redraws only the moving picture and cuts it to the shape with a mask. That picture is made of sprites (motes, embers, haze, glints) painted once and placed, swell strips repeated by a wrapping bitmap brush, and Parallax's pre-painted layers. DirectComposition stacks the layers. Sunday moves slowly and redraws at 30 fps, Parallax redraws only while the cursor moves, and Void only while a click ring plays.
- **Frame rate follows the display, up to 360 Hz and beyond.** On Windows 11, frames are timed by the desktop compositor's clock, which follows the fastest display and variable refresh rates; on Windows 10, by the vertical refresh of the display the mouse is on. Anything the user moves renders at the full refresh rate (144 or 360 fps on such displays): a block being placed, a space being reshaped, painting, drawing and erasing, scrolling a file, click bursts. That frame then only draws what moved: the ambient animation (wobble, twinkle, drift, and the cursor's parallax) moves in whole art pixels and runs at 60 fps whatever else is going on. Frames that come out identical are skipped. With no spaces, the app is idle.
- **Overlays are on the GPU too.** Drawing (Ctrl+Alt+S), the erase brush, placing a block and moving a space show on DirectComposition windows, like the spaces, and go out in the same compositor commit as the spaces they move: nothing is drawn in software. A stroke redraws only the rectangle its newest pieces cover; the shape's fill under it follows at 60 fps. The brush circle, the handles, the arrows and the dashed frame are pictures drawn once that only move (the frame's sides are long dashed lines cut to length).
- **Surfaces keep room to spare.** Every surface and bitmap redrawn at each new shape is made bigger than needed (in 256-pixel steps, half as big again as it grows) and shown only as far as it is used, so a space reshaped or wobbling every frame keeps drawing into the same ones. Painted themes keep their effects, masks and gradients from shape to shape. While a shape changes under the hand, Parallax stretches the bands it has instead of painting them; the shape is drawn properly as soon as it rests or holds still for 150 ms.
- **A lost GPU is recovered.** A driver update, a GPU reset or an external GPU unplugged makes every device useless. The next frame notices, makes new devices on whatever GPU there is, and every space draws itself again from its record; an open overlay closes. Without a usable GPU (WARP, remote desktop), interactive frames are capped at 60 a second and the jelly is off.
- **Opened files** are notes with a file attached: Markdown is parsed with Markdig into paragraphs with styled runs, each laid out once with DirectWrite; only the paragraphs in view are drawn, clipped to the note and faded at an edge with more beyond. Their text is saved in `notes\` next to `spaces.json`, one file per version of a note, written once: `spaces.json` is written out after every change (every paint stroke), and only names them. Texts no note or earlier version needs are removed on saving.
- **Clicks.** Space windows are shaped with a window region (the outline plus about 10 DIPs for the rim), so clicks anywhere else reach whatever is underneath. A floating space, and the frame-time graph, let every click through: Windows skips a window in hit testing only if it is both layered and transparent (`WS_EX_TRANSPARENT` alone does nothing). WPF takes the layered style off its own windows, and does it again each time one is resized, so a subclass in front of WPF's window procedure puts the style back (`Win32.SetClickThrough`). Layered at full opacity, the window still shows its DirectComposition content as before.
- **No blinks.** When an app is activated, minimised or restored, spaces re-stack straight away through a window-event hook, not on the next frame. After one of our popups hands focus back to an app, its spaces are re-stacked immediately. When spaces merge, or a block is created from its preview, the new window is on screen before the old ones leave.
- **Layers.** Each app window is its own layer, and the desktop is another. Spaces only merge with spaces on the same layer.
- **Following apps.** A space on an app is saved in coordinates relative to that window's rectangle at the time, with the app's program, window class and title. On every display refresh, whether or not a frame is drawn, the space checks its window's rectangle, which costs a few system calls.
  - **While the app moves or is resized, the space doesn't move or redraw its own window.** Moving a window with a click-through shape costs several milliseconds a time. Instead, the space "floats": its window spreads over the whole screen, see-through and click-through, and the desktop compositor places the picture where the app is. For a move that is a shift. For a live resize it is a GPU stretch of what was already drawn.
  - **Dreamy's jelly wobble** (`Rendering/Jelly.cs`) is a damped spring on each edge of the bounds: about five swings a second, each a third of the one before, stopped at half a second. A resize kicks the moved edges backwards; resizes smaller than an art pixel or two add up first, so a block snapping to the pixel grid as it follows the mouse doesn't jitter. Each frame the outline is mapped point by point, with long sides split so they can bend, and drawn properly like any other new shape. Paint and text are placed on their own and stay still.
  - **A space lifted with Ctrl+Alt+M floats the same way until it is dropped, but is never stretched.** A new shape is drawn properly on the next display refresh (however many changes came in meanwhile), into the same floating window. Only the picture changes, in one compositor commit, so nothing jumps or flashes. Its click-through shape is made once, when it lands. Whenever a window does move (spreading out to float, or landing back in its own window), the picture's new placement is committed straight away with it.
  - **About 150 ms after the app keeps still, the space settles** back into its own window with its click-through shape. After a resize, it is also drawn properly at the new size.
  - A one-off change of size (maximise, restore, snap) is drawn properly at once.
  - The mapping always starts from the saved coordinates, so resizing back and forth never wears the shape down. Paint and text added meanwhile are mapped back into the saved coordinates. A window that closes has its spaces kept in the file. They come back when a matching window is shown: the app is checked on window-show events, and every 2 seconds as a fallback.
- **Cost.** Measured with the performance project on the development laptop (Intel Core Ultra 7 256V with Arc 140V graphics, 200% scaling, 60 Hz panel, frames started as on a 360 Hz display). These are medians per frame, as ranges because the laptop varies from run to run. The budget for a 360 Hz display is 1.7 ms of CPU per frame, and for 144 Hz it is 4.1 ms.
  - Painting a stroke takes 0.3–0.5 ms a frame (it was 2–2.8 ms).
  - Drawing a space takes 0.25–0.45 ms; it used to keep about one CPU core busy, drawn in software. The erase brush takes 0.55–0.6 ms, and placing a block 0.3–1.1 ms.
  - Moving a space, or following an app being dragged, takes under 0.2 ms.
  - Reshaping a 480×320 space covered in paint and text takes 0.7–2.4 ms depending on the theme (it was 17–42 ms), and Dreamy's jelly 0.6–1.4 ms (it was 15 ms). The first few frames of a reshape are slower, while the space's surfaces grow.
  - Scrolling an opened file takes 0.3–1.2 ms, a 1 MB file included. Opening files takes a moment once: 0.1–0.7 s for two files, 0.5–1.5 s for 1 MB of Markdown.
  - The ambient animation takes 0.3–0.5 ms a frame per space in the moving themes, 60 times a second, and nothing for Void.
  - Painting and drawing allocate under 1 KB a frame. Reshaping allocates 5–50 KB a frame (it was 1.4 MB), so garbage collections are rare.
  - A lost GPU is recovered in 50–130 ms.
- **It's system-DPI aware** (see `app.manifest`), so everything uses one DIP coordinate space across monitors.

## Known limits (MVP)

- When an app is resized, paint keeps its size and its place from the app window's top-left, as the app's own content does; text keeps its size. Both can overflow a space that shrinks.
- Spaces kept for an app that is never opened again stay saved, out of sight, until "Erase all spaces". An app whose program can't be read (some protected system processes) can't be told apart, so its spaces go to the desktop when its window closes.
- Within about 10 DIPs of a space's rim, clicks go to the space rather than to the app underneath.
- While a space follows a moving or resizing app, and for about 150 ms after, it is click-through: you can't paint on it until it settles. During a live resize its paint and outline are briefly stretched, until it is drawn at the new size.
- Text uses the system UI font. Undo works one item at a time.
- **T** and **E** are plain letters. If you move the mouse onto a space and start typing into the app underneath within 3 seconds, those first letters go to the space. After the mouse has rested for 3 seconds they go to the app. Once the mouse has rested, move it a little before pressing **T** or **E** for the space.
- Win+D ("show desktop") hides desktop-level spaces until other windows are restored.
- On monitors whose DPI differs from the primary one, Windows bitmap-scales the spaces.
- Scrolling an opened file with the wheel needs Windows' **Scroll inactive windows when I hover over them** (on by default), since spaces never take the keyboard.
- An opened file is a copy of its text when it was opened: later changes to the file don't show. Markdown tables show as their raw text, and images only as their description.
- The first frames of a reshape can take a few milliseconds while the space's surfaces grow to the new size.
