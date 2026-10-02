# Lumine Product Principles

Status: product source of truth for Lumine v2  
Baseline: `release/v1.1.0` product behavior and specifications  
Implementation target: current `develop` greenfield .NET/Avalonia architecture

## 1. Why this document exists

Lumine v2 is a greenfield implementation, not a new product.

The v1 implementation may be replaced where its technology, performance, lifecycle, synchronization, or architecture is weak. The product philosophy, user-facing capability, information architecture, brand continuity, and interaction model are not silently discarded.

When a v2 design conflicts with a v1 product principle, the conflict must be explicit and resolved by a documented product/architecture decision. "Greenfield" is not sufficient justification for dropping product behavior.

## 2. Product identity

Lumine is a **local-first creative image archive and high-volume image manager**.

It is not merely:
- a folder browser,
- a thumbnail grid,
- a tag database,
- an AI search front-end.

The product serves two connected jobs:

1. **See and navigate large local image collections quickly.**
2. **Preserve the human meaning around those images**: organization, evaluation, creative context, lineage, grouping, and publication history.

The image itself remains central. Metadata and AI assist the image workflow rather than replacing it.

## 3. Invariants carried from v1

### P1 — Image viewing comes first

The primary interaction loop is browse -> inspect -> compare -> organize.

The viewer must remain responsive with large libraries. Background work must yield to visible/foreground interaction. The UI must avoid blank-frame churn, unnecessary reloads, and work that scales directly with total library size while the user is only looking at a small viewport.

### P2 — Local-first and original-file-first

The user's local image file is the source image.

Lumine must not require cloud storage, cloud processing, or remote accounts for the core image-management experience. The application must not silently duplicate the original collection into a second managed media library.

The v1 creative-workflow specification explicitly treats replacing the local file as the source image as a non-goal.

### P3 — Do not turn browsing into an opaque second image library

v1 explicitly states that display thumbnails are not persisted as separate generated files. Viewer-sized images are decoded from originals into a bounded memory cache.

That product intent is preserved:

- Lumine must not silently grow an effectively permanent duplicate thumbnail library as a normal consequence of browsing.
- memory caches must be bounded;
- any disk-assisted optimization must be bounded, disposable, transparent in settings/diagnostics, and justified against the v1 memory-first behavior;
- persistent disk acceleration may exist only as an explicit bounded opt-in.

#388 / ADR 0001 resolved this invariant: MemoryOnly is the v2 product default. PersistentDisk remains an explicit acceleration option because representative physical testing showed MemoryOnly stayed within the accepted responsiveness envelope without writing display thumbnails to disk.

### P4 — Bounded resource use

Large libraries must not imply a UI object, decoded bitmap, open handle, or memory footprint per asset.

Decode concurrency, decoded-image memory, prefetch, background work, query caches, and any optional disk cache must have explicit limits.

### P5 — Organization is user-owned

The user can organize images without AI and without an external service.

Core organization includes:
- libraries and folders,
- tags,
- notes,
- rating,
- favorite state,
- status,
- color labels,
- sorting and filtering.

Automatic suggestions may assist later, but user metadata remains first-class and usable independently.

### P6 — Multi-selection is a first-class workflow

Lumine is not restricted to one-image-at-a-time editing.

The product must support:
- single selection,
- Ctrl-style additive/toggle selection,
- Shift-style range selection,
- bulk organization actions,
- explicit clearing of selection.

v1 also uses multi-selection as the natural entry point for creative grouping and publication records.

### P7 — Lumine preserves creative context, not only file metadata

The v1 Creative Workflow specification defines Lumine as a creative archive that preserves how images belong together and how they were derived/published.

The domain concepts that define this direction are:

- **Asset** — one physical image file plus local metadata.
- **Work** — a human-facing creative unit containing ordered assets.
- **Generation Group** — assets that share a generation intent/run family and generation context.
- **Asset Relation** — directed lineage such as variation, img2img, inpaint, outpaint, upscale, crop, edit, animation, reference.
- **Publication** — a snapshot of what was actually published, including ordered images and destination-specific information.

Group membership and lineage are separate concepts. Ordered membership matters. Manual organization must work without automatic extraction.

### P8 — Context must be visible, not hidden behind IDs

The UI should answer human questions:
- What am I looking at?
- Where is it?
- How is it organized?
- What work/group does it belong to?
- What came before/after it?
- Where and how was it published?

Names, relationship direction, ordered membership, tags, status and publication copy are more important in the main UX than internal database identifiers.

### P9 — Fast browsing and deep inspection are different workloads

Grid/list browsing should favor low latency and bounded work.

Detail/full-screen viewing may spend more resources for quality when explicitly requested. Fit, previous/next, zoom, pan and 1:1/original inspection are part of the viewer contract.

