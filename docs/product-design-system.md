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
└─ workspace
   ├─ product header / primary actions / current status
   ├─ current library path
   └─ image surface
```

The current Image/Viewer runtime remains unchanged. #389 only changes presentation and composition around that proven path.

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

## Performance rule

The design shell must not introduce image decoding, library enumeration or extra Viewer data work. The existing native Viewer remains the only image surface; shell state changes are lightweight Avalonia controls around it.
