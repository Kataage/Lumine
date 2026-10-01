# Lumine v1 -> v2 Product Gap Audit

Parent: #385  
Audit issue: #386  
Baseline: `release/v1.1.0`  
v2 baseline: `develop@9a262a5a5a23845f4950d1f1fd147962fb3bbc88`

This audit is about **product parity and product intent**, not source-code parity.

## 1. Executive finding

The greenfield v2 Core has established a substantially stronger technical foundation for performance, bounded resources, filesystem synchronization, lifecycle and NativeAOT deployment.

It has **not** yet re-established the Lumine product.

Current v2 App composition is intentionally minimal:
- open one folder;
- show a thumbnail grid;
- show a detail viewer;
- expose runtime diagnostics.

That was sufficient for Core acceptance, but it omits most of the v1 product shell, organization workflows, creative archive concepts, branding and navigation.

Therefore #295 is treated as the **technical Core gate**, while #385 is the **product gate before AI**.

## 2. Product-gap matrix

| Area | v1 behavior / intent | current v2 | Classification | Target v2 |
|---|---|---|---|---|
| Brand | Lumine icon/logo, dark branded shell, "制作アーカイブ" identity | generic Fluent shell, title "Lumine v2", no product shell | REIMPLEMENT + IMPROVE | recognizable Lumine v2 design system and icon |
| Welcome | branded onboarding, explains local/no-copy/no-disk-thumbnail behavior | developer-style "open folder" placeholder | REIMPLEMENT | polished first-run/onboarding |
| Libraries | registered libraries, selection and management | direct folder open only in App shell | REIMPLEMENT | persistent library workspace using v2 Library Core |
| Folders | hierarchical folder filtering | backend paths exist; no product navigation | REIMPLEMENT | fast folder tree / scope navigation |
| Tags | tag navigation and management | metadata schema/query supports tags; no product UI | REIMPLEMENT | tag browser + create/manage/filter |
| Publication history | first-class sidebar destination | absent | REIMPLEMENT | restore publication history surface |
| Settings | viewer/cache/extensions and product settings | diagnostics + low-level settings foundation | REIMPLEMENT + IMPROVE | cohesive product settings |
| Grid | virtualized image grid | present and fast | KEEP + IMPROVE | retain v2 engine, rebuild presentation/interaction |
| List | alternate list view | absent | REIMPLEMENT | restore list mode |
| Thumbnail size | Small/Medium/Large style control persisted | fixed v2 tile policy | REIMPLEMENT | continuous or stepped density control |
| Search | filename/memo-oriented search | backend text/metadata query exists; no product search UI | REIMPLEMENT | top-level immediate search |
| Sort | modified/created/name/size/rating/status in v1 | backend currently smaller sort set | REIMPLEMENT | restore user-facing sort set where data exists |
| Filters | folder/status/rating/tags + visible chips | backend query supports tags/rating/favorite/status/color; no product filter UX | REIMPLEMENT + IMPROVE | composable filters with legible active state |
| Selection | single, Ctrl toggle, Shift range | current grid is centered on selected asset, not v1 multi-select workflow | REIMPLEMENT | desktop multi/range selection |
| Bulk actions | rating/status/favorite/color/work/publication/delete | absent | REIMPLEMENT | context-aware bulk action surface |
| Rating | 1–5 + shortcut | backend metadata exists | REIMPLEMENT | fast keyboard/mouse editing |
| Favorite | metadata + shortcut | backend exists | REIMPLEMENT | visible quick action/filter |
| Status | unsorted/reviewed/candidate/published | generic string field exists | REIMPLEMENT | restore product vocabulary/configuration |
| Color label | per-asset/bulk | backend exists | REIMPLEMENT | restore visual label workflow |
| Notes | per-asset notes/search context | backend exists | REIMPLEMENT | detail editor |
| Detail panel | contextual right-side panel | technical detail viewer exists | REIMPLEMENT + IMPROVE | combine visual inspection + organization/context |
| Full-screen viewer | modal/full image inspection | detail viewer occupies permanent split pane | REIMPLEMENT + IMPROVE | focused image-view mode with fast navigation |
| Prev/next | keyboard/button navigation | v2 Detail has prev/next | KEEP + IMPROVE | integrate into product viewer |
| Fit/zoom/pan/1:1 | explicit image-inspection tools | implemented in v2 Detail engine | KEEP + IMPROVE | preserve engine, redesign controls |
| Loading/empty/error | branded/user-oriented states | mostly technical placeholders | REIMPLEMENT | complete state language |
| Shortcuts | rating/favorite/detail plus viewer navigation | partial viewer keyboard behavior | REIMPLEMENT | documented consistent shortcut map |
| Work | human creative unit | absent | REIMPLEMENT |
| Generation Group | generation context + ordered assets | absent | REIMPLEMENT |
| Asset Relation | directed lineage | absent | REIMPLEMENT |
| Publication | ordered publication snapshot and platform metadata | absent | REIMPLEMENT |
| Creative organizer | multi-select grouping/linking | absent | REIMPLEMENT |
| Source file ownership | local file stays source | preserved | KEEP | remain invariant |
| Thumbnail philosophy | no persistent display-thumbnail files; bounded memory ImageBitmap LRU | persistent WebP cache is normal grid source | RECONSIDER | benchmark/ADR; v1 philosophy is default product constraint |
| Decode scheduling | priority-limited memory decode, queued priority promotion | v2 has bounded foreground/background thumbnail pipeline | KEEP concept | reuse stronger v2 arbitration |
| Background sync | v1 periodically called sync; known weakness | v2 event-driven watcher/USN/reconcile fallback | KEEP v2 | do not regress to periodic full scan |
| Portable/local | local app data / portable-local storage | v2 AppDataPaths foundation exists | REIMPLEMENT product UX | clear portable behavior/settings |
| Dialogs | application-owned destructive dialogs | product dialogs not built yet | REIMPLEMENT | native/app-owned confirmation |
| AI independence | manual organization works without AI | Core is AI-free | KEEP | AI remains optional |

