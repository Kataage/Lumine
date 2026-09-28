# Lumine v2 Core Viewer App shell

Issue #309 connects the completed Core modules to the production Avalonia window.

## v2-only data boundary

The App does not read, import or migrate Lumine v1 state.

By default, v2 application-owned state is stored below:

`%LOCALAPPDATA%\Lumine\v2`

The root may be overridden with `LUMINE_DATA_DIR` for portable/test scenarios. The effective paths are resolved by `AppDataPaths`:

- `library.db`
- `thumbnails\`

A selected image library may not contain Lumine's own data directory. This prevents persistent generated WebP thumbnails from recursively entering the indexed source library.

## Production ownership graph

`MainWindow` owns only user interaction and presentation state.

`CoreViewerRuntime` owns the runtime graph:

```text
LibraryService
  |
  +-- WindowsLibrarySyncSession
  |
ThumbnailCache
  |
ThumbnailPipeline
  |
  +-- ImageViewerThumbnailProvider
  +-- ImageViewerDetailProvider
  |
CursorPagedViewerAssetProvider
  |
  +-- ViewerSession
  +-- ViewerDetailSession
```

`CoreViewerShell` composes the real `ThumbnailViewerControl` and `DetailViewerControl` from those production sessions. Tests and the MainWindow use this same path rather than a test-only viewer composition.

## Opening a library

1. initialize the v2 SQLite database
2. register/match the selected library root
3. start Windows incremental synchronization
4. allow sync bootstrap to reconcile only when required
5. query the current asset count
6. create Image/Viewer runtime objects from `Program.ResourcePolicy`
7. attach the Grid + Detail shell

Warm startup does not unconditionally walk the thumbnail cache. Windows sync decides between the stored USN checkpoint and reconciliation.

## Shutdown

Window close is coordinated:

1. cancel and drain any in-progress library open
2. detach the visual shell
3. stop/drain Windows filesystem synchronization
4. drain Detail and Grid Viewer sessions
5. dispose the paged metadata provider
6. cancel/drain ThumbnailPipeline workers
7. flush requested diagnostics
8. allow the final window close

Repeated/concurrent subsystem disposal follows each subsystem's shared-completion contract.

The compositor-safe release of a previous full-resolution original is intentionally tracked separately by #331, now that #309 provides the real visual path on which that contract can be proven.

## Acceptance

The normal App smoke now exercises the production runtime/shell with real images through:

- Grid asset exposure
- Detail preview
- selection changes
- 1:1 original promotion
- zoom
- pan
- Fit
- coordinated shutdown

CI also executes the published NativeAOT `Lumine.App.exe` with `--product-smoke`. This starts and shuts down the production non-visual runtime graph from the actual published executable, instead of treating file existence as sufficient evidence.
