# Viewer Core

Issue #289 implements the high-volume thumbnail viewer on top of Avalonia's native control stack.

## Virtualization decision

Lumine does not use WebView/DOM rendering and does not create one Control per library asset.

Avalonia's `ListBox` uses `VirtualizingStackPanel` by default. Lumine exposes a lazy `IList` of **row indexes**, not a materialized collection of asset controls or row models. A realized row creates only the handful of tiles that fit the current viewport width.

This keeps the number of UI objects tied to viewport size rather than library size. The 100k headless smoke verifies a bounded realized row/tile count and scrolls directly near the end of the logical collection.

A custom Skia grid is intentionally deferred. It is justified only if the standard virtualized-row implementation fails the measured acceptance budgets.

## Metadata paging

`CursorPagedViewerAssetProvider` adapts a cursor/keyset page source to logical viewer indexes.

- page size is bounded
- cached metadata pages are bounded
- sparse cursor checkpoints are bounded
- a random seek walks forward from the nearest retained checkpoint
- no SQL OFFSET contract is introduced by Viewer

Viewer itself has no dependency on Lumine.Library. The App composition layer maps `AssetCursor` / `AssetPage` to the Viewer interfaces.

## Thumbnail path

Viewer has no dependency on Lumine.Image. The App composition layer maps a `ViewerAsset` to the persistent Image Core cache through `ThumbnailPipeline`.

Visible tiles request foreground priority. Adjacent rows request background prefetch. Identical requests are coalesced. When all waiters for stale work disappear, the shared provider request is cancelled, which propagates into Image Core's native cancellation path.

Grid tiles only decode the persistent thumbnail cache path. Original files are never decoded by Viewer.

## Decoded bitmap cache

`DecodedBitmapCache` has hard entry and estimated decoded-byte limits.

- decoded size is estimated as width x height x 4
- only unleased LRU entries can be evicted
- if active leases pin the entire budget, admission fails rather than exceeding the configured hard limit
- tile detach releases the lease
- cache disposal releases all Avalonia bitmaps

## Interaction

The initial Viewer includes:

- mouse selection
- Left/Right/Up/Down navigation
- Home/End navigation
- scroll-to-selection
- column recomputation from Avalonia DIP width
- resize/DPI-safe layout through device-independent units

## Acceptance

CI runs a real Avalonia headless 100k smoke and records 10k / 50k / 100k benchmark artifacts for:

- first viewport realization
- fast-scroll refresh
- maximum realized rows
- maximum attached tiles
- stale thumbnail cancellations
- decoded bitmap cache occupancy
- absolute and incremental peak working set

The realized UI object count must remain a function of the viewport, not the total asset count.
