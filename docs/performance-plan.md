# Plan: every action at the display's refresh rate (144–360 Hz), and text files on spaces

Phases 0–7 are performance work. Phase 8 is a new feature (`.txt` and `.md` files on spaces), which builds on Phase 2.

## Status (30 September 2026)

Measured with `tests/InfiniteSpace.Perf` on the development laptop: Intel Core Ultra 7 256V with Arc 140V graphics, 200% scaling, a 60 Hz panel, and frames started as on a 360 Hz display. The figures are medians per frame (p50), given as ranges across themes and runs, because this laptop varies by 20–50% from run to run.

| Action | Before | Now (p50) | 360 Hz budget (1.7 ms) |
|---|---|---|---|
| Painting a stroke | 2–2.8 ms | 0.3–0.5 ms | met |
| Drawing a space (Ctrl+Alt+S) | ~1 core in WPF software | 0.25–0.45 ms | met |
| Erase brush | ~1 core | 0.55–0.6 ms | met |
| Placing a block, dragging handles | ~1 core | 0.3–1.1 ms | met |
| Moving a space, following an app | 0.1–2 ms | 0.05–0.2 ms | met |
| Reshaping, paint and text on the space | 12–42 ms, 1.4 MB/frame | 0.7–2.4 ms, 5–50 KB/frame | met by most themes; Ocean, Burn and Claudish are close |
| Dreamy jelly | 15 ms | 0.6–1.4 ms | met |
| Scrolling a file (up to 1 MB) | – | 0.3–1.2 ms | met |
| Ambient animation, per space, 60 fps | 0.7–1.6 ms | 0.3–0.5 ms | – (ambient) |

**Tails.** Steady frames stay under budget. The slowest 1% (p99) are still 3–25 ms, and they come from the first 20–70 frames of an action:

- the space's surfaces and bitmaps grow to a new size (new DirectComposition surfaces, their first upload);
- in development builds, the JIT compiles code on first use.

The published app is compiled ahead of time (ReadyToRun). That makes the first frames cheaper, but not the surfaces growing.

| Phase | State |
|---|---|
| 0 Measure | **Done.** The perf project covers every action, both frame clocks and GPU recovery. It reports p50/p99/max, allocations, collections and frames missed at 360 Hz; it can save pictures of the windows as the compositor shows them (`PrintWindow` with `PW_RENDERFULLCONTENT`) and dump every frame's time. There is an `InfiniteSpace-Frames` EventSource, and `"ShowFrameStats": true` shows a graph. |
| 1 GPU overlays | **Done.** All four overlays are DirectComposition windows (`Shell/GpuOverlay.cs`), drawn in the same commit as the spaces. |
| 2 Ink on the GPU | **Done.** Line breaks are identical to the WPF text box (tested at four sizes and 44 widths). |
| 5 Surfaces, allocations, ReadyToRun | **Mostly.** Done: surfaces and bitmaps with headroom (whole-surface updates, so nothing is copied along); reused raster arrays; the `SustainedLowLatency` GC mode; ReadyToRun on publish. Left: reshape frames still allocate 5–50 KB, so steady-state allocation isn't zero yet. |
| 3 Painted themes | **Mostly.** Done: effects, masks, gradient colours and frame bitmaps are reused, and the Claudish boil cycles through 8 drawings that are tessellated once. Parallax draws a draft (stretched bands) while the shape moves. Left: reduced-quality blur during a reshape isn't needed at these costs; the Glory p99 target (1.5 ms) isn't met, because of the tails above. |
| 4 Dreamy on the GPU | **Not done.** Instead, the CPU raster got faster: a counting sort of edges instead of a comparison sort, the look redrawn only at 60 fps (not every interactive frame), and arrays reused. The shader port is still the way to get under 0.1 ms. |
| 6 Pacing per monitor | **Done.** Windows 11 uses the compositor clock; Windows 10 waits on the vblank of the cursor's monitor, across all adapters. The 8 ms sleep that dropped frames above 240 Hz is gone, and only a sleeping display is guarded against. Not yet checked on a real high-refresh secondary display. |
| Device lost, WARP | **Done.** A lost GPU is detected (removed reason, device state, failed calls) and everything is rebuilt in 50–130 ms. WARP caps interactive frames at 60 fps and turns the jelly off. Not done: the ARM64 build. |
| Click-through (found on the way) | **Fixed.** A floating space (following an app, lifted, or settling) was never really click-through, because `WS_EX_TRANSPARENT` alone doesn't make Windows skip a window. It is now layered too. A subclass keeps it layered, since WPF takes the style off whenever the window is resized. The same applies to the frame-time graph. Checked by the perf `clickthrough` and `stats` scenarios and by a unit test. |
| 7 Render thread | **Skipped.** Phases 1–6 bring steady frames under budget. |
| 8.1 / 8.2 Files on spaces | **Done.** |
| 8.3 Polish | **Mostly.** Done: the drop burst and fade-in, the Dreamy wobble, the file name at the top (double-click opens the file), Ctrl+click links. Left: "reload from file" and tables. |

