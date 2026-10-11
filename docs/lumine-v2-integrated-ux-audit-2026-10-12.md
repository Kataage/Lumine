# Lumine v2 — Issue #635 integrated UX evidence audit (2026-10-12)

**Status: AUTOMATED EVIDENCE REVIEWED / REAL WINDOWS OWNER APPROVAL NOT ESTABLISHED.**  
Issue: [#635](https://github.com/Kataage/Lumine/issues/635) · Separate scrolling release blocker: [#634](https://github.com/Kataage/Lumine/issues/634) · Audited implementation: [`develop@a186cdce`](https://github.com/Kataage/Lumine/commit/a186cdce4e2763f8999bafc9a3160a1172df430c), through Stage 3T. This is a **whole-journey design/acceptance audit**, not a claim that passing CI delivers the owner's requested polished product.

## Evidence and reproducible comparison

1. **Baseline**: [2026-10-10 Windows CI #38028622123](https://github.com/Kataage/Lumine/actions/runs/38028622123), artifact `lumine-ui-visual-evidence-38028622123` (63 PNG captures); reviewed earlier in [baseline composition audit](lumine-v2-ui-audit-2026-10-10.md).
2. **After Stage 3T**: [2026-10-12 Windows CI #38162018463](https://github.com/Kataage/Lumine/actions/runs/38162018463), `lumine-ui-visual-evidence-38162018463` artifact (75 PNG captures and `manifest.tsv`); this PR's strict `windows-core` **succeeded**, including mounted Avalonia App Smoke, Viewer Grid/List first-render scrolling, memory/Library/Image benchmarks, NativeAOT/Portable and the Windows PowerShell real-library checks.
3. **Direct artifact inventory**: extracted both unmodified ZIPs; all **63 old capture names still exist** in the after artifact, all **63 matched PNG pixel dimensions**, and **12 additional names** were captured. This comparison establishes regression-evidence continuity, **not** that the layout became subjectively better. Pixel raster differences are not an acceptance metric because fonts/runner rendering and fixture state can vary.
4. **Code/behavior contract**: `tests/Lumine.App.Smoke/Program.cs` asserts the expected named screenshots and nonempty image bytes; `.github/workflows/v2-foundation.yml` runs its actual `App Detail adapter smoke test` with `--visual-output` and publishes a reviewable artifact. `docs/product-acceptance.md` separately requires the shipped NativeAOT executable plus a representative, read-only source library and a single final owner review. It does **not** permit closing #635 or #634 from CI.

### Additional visual evidence since baseline

These twelve captures existed in the reviewed after artifact but not in the reviewed October 10 baseline:

- Focus semantics: `design-system-focus-100.png`, `design-system-focus-225.png`, `folders-focus-420x600.png`, `folders-focus-420x600-text225.png`.
- Publication progressive disclosure: `inspector-publication-collapsed-1100x720.png`, `inspector-publication-expanded-1100x720.png`.
- Other Inspector tabs in both standard and enlarged text: `inspector-tab-1-900x600.png`, `inspector-tab-2-900x600.png`, `inspector-tab-3-900x600.png`, plus corresponding `-text225.png` variants.

**Do not confuse evidence coverage with broad-screen equivalence:** not every destination/state is represented at every one of 900×600, 1024×768, 1440×900, 1920×1080 and 100/125/150/200/225% text scaling. The design-system has all five scale samples; the end-to-end composition matrix is sampled, not a verified Cartesian product.

## Integrated Library → Browse → selection → Inspector → Focused Viewer → return

| Journey boundary | Automated evidence currently available | Still unproven without real-Windows owner use |
|---|---|---|
| First launch → Library selection → Browse | `welcome-1440x900.png`, `loading-1440x900.png`, `browse-900x600.png`, `browse-1440x900.png`, `browse-1920x1080.png`; NativeAOT Product Acceptance workflow in `docs/product-acceptance.md` | Real initial discoverability, representative-library visual density and everyday navigation comfort |
| Library / Folder / Tag / Publication destinations | Compact `navigation-overlay-900x600.png`, pinned `navigation-pinned-1440x900.png`, Folder/Tag/Publication list captures, mounted layout and keyboard smoke; Stage 3T overlay-to-search Ctrl+F regression | Whether hierarchy and labeling feel unified to the user; accessibility technology and high-contrast behavior on physical Windows |
| Browse search / filter / display and selection | `browse-filter-open-900x600.png`, `browse-display-open-900x600.png`, `browse-active-filter-900x600.png`, `bulk-selection-900x600.png`; Stage 3S immediate Enter/search-only Escape and live-focus tests | Whether frequent actions are obvious without training, and bulk commands avoid covering real thumbnails in dense libraries |
| Selection → Inspector organization / creative / publication / information | `inspector-900x600.png`, each tab at standard and 225% text, 1100×720 Organize/Creative/Publication/Information captures, staged publication disclosure | Field/action density on real metadata and actual keyboard/screen-reader navigation under all text sizes |
| Inspector or thumbnail → Focused Viewer → return | `focused-viewer-900x600.png` / `-text225.png`; App Smoke exercises opening, full-client lightbox, modal focus confinement, close/back and return to live thumbnail or Inspector invoker | Actual comfort with full-resolution images, input latency/zoom/pan, and long scrolling selection continuity |
| Settings → workspace and dialogs | `settings-900x600.png`, `settings-900x600-text225.png`, `settings-1440x900.png` and advanced form capture; mounted save/error/focus tests from Stages 3G–3R | Whether advanced options are easily found and the common workflow stays focused, without unexpected data or cache changes |
| Empty / No Match / Error recovery | `empty-library-1440x900.png`, `no-match-1440x900.png`, `error-900x600-text225.png` and code assertions | Legibility and recovery clarity with genuinely large or malformed libraries |
| Wheel-scroll and actual thumb visibility | CI automated Skia Grid/List routed wheel, first-raster unready checks, 10k/100k virtualization and caps; downloadable wheel artifacts on CI #38162018463 | **#634: observable scroll blanking on real GPU and real image formats/volumes. Still release-blocking.** |

**Source limitation:** the UI screenshot fixtures mainly show two deliberately synthetic color-test images. Their unused canvas area is **not** evidence of wasteful real-library layout; screenshot existence and UI test success are **not** evidence that the user's previously rejected visual design is now accepted. The separate benchmark fixtures simulate higher counts but are not the owner's actual image collection.

## Cross-surface findings and priority

1. **Design-document status is stale.** `docs/lumine-v2-holistic-ux-spec-v0.1.md` still says “PROPOSED, NOT IMPLEMENTED” at its head even though Stage 2A through 3T have been delivered over many PRs. Update the status to “incremental implementation / owner approval pending” while preserving the original v0.1 design decisions as historical baseline. A status line must not silently declare those decisions fully implemented.
2. **Automated surface coverage is broad but segmented.** Screenshots demonstrate that the major destinations render, and App Smoke has substantial **mounted** navigation, focus, and focused-viewer return assertions. What they do **not** prove is the *perceived* continuity of the entire ordinary workflow with a real, varied large library and hardware. Do not replace this missing signal with one more isolated CSS/button/keyboard polish PR. Cross-surface flow should be assessed as a single reviewer session, using the existing NativeAOT Portable acceptance artifact.
3. **Physical performance remains a separate hard gate.** #634 has a synthetic first-raster zero-unready regression test; physical owner-observed blank strips must not be waved away by synthetic numbers or hidden by thumbnail placeholders and decoration.
4. **Actual visual design is not certified.** The 900×600 composite view shows a compact rail, search row and image canvas, while Inspector is an overlay, and the 1440×900 pinned view uses a single sidebar. The source and screenshots support these layout facts; they do not establish Apple-level polish, high contrast, perceptual alignment, or first-time task discovery.
5. **Retain the existing correctness architecture.** Keep original files read-only by default, MemoryOnly thumbnail default, the shared dark tokens and NativeAOT Portable, with no heavy WebView/AI dependency. Any UX improvement must preserve virtualized Grid/List and strict performance thresholds.

## Decision / next release gate

- **Verified**: the 2026-10-12 Stage 3T Windows CI and its 75 after-captures; named/dimension coverage of all 63 baseline images; major flow-specific mounted tests and automation contracts are present.
- **Partially demonstrated**: holistic 900/1440 composition and progressive disclosure, via headless fixtures with independently validated accessibility/keyboard assertions where implemented.
- **Not verified**: a cohesive perceived real-photo user experience, every 20-way screen/text-scale combination, true assistive-technology compatibility and high-contrast physical behavior, or real-device first-raster scrolling (#634).
- **Release decision**: **keep #635 OPEN**, and separately **keep #634 OPEN** until the single final consolidated owner walkthrough with representative library passes. The produced [NativeAOT Product Acceptance artifact](https://github.com/Kataage/Lumine/actions/runs/38162018463) is an *input to* that review, not proof the review happened. No new intermediate manual test burden should be placed on the owner.

This document is a dated audit snapshot. Future implementation should only follow **specific verified shortcomings**, update the same evidence matrix, and use bounded Issue/PR scopes rather than assuming additional speculative polish is progress.
