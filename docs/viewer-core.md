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


## Performance budgets

The hosted Windows benchmark uses 10k, 50k and 100k logical libraries. CI fails if any run exceeds:

- first viewport realization: 1.5 s
- 40-jump fast-scroll refresh workload: 1.5 s
- realized rows: 16
- attached tiles: 128
- thumbnail requests in the scroll workload: 1,600
- decoded cache: 64 entries / 32 MiB
- absolute peak working set: 160 MiB
- incremental peak working set: 128 MiB
- fast-scroll managed allocation: 96 MiB

The gate also requires stale thumbnail cancellation, zero remaining in-flight work, and explicit non-scaling checks between 10k and 100k for realized rows, attached tiles, and peak working set.

## Cursor integration audit

The UI-only benchmark is not sufficient because a direct logical asset provider can hide cursor traversal cost. The 100k CI case therefore also builds a real SQLite Library Core fixture and exercises:

- a cold logical seek to the last asset through `LibraryService -> keyset paging -> CursorPagedViewerAssetProvider`
- 40 deterministic random logical seeks after sparse checkpoints have been established
- page-fetch counts in addition to latency
- the same hard metadata-page and cursor-checkpoint limits

This keeps the Viewer benchmark honest about the actual Library Core integration path while preserving keyset paging; no SQL OFFSET path is introduced.

## Decoded-cache realism audit

The performance fixture uses multiple distinct 512 x 512 WebP cache paths rather than a shared 1 x 1 placeholder. This forces real Avalonia bitmap decode, LRU churn, and byte accounting during the 100k fast-scroll workload. CI rejects a 100k run that does not reach meaningful decoded-cache pressure, so a future benchmark cannot accidentally pass by sharing one tiny bitmap across every logical asset.