## Goal

Every interactive action keeps up with the display, up to 360 Hz. The app can't show more frames than the display's refresh rate: the desktop compositor presents once per refresh. So "300 fps" means finishing each frame well within one refresh on a 300 Hz+ display.

**Budget:** a frame's CPU work (UI thread plus render work) must stay within 60% of the refresh period, leaving room for the compositor.

| Display | Refresh period | Frame budget (p99) |
|---|---|---|
| 144 Hz | 6.9 ms | 4.1 ms |
| 240 Hz | 4.2 ms | 2.5 ms |
| 360 Hz | 2.8 ms | **1.7 ms** (the target) |

The ambient animation (rim wobble, twinkle, drift) stays at 60 fps. It moves in whole art pixels and looks the same faster, and 60 fps saves battery.

**Must keep working:** Windows 10 22H2 and Windows 11, on NVIDIA, AMD, Intel, Qualcomm (ARM64) and WARP (software). That includes hybrid-GPU laptops, and several monitors with mixed DPI and refresh rates.

## Where the time goes now

Measured on the development laptop (200% scaling, 60 Hz panel) with the scratch harness from the reshape work.

| Action | Now per frame | Main cost |
|---|---|---|
| Drawing a space (Ctrl+Alt+S), erase brush | about 1 CPU core while dragging | WPF full-screen transparent overlay (`OverlayWindow`), redrawn in software every change |
| Block / move overlays (handles, hints) | same overlay cost | the same overlay |
| Reshape / jelly, Dreamy | ~4.5 ms (max ~10) | ink re-raster 1.7–2.9 ms (WPF software `RenderTargetBitmap`); `DreamRaster` rebuild ~1 ms; new DComp surfaces every size |
| Reshape, Claudish | ~6.4 ms | ink raster; ink ribbon outline |
| Reshape, Glory (and other painted themes) | ~10 ms (spikes 20–30) | stills repainted per shape with Direct2D blur/shadow effects; new bitmaps each time |
| Painting a stroke | ~0.3–1 ms per segment | WPF software raster of the segment plus a CPU blend |
| Ambient frame (one mid-size space) | 0.15–0.8 ms | `DreamRaster` on the CPU (Dreamy); vector redraw (painted themes) |
| Allocation during reshape | 0.2–0.5 MB/frame, 1–2 gen2 GCs per 30 frames | WPF `RenderTargetBitmap` memory pressure; per-shape arrays |
| Frame pacing | follows the primary monitor only | `FrameClock` waits on the primary output's vblank |

## Phases

The phases are ordered by visible gain per risk. Each phase ends with its measurements re-run against the budget, and ships on its own.

### Phase 0: Measure first (small)

- Promote the scratch harness (`live`: real `SpaceManager` + GPU, off-screen) into a `tests/InfiniteSpace.Perf` console project. Script each action:
  - draw stroke, paint stroke, erase stroke
  - block move/resize
  - reshape drag, jelly
  - app drag-follow
  - ambient animation, one to five spaces, each theme
- Record per action: p50, p99 and max frame time; allocations per frame; gen0/gen2 counts; frames missed at a simulated 360 Hz.
- Add an `EventSource` (`InfiniteSpace-Frames`) with per-frame timing, so `dotnet-counters` / PerfView / WPA can watch the real app.
- Add a hidden setting (`settings.json` → `"ShowFrameStats": true`) that draws a tiny frame-time graph in a corner.
- **Done when:** a baseline table like the one above is produced automatically, in Release and self-contained.

### Phase 1: GPU overlays (biggest visible win)

WPF `AllowsTransparency` windows are layered windows. WPF renders them in software and pushes the whole changed area on each update, and ours cover the entire virtual screen.

