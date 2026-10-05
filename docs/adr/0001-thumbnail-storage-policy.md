# ADR 0001: Display thumbnail storage defaults to MemoryOnly

- Status: Accepted
- Date: 2026-10-02
- Issue: #388
- Parent product gate: #385
- Representative build: `a4e2057e6473fe51bf0d793c854b32c5605b9711`

## Context

Lumine v1 explicitly avoided a persistent display-thumbnail library. It decoded the original into bounded memory representations and treated the original local image as the source image.

The greenfield v2 Core initially adopted persistent WebP thumbnails because they made it straightforward to prove large-library responsiveness. That implementation is technically sound, bounded and fast, but it changes a stated Lumine product philosophy by creating a durable secondary image cache during ordinary browsing.

#388 implemented two storage policies on the same v2 pipeline:

- `PersistentDisk` — generated WebP display thumbnails are persisted beneath Lumine app data.
- `MemoryOnly` — the exact same generated WebP bytes are retained only in a bounded in-process encoded-memory LRU and passed directly to Viewer decoding.

Both policies share:

- the same libvips thumbnail profiles and color/orientation processing;
- the same source identity/metadata rules;
- the same foreground/background scheduling and cancellation;
- the same bounded decoded-bitmap caches;
- the same Viewer interaction sequence.

Image smoke proves the encoded WebP output is byte-identical for the same source/profile. Storage policy therefore does not change thumbnail image quality.

## Representative physical evidence

The A/D comparison used the same NativeAOT build and the same 3,651-asset Windows library.

### PersistentDisk

- Cold first viewport: 464.973 ms
- Cold max fast-scroll: 355.861 ms
- Warm first viewport: 33.875 ms
- Warm max fast-scroll: 35.028 ms
- Cold peak working set: 490,192,896 bytes
- Warm peak working set: 398,794,752 bytes
- Persistent display-thumbnail data: 94 files / 2,260,586 bytes
- Cold generated thumbnails: 94
- Warm generated thumbnails: 0

### MemoryOnly

- Cold first viewport: 604.512 ms
- Cold max fast-scroll: 550.112 ms
- Warm first viewport: 464.920 ms
- Warm max fast-scroll: 442.591 ms
- Cold peak working set: 497,209,344 bytes
- Warm peak working set: 495,828,992 bytes
- Persistent display-thumbnail data: 0 files / 0 bytes
- Encoded-memory cache after workload: 2,260,586 bytes of a 256 MiB bound
- Cold generated thumbnails: 94
- Warm generated thumbnails: 94

### Delta

MemoryOnly versus PersistentDisk:

- Cold first viewport: +139.539 ms
- Cold max fast-scroll: +194.251 ms
- Warm first viewport: +431.045 ms
- Warm max fast-scroll: +407.563 ms
- Cold peak working set: +7,016,448 bytes
- Warm peak working set: +97,034,240 bytes
- Persistent thumbnail bytes: -2,260,586 bytes

Both MemoryOnly Cold and Warm remained far inside the 1,500 ms product fast-scroll target. Neither policy produced Viewer tile failures. Metadata full-file hashing remained zero.

The synthetic Windows CI comparison also favored MemoryOnly for in-process work:

- cold generation: 76.1 ms PersistentDisk vs 45.2 ms MemoryOnly;
- 1,000 storage hits: 588.7 ms PersistentDisk vs 8.3 ms MemoryOnly;
- 64-request batch: 853.5 ms PersistentDisk vs 746.4 ms MemoryOnly;
- MemoryOnly persisted zero files.

The large Warm advantage of PersistentDisk on the representative run is expected: it reuses display thumbnails across processes. MemoryOnly intentionally does not.

## 2026-10-06 revalidation note (#536)

The final product-owner acceptance on merged develop
`9a9b30603f3d3c7e79800c6ef103ed6cb6d8f883` exposed a true cold-path
failure on the same representative Windows library family:

- 3,685 assets;
- MemoryOnly Cold first viewport: 1,661.186 ms;
- MemoryOnly Cold max fast-scroll: **4,593.624 ms**;
- MemoryOnly Cold Detail preview: **18,534.723 ms**;
- following Warm max fast-scroll: 853.165 ms;
- zero thumbnail failures and zero metadata full-file hash fallbacks.

