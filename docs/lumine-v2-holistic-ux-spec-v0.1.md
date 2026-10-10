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

### Stage 2F — Organize action accessibility at compact heights (2026-10-10)

**Problem observed from source and 900×600 capture:** the Organize tab previously placed Save, Reset and Retry/status after Notes inside the same ScrollViewer. On short Windows canvases the user had to scroll through metadata before reaching the action that commits edits. This hides the main action behind lower-priority fields.

**Bounded implementation:** keep the original Save/Reset/Retry/status controls in a persistent footer row outside the Organize tab's ScrollViewer. The row is visible only on Organize; Creative/Publication/Information tabs keep independent scroll contents without blank footer space. Existing Ctrl+S, dirty state, persistence, validation, retry and keyboard focus are retained. At 150–225% Windows text factor the buttons wrap inside the same footer rather than clip or shrink typography. No new toolbar, duplicate editor, DB API, image surface or request pipeline.

**Validation:** mounted App smoke asserts the footer sits outside tab scroll, appears only on Organize, contains visible buttons, and keeps the four-tab/900×600/1440×900/225% matrix working. Pair CI screenshot names before/after to review geometry. Physical Windows user approval is still required for #635. Independently, #634 must retain real GPU scrolling acceptance and the <=1,600 source / <=32 MiB bitmap / NativeAOT / portable MemoryOnly gates.

### Stage 2G — one Inspector action grammar across Organize, Creative and Publication (2026-10-10)

**Problem:** After Stage 2F kept Organize Save/Reset in view, Creative '新規作成 / 既存へ追加' and Publication '公開記録を作成' still occupied the scrollable content above Work/Lineage/history lists. Their discoverability varied with tab height and list length. Information's Copy path and EXIF retry are different: they act on their adjacent information and must remain there.

**Implementation:** Reuse the existing Lumine semantic raised-surface footer treatment for the *same* Creative creation/addition flyouts and Publication record-creation button. The scrollable body on Creative now contains Work / Generation Group / Lineage; the scrollable body on Publication contains publication history, while task-level actions remain in the bottom action region. Only the selected tab's footer occupies layout space; Information gets no empty footer. Existing Work/Group/Publication dialogs, state, keyboard/focus, selection, data APIs and native preview bitmap leases are unchanged. For >=150% text, the two Creative menus stack rather than fighting for narrow horizontal width. Async creative/publication failures appear in their **own visible footer**, not in the hidden Organize save status.

**Verification:** Mounted Windows App smoke checks that all three footers share one layout row outside the per-tab ScrollViewer, show only for the relevant tab, preserve separate failure feedback and stay within the Inspector bounds at existing 900/1024/1440/1920 and 225% text. Add matching screenshot artifacts for Creative, Publication and Information at 900x600 and at 225% without changing existing screenshot baselines. Do not weaken 10k/100k Viewer resource ceilings, MemoryOnly portable/NativeAOT, or separately required real physical Windows acceptance for Issues #635 and #634.

### Stage 2H — progressive Browse no-match recovery preserving navigation scope (2026-10-10)

**Observed problem:** A single '条件をすべて解除' action wiped Search text, selected Folder and every facet when query results were empty, while the active chip shelf's bulk removal erased the Folder navigation scope. This violates UX-03's explicit contract that clearing refinements must not silently discard unrelated Search/Sort and undermines the Library→Folder→Browse continuity promised by D01/D02.

**Decision:** Keep three independent intents: `SearchText` is the current search; `FolderPath` is the navigation scope; `SortOrder` is display preference. Tags, minimum rating, status, favorite-only and color are refinements. Chip bulk removal clears *only* refinements, preserves Search, Folder and Sort; an individual Folder chip can still clear the scope explicitly. The bulk clear button appears only when refinements exist, avoiding a button that does nothing when the only chip is the Folder.

