# Lumine v2 application lifecycle

Issue #293 closes the Core lifecycle contract after the real MainWindow and compositor paths exist.

## Application data layout

The normal Windows data root is `%LOCALAPPDATA%\Lumine\v2`. `LUMINE_DATA_DIR` remains the portable/test override.

The v2-owned layout is:

- `library.db` — SQLite metadata/search state
- `thumbnails\` — persistent generated thumbnails
- `settings.json` — versioned bounded Core resource settings
- `instance.lock` — data-root-scoped single-instance ownership
- `runtime.unclean` — crash/unclean-shutdown recovery marker
- `logs\runtime.log` — small bounded lifecycle/event log

The event log rotates at 2 MiB and retains two rotated files in addition to the current log. Logging is best-effort and never owns background workers.

## Startup sequencing

Production startup resolves lifecycle state before creating Avalonia/Image runtime objects:

1. resolve/create the v2 data root
2. acquire the exclusive data-root instance lock
3. detect a retained `runtime.unclean` marker
4. load `settings.json`
5. validate settings through `CoreResourcePolicy.Resolve`
6. fall back to bounded defaults when persisted settings are malformed/out of range
7. write the current `runtime.unclean` marker
8. start Avalonia / the requested product smoke path
9. create Image/Viewer runtime objects only from the already-resolved policy

This preserves #332's rule that libvips process-global configuration is established from one effective policy before the first production `ThumbnailCache`.

Settings writes use a temporary file plus atomic replacement. A malformed settings file is treated as an optional-subsystem failure: Core remains usable with bounded defaults and exposes a warning in the runtime diagnostics view/log.

## Single-instance policy

v2 uses one process per application data root.

The lock is intentionally scoped to the data root rather than machine-global. This prevents two processes from concurrently owning the same SQLite/settings/cache lifecycle while still allowing isolated portable/test roots.

A stale `instance.lock` file is harmless: ownership is the open exclusive file handle, not file existence. OS process teardown releases the handle after a crash.

## Unclean-shutdown recovery

`runtime.unclean` is existence-based:

- it is written once startup ownership has been established
- it remains present for crashes, forced termination, or an undrained application lifetime
- it is deleted only after the real MainWindow ownership graph has drained and the desktop lifetime returns cleanly

SQLite itself performs WAL recovery when reopened. Lumine does not add an unconditional full database or thumbnail-cache scan to warm startup.

Interrupted thumbnail temp files remain non-addressable cache entries and are cleaned by existing directory-local generation cleanup or explicit maintenance. Runtime diagnostics only walk the thumbnail cache when the user explicitly requests that view.

## Shutdown sequencing

The real MainWindow shutdown remains cancellation + drain, not cancellation-only:

1. cancel/drain an in-progress library open
2. detach the visual shell
3. stop/drain Windows filesystem synchronization
4. drain Detail and Grid Viewer work
5. dispose the paged metadata provider
6. cancel/drain ThumbnailPipeline work
7. run `PRAGMA wal_checkpoint(TRUNCATE)`
8. clear pooled SQLite connections
9. flush requested diagnostics
10. allow final window close
11. after the desktop lifetime returns, delete `runtime.unclean`

If an owned shutdown stage fails, the recovery marker is intentionally retained.

## User-visible runtime diagnostics

The MainWindow exposes an explicit `Runtime diagnostics…` action. It reports:

- data/database/cache/settings/log paths
- previous-shutdown recovery state
- settings fallback warning, when present
- the effective bounded `CoreResourcePolicy`
- active library and asset count
- thumbnail cache file/byte/interrupted-write counts

The cache walk happens only for this explicit diagnostic request; it is not part of normal warm startup.

## Acceptance coverage

The App smoke covers:

- persisted resource policy reload
- invalid-settings fallback
- same-data-root single-instance exclusion
- clean restart vs retained unclean marker recovery
- real MainWindow composition
- repeated launch/open/close
- close while original/navigation work is in flight
- runtime diagnostics
- post-close deletion of library/data roots to catch leaked Windows handles

The published NativeAOT product smoke runs three independent process launches against the same data root. Each process opens/closes the production runtime graph twice, must leave a clean recovery marker state, and must leave no non-empty SQLite WAL.

This lifecycle gate remains separate from #295 real-library performance/UX acceptance.
