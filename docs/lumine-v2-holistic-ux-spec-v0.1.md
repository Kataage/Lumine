# Lumine v2 — holistic image-first UX contract, v0.1 (2026-10-10)

**Owner Issue:** [#635](https://github.com/Kataage/Lumine/issues/635) · **Status: PROPOSED, NOT IMPLEMENTED** · Baseline: `develop@839df08108e392e9925c2006b15fa731617f9b3a`. Review evidence: [automated-screenshot audit](lumine-v2-ui-audit-2026-10-10.md).

This is a **whole-product layout and interaction specification before new code churn**, not another cosmetic PR. Protect the approved Lumine dark palette and the local-first/portable/no-AI Core. The v1 philosophy is defined in [product-principles.md](product-principles.md), not superseded by this document.

## 1. Design thesis and decisions

**Primary job:** browse and view images with minimal friction. **Secondary job:** organize them in place. **Tertiary job:** manage advanced creative/archive context, storage and diagnostics. Information architecture stays product-based: Library, Folders, Tags, Publication, Settings; search, sort/filter/view and Inspector are actions/contexts **within** that workspace, not peer global navigation destinations.

These principles are adapted from official 2026 Apple HIG, not copied native/macOS widgets: [design principles](https://developer.apple.com/design/human-interface-guidelines/design-principles) (purpose/agency/simplicity/craft), [layout](https://developer.apple.com/design/human-interface-guidelines/layout) (content and alignment, Sept 2026), [sidebars](https://developer.apple.com/design/human-interface-guidelines/sidebars) (hierarchy/adaptability) and [toolbars](https://developer.apple.com/design/human-interface-guidelines/toolbars) (frequent actions, overflow). **Windows Avalonia** focus, keyboard navigation, familiar context menus, text scale and contrast win when a macOS idiom conflicts.

**Decision D01 — one primary navigation model:** the same Library/Folder/Tag/Publication/Settings destinations at every size. At <1200 DIP show the existing 72-DIP rail plus a dismissible pane. At >=1200 an optionally pinned **single** 288-DIP sidebar replaces, rather than accompanies, the rail. Do not default two permanent navigation columns.

**Decision D02 — stable image canvas:** Browse is a single image area with one command row and optional **separate** filter chip shelf. Entering selection mode shows a contextual bottom action surface, not a replacement for the command bar; no resize of tile width on selection. Do not move a selected tile under obscuring chrome.

**Decision D03 — Inspector is explicitly secondary:** open on deliberate selection/Info command, keep its four existing jobs `整理 / 制作 / 公開 / 情報`. Below 1600 DIP it is a dismissible drawer, never a permanent second column; wide pinned layouts may dedicate 320–380 DIP. Compact forms use internal scrolling with a persistent header and close button. Preserve lower fields; do **not** replace them with truncated strings.

**Decision D04 — Focused Viewer is image-first:** full client image plane; compact, discoverable prev/next, Fit, 1:1, zoom, Info, fullscreen and Back/close overlay. Controls must remain keyboard reachable and return focus and scroll anchor to the Browse item. No preview-sized permanent detail pane in the image-view mode.

**Decision D05 — one semantic design system:** reuse `LumineVisualPalette` -> `LumineDesign` -> `ViewerVisualTokens`, plus `LumineProductStyles.axaml`. New components use semantic tokens; do not fork hex colors, font metrics or duplicated styles. Keep `MemoryOnly` default.

## 2. Annotated before/after composition drafts

Layouts below are **engineering proposals** for the complete Library→Browse→select→Inspector→Focused Viewer→Back journey, not pixels sampled from original screenshots. Exact source baselines are in [audit](lumine-v2-ui-audit-2026-10-10.md).

### Compact 900×600, 100% text

**Observed current (layout behavior, simplified):**

```text
┌──── 72 rail ─┬───────── 828 Browse workspace ───────────────────────────┐
│ destinations │ Search                         Filter  Display            │
│              ├─────────────────────────────────────────────────────────┤
│ library/etc  │ Grid/List images                                 [       │
│              │      (2 synthetic fixture tiles only)           Inspector│
│              │                                                 overlay ]│
│ Settings     │                                                        │
└──────────────┴─────────────────────────────────────────────────────────┘
A: width <1200 => secondary nav floats above Browse
B: width <1600 => Inspector overlays right side (~300-340 DIP)
```

**Proposed first-prototype target:**

```text
┌──── 72 rail ─┬────────── Browse header (max 56 DIP) ───────────────────┐
│ [Library]    │ [Library/scope title]  [Search]        [Filter] [Display]│
│ [Folders]    ├──── active chips (when filters exist; not empty gap) ───┤
│ [Tags]       │                                                         │
│ [Published]  │     VIRTUALIZED IMAGE GRID / LIST                       │
│              │     focus and selection are visible in-place           │
│              │     (density chosen by user, not fixed by redesign)    │
│              │                                                         │
│ [Settings]   │             [selection actions only when selected]      │
└──────────────┴─────────────────────────────────────────────────────────┘
C: open Inspector => anchored right DRAWER with labeled close/Back
D: open navigation => transient pane; Escape/light dismiss restores focus
E: focused viewer => replaces browsing chrome with image + compact controls
```

At 900×600 and 225% text, **do not** force Search, Filter and Display to shrink to illegible heights. Keep Search and one clear overflow/disclosure control reachable; if the header requires an extra row, reduce lower-priority adornments first. The exact breakpoint must be derived from **measured desired size** (Avalonia actual measure/arrange), not a guessed pixel threshold or manual fixed font shrinking. No new 225%-specific duplicate styles.

### 1440×900, default or pinned navigation

**Observed present two states:** a 72-DIP rail if not pinned, or an **unified 288-DIP sidebar** when pinned; not both. Inspector remains a right overlay below 1600.

**Proposed:**

```text
┌──────── nav 288 optional ──────┬────────── Browse 1152 DIP when pinned ─────────┐
│ Lumine + Library/Folder/Tag    │ Scope/title      Search       Filter  Display  │
│ Publication + Settings        ├───────── active chips when filtering ─────────┤
│                                │                                               │
│ [scannable current collection] │        LARGE VIRTUALIZED IMAGE CANVAS          │
│                                │        metadata secondary to thumbnails       │
│                                │                                               │
└────────────────────────────────┴───────────────────────────────────────────────┘
F: Inspector not permanently subtracting 300-340 DIP at 1440
G: intentional drawer open keeps selected item/scroll context unchanged
```

**Canvas width floor proposals:** with Inspector hidden, Browse gets >=**828 DIP at 900** and >=**1152 DIP at 1440 pinned**, before the existing gutters, i.e. >=92%/80% of the full window respectively. Never add a third always-visible fixed pane to reduce these. At 1920 dual pin may reserve 288+380, leaving >=1252 DIP before gutters. **These are contract arithmetic targets, not achieved performance metrics.**

### One complete journey prototype contract

```text
Library selection
  → Browse Grid/List (search/filter/sort/density retained)
  → Select 1 / Ctrl or Shift multi-select (no Browse jump)
  → Contextual Inspector: Organize | Creative | Publication | Info
  → Focused Viewer (image takes client area; prev/next, Fit, 1:1)
  → Back/Escape to same Library, scope, filter, selected item, scroll anchor
```

Focus restoration must target a **live, enabled, visible** Browse control. Do not refocus a recycled virtualized tile without resolving its current instance. Dialog dismissal likewise returns to a live invoker.

## 3. Shared visual grammar (preserve vs change)

| Layer | Explicit v0.1 rule |
|---|---|
| Brand | Keep icon/Lumine name, Japanese-first labels, existing `#09090B/#0D0D10/#151518` dark hierarchy and neutral accent. **No bright new app accent** without later approval |
| Typography | Reuse Yu Gothic UI/Yu Gothic/Meiryo/Segoe UI. Body **14 DIP**, caption **12**, emphasis **18**, brand **24** at 100%, multiplied by existing independent Windows text factor up to **2.25**. No 10-DIP product labels for essential actions |
| Spacing | Reuse **4/8/12/16/24 DIP** major rhythm; **2/6** only for internal fine alignment. Align command/selection/Inspector on shared content edges; avoid ad-hoc per-window margins |
| Radius and borders | Reuse existing 8-DIP control / 10-DIP panel radius. Strong borders only for semantic focus/selection/error or delineating a meaningful pane, not every nested group |
| Primary action | Only one prominent action per state; ordinary frequent view commands together, destructive in labeled confirm, advanced in secondary flyout/overflow. Never put dangerous Delete adjacent to a near-identical neutral icon |
| State messages | Welcome, Indexing, Empty, NoMatch, Error each present **state + impact + one primary recovery action**. NoMatch must identify active filters and offer clear/edit without clearing Search/Sort unintentionally |
| Selection | Single/Ctrl/Shift preserved, selected count readable. Selected state indicated by check/shape and focus border, not color alone |
| Information density | Image tile size and captions remain user-configurable. Hide advanced data behind Inspector tab/section disclosure **without removing capability** |
| Windows interaction | Visible focus ring, keyboard order, context menus and 100–225% text sizing. Tooltips aid icons, not their sole means of being understood |

## 4. Semantic component architecture for subsequent PRs

Do NOT implement all surfaces simultaneously. New shared pieces should first be modeled in the existing App/Viewer design boundary:

1. **`ProductWorkspaceLayout`** (proposed coordinator, not yet a new class): one source for nav/pinned/drawer geometry; no independent local breakpoints for every page. Never use absolute positioning to fake available canvas.
2. **`BrowseCommandSurface`**: keep existing search/filter/display behavior, refactor presentation into frequent controls and a conditional chip shelf; keyboard focus and filter state not recreated on layout.
3. **`ContextualActionSurface`**: selection bar and Inspector entry are layered without changing the virtualized gallery’s scroll metrics; no second image request pipeline.
4. **`InspectorDisclosure`**: four tabs, frequent edit first, advanced sections collapsible, one scroll owner and sticky accessible header.
5. **`FocusedViewerChrome`**: one toolbar grammar shared with Browse; centered image/keyboard shortcuts using existing bounded Detail session.
6. **`ProductStateSurface`**: shared state-message contract and consistent recovery/focus return.
7. **`ProductFormField`**: shared label/help/validation for Settings and organization dialogs, not a style duplication of Avalonia controls.

The exact class names are illustrative until implementation review. The existing `MainWindow`, `CoreViewerShell`, `BrowseWorkspaceControls`, `ContextualAssetDetailPanel`, `ProductSettingsView` and `LumineProductStyles.axaml` remain the primary integration sites.

## 5. Acceptance invariants (design targets; not yet proven)

| ID | Required check and evidence | Automation vs owner |
|---|---|---|
| UX-01 | One global nav model, all five destinations reachable at 900/1024/1440/1920; no two permanently competing left panes | geometry + keyboard smoke, full Windows owner |
| UX-02 | With Inspector hidden and nav pinned, Browse canvas width >=828 at 900 compact and >=1152 at 1440; no unexpected resize on selection | measured Avalonia bounds / screenshot comparison |
| UX-03 | Frequent Search, Filter and Display identifiable and keyboard reachable; active chips individually removable, clear-all never erases unrelated Search/Sort state | interaction smoke; reproducing focus removal path mandatory |
| UX-04 | Inspector opens/closes with focus returned, preserves selection/scroll; four tabs and fields usable at 900×600 and 225% without clipped required actions | headless focus/bounds + physical keyboard/UI |
| UX-05 | Focused image remains full-client; Fit/1:1/zoom/prev/next/back and info are visibly discoverable, focus restored | App/Viewer smoke, before/after PNG + owner |
| UX-06 | Empty/loading/error/no-match clearly distinguish state and recovery; no silent progress or overloaded technical copy | test states across resolutions |
| UX-07 | No visible controls clipped at 900×600 / 1024×768 / 1440×900 / 1920×1080 with 100/125/150/200/225% text factor; high-contrast/focus and reachability | 20-case geometry evidence plus physical Windows accessibility review |
| UX-08 | 10k/100k virtualized scrolling bounded; ordinary next-row Bitmap reuse and 10-routed-wheel Grid/List **0 first-raster unready**; 10k source requests <=1600, Bitmap <=32MiB | unchanged strict CI. **Real physical GPU #634 stays separately open** |
| UX-09 | MemoryOnly default, portable NativeAOT, local originals untouched, no WebView/cloud services forced | Core/portable smoke, app verification |
| UX-10 | No user-facing capability silently dropped; Library/Folder/Tag/Publication, Work/GenerationGroup/Relation, metadata editing and bulk actions reachable | explicit v1 parity matrix + owner acceptance |

**Failure handling:** A screenshot artifact existing is not a UX pass. Each visual or keyboard case needs a pass/fail/blocked/not-run entry; no new label 'Apple-level achieved' from a build compiling. Before-after comparisons must use unchanged baseline PNG file names from [CI #38028622123](https://github.com/Kataage/Lumine/actions/runs/38028622123), and a new PR must produce matching after snapshots plus geometry/focus assertions.

## 6. Incremental execution plan (one bounded PR per part of #635)

1. **PR A (this design stage):** screen/code audit + v0.1 design spec and source evidence links. **No production UI changes**, no product-quality claim; get design direction auditable before a broad refactor.
2. **PR B:** prototype **one complete journey** Library→Browse→selection→Inspector→Focused Viewer→Back at 900 and 1440, using existing UI logic; adjust shared layout/presentation only. Capture paired before/after screenshots and exact bounds/keyboard focus assertions. No backend/thumbnail queue changes.
3. **PR C:** roll approved grammar through Inspector tabs and metadata / bulk selection forms; preserve workflow state and accessible focus.
4. **PR D:** Library/Folder/Tags/Publication navigation, Settings, empty/loading/error and dialogs from **the same tokens/components**.
5. **PR E:** full display/text-scale matrix + real Windows physical owner review, edit failures into focused child issues. #635 remains **OPEN** until user explicitly approves final product UX. #634 likewise stays open until its distinct physical scroll gate passes.

## Non-goals / change control

- No pixel copying of macOS, no WebView or heavyweight third-party UI library, no change to the local archive data model and no AI dependency.
- No increase to viewport realization or prefetch, no weakening 1,600 request / 32MiB native Bitmap / 100k virtualization limits.
- No deleting advanced metadata/settings just to make screenshots clean.
- No unapproved design rebrand; retain Lumine’s Japanese-first identity.
- No claim that headless Skia PNGs are physical Windows GPU-present evidence.

**Versioning:** v0.1 is a draft. Any later change to nav jobs, palette, canvas-width minimums or accepted functionality requires a dated v0.2 diff entry and explicit issue-level rationale; do not overwrite the design agreement silently.


### Stage 2A — contextual Browse header prototype (2026-10-10)

The first bounded implementation changes the **existing Browse command row** to keep a live two-line **library + folder-scope** context at the start of Search/Filter/Display; it does not add a persistent sidebar, rebuild the Browse query controls, change Grid/List request paths or move the Inspector/Focused Viewer layers. The context uses only the registered human Library name and **last folder segment**, never the full path. The same 184-DIP semantic context region works at 900 and 1440 and ellipsizes long titles, while Search remains >=260 DIP and the two explicit command buttons remain reachable. Existing screen captures will be regenerated at the same names for paired comparison. Geometry, complete focus and broad journey tests remain necessary, and this is **a prototype sub-step, not the completion of the holistic journey**.

Native Ave/Viewer performance budgets and owner acceptance remain unchanged. Phase 2B will consolidate navigation, Inspector/Focused Viewer focus and return before declaring the complete end-to-end prototype.


### Stage 2B — Inspector → Focused Viewer → origin-aware return (2026-10-10)

The second bounded transition preserves the **actual keyboard invocation source** when opening Focused Viewer. A user who invokes `画像を表示` from a live Inspector control must return to that same enabled/attached/visible command after the modal is dismissed, even if they navigated to another image inside the focused viewer; Browse selection remains unchanged. Invocations from a virtualized grid tile keep the previous **semantic asset-index** focus restoration rather than retaining a recycled `Control`. If Inspector closes, disables or detaches while the modal is active, return to the selected grid asset safely instead of forcing focus into a hidden control. This is a narrow real UX change and corresponding mounted MainWindow smoke regression, not a claim that the complete cross-product design is finished.


### Stage 2C — deliberate Inspector keyboard entry (2026-10-10)

When the gallery itself owns keyboard focus and a selected image exists, plain **I** opens Inspector and transfers focus to the **currently selected Inspector tab** after layout; it does not reset the tab or its internal scrolling. Mouse/context-menu and programmatic Inspector opens intentionally retain their existing keyboard-focus behavior. The deferred transfer is contingent on the selected Gallery asset still being the original one, Inspector remaining visible and the user's focus still being on the Gallery; it never steals focus after an intervening command or while a Focused Viewer lightbox is modal. Explicit Inspector close returns to the invoking virtualized asset as before. Mounted App smoke tests the full I→tab focus→close→asset-focus sequence. This bounded increment does not claim a full holistic visual redesign or physical-owner acceptance.


### Stage 2D — short-window Inspector image-first metadata density (2026-10-10)

The Inspector retains its current four tabs and edit fields, but the **secondary preview** in a short compact-height window now occupies **80 DIP** instead of **120 DIP** when the actually arranged shell height is below **720 DIP**. A compact Inspector in a taller window remains at 120 DIP, a wide/docked Inspector at 148 DIP. The main Gallery/Focused Viewer image sizing, full-size/detail images, 14/12 DIP text size, tab control, label size, keyboard focus, selection/scroll state and photo original are unchanged. Width alone is insufficient for this constraint: `CoreViewerShell.SizeChanged` forwards actual height to the Inspector; later geometry reapplications read current bounds. Automated 900×600 and 1440×900 App acceptance measures the expected preview height and captures the existing Inspector screenshot names to reveal any real difference. This is one responsive composition improvement, not a claim of whole-product owner acceptance.

### Stage 2E — compact Inspector summary composition (2026-10-10)

On compact short desktop canvases (arranged shell height **below 720 DIP**), the secondary Inspector now presents the existing bounded **80-DIP preview in a 96-DIP-wide leading column**, alongside the **same live filename and image facts** rather than as three full-width rows above the four edit tabs. The actual metadata and image preview controls are moved by Grid row/column assignment only; they are **not reconstructed** on resize or selection, so preview lease and tab scroll state are retained. The summary text may wrap instead of silently clipping. At >=720 DIP the original stacked 120-DIP compact presentation remains, while the wide 148-DIP Inspector stays unchanged. The new mounted App smoke checks responsive layout state across the existing window matrix and the actual 900x600 Inspector route. Reuse existing named 900x600/1440x900/225%-text PNGs to compare geometry; these do **not** prove physical owner acceptance. 

This is a bounded **information hierarchy** change under #635, not a smaller font, a separate Inspector or a weakening of the #634 Viewer/MemoryOnly/portable performance contract. If 225% text or long filenames overflow the available right column, retain full information via wrapping/accessible controls and record the test as failed rather than shortening source metadata.