**No Match action hierarchy:** show the most useful explicit recovery for the active constraints: (1) refinements -> '絞り込みを解除' while retaining Search/Folder/Sort; (2) no refinements but Search -> '検索を解除' while retaining Folder/Sort; (3) Folder-only -> 'すべての画像を表示' while retaining Sort; (4) no active constraints -> '画像フォルダーを追加' rather than a no-op. This is derived from one immutable `BrowseFilterState` plan and used in both the in-place `CoreViewerShell` empty-result overlay and the fallback MainWindow no-match state. The in-place recovery uses the existing Browse controller instead of remounting its command row; no duplicated query path or new state store. Secondary Filter review still opens the same Browse filter panel.

**Validation:** mounted Windows App smoke covers Search-only no-match primary label/click and shell identity; pure progressive plan assertions check tag/rating/status/favorite/color clearing, Search/Folder/Sort retention, and explicit final fallback; existing chip focus retention and 900×600 / 225% visual acceptance remain mandatory. No change to image request scheduling, native decoder, GPU source, file model, portable MemoryOnly, NativeAOT or #634 real hardware acceptance. Issue #635 stays OPEN for actual product-owner UX review.

### Stage 2I — cancellable library loading state (2026-10-11)

**Observed gap:** The global Welcome/EmptyLibrary/Error states already present their next meaningful action, whereas Loading exposed only an indeterminate bar plus the ongoing discovered/registered progress. During a large or mistakenly selected library scan the user could see status but could not stop it from the screen. This breaks D02's control/agency and the ProductStateSurface rule (state, impact, one recovery action).

**Decision and implementation:** Show a plainly labeled secondary `読み込みを中止` action below the existing live scan progress in the same Lumine semantic state card. The button cancels **only** the exact `CancellationTokenSource` belonging to the state card's library-open operation; a stale control cannot cancel a subsequent open or affect Welcome/Workspace/Error. On invocation disable the button and replace the progress caption with `読み込みを中止しています…` while the existing `OperationCanceledException` path tears down temporary runtime resources, returns to Welcome, and preserves the existing local/privacy safety behavior. Loading never starts a second scan and does not force-close the app. Existing retry/reselect for Error, add-folder for EmptyLibrary, and progressive scope recovery for NoMatch are unchanged.

**Verification:** Mounted headless Windows App smoke exercises button enabled/name, actual CancellationTokenSource state, one-shot disabled state, cancellation status with no duplicate toast, stale-button non-action after Welcome, and its pre-cancel 1440x900 screenshot. Full NativeAOT, default portable MemoryOnly, Viewer 10k/100k and strict native image cache budgets remain mandatory; #634 physical GPU scrolling and #635 human quality acceptance stay open.

### Stage 2J — keyboard continuity through No Match and query recovery (2026-10-11)

**Observed gap:** The established No Match empty-result overlay clears Gallery selection and hides Inspector, but previously had no explicit keyboard focus transfer. When an image tile was focused and a query returned zero results, the virtualized tile could be recycled or detached, leaving keyboard users without a visible active recovery command. Following the recovery action, the overlay itself was removed without restoring focus to a live Gallery item. By contrast, a user editing the Browse Search textbox must keep its caret; No Match cannot hijack that focused input.

**One ownership rule:** Before the query's `PrepareForSessionRebindAsync`, MainWindow captures whether keyboard focus was on the Gallery container or one of its realized tiles. Only if that Gallery-owned query turns empty, the mounted No Match overlay hands keyboard focus to its existing primary recovery button, after layout and only if nobody else has acquired a live focus target. Detached virtual tiles count as stale focus, not a fresh user choice. When the focused recovery button successfully restores nonempty results, the same mounted Viewer restores semantic tile focus to the previous selection if present, otherwise first visible asset, via existing `RestoreAssetFocus` pending-realization contract. Search textbox, navigation, Filter flyouts, Inspector and modal Focused Viewer retain exclusive focus if they own it. The pointer path is unchanged; no new shortcut or visible decoration.

