# UI visual regression evidence

Lumine keeps deterministic layout/state assertions in `Lumine.App.Smoke` as the CI gate and supplements them with rendered PNG evidence from the same build.

## CI contract

The App smoke receives:

```text
--visual-output=artifacts/visual-regression
```

When enabled it captures the required product surfaces below:

- `design-system-scale-100.png`
- `design-system-scale-125.png`
- `design-system-scale-150.png`
- `design-system-scale-200.png`
- `design-system-scale-225.png`
- `welcome-1440x900.png`
- `loading-1440x900.png`
- `browse-900x600.png`
- `browse-1024x768.png`
- `browse-1440x900.png`
- `browse-1920x1080.png`
- `browse-filter-open-900x600.png`
- `browse-active-filter-900x600.png`
- `navigation-overlay-900x600.png`
- `navigation-pinned-1440x900.png`
- `tags-assignment-1100x720.png`
- `tags-create-900x600-text225.png`
- `tags-create-custom-color-900x600-text225.png`
- `tags-edit-900x600-text225.png`
- `tags-edit-custom-color-900x600-text225.png`
- `inspector-900x600.png`
- `inspector-1440x900-pinned.png`
- `focused-viewer-900x600.png`
- `focused-viewer-900x600-text225.png`
- `settings-1440x900.png`
- `settings-advanced-1440x900.png`
- `settings-900x600.png`
- `empty-library-1440x900.png`
- `no-match-1440x900.png`
- `error-1440x900.png`
- `error-900x600-text225.png`

The tag-create capture explicitly opens the real shared `TagColorEditor` / Avalonia `ColorPicker` drop-down and validates a realized `ColorSpectrum`. The tag-edit capture keeps the edit Flyout itself visible so the prefilled name/color/count and save/cancel layout remain reviewable. Both use Lumine's existing 225% text-scale simulation.

A `manifest.tsv` records each captured surface, pixel dimensions and PNG byte size. CI fails if the required set is incomplete or a rendered frame is empty. Existing geometry, clipping, containment, virtualization and interaction assertions remain the deterministic gate.

## Why PNG evidence is not a pixel-golden gate yet

The Windows CI runner, Skia and font rasterization can change at the pixel level when the runner image or rendering stack is updated. A hard pixel diff would therefore risk blocking product work on renderer noise rather than UI regressions.

For now:

1. structural/layout assertions gate CI;
2. the exact CI build always emits reviewable PNG evidence;
3. the Product Portable real-machine acceptance remains authoritative for final product review.

A golden/tolerance gate can be added later if repeated CI captures demonstrate stable raster output across runner updates.

Design-system review must confirm the shared primary/secondary/danger grammar, keyboard navigation, non-color selection cues, and the 100/125/150/200/225% evidence matrix.
