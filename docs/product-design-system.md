# Lumine v2 design system and application shell

Issue #389 defines the first product-facing visual contract for the greenfield Avalonia application.

## Product continuity

The v2 shell intentionally carries forward the recognizable v1 product language rather than the old React implementation:

- the existing Lumine application icon is the brand anchor;
- the workspace is dark and image-first;
- navigation stays compact on the left;
- product labels are Japanese-first;
- library state and primary actions stay above the image surface;
- diagnostics remain available but are secondary to normal product actions.

The shell is not a pixel copy of v1. It is an Avalonia-native foundation for #390-#397.

## Tokens

The source of truth is `LumineDesign` in `src/Lumine.App/LumineDesign.cs`.

It defines:

- background, surface, raised surface and border colors;
- foreground and muted text colors;
- accent, focus, warning and destructive semantic colors;
- Japanese-capable desktop UI font fallback;
- navigation width, header height, control height, spacing and corner radius;
- primary/secondary button treatment;
- reusable product-state surfaces.

The palette is derived from the v1 dark workspace contract (`frontend/src/index.css` on `release/v1.1.0`) while using native Avalonia controls.

## Shell hierarchy

```text
MainWindow
├─ compact Lumine navigation rail
├─ adaptive navigation pane
├─ workspace
│  ├─ browse command bar (search / sort / filter / view / density)
│  ├─ contextual selection bar (only while multi-selecting)
│  ├─ virtualized image grid or list
│  └─ contextual inspector (side panel on wide windows, overlay on compact windows)
└─ root lightbox
   └─ edge-to-edge image viewer with overlay controls
```

The image surface remains dominant. Navigation and the inspector adapt before they can starve the image canvas. The focused viewer is mounted at the MainWindow root rather than inside the browse workspace, so it can use the full client area.

## Product states

The shell provides branded states for:

- first run / no library;
- library opening / indexing;
- empty library;
- recoverable library-open error;
- normal Viewer workspace;
- unclean-start recovery status.

Search/filter no-match is represented by the same reusable state surface and is wired by the later browsing issue.

## Interaction boundaries

This issue deliberately does **not** implement the data behavior owned by later issues:

- #390 wires Library / Folder / Tag / Publication navigation;
- #391 wires search, sort, filters, grid/list and density;
- #392 adds desktop multi-selection/bulk actions;
- #393 replaces the temporary Core split detail layout with contextual/focused viewing;
- #396 owns the full Settings and dialog surfaces.

## Branding

The v1 `appicon.png` is copied as an Avalonia resource and is shown in the shell and Window icon. A Windows ICO generated from the same 256px PNG is set as `ApplicationIcon`, so the published executable, Window chrome and in-product brand use the same source artwork.

## Performance and scale rule

The shell must not put image decoding or unbounded collection work on the UI thread.

- image grid/list rendering remains virtualized;
- Folder / Tag / Publication navigation uses recyclable list containers;
- Select-All and large Shift selections are represented as compact ranges rather than one object per selected asset;
- bulk actions resolve selected asset IDs in pages instead of materializing full ViewerAsset objects unnecessarily;
- navigation, inspector and lightbox composition remain lightweight Avalonia controls around the Viewer runtime.


## Interaction-first UX rule (real-user acceptance correction)

The first real-user Product Acceptance for #397 failed because the shell exposed too much of its interaction model through explanatory text instead of through the controls themselves.

The product rule is now:

> Primary Lumine workflows must be understandable from visual hierarchy, familiar iconography, control placement, selection/hover state, and direct manipulation. Explanatory prose must not be required to discover ordinary actions.

This does **not** mean "remove all text". Text remains appropriate for:
- names and values whose meaning cannot be represented safely by an icon alone;
- search input and filter values;
- destructive confirmations;
- loading, error, empty, and recovery states;
- supplemental tooltips for icon-only controls;
- accessibility names.

Text is **not** a substitute for:
- an obvious way to open the image viewer;
- visible zoom controls;
- recognizable navigation;
- selection state;
- previous/next image controls;
- clear inspector affordance.

### v1 interaction findings

A direct audit of `release/v1.1.0` found several interaction patterns that made v1 more self-explanatory than the first v2 Product build:

- the global left rail used recognizable vector icons plus short labels;
- active navigation was visually distinct;
- thumbnail double-click opened the image viewer;
- Enter/Space opened the selected thumbnail;
- thumbnail hover exposed visual info/open actions;
- the full-image viewer kept the image surface dominant and exposed zoom controls and previous/next visually;
- the image surface itself accepted wheel zoom and drag pan.

These are product interaction requirements, not React-specific implementation details. v2 should retain or improve them using native Avalonia controls.

### External design checks

The v2 interaction model should remain consistent with current desktop guidance:

- Fluent navigation: keep labels brief, easy to scan, and use simple recognizable icons where possible.
- Fluent toolbar: commands supporting the main task should be available where they are needed; familiar icons may replace button text.
- Tooltips: supplemental only. Essential task information must remain visible in the UI.
- Common image-management applications (Eagle, Lightroom, digiKam): grid-to-viewer transition, previous/next, Fit/100%, zoom level, zoom in/out and pan are direct viewer affordances rather than hidden documentation.

## Visual and accessibility contract

- ordinary product text uses the Lumine type ramp; normal captions do not shrink below 12 DIP;
- semantic warning/destructive text must retain normal-text contrast against product surfaces;
- icon-only controls require an accessibility name, and implemented keyboard shortcuts are exposed through accelerator metadata;
- high-risk geometry is regression-tested at the minimum supported window size and at 125%, 150% and 200% scaling;
- thumbnail action icons must stay centered inside consistent button bounds;
- focused images must start centered in the full-client lightbox;
- secondary commands use contextual flyouts/menus rather than permanently expanding primary image chrome.

## Acceptance implication

Automated tests may verify that commands exist and execute, but that is insufficient for Product Acceptance.

Manual acceptance must verify that a first-time user can, without reading instructions:
1. identify the main navigation destinations;
2. open an image from the grid;
3. move to previous/next images;
4. zoom in/out and return to Fit/1:1;
5. recognize and open image details;
6. distinguish selected and unselected images;
7. search/filter/sort without deciphering developer-oriented text.

If an evaluator has to be told "the feature is there; use this shortcut" for an ordinary workflow, the UX gate fails.