**Acceptance:** Mounted Windows App smoke starts on a real focused Gallery asset after sorting, executes No Match through a real query, confirms the named recovery button holds focus, opens/closes existing Filter review, invokes recovery, and checks keyboard focus returns to a realized Gallery tile without Viewer shell replacement. Run unchanged 900–1920 window/100–225% and Viewer/NativeAOT performance gates; capture existing No Match and Browse PNG names. This is bounded Issue #635 keyboard behavior, not Issue #634 actual GPU scroll approval or the final human holistic UX acceptance.

### Stage 2K — keyboard continuity across library Loading/Welcome/Error/EmptyLibrary/Workspace (2026-10-11)

**Observed gap:** After Stage 2J fixed the Gallery→NoMatch focus cycle, global library product-state transitions still replaced a focused primary action with a new visual tree. An Error retry disables its own button before async Loading/EmptyLibrary; a user-initiated Loading cancel also disables its button before returning to Welcome. Without explicitly transferring focus, keyboard navigation can land on a detached or disabled control even when the new state contains a clear next action.

**Decision:** Keep `LumineDesign.CreateProductState` as the one visual grammar, and add a **MainWindow-owned focus handoff only at library state presentation**. Capture focus only when it belongs to a button inside the outgoing product state, or explicitly from the active Loading-cancel/Error-retry command before disabling that control. On the current destination's attached layout, move it to the first enabled visible action, or the existing Gallery semantic focus owner if a populated Workspace was opened. A fast async transition may skip the Loading layout entirely, so keep the keyboard focus ownership marker until the live destination is actually focused. Never focus from an old stale presentation callback, steal Search/navigation/modal/other active focus, or act during shutdown; normal pointer and incidental status updates are unchanged.

**Acceptance:** Windows mounted App smoke focuses the real Loading Cancel CTA, verifies CTS cancellation/disabled state, changes to Welcome and asserts its Add Folder button has focus. Independently, it opens a genuinely missing library root (Error), focuses and clicks real Retry rather than invoking the host helper, creates the root, waits for actual async Error→Loading→EmptyLibrary, and verifies Add Folder is focused. Existing 1440x900 Loading/Error/Welcome and 900x600 Browse PNGs must remain mandatory and remain semantically unchanged. Viewer 10k/100k, routed-wheel, 32 MiB decoded bitmap, native AOT and default portable MemoryOnly constraints remain strict; Issues #635 and #634 stay OPEN for owner/hardware acceptance.

### Stage 2L — semantic action focus ring parity (2026-10-11)

**Observed design-system defect:** `LumineProductStyles.axaml` already assigned visible keyboard `:focus` borders to secondary, tertiary, icon, chip and navigation controls, but *primary* and *danger* semantic buttons had no corresponding focus style. Those are exactly the high-priority Save/Retry/Delete actions reached during state recovery. Existing secondary/tertiary rules introduced a new 1-DIP border only on focus (from a zero-thickness rest), so keyboard navigation could subtly shift label geometry at high text scaling. A primary icon variant had its 1-DIP focus brush hidden by an unconditional zero-thickness override.

**Decision:** Standardize primary, secondary, tertiary and danger semantic commands on a reserved *transparent* 1-DIP border at rest; set the same Lumine Focus brush when focused, independent of danger/selection/accent semantics. Keep background, semantic color, disabled action, hover and pressed tokens unchanged. The optional primary icon variant retains the 1-DIP ring without affecting its 34-DIP fixed icon geometry. Do not add redundant per-control resources or change the source Viewer focus/keyboard mechanics.

**Acceptance:** Extend existing mounted Windows design-system specimen across 100, 125, 150, 200 and 225% text to verify six buttons (primary/secondary/tertiary/danger/icon/primary icon) each accept actual keyboard focus, expose the exact shared Focus color and 1-DIP thickness, and preserve every button's relative layout bounds while tabbing through. Add exact named **required** 900×600 screenshot evidence for focused primary at 100 and 225%. Keep existing 10k/100k Viewer, first wheel raster, 32 MiB native decoded bitmap, portable MemoryOnly and NativeAOT gates strict. Parent #635 stays OPEN for physical owner UX acceptance; #634 real Windows GPU scrolling remains separate.

