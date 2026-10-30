# Lumine v2 Settings / portable / product-state contract

Issue: #396  
Parent: #385

## Product settings surface

The Settings destination is a normal product surface. Runtime diagnostics remain available for troubleshooting, but no primary workflow requires reading diagnostic text.

User-facing settings are intentionally limited to stable product concepts:

- browse defaults: grid/list, density, sort
- encoded thumbnail memory-cache budget
- thumbnail storage policy:
  - **MemoryOnly** is the product default
  - **PersistentDisk** is explicit opt-in
- application-owned data/storage location
- safe deletion of disposable display-thumbnail cache
- diagnostics entry point
- reserved local-AI section that does not load models during the non-AI product phase

Low-level worker counts, queue capacities, libvips limits, and similar Core tuning remain diagnostics/advanced implementation details rather than ordinary product controls.

## Thumbnail RAM budgets (#634, 2026-10-30)

For an installation with no explicit resource-policy overrides, both independent
thumbnail caches now default to **1 GiB** maximum residency:

- **Decoded/visible and ahead-of-scroll Bitmap cache:** 1 GiB and at most
  **2,048 entries** (previously 96 MiB and 96 entries). Individual native
  Bitmaps are still bounded by the 384px thumbnail decode dimension, decoder
  concurrency and global native-resource protections.
- **Encoded in-process thumbnail reuse:** 1 GiB (previously 256 MiB).
  `MemoryOnly` is still the default; this cache is **not** persistent across
  application restarts. Enable the existing PersistentDisk option explicitly
  if repeat-launch reuse is more important than disk writes/usage.

These are **upper bounds**, not startup reservations or an instruction to fill
the RAM with cold thumbnails. The normal scrolling scheduler admits at most
three nearby viewports of **new** background predecode work per plan, while
previously visited thumbnails can stay in the expanded decoded LRU. Far seeks
keep their small independent bounds and foreground-visible loading remains
higher priority. A larger RAM ceiling must never trigger a 2,000-entry
background decode burst at startup.

Advanced Settings offers **1–4 GiB encoded** and **1–2 GiB decoded** budget
choices as well as legacy small budgets. A user-persisted smaller resource
policy continues to win over these revised defaults; settings are saved
immediately and applied on next launch. The app may use substantially more
RAM over a long session, but must remain within its decoded/encoded
entry/byte limits. This is not a fix for a slow full filesystem
reconciliation or a guarantee of instant first-ever thumbnail generation.

## Storage modes

### Local user data

Default normal installation mode:

`%LOCALAPPDATA%\Lumine\v2`

Contains Lumine-owned state such as:

- `library.db`
- `settings.json`
- `thumbnails\` when PersistentDisk is enabled
- `logs\`
- runtime/instance markers

Original images are not copied into this directory.

### Portable

Portable mode is enabled by any of:

- `--portable`
- `LUMINE_PORTABLE=1`
- a `portable.flag` file next to the executable

The Lumine-owned root becomes:

`<executable directory>\data`

All application-owned state stays inside that directory, so the executable directory can be moved as one portable unit.

The NativeAOT **Product acceptance / portable artifact is packaged with `portable.flag` beside `Lumine.App.exe`**. Therefore the normal extracted-artifact contract is:

1. extract the ZIP,
2. double-click `Lumine.App.exe`,
3. Lumine-owned state is created under `<executable directory>\data`.

No command-line switch or environment variable is required for the shipped portable artifact. Removing `portable.flag` intentionally returns launch behavior to the normal Local user data rules unless another explicit override is supplied.

CI validates both halves of this contract: the marker must be present in the publish directory, and the NativeAOT executable is launched without `--data-dir` to prove that it actually creates `<exe>\data\library.db`. The temporary verification data is removed before artifact upload so user/runtime state is never baked into the package.

### Custom

`LUMINE_DATA_DIR=<path>` overrides the normal default storage root.

The explicit `--portable` launch flag takes precedence when supplied for that process.

## Cache safety

The thumbnail directory is disposable derived data.

"表示用cacheを削除" uses ThumbnailCache pruning and only removes generated thumbnail files from the application-owned thumbnail-cache root.

It must never remove:

- source/original images
- `library.db`
- ratings
- favorites
- tags
- notes
- Work membership
- Generation Group membership/context
- lineage
- Publication snapshots
- settings

Switching from PersistentDisk back to MemoryOnly retires the old persistent display-thumbnail cache and removes it asynchronously without touching originals or user metadata.

## Restart semantics

These changes take effect immediately:

- browse view mode
- browse density
- browse sort/default query

These are persisted immediately but take effect on the next application launch:

- thumbnail storage mode
- encoded thumbnail memory-cache budget
- low-level resource-policy settings

An environment override for thumbnail storage is process-local and is never written back into the persisted preference.

## Dialog contract

Important or destructive actions use Lumine-owned dialogs rather than platform/browser prompts.

Current consumers include:

- deleting original source files
- unregistering a library
- enabling persistent thumbnail storage
- deleting disposable display-thumbnail cache

Dangerous source-file deletion is visually distinct from reversible/non-destructive actions.

## Product-state contract

The app exposes explicit user-facing states for:

- Welcome / first run
- Recovery after an unclean previous shutdown
- Loading / library preparation / query update
- Workspace
- EmptyLibrary — the library truly contains zero supported images
- NoMatch — the library contains images, but the active search/filter/scope returns zero
- preview/original loading
- unsupported/unreadable preview
- recoverable library/query failure
- fatal/bootstrap failure
- coordinated shutdown

The fatal/bootstrap path still uses Lumine branding and explains the failure without requiring console output or runtime diagnostics.

## Validation

App smoke covers:

- portable/custom path resolution
- Product runtime smoke fallback to normal portable resolution when `--data-dir` is omitted
- explicit Product runtime `--data-dir` custom override

CI packaging additionally covers:

- `portable.flag` is present in the NativeAOT publish directory before artifact upload
- a packaged NativeAOT launch without `--data-dir` creates its database under `<exe>\data`
- temporary package-verification data is deleted before upload
- settings persistence across restart
- product Settings composition
- distinct NoMatch and EmptyLibrary states
- safe cache deletion preserving originals and user-owned metadata
- fatal/bootstrap product-state window
- existing repeated MainWindow lifecycle and NativeAOT runtime gates