- Replace `OverlayWindow`'s visual layer with a DirectComposition overlay, like the spaces use:
  - an `HwndSource` with `WS_EX_NOREDIRECTIONBITMAP`, input still handled in WPF/Win32;
  - pixels from a DComp surface drawn with Direct2D.
- **Drawing (Ctrl+Alt+S):** append stroke segments to a D2D path and draw only the new segment into the surface (dirty rectangle). Closing the shape draws the fill once.
- **Erase brush:** the brush circle is a small DComp visual that moves, a transform change with no redraw. Erased trails are drawn as incremental segments.
- **Block / move overlays:** arrow handles, the dashed frame and the size label become small DComp visuals that move by transform. They are rasterised once, on the GPU.
- **Hint bar:** drawn once with DirectWrite into its own visual.
- Mouse input keeps coming from the window; nothing about the interaction changes.
- **Done when:** drawing, erasing and dragging handles run within budget at 360 Hz, with under 5% of one core while dragging.

### Phase 2: Ink on the GPU

- Replace `InkLayer`'s WPF `RenderTargetBitmap` with Direct2D drawing into a GPU ink bitmap (`ID2D1Bitmap1`, target):
  - **Strokes:** `ID2D1PathGeometry` per stroke, cached, plus `ID2D1GeometryRealization` for fast redraws. The width comes from the stroke and colour from the stroke or theme ink.
  - **Notes:** DirectWrite `IDWriteTextLayout` using the system UI font, the note's size, and `MaxWidth` = the note's `Width`.
  - **Clip:** draw into a layer with a geometric mask (`PushLayer`), or reuse the look's mask with `DestinationIn` as `SceneLook.Cut` does.
- **Painting:** draw each new segment straight into the GPU bitmap, then copy its dirty rectangle into the ink surface. No CPU blend and no `int[]` pixel array.
- **Moves:** the ink bitmap is shifted, not redrawn (as now).
- **Risk: text wrapping.** DirectWrite and WPF's text engine may break lines differently. The editing `TextBox` (WPF) must still wrap where the drawn note does.
  - Re-run `TheTextBox_WrapsWhereTheNoteWill` against the DirectWrite layout.
  - If they differ, draw the note from the text box's own line breaks (store them with the note), or render the editing preview with DirectWrite too.
- **Done when:** ink redraws are under 0.3 ms per frame. `RasterisingOnlyTheInksArea…` becomes a pixel comparison against the old path, with a tolerance for glyph antialiasing.

### Phase 3: Cheaper shape changes for painted themes (Glory, Burn, Sunday, Parallax, Ocean, Void, Claudish)

- **Reuse, don't recreate:** keep the `ID2D1Effect` objects (Shadow, blur) and the intermediate bitmaps alive per space. Allocate bitmaps with headroom, as `InkLayer` and `DreamRaster` now do, and redraw into them. `canvas.Paint` allocations disappear.
- **Live reshapes:** while a shape changes under the hand (`SpaceWindow.ReshapeLive`), draw the stills at reduced quality:
  - blur at half resolution, scaled up;
  - no cast shadow;
  - a single outline pass.
  - Then redraw at full quality once the shape rests (the same moment the window lands).
- **Claudish:** cache the hand-drawn ribbon outline per shape. Boil frames only change the ribbon's offsets.
- **Done when:** a Glory reshape frame is under 1.5 ms p99.

### Phase 4: Dreamy on the GPU

- Port `DreamRaster` to a pixel shader rendered at art resolution into the art surface:
  - the void fill and twinkling stars (same hash, same layers);
  - the one-pixel rim with its travelling waves and click ripples.
- The outline becomes a mask texture (and a distance field for the rim). It is rasterised by Direct2D only when the shape changes, including each jelly frame, which is cheap on the GPU.
- Ship precompiled shader bytecode, built with `fxc`/`dxc` at build time and targeting Shader Model 4.0 so feature level 10 GPUs and WARP run it. Either register it as a Direct2D custom effect, or render with Direct3D 11 directly into the DComp surface.
- Keep the CPU `DreamRaster` as the reference for tests: compare the GPU output pixel for pixel at art resolution, frame for frame, using the same hash.
- **Done when:** a Dreamy ambient frame costs under 0.1 ms CPU, and a jelly/reshape frame under 1 ms.

### Phase 5: Surfaces and allocations

