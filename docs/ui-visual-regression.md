# UI visual regression evidence

Lumine keeps deterministic layout/state assertions in `Lumine.App.Smoke` as the CI gate and supplements them with rendered PNG evidence from the same build.

## CI contract

The App smoke receives:

```text
--visual-output=artifacts/visual-regression
```

When enabled it captures the required product surfaces below:

- `browse-900x600.png`
- `browse-1440x900.png`
- `browse-filter-open-900x600.png`
- `browse-active-filter-900x600.png`
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
- `no-match-1440x900.png`
- `error-1440x900.png`

The tag-create capture explicitly opens the real shared `TagColorEditor` / Avalonia `ColorPicker` drop-down and validates a realized `ColorSpectrum`. The tag-edit capture keeps the edit Flyout itself visible so the prefilled name/color/count and save/cancel layout remain reviewable. Both use Lumine's existing 225% text-scale simulation.

A `manifest.tsv` records each captured surface, pixel dimensions and PNG byte size. CI fails if the required set is incomplete or a rendered frame is empty. Existing geometry, clipping, containment, virtualization and interaction assertions remain the deterministic gate.

## Why PNG evidence is not a pixel-golden gate yet

The Windows CI runner, Skia and font rasterization can change at the pixel level when the runner image or rendering stack is updated. A hard pixel diff would therefore risk blocking product work on renderer noise rather than UI regressions.

For now:

1. structural/layout assertions gate CI;
2. the exact CI build always emits reviewable PNG evidence;
3. the Product Portable real-machine acceptance remains authoritative for final product review.

A golden/tolerance gate can be added later if repeated CI captures demonstrate stable raster output across runner updates.