The product may use different image-quality/resource policies for browsing and deliberate inspection.

### P10 — Stable visual continuity, improved UX

v2 must look like a successor to Lumine rather than an unrelated default Avalonia sample.

Visual invariants from v1 include:
- dark image-focused workspace;
- Lumine logo/icon visible as a brand anchor;
- compact desktop information density;
- persistent left-side navigation/workspace concept;
- central image browsing surface;
- top-level search/view/sort/filter controls;
- right-side contextual detail when appropriate;
- clear selected/active states;
- subdued chrome that does not compete with images.

These are not a requirement for pixel-identical reproduction. Layout, typography, spacing, accessibility, density, discoverability and responsive behavior should be improved when doing so preserves the Lumine mental model.

### P11 — The core navigation model is durable

The v1 navigation model exposes product concepts rather than implementation concepts:

- Libraries
- Folders
- Tags
- Publication history
- Settings

The v2 information architecture may refine or regroup these surfaces, but their user jobs must remain reachable without burying them behind diagnostics or developer-oriented UI.

### P12 — Search/filter state must be legible and reversible

Search, folder scope, status, rating and tags can all narrow the visible collection.

Active constraints must be visible, individually removable, and clearable as a group. The user should not wonder why an image disappeared.

Viewer preferences such as view mode, sorting and thumbnail size should survive normal restart.

### P13 — Destructive actions require clear application-owned confirmation

Deleting source files or destructive metadata/workflow actions must not happen through ambiguous clicks.

v1 specifically rejects browser/JavaScript `alert`, `confirm`, and `prompt` as application UX. v2 should use native/application-owned dialogs with clear scope, consequence and recovery limitations.

### P14 — Failure and background activity are visible

Scanning, loading, synchronization and failures must have understandable states.

The user should be able to distinguish:
- loading,
- empty result,
- filtered/no-match result,
- unsupported preview,
- error,
- background scan/sync progress.

A silent stall is not acceptable product behavior.

### P15 — AI is optional enhancement, not product identity

Lumine Core must remain a complete local image-management and creative-archive application with AI disabled or absent.

AI may later add semantic retrieval, tagging, captioning, prompt assistance and analysis, but it must build on the same user-owned library, metadata and creative domain rather than become a parallel product.

## 4. v1 interaction/feature baseline

The v2 product plan must account for at least the following v1 capabilities.

### Library/navigation
- add image folder as a library;
- switch libraries;
- library enable/disable/manage actions;
- folder-tree navigation;
- tag navigation/management;
- publication-history navigation;
- settings surface;
- initial welcome/onboarding state;
- scan/progress/error states.

### Browse
- grid and list modes;
- virtualized large-library browsing;
- user-controlled thumbnail size;
- search;
- sort field and direction;
- folder/status/rating/tag filtering;
- visible filter chips and clear-all;
- single, additive and range selection;
- visible selected count;
- no blank flash when an already-decoded representation can be reused.

### Organize
- rating;
- favorite;
- color label;
- status;
- tags;
- notes;
- bulk operations;
- explicit source-file deletion flow.

### Inspect
- image detail;
- full-screen/image viewer;
- previous/next;
- fit;
- zoom;
- pan;
- actual/1:1 inspection;
- technical/contextual metadata.

### Creative archive
- works;
- generation groups and generation context;
- directed asset relations;
- ordered membership;
- publication snapshots;
- destination-aware publication fields;
- manual creative organization without AI.

### Desktop quality
- Lumine icon/logo and recognizable brand;
- Japanese-first labels;
- dark image-focused visual hierarchy;
- keyboard shortcuts for frequent operations;
- persisted viewer preferences;
- portable/local storage behavior;
- application-owned dialogs;
- useful loading/error/empty states.

## 5. What v2 may improve

v2 is expected to improve:
- startup and browsing latency;
- memory/handle/resource bounds;
- filesystem synchronization;
- shutdown/lifecycle correctness;
- native rendering;
- accessibility/focus behavior;
- keyboard discoverability;
- state clarity;
- information density;
- responsive sizing;
- search/filter composition;
- visual polish;
- settings organization;
- diagnostics;
- implementation maintainability.

Improvement must preserve the user's mental model unless a product change is explicitly approved.

## 6. Change-control rule

For every v1-facing capability encountered during #386, classify it:

- **KEEP** — behavior/identity is already appropriate and must remain.
- **IMPROVE** — preserve the product intent but improve its UI/UX.
- **REIMPLEMENT** — same requirement, new v2 implementation.
- **RECONSIDER** — requirement conflicts with new performance/technical evidence and needs a benchmark/ADR.
- **DROP** — only after explicit product-owner approval.

No user-facing v1 capability is dropped by omission.