The original #388 A/D runner always executed `PersistentDisk` first and
`MemoryOnly` second. Therefore the reported MemoryOnly "Cold" result was
application-cache cold, but could benefit from the OS filesystem/page cache
populated by the preceding PersistentDisk run. It was not a symmetric
machine-level cold comparison.

#536 corrects that benchmark interpretation and revalidates the product
default under the current canvas-first UI, which also exposes a substantially
larger image viewport than the earlier split Core shell.

The product default remains `MemoryOnly` while #536 is being fixed. This
note does **not** authorize silently switching ordinary browsing back to a
persistent display-thumbnail corpus. The first remediation is to make visible
work outrank background prefetch and restore shrink-on-load for Detail
preview. A hybrid or default-policy change still requires new representative
evidence and product-owner approval.

## Decision

**MemoryOnly is the Lumine v2 product default.**

PersistentDisk remains a supported explicit opt-in acceleration policy.

Rationale:

1. MemoryOnly satisfies the representative large-library responsiveness target with substantial margin.
2. It restores the v1 product invariant that ordinary browsing does not silently build a durable secondary display-thumbnail library.
3. It keeps resource use bounded.
4. It preserves the same thumbnail pixels and the stronger v2 native decode/scheduling architecture.
5. The measured PersistentDisk advantage is useful but is not required to meet the current product performance target.
6. The additional MemoryOnly working set remains within the explicit v2 memory bounds.

A hybrid policy is **not** introduced now. There is no current evidence that MemoryOnly needs disk assistance to meet the accepted product envelope. Hybrid complexity should only be added if a future representative workload demonstrates a real failure.

## Configuration contract

Persisted application setting:

```json
{
  "thumbnailStorageMode": "MemoryOnly"
}
```

Accepted values:

- `MemoryOnly` — product default.
- `PersistentDisk` — explicit opt-in acceleration.

The process environment variable `LUMINE_THUMBNAIL_STORAGE_MODE` may temporarily override the persisted value for diagnostics, benchmarks and acceptance. An environment override is never written back as the user preference.

A product-facing Settings UI is deferred to #396. The persistence/runtime contract is established here so that UI work does not redefine policy.

## Migration / cleanup

When the effective mode is MemoryOnly:

- Lumine does not write display thumbnails to the persistent thumbnail directory;
- an existing v2 persistent-thumbnail directory is atomically retired out of the active cache path before Viewer startup;
- retired cache directories are deleted best-effort outside the startup critical path;
- clean shutdown waits for the current retirement cleanup attempt;
- cleanup failures are logged and may be retried on a later launch;
- original images, the Library database and user metadata are never touched by this cleanup.

When the effective mode is PersistentDisk, the persistent cache is preserved and normal bounded cache maintenance applies.

Switching from PersistentDisk back to MemoryOnly retires the persistent cache on the next launch.

## Consequences

Positive:

- restores Lumine's no-persistent-display-thumbnail product philosophy;
- avoids normal-browsing disk growth;
- portable data contains no required display-thumbnail corpus;
- cache deletion/rebuild is no longer part of ordinary product correctness;
- v2 scheduling, source identity, cancellation and rendering improvements remain.

Trade-offs:

- process restart loses display-thumbnail reuse;
- Warm first viewport and Warm fast-scroll are materially slower than PersistentDisk;
- working set is higher during the representative Warm workload;
- source thumbnail generation repeats across processes.

These are accepted because the measured MemoryOnly path still satisfies the current user-facing performance target.

## Revisit criteria

Reconsider a hybrid or opt-in PersistentDisk recommendation only if representative evidence shows one or more of:

- MemoryOnly fast-scroll exceeds the accepted product gate;
- first viewport becomes subjectively unacceptable on supported hardware;
- repeated source decode causes harmful I/O/CPU behavior;
- a supported heavy image format cannot remain responsive within bounded memory;
- portable/local workflows explicitly benefit from user-chosen persistent acceleration.

Do not reintroduce PersistentDisk as the default solely because it benchmarks faster.
