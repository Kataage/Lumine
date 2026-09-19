# Semantic exact-vector persistent index

Lumine keeps Semantic Search exact by default. The persistent index is a disk-backed
cache of the normalized vectors that already exist in SQLite; SQLite remains the
source of truth.

## File layout

Each snapshot is immutable and addressed by semantic model provenance plus the
SQLite semantic-index generation:

`semantic-exact-v1-<provenance-hash>-<generation>.bin`

The file contains:

1. a 4096-byte versioned header,
2. one little-endian `int64` asset ID per vector,
3. alignment padding,
4. contiguous little-endian `float32` unit vectors.

The header records the engine/model/version, DB generation, vector count,
dimensions, vector offset, and SHA-256 of the payload. On open Lumine validates
the header, provenance, generation, file size, unique positive asset IDs, and
payload checksum before using the snapshot.

Vectors are memory-mapped read-only. The vector payload is not copied into the
Go heap. The asset-ID-to-position map remains in Go memory.

## Startup and rebuild behavior

At Semantic Search startup Lumine reads the current semantic-index generation
from SQLite and attempts to open the matching immutable snapshot.

- Valid current snapshot: mmap and use it directly.
- Missing/stale snapshot: rebuild the exact in-memory index from SQLite and
  persist an immutable snapshot in the background.
- Corrupt snapshot: remove that generation file, rebuild from SQLite, and
  persist again.
- Model/version change: use a different provenance-keyed snapshot.
- Old generations for the current model are cleaned up after a new snapshot is
  adopted. Snapshots belonging to other models are preserved.

Windows snapshot files are never replaced while mapped. A new generation gets a
new file name. This also allows another Lumine process to publish the same
generation safely: a losing writer validates and reuses the already-published
immutable file instead of deleting it.

## Incremental embeddings

Newly completed embeddings are inserted into an in-memory overlay immediately,
so they are searchable without rebuilding the base mmap.

A debounced persistence worker writes a new DB-generation snapshot. An embedding
can be written slightly before its analysis row becomes `ready`; if that
happens, the overlay entry is retained and the worker keeps retrying until the
new snapshot actually contains it. Only overlay entries represented by the
adopted snapshot are compacted away.

Semantic DB generation changes only for data that can affect the ready
Semantic Search join. Ordinary `queued` / `running` / `failed` lifecycle
changes do not invalidate a valid snapshot.

## Exactness

Search remains an exact dot product over normalized vectors (cosine similarity)
with deterministic asset-ID tie breaking. No ANN/HNSW approximation is used.

## Disk and memory footprint

Snapshot size is approximately:

`4096 + align(count * 8) + count * dimensions * 4` bytes.

For 768-dimensional vectors:

| Vector count | Snapshot size |
| ---: | ---: |
| 20,000 | 58.75 MiB |
| 100,000 | 293.74 MiB |

The mmap payload is file-backed rather than copied to the Go heap. The open
benchmark still touches the whole payload to verify SHA-256, so pages may enter
the OS file cache and are reclaimable by the OS.

## CI benchmark sample

GitHub Actions Linux runner, Go 1.23, one iteration, 768 dimensions:

| Benchmark | Result | Go heap/op |
| --- | ---: | ---: |
| Exact search 20k | 20.09 ms | 0.32 MiB |
| Persistent snapshot open/validate 20k | 45.62 ms (1.35 GB/s) | 0.61 MiB |
| Exact search 100k | 106.66 ms | 1.53 MiB |
| Persistent snapshot open/validate 100k | 222.56 ms (1.38 GB/s) | 2.69 MiB |

These are CI-runner measurements, not promises for user hardware. The important
startup property is that opening a valid snapshot no longer executes a
SQLite-BLOB read + float32 decode for every stored vector.

Run the benchmarks manually with:

```bash
go test ./internal/commands -run=^NO_TESTS$ -bench=^BenchmarkSemanticMemoryIndexSearch20K$ -benchtime=1x -benchmem
go test ./internal/commands -run=^NO_TESTS$ -bench=^BenchmarkSemanticMemoryIndexSearch100K$ -benchtime=1x -benchmem
go test ./internal/commands -run=^NO_TESTS$ -bench=^BenchmarkSemanticPersistentSnapshotOpen20K$ -benchtime=1x -benchmem
go test ./internal/commands -run=^NO_TESTS$ -bench=^BenchmarkSemanticPersistentSnapshotOpen100K$ -benchtime=1x -benchmem
```