- `SpaceSurface.Layer.Ensure` creates a new DComp surface on every size change, as in each reshape or jelly frame. Instead, allocate with headroom (say, the next 256-pixel step) and show only the used part with a visual clip.
- Aim for zero allocations per frame in steady state:
  - pools for per-shape arrays;
  - no LINQ or closures in `Universe.Tick`, `SpaceWindow.RenderFrame` and `Jelly`;
  - `GCSettings.LatencyMode = SustainedLowLatency` during interactive sessions.
- Publish with **ReadyToRun** (`-p:PublishReadyToRun=true`) and keep tiered PGO on. This removes JIT stalls on the first interactions after start.
- **Done when:** no gen2 GC during any scripted action, and no first-use hitch in a cold start.

### Phase 6: Frame pacing per monitor

- On Windows 11 (22H2+), pace from the compositor clock with `DCompositionWaitForCompositorClock`. It follows the fastest active monitor and handles variable refresh rate.
- On Windows 10, keep vblank waits but pick the output of the monitor where the user is interacting (the cursor's monitor), not always the primary. Fall back to `DwmFlush`.
- Remove the `Thread.Sleep(8)` fallback spin guard. Keep only a guard against a sleeping display.
- **Done when:** on a 60 Hz primary plus a 240 Hz secondary, actions on the secondary run at 240.

### Phase 7: A render thread (only if still needed)

- **Check first:** if phases 1–6 bring every action under budget, skip this phase.
- Otherwise, move `GpuCompositor`, `Universe`, the looks and the surfaces to a dedicated render thread that owns the DComp device and D3D context.
- The UI thread (input, WPF text box, overlays' input) sends immutable updates to it: shape, ink change, placement, theme.
- This keeps WPF layout, input bursts and GC on the UI thread from delaying a frame.
- **Largest refactor, highest risk:** window placement (`SetWindowPos`, regions) stays on the UI thread and must stay in step with the picture. Plan it separately if phases 1–6 fall short.

### Phase 8: Text and Markdown files on spaces (new feature)

Open a `.txt` or `.md` file on a space as a "file note": wrapped to a width, clipped to the space's shape, styled in the theme's colours, and scrollable. It is built in three steps: a quick readable version, the styled version after Phase 2, then polish.

**Opening a file**
- Drop a `.txt` or `.md` file from Explorer onto a space. Space windows accept file drops (`DragAcceptFiles` / `WM_DROPFILES`), and the note's top-left goes where it was dropped.
- Or use **Open a file…** on the right-click palette, which opens a file dialog and places the note at the right-clicked point.
- Limits and encodings:
  - Files up to 1 MB. Bigger files are refused with a tray notice.
  - The encoding is read from the BOM (UTF-8, UTF-16); files without one are read as UTF-8, falling back to the system code page. Line endings are normalised.
- A snapshot of the text is kept, so the note survives the file being moved or deleted. "Reload from file" can come later.

**Model and storage**
- A `FileNote` ink kind next to `TextNote`, holding:
  - the snapshot, the kind (plain or Markdown), and the source path (for display only);
  - its place, wrap width, visible height and scroll position;
  - its colour and size, like other notes.
- It takes part in undo/redo and edits (`Order`, `Previous`) like notes.
- **Storage:** today `SpaceStore` rewrites the whole `spaces.json` on every change, including every paint stroke. File text therefore goes in separate files (`notes/{id}.md`, written once and atomically). `spaces.json` holds only a reference. Separate files no longer referenced are removed when saving.

**Markdown**
- Parse with **Markdig** (NuGet, BSD-licensed) into our own block list:
  - headings (levels 1–3 styled; 4–6 as bold);
  - paragraphs, with **bold**, *italic*, `inline code` and ~~strikethrough~~;
  - bullet, numbered and task lists (task boxes shown, not clickable at first);
  - quotes, fenced code blocks, horizontal rules, links;
  - tables later.
- **Safety:**
  - Raw HTML is shown as text, never rendered.
  - Images aren't loaded at first. Local images could come later; remote ones never, because fetching them would reveal the user's IP.
  - Links only open `http`/`https` targets, on Ctrl+click, in the default browser.

**Layout and drawing** (on the Phase 2 ink path)
- One DirectWrite text layout per block, cached, re-laid only when the width, size or theme changes.
- Only the blocks in view are drawn. A long file costs no more than a short one.
- **Scrolling** shifts cached block bitmaps and draws only the newly revealed strip, with no re-layout.
- Clipped to the space's outline, with a soft fade at the note's top and bottom edges while there's more to scroll.
- **Per-theme style:**
  - text in the theme's ink, headings and links in a theme accent (a new `Themes.AccentOf`);
  - code blocks on a tinted backing, quotes with an accent bar.
  - Dreamy gets a light star-dust tint on its headings.

**Interaction**
- **Wheel** over a file note scrolls it. Wheel messages reach the space under the cursor, which never takes focus.
- **E** over it opens the raw text in the text box (the same editor, with its move and reshape arrows). Committing re-renders it. **T**, drag-to-move, ↔ width and ⤡ size work as for notes.
- Double-click the note's file name (shown small at its top) to open the original file in its default app.
- **Drop effect:** a click-style burst where it lands and a short fade-in of the lines (~300 ms). In Dreamy, also a small jelly wobble (Dreamy only, like the resize jelly).

**Steps**
1. **Readable (before Phase 2, small):** `.txt` and `.md` shown as plain wrapped text through today's note drawing (`InkLayer`), with Markdown markers left as typed. Includes dropping files, separate-file storage, scrolling by redrawing, and undo.
2. **Styled (after Phase 2, medium):** Markdig blocks with DirectWrite, theme styles, drawing only visible blocks, scrolling by shifting cached bitmaps, fading edges.
3. **Polish (small):** drop burst and fade-in, Dreamy wobble, file-name header, Ctrl+click links, reload from file.

**Tests**
- **Parsing:** Markdown → blocks (each element; raw HTML stays text; links filtered).
- **Reading files:** encoding detection and the 1 MB limit.
- **Layout:** heights and wrapping at widths, and that the editing text box wraps like the drawn note.
- **Rendering:** pixels at 100%, 125%, 150% and 200% scaling, clipped to odd shapes; scrolling clipped at the edges.
- **Storage:** round trip through the separate files, and orphaned files removed.
- **Performance:** a scroll frame under 1 ms at 360 Hz for a 1 MB file (Phase 0 project).

## Working on every GPU and Windows version

- **Requirements:** Direct3D 11 at feature level 10.0+, DirectComposition and Direct2D 1.1. That covers every DirectX 10+ GPU from NVIDIA, AMD, Intel and Qualcomm, and WARP.
- **Device lost** (driver update, GPU reset, adapter removed, eGPU unplugged):
  - Today nothing handles it. Check `GetDeviceRemovedReason` after failed calls and `Commit`.
  - Recreate `GpuCompositor`, all surfaces and all look resources, then redraw every space from its record.
  - Test it with `dxcap -forcetdr` or by disabling the GPU in Device Manager.
- **Hybrid GPUs:** create the device on the adapter driving the display (the default adapter normally is). Check on an NVIDIA Optimus laptop and an AMD switchable laptop.
- **WARP** (no usable GPU, remote desktop, VMs): detect it and cap interactive rendering to 60 fps. Also turn the jelly off there.
- **ARM64:** also publish `win-arm64` (Snapdragon laptops). The shader bytecode is the same.
- **Test matrix:**
  - GPUs: one each of NVIDIA, AMD and Intel (iGPU), Qualcomm ARM64, WARP.
  - Windows versions: Windows 10 22H2 and Windows 11 24H2.
  - Displays: 100%, 150% and 200% scaling; 60, 144 and 240 Hz; two monitors with mixed DPI.
  - Run the Phase 0 perf project on each.

## Order and rough size

| Phase | Gain | Risk | Size |
|---|---|---|---|
| 0 Measure | enables everything else | low | small |
| 1 GPU overlays | drawing, erasing, handles: the worst today | medium | medium |
| 2 Ink on GPU | painting and reshape frames | medium (text wrapping) | medium |
| 5 Surfaces, allocations, ReadyToRun | spikes and hitches | low | small |
| 3 Painted themes | Glory and friends' reshapes | medium | medium |
| 4 Dreamy shader | ambient and jelly | medium | medium–large |
| 6 Pacing per monitor | high-refresh secondary monitors | low | small |
| Device-lost recovery | robustness everywhere | low | small (do alongside Phase 1) |
| 7 Render thread | last few milliseconds | high | large; only if needed |
| 8.1 Files on spaces: readable | open `.txt`/`.md` on a space (can come before Phase 2) | low | small |
| 8.2 Files on spaces: styled | Markdown styled per theme, fast scrolling (needs Phase 2) | medium | medium |
| 8.3 Files on spaces: polish | drop effects, Dreamy wobble, links, reload | low | small |

After each phase: run the tests and the Phase 0 perf project, then update the README's "Cost" paragraph with the new numbers.