### Stage 2M — sidebar and Folder keyboard focus stability (2026-10-11)

**Observed gap:** After the shared semantic buttons adopted a fixed 1-DIP focus ring, the separate compact/global sidebar (`lumine-nav-item`), library-open row (`lumine-library-open`) and folder row (`lumine-folder-row`) still had **0 DIP border when resting, 1 DIP when focused**. That changes the border-box interior and can move text/labels or neighbouring controls when navigating via Tab, especially on the 420-DIP narrow navigation pane or 225% text. Folder disclosure buttons already use the shared fixed icon style and semantic restore logic; they must not create duplicate keyboard targets for leaf nodes.

**Decision:** Reserve a transparent 1-DIP resting focus border for precisely those three navigation button classes and keep the existing Lumine Focus brush on `:focus`; do not change hover/selected paint, the folder checkmark (`表示中` accessible name), disclosure-state focus generation, ListBox virtualization, or action implementations. This follows Stage 2L's ring contract rather than introducing a new per-row visual. Keep a clear keyboard ordering: expandable folder disclosure before the folder selection action; disabled/fully transparent leaf disclosure is not a keyboard stop.

**Acceptance:** Extend mounted Windows smoke to require fixed resting 1-DIP borders on the sidebar and both selected/unselected folder rows; focus a live global nav target and assert exact Focus brush and unchanged bounds; expand the folder tree and assert disclosure appears before selection in realized traversal and leaf disclosure is disabled; focus selected folder at 100% and 225%, verify exact ring color and stable neighbour bounds. Capture **two new mandatory screenshots** `folders-focus-420x600` and `folders-focus-420x600-text225` in the exact UI evidence manifest. Preserve all existing Windows release/App/Viewer 10k/100k/Skia raster/Image/NativeAOT/default portable MemoryOnly gates, and do not close #635 holistic human UX or #634 physical GPU scrolling acceptance.

### Stage 2N — keyboard continuity through contextual navigation refresh (2026-10-11)

**Observed gap:** The global sidebar already restores keyboard focus to the matching live destination after the rail or pinned sidebar is rebuilt, and Folder disclosure restores focus by semantic path after its local ListBox recycling. A separate asynchronous route was still missing: `RefreshNavigationAsync()` calls `RenderNavigationDestination()`, replacing the **whole** contextual Folder/Tag/Library/Publication control tree even if a keyboard user is focused on a live action in the same destination. The old action is detached; neither the global-navigation nor disclosure restoration covers it. This can occur after library metadata refresh, scan/tag changes or while coming back from a destination with fresh data.

**Decision:** On each `RenderNavigationDestination` of the **same rendered destination**, capture contextual keyboard ownership only when a focused Button belongs to the outgoing contextual view. After replacement, use a generation-guarded UI-input callback to rebind focus to the matching live button via its AutomationId first, its stable folder ToolTip path for folder rows (whose spoken name may change on selection), or its accessible name. Do not carry a contextual action across a genuinely different destination, force an unrelated fallback when a row is removed, restore into a hidden pane or modal lightbox, or override a later live focused Search/global navigation/action. This complements the earlier disclosure/semantic-product-state focus handoffs without rebuilding the entire app shell or changing row paint.

**Acceptance:** Mounted Windows App smoke opens the populated library and unified pinned sidebar, focuses the actual Folder `すべての画像` action, triggers same-destination synchronous and asynchronous navigation view rebuild, asserts the new action instance owns keyboard focus, then repeats the refresh while deliberately moving focus to the global Library command before the deferred callback. The global command must retain focus and activate Library successfully, with its new selected global control focused and actual Library cards shown. No new screenshots are needed because the change is keyboard-only; preserve the strict 900–1920px/100–225% visual evidence manifest and unchanged Viewer 10k/100k, routed-wheel, native bitmap, NativeAOT and default portable MemoryOnly gates. Parent #635 and real physical GPU #634 stay OPEN.

### Stage 2O — preserve active Settings, Tag and Publication drafts during metadata refresh (2026-10-11)