## 3. Lumine information architecture to preserve

The v1 shell exposes a durable mental model:

```text
Lumine
├─ Libraries
├─ Folders
├─ Tags
├─ Publication history
└─ Settings

Main workspace
├─ search
├─ sort / view / density
├─ active filters
├─ image grid/list
├─ multi-selection actions
└─ contextual detail

Focused image view
├─ previous / next
├─ fit / zoom / pan / 1:1
└─ image / metadata / creative context
```

The exact controls and panel widths may change. The user jobs represented by this hierarchy must not disappear.

## 4. Target v2 UX direction

### Shell

Use the v1 hierarchy, but reduce unnecessary chrome:
- narrow branded navigation rail or compact sidebar;
- expandable workspace panel for Libraries/Folders/Tags/Publications/Settings;
- central image surface gets the majority of area;
- top command/search region is compact and stable;
- detail appears on demand instead of permanently consuming a large percentage of the grid.

### Browse

Optimize for image scanning:
- images dominate tiles;
- filename/metadata decoration is secondary and configurable;
- density changes do not blank the viewport;
- selection state remains obvious;
- scrolling reversals and jumps preserve responsiveness;
- visible work outranks prefetch/background work.

### Detail

Separate two modes:
1. **context panel** for organization/metadata/creative information;
2. **focused viewer** for visual inspection.

Do not force both responsibilities into the always-visible two-column Core acceptance shell.

### Organization

Common actions should be possible without opening a heavy editor:
- rate/favorite/status/color from selection or detail;
- tag/notes/context from detail;
- multi-selection exposes bulk and creative actions;
- active filters remain visible/reversible.

### Creative workflow

Reintroduce the v1 creative-domain concepts after the basic shell/metadata workflow is stable.

Do not require AI inference to create Works, Generation Groups, Relations or Publications.

## 5. Thumbnail/cache decision to make before product Viewer implementation is final

### v1 baseline

v1:
- fetched the original through a local file endpoint;
- used `createImageBitmap` sized to the display target;
- kept decoded representations in an LRU-like memory cache;
- defaulted to 1024 MiB;
- exposed 256 / 512 / 1024 / 2048 MiB memory budgets;
- limited concurrent decodes and gave visible work higher priority;
- reused the nearest decoded representation as an immediate placeholder;
- explicitly avoided generated display thumbnails on disk.

### current v2

v2:
- uses libvips thumbnail generation;
- persists WebP thumbnail files;
- uses that persistent cache as the normal grid source;
- bounds disk and decoded memory usage;
- has proven very strong cold/warm scrolling after #375/#380/#383 fixes.

### decision

Do not delete the proven v2 path before measurement.

Benchmark these policies with the same representative library and acceptance tooling:

A. **memory-only** — v1 product philosophy implemented with libvips/native v2 scheduling.  
B. **memory-first hybrid** — no normal persistent thumbnail library; bounded disposable disk assistance only for demonstrably heavy cases.  
C. **opt-in persistent cache** — memory-first by default, persistent acceleration explicitly user-enabled.  
D. **current persistent cache** — performance control/baseline.

Measure:
- startup,
- first viewport,
- fast-scroll,
- direction reversal,
- warm revisit,
- source reads,
- total bytes read,
- CPU,
- peak RAM,
- disk growth,
- cache cleanup,
- portable behavior,
- image quality.

Product constraint: A/B/C are preferred if they can preserve practical v2 responsiveness. D requires an explicit product-owner decision because it changes a stated v1 product philosophy.

## 6. Work order

Do not start AI after #295. Use this order:

1. **Product principles / gap audit** — this document + `product-principles.md`.
2. **Image cache ADR benchmark** — settle the v1 no-persistent-thumbnail philosophy against v2 performance evidence.
3. **Design system + branded App shell** — logo/icon, dark visual tokens, navigation, welcome/loading/error shell.
4. **Library / Folder / Tag navigation**.
5. **Grid + List + density + search/sort/filter**.
6. **Desktop selection + bulk organization actions**.
7. **Contextual detail metadata editor**.
8. **Focused/full-screen image viewer** using the proven v2 Detail engine.
9. **Creative archive domain/UI** — Work / Generation Group / Relation / Publication.
10. **Settings / portable / diagnostics integration**.
11. **Product acceptance** on the representative Windows library.
12. Only then start the AI research/benchmark phase.

## 7. Product acceptance definition

#385 does not pass because screens exist.

It passes only when:
- a v1 user can identify the application as Lumine without explanation;
- all baseline user jobs in this audit are implemented or explicitly approved for later scope;
- browsing remains within the v2 Core performance envelope;
- selection, organization, detail and focused viewing are usable without AI;
- cache behavior matches the approved product philosophy;
- background synchronization does not reintroduce periodic full-library work;
- local/portable behavior is understandable;
- no unresolved P0/P1 product issue remains;
- a human usability pass confirms the application feels coherent rather than like stitched-together Core controls.
