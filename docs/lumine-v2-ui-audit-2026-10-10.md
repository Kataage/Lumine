# Lumine v2 — #635 product UI composition baseline audit (2026-10-10)

**Issue:** [#635](https://github.com/Kataage/Lumine/issues/635) · **Audit revision:** `839df08108e392e9925c2006b15fa731617f9b3a` · **Status:** observed automated/headless evidence; *not* a real-Windows UX pass.

## Evidence actually reviewed

[Windows develop CI #38028622123](https://github.com/Kataage/Lumine/actions/runs/38028622123) generated the downloadable **`lumine-ui-visual-evidence-38028622123`** artifact with 63 PNG files plus `manifest.tsv` (64 entries). The review inspected the contact sheet and independently enlarged the 900×600 Inspector at 225% text scale, and inspected the file manifest. These are **headless visual-regression screenshots of deliberately sparse, saturated test fixtures**: the two synthetic Browse thumbnails and otherwise empty black canvas are **not evidence** that real large image collections suffer low screen utilization. They are useful for control geometry, density, layering, alignment and reachability inspection only. No source library images were copied into this repository.

| Screenshot evidence from artifact | What is confirmed / what remains open |
|---|---|
| `browse-900x600.png`, `browse-1024x768.png`, `browse-1440x900.png`, `browse-1920x1080.png` | Grid remains the central surface and Search/Filter/Display are visible; the synthetic fixture has only two images so content density is not measurable |
| `browse-1440x900-sidebar.png`, `navigation-pinned-1440x900.png`, `navigation-overlay-900x600.png` | Both a unified pinned-sidebar state and compact overlay exist; the navigation model changes by width and pin state |
| `browse-active-filter-900x600.png`, `browse-filter-open-900x600.png`, `browse-display-open-900x600.png` | Search, reversible chips, filter and display affordances are present; focus behavior requires interaction assertions, not PNGs |
| `inspector-1920x1080-pinned.png`, `inspector-900x600.png`, `inspector-900x600-text225.png` | Right-side Inspector has different dock/drawer states. At 900×600 and 225% text, preview, tabs and controls dominate the visible drawer; lower editing sections require internal scrolling. This is not by itself evidence of clipping |
| `focused-viewer-900x600.png`, `focused-viewer-900x600-text225.png` | Focused image dominates, with floating zoom/next/close controls; actual keyboard discovery and hit targets need a human test |
| `bulk-selection-1100x720.png`, `bulk-organize-1100x720.png`, `inspector-organize-1100x720.png`, `inspector-creative-1100x720.png`, `inspector-publication-1100x720.png`, `inspector-information-1100x720.png` | Selection actions, four inspector tabs and creative metadata are represented; numerous simultaneously visible field cards are candidates for disclosure grouping, **not** evidence the workflow is unusable |
| `settings-1440x900.png`, `settings-900x600-text225.png`, `settings-advanced-1440x900.png` | Settings and advanced preferences use scrollable stacked forms; do not claim visual consistency without a whole-product review |
| `welcome-1440x900.png`, `loading-1440x900.png`, `empty-library-1440x900.png`, `no-match-1440x900.png`, `error-900x600-text225.png`, `dialog-*.png` | Dedicated state and dialog surfaces exist; a common semantic hierarchy is the intended redesign goal |

`docs/acceptance/windows-keyboard-scaling-matrix.md` already specifies **900×600, 1024×768, 1440×900, 1920×1080** and independent **100%, 125%, 150%, 200%, 225% text scale**. A generated screenshot is not a passed keyboard or physical Windows test. The [#634 merged change](https://github.com/Kataage/Lumine/pull/659) fixes synthetic Grid/List wheel first-raster P1, but real physical image-library acceptance remains open: **the #635 redesign must not conceal this with decorations, placeholder animations or a larger cache**.

## Code-verified geometry and interaction inventory

| Present implementation (exact source) | Measured by code, not inferred from images | #635 design question |
|---|---|---|
| `MainWindow.cs:153-154` | Minimum **900×600 DIP**, default **1440×900 DIP** | preserve minimum while ensuring content and commands remain reachable at 225% text |
| `LumineDesign.cs:115,137-145` | Left rail **72 DIP**; header token **56**, compact control **34**, compact command **32**; spacing **2/4/6/8/12/16/24** | one coherent spacing/type/action grammar across App/Viewer, not nested per-surface overrides |
| `MainWindow.ApplyNavigationLayout` | Below **1200 DIP** uses 72 DIP rail plus a temporary **250–300 DIP** pane; when pinned and >=1200, a unified **288 DIP** sidebar replaces the rail | preserve Library/Folder/Tags/Publication/Settings discoverability; no double sidebars |
| `CoreViewerShell.ApplyInspectorLayout` | Below **1600 DIP** Inspector is a **300–340 DIP** overlaid drawer, not a permanent column; >=1600 can dock at **320–380 DIP** when pinned | opening details must not unexpectedly change scroll position, crop active thumbnail, or bury close/back |
| `BrowseWorkspaceControls` | Search minimum **260 DIP**, Filter and Display next to it; sort/rating/status/tag/favorite/color housed in a flyout; active chips shown in a separate row | users must understand scope and be able to undo each filter without remembering a hidden shortcut |
| `CoreViewerShell` | Selection toolbar floats on top of the image surface; single/grid and focused viewer are separate layers | avoid commands occluding selected thumbnails or the Inspector |
| `ContextualAssetDetailPanel` | Four tabs `整理`, `制作`, `公開`, `情報`; compact Inspector uses internal scrolling | keep the four domain jobs but order frequent metadata actions before advanced creative fields |
| `LumineVisualPalette` | `#09090B` background, `#0D0D10` surface, `#151518` raised, `#FAFAFA` foreground, `#A1A1AA` secondary, `#29292F` subtle border | retain brand palette; reserve strong outlines for focus and selected state |
| `LumineDesign` + `WindowsTextScale` | UI font fallback Yu Gothic UI/Yu Gothic/Meiryo/Segoe UI; 14 DIP body, 12 DIP caption, 18 emphasis, 24 brand multiplied by 1–2.25 text factor | do not shrink legible captions to cheat 225% layouts; distinguish DPI from text scale |
| `ViewerSession` / `ThumbnailViewerControl` | virtualized rows, decoded Bitmap cap **32MiB**, strict 10k genuine wheel budget **<=1,600** and active foreground priority | the redesign cannot replace virtualized browsing with static Cards for 10k/100k images |

### Baseline width arithmetic (DIP, geometry from code)

- **900×600 compact Browse:** 72-DIP rail leaves **828 DIP** nominal central workspace (92% of full width before gutters). A transient Inspector overlays about **315 DIP** when the workspace width is ~828, leaving ~513 DIP of unobscured image surface while open. This is an **estimate from the code clamp**, not an extracted pixel measurement; overlay is conditional.
- **1440×900 with pinned navigation:** 288-DIP unified sidebar leaves **1,152 DIP** central workspace (80% of total width before gutters). Non-pinned state uses the 72-DIP rail and 1,368 DIP canvas. Inspector remains a transient ~300–340-DIP drawer at this size rather than a docked second permanent pane.
- **1920×1080 with pinned navigation/Inspector:** 288-DIP sidebar leaves 1,632 DIP workspace, of which at most 380 DIP is allocated to the explicitly pinned Inspector, leaving **>=1,252 DIP** Browse canvas (before gutters). Whether users actually pin both is a state decision, not a screenshot default.

## User journeys audited (behavioral contract, not physical acceptance)

| Journey | Current entry/surfaces | Design-stage action |
|---|---|---|
| First run → register/use library | Welcome → Library navigation → Browse | keep one obvious primary action; no diagnostic-centric empty state |
| Library/Folder/Tag/Publication switch | left rail or unified sidebar / temporary pane | make destination vs scope vs selected library visually distinct |
| Browse / search / filter / display | search+Filter+Display, filter chips, Grid/List/density/sort | reduce simultaneous command layers; maintain one-row frequent toolbar and reversible chip shelf |
| Browse → selection → bulk organize | tile selection, contextual toolbar, Inspector | preserve selection even when flyout or layout changes |
| Browse → Inspector (organize/creative/publication/info) | overlaid/pinned Inspector | image stays primary; deep fields use progressive disclosure |
| Browse → Focused Viewer → back | root fullscreen overlay, zoom/pan/next/close | no context reset: restore selection, focus, scroll anchor |
| Settings/diagnostics | Settings page / advanced controls | separate daily settings from technical diagnostics without deleting either |
| Empty / loading / no-match / error / dialogs | shared product state components | one message, one explanation, one next action; destructive actions confirm scope |

## Risks to validate, not assumptions to label 'fixed'

1. **Control density:** 900×600 at 225% may require fewer inline control groups. Never solve by lowering text sizes or hiding essential search/back/view commands.
2. **Focus integrity:** search chip-removal smoke was observed failing once in PR #660 CI, then passed on identical SHA. Its real reason is **not established**; #635 should preserve and strengthen deterministic focus checks, not call it a confirmed bug or waive it.
3. **Real collection density:** synthetic screenshots show two items only. Need owner library screenshots/privacy-safe geometric diagnostics to judge thumbnail count, scroll speed and visual hierarchy under hundreds/thousands of photos.
4. **Contrast/color/keyboard:** automated scale PNG evidence does not establish WCAG contrast or screen-reader correctness; measure semantic contrast and physical keyboard behavior.
5. **Cross-surface harmony:** styling exists already in `LumineProductStyles.axaml` and `LumineDesign.cs`. A new design must consolidate rather than add a third parallel token registry.

## Audit conclusion

The evidence **justifies a holistic composition contract**; it does **not** justify assuming the current layout failed solely because screenshot fixtures have black space, nor changing runtime scheduling or capping fonts by eye. The proposed versioned design is [`lumine-v2-holistic-ux-spec-v0.1.md`](lumine-v2-holistic-ux-spec-v0.1.md), status **DRAFT for first complete journey prototype**, not implemented or approved product acceptance. Screenshot before/after comparisons should use the exact named 2026-10-10 baseline files above and upload new matching geometries, not alter old evidence.