**Observed data-loss UX gap:** Stage 2N restored focus for focused *buttons* in the contextual navigation tree, but `RefreshNavigationAsync` still discarded whole Settings, Tag and Publication visual trees during a background metadata refresh, even when a user was typing in a Settings custom-extension field, a Tag search, or editing a Tag/Publication flyout. The resulting edit-state recreation could erase unsaved text, filtered search and create/edit draft forms. The popup controls in Tag and Publication editors are hosted in Avalonia PopupRoot rather than descendants of the navigation panel, so checking only the focused text box inside the panel is insufficient.

**Decision:** Separate explicit navigation from automatic metadata refresh. Continue refreshing the authoritative SettingsSnapshot, tag/folder/library facets and publication metadata in `RefreshNavigationAsync`, but permit `RenderNavigationDestination(preserveActiveEditor: true)` to keep the currently mounted same-destination view while either (1) the focused TextBox belongs to that view, including Settings, or (2) an anchored Button/DropDownButton's Flyout is open in that view, including Tag and Publication editors. Do not preserve the view on an explicit user-requested destination change; do not hold it after input focus leaves and the popup is dismissed. A later automatic refresh with no active editor may apply the fresh snapshot normally. No draft is written to the database by this guard; discard on explicit navigation remains unchanged.

**Acceptance:** Mounted Windows App smoke opens a real library and tests (a) focused Tag search text retained through async metadata refresh, (b) Tag create-flyout typed name and open popup retained through another refresh, (c) Publication destination editor typed name and open popup retained through refresh, and (d) focused Settings custom-extension text retained while fresh metadata is gathered. Once focus moves to the Settings global nav control, a subsequent refresh must replace the page as before and keep deliberate nav focus. All existing screenshot and Viewer 10k/100k, Skia routed wheel, native bitmap, NativeAOT and portable MemoryOnly limits remain strict. #635 real Windows owner UX and #634 physical GPU acceptance remain independent OPEN gates.

### Stage 2P — measured-width Browse command bar without editing interruption (2026-10-11)

**Observed structural limitation:** Browse's primary command row was fixed to four columns (`184,*,Auto,Auto`) at every window width and text scale. The Search control has a necessary 260-DIP minimum; Filter's semantic label grows with active refinement count, and text scaling up to 225% increases both command desired widths. With less horizontal space than their aggregate intrinsic widths, the Grid can no longer fit the single line. Merely shrinking Search, hiding Filter/Display, or truncating controls would make everyday image browsing harder. The original product contract (Section 2, compact 900x600) already calls for measurement-driven extra-row disclosure rather than a magic window-width cutoff.

**Decision:** Keep exactly the same mounted Browse Search TextBox, Filter Button/Flyout and Display Button/Flyout, same stable library/scope identity and chip shelf. When the *actual allocated primary-row width* is below `184 + Search.MinWidth + Filter.DesiredSize.Width + Display.DesiredSize.Width + 3 × spacing`, reflow the existing Grid into a two-line grammar: first line identifies current Library/Folder scope; second line exposes Search and both frequently needed commands in that keyboard order. With enough measured width, return to the one-line grammar without recreating editors or reissuing queries. The responsive arrangement only changes Grid attached row/column properties and reuses the original design tokens and layout controls. Search expands to take the available second-line width; normal wide Browse remains unchanged. The image viewport remains virtualized; no extra permanent navigation pane, nested toolbar, WebView or ineligible GPU caching is added.

**Verification:** deterministic threshold tests and mounted 225% text acceptance create an actual narrow Browse surface, demand both filter/display and Search remain within the command-bar bounds, retain a live Search instance, caret/focus and tag scope across re-expansion, and verify correct semantic row/column grouping. The existing exact screenshot manifest and full 900/1024/1440/1920 Windows UI smoke/Viewer/NativeAOT/MemoryOnly limits remain mandatory. This does **not** constitute holistic real Windows owner acceptance; #635 remains OPEN and #634's hardware scrolling acceptance is unchanged.
