# Windows filesystem synchronization

Issue #290 replaces periodic whole-library polling with a Windows-first incremental pipeline.

## Normal path

- `ReadDirectoryChangesW` watches the registered root recursively with a fixed 32 KiB buffer.
- create / modify / delete / rename events enter a bounded 4,096-item queue.
- a 75 ms coalescing window removes duplicate bursts before SQLite updates.
- file rename updates the existing asset row so stable asset identity survives.
- directory add/remove/rename is treated as structurally ambiguous and triggers reconciliation rather than guessing.
- no idle timer performs periodic full-library walks.

Microsoft documents that `ReadDirectoryChangesW` can report success with zero returned bytes when its change buffer overflows. Lumine treats zero bytes, malformed native records, queue overflow, and excessive coalescing as explicit reconciliation conditions.

## Reconciliation safety

Reconciliation uses a monotonically increasing database generation. Each observed asset is stamped with the current generation while enumeration streams in bounded batches. Missing rows are deleted only after the entire enumeration completes with zero filesystem failures.

If enumeration is partial or fails, the generation is left incomplete, `reconcile_required` remains set, and **no destructive deletion occurs**.

## Restart / USN policy

For local NTFS drive-letter volumes Lumine probes the USN Change Journal.

- the persisted checkpoint contains both journal ID and next USN;
- journal ID mismatch is a gap;
- a checkpoint older than `max(FirstUsn, LowestValidUsn)` is a gap;
- unsupported/malformed journal records, directory topology changes, excessive records, or excessive relevant changes fall back to reconciliation;
- normal V2 file records can recover add/modify/delete/rename changes that happened while Lumine was stopped;
- network/non-NTFS/access-denied environments use startup reconciliation fallback instead.

The journal is never created or modified by Lumine. Query/read access is opportunistic and failure-safe.

## Startup/shutdown race policy

The watcher starts **before** restart recovery. Events arriving while USN catch-up or reconciliation is establishing the baseline are retained in a bounded bootstrap buffer and replayed afterward.

At shutdown, Lumine samples the USN boundary while the watcher is still active, then stops the watcher and drains the database queue. This deliberately stores a conservative checkpoint: replaying an already-applied event after restart is acceptable; skipping an unobserved event is not.

## Resource policy

- watcher native buffer: 32 KiB per library;
- event queue: 4,096;
- bootstrap buffer: 4,096;
- reconciliation ingest batch: default 2,048;
- USN catch-up: at most 100,000 volume records / 4,096 relevant changes before safe reconciliation fallback;
- no full-library resident path set is used.

## Acceptance

Windows CI performs real filesystem operations against a temporary library:

- create / modify / rename / delete;
- stable identity through rename;
- directory-triggered reconciliation;
- idle period with no recurring reconciliation;
- shutdown checkpoint;
- offline file creation followed by USN delta or explicit fallback recovery;
- complete stale deletion;
- partial reconciliation non-destruction.
