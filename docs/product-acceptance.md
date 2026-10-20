# Lumine v2 final Product Acceptance

Issue: #397  
Parent: #385  
Technical Core baseline: #295

This is the final non-AI product gate. Passing CI is required, but it is not sufficient to close #397.

## What is automated

The shipped NativeAOT executable now runs the existing real-library Core acceptance and then a product-workflow scenario inside the same process.

The representative multi-thousand-image library remains read-only from the acceptance workflow.

Product mutations are performed only in an isolated scratch library beneath the acceptance data root.

Automated product gates cover:

- Lumine identity and the approved navigation hierarchy
- Library / Folder / Tag / Publication / Settings destinations
- Grid / List and density switching
- search/filter state transitions
- NoMatch versus truly EmptyLibrary
- contextual detail
- focused viewer
- previous / next
- Fit / zoom / pan / 1:1
- multi-selection
- complete user metadata round-trip: rating, favorite, status, color label, tags, notes
- composed search/filter visibility after metadata editing
- Work creation
- Generation Group creation
- directed Relation / lineage
- Publication snapshot and archive context
- Settings surface and bounded cache/resource state
- portable-root contract
- coordinated shutdown
- existing Core performance/resource/filesystem gates

`cold.json` / `warm.json` contain `product.*` metadata and the normal `summary.json` contains a `productAutomated` section.

## One-command final acceptance

Use the Product Acceptance wrapper from the Windows x64 NativeAOT acceptance artifact:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Run-ProductAcceptance.ps1 `
  -Exe ".\Lumine.App.exe" `
  -Library "D:\path\to\representative-library" `
  -OutputDirectory "D:\Lumine-product-acceptance" `
  -InteractiveReview
```

Defaults:

- minimum representative assets: 1,000
- browse phase: 60 seconds per cold/warm run
- idle settle: 10 seconds per run
- max fast-scroll refresh: 1,500 ms
- thumbnail policy: MemoryOnly

The wrapper first runs automated cold/warm NativeAOT acceptance. If that passes, `-InteractiveReview` reopens the same executable using the same acceptance database/data root for normal product use.

The representative library is already registered by the automated phase, so select it from **ライブラリ** and perform the manual review before closing Lumine.

After Lumine closes, the wrapper asks the final human-observation questions and writes:

- `product-summary.json`
- `MANUAL-CHECKLIST.md`
- normal `summary.json`
- `cold.json`
- `warm.json`
- optional `warm-steady.json`

## One final physical Windows session for both #635 and #634 (2026-10-20)

Do not ask the owner to run separate repeated intermediate acceptance sessions.
When the *final implementation candidate* is ready, use the **same** shipped
NativeAOT executable and representative mixed JPEG/PNG/WebP/BMP image library
for the product-wide and scroll-specific review **in one application launch**:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build\Run-ProductAcceptance.ps1 `
  -Exe ".\artifacts\native-aot\Lumine.App.exe" `
  -Library "D:\path\to\representative-library" `
  -OutputDirectory "D:\Lumine-final-review" `
  -InteractiveReview -IncludeViewerScrollReview
```

Use the **successfully built and merged `develop` product acceptance ZIP** with
the scripts in its `build/` folder, and execute from that ZIP's extracted
repository-relative directory. The representative source library is read-only
in the automated acceptance scenario; product mutations use its separate
scratch root. Its automatic Windows Core gate must complete before the human
session starts. The explicit `-IncludeViewerScrollReview` switch requires
`-InteractiveReview` and `MemoryOnly` thumbnail storage; normal usage of
`Run-ProductAcceptance.ps1` is unchanged.

During the *same launched app*, review the ten original UX and navigation
criteria plus five physical Grid/List wheel tests (ordinary wheel, continuous
forward/reverse, List fourth wheel boundary, far jump and recovery). Close
the app once and answer the 10 product questions and 5 scroll questions.
The five questions are shared with `Run-ViewerScrollOwnerReview.ps1`, not
maintained as an independent inconsistent second checklist.

The following evidence is produced together without private image pixels
or library path strings:

- `product-summary.json` — original automated result and 10-item product
  review, plus `consolidatedViewerScrollReview`,
  `viewerScrollManualDecision` and `viewerScrollChecks`.
- `viewer-scroll-owner-report.json` — independent Issue #634 five-check
  `pass/fail/pending` observations, hardware inventory only as context.
- Original `summary.json`, `MANUAL-CHECKLIST.md`, `cold.json`,
  `warm.json` and other ordinary acceptance evidence.

If any wheel check **fails**, the consolidated result is `fail`; if any
wheel check is **skipped**, or the environment forces the software renderer,
it cannot report `pass`. The product-specific
`manualDecision` remains the original 10-item human evaluation; the combined
result additionally gates on the separate wheel verdict. This is **human
self-report**, not an actual GPU present-fence timing measurement. Only the
product owner can approve #634 and #635 after reviewing these outputs.
Their approval does not silently resolve Issue #714 or other open release
blockers. Do not close those issues on CI or screenshot output alone.

## Manual checks that cannot be decided by automation

The final reviewer must confirm all of these:

1. **Lumine identity** — It clearly looks and feels like Lumine, the dark visual hierarchy is coherent, and normal workflows do not expose generic Core/test-shell UI.
2. **Shell / navigation** — Global navigation, contextual Library/Folder/Tag/Publication panes and the image canvas read as one coherent hierarchy; 900x600 uses overlays rather than competing permanent sidebars.
3. **Browse usability** — Grid/List, density, search/sort/filter, tag assignment and direction-reversing fast scrolling feel responsive, without disruptive blanking or excessive command chrome.
4. **Organization workflow** — Single/Ctrl/Shift selection, Inspector metadata editing, bulk organization and destructive confirmations are understandable and dependable.
5. **Viewer discoverability and comfort** — A first-time user can discover how to open an image, and previous/next, Fit, zoom, 1:1, Info, fullscreen, pan and close/back are clear and comfortable without relying on hidden shortcuts.
6. **Product states / feedback** — Welcome, Loading, Empty, No Match and recoverable Error each have one clear message and next action; success feedback is transient and technical detail stays secondary.
7. **Creative archive** — Work, Generation Group, directed Relation and Publication are understandable and useful without AI.
8. **Settings / storage** — Common settings are easy to find, advanced cache/storage/diagnostics are progressively disclosed, and portable/storage behavior has no surprising side effects.
9. **Accessibility / responsive polish** — Keyboard-only navigation works logically; explicitly review 900x600, 1024x768, 1440x900 and 1920x1080; explicitly review Windows text scale at 100%, 125%, 150%, 200% and 225%; no required control/text clips or becomes unreachable; selected/destructive states remain understandable without color alone.
10. **Daily-use product verdict** — No unresolved P0/P1 issue is observed and normal use feels like Lumine rather than a technical prototype.

The last item is intentionally subjective and belongs to the product owner. Automation must not substitute its judgment.

## Result semantics

`product-summary.json` has three decisions:

- `automatedDecision`
- `manualDecision`
- `combinedDecision`

Possible combined values:

- `pass`
- `automated-pass-manual-review-required`
- `fail`

#397 may only close when `combinedDecision` is `pass`, there is no unresolved P0/P1 product issue, and the product owner accepts the result.

## What to send back

After the real Windows review, provide `product-summary.json` (or paste its contents) plus any observations that should become follow-up issues.

If the result is `pass`, first verify that #443 and #410 have no remaining P0/P1 follow-up, then close the acceptance chain in dependency order (#443 -> #410 -> #397). #385 closes only after its complete Product Epic conditions are confirmed. AI research/integration remains blocked until #397 and #385 are complete.

If any manual item is `fail`, #397 stays open and the failure should become a focused product issue rather than being waived implicitly.


For the focused P1 scroll-quality Issue #634, the shipped Windows product acceptance artifact also includes `build/Run-ViewerScrollOwnerReview.ps1` and [the shorter human review instructions](viewer-scroll-owner-review.md). This is a **separate five-interaction real-Windows manual check**, not a replacement for the comprehensive #397 product acceptance or physical Windows GPU verification.


## CI teardown forensic data (#737, 2026-10-15)

[Issue #737](https://github.com/Kataage/Lumine/issues/737) tracks an intermittent strict Windows App Detail adapter smoke failure where `Directory.Delete` cannot remove `library.db` from the isolated smoke root after the normal clean-shutdown sequence. [PR #736 CI #38596829052 attempt 1](https://github.com/Kataage/Lumine/actions/runs/38596829052) showed this exact failure, while attempt 2 hit a **different** strict high-count Tag navigation budget failure (tracked separately as [#738](https://github.com/Kataage/Lumine/issues/738)). Neither proves a regression in #714's held-reader Library smoke, which already passed before the App step.

The existing App smoke teardown tries up to **40 directory deletions**, clearing SQLite pools and awaiting GC/finalizers on failures, with **25ms** between attempts. The original final-iteration exception filter `when (cleanupAttempt < 39)` let the last raw `IOException` escape before the intended diagnostic message. The forensic correction **still fails** on the 40th unsuccessful attempt, but records which remaining database files allow/disallow exclusive read/write opens, the current process PID/handle count, and the final Win32-derived HRESULT. It probes only on failure and does not weaken App/Viewer/Library/Filesystem performance thresholds, hide leaked handles, lengthen the wait or claim it has identified the owner.

If the new evidence still shows a lock, investigate outstanding app background tasks, connection/reader disposal and Windows external file handles before changing cleanup/restart behavior. A passing run does not erase the prior failures; require repeatability for a final product acceptance candidate.


## Issue #737 — superseded navigation DB task ownership at MainWindow shutdown (2026-10-16)

The strict Windows [PR #743 CI #38787435176](https://github.com/Kataage/Lumine/actions/runs/38787435176) failed at *App Detail adapter smoke*, **before any Library 10k benchmark**; the original App 10k Tag navigation was still **1556.0ms <2000ms** and screenshot/Viewer tests had passed. New #737 forensics from the same runner caught `repeated-data-0/library.db` held under `0x80070020` after the original **40** bounded deletion attempts, process PID=6672, handle count=398. The CI was supposed to exercise unrelated benchmark-only COMMIT ordinal instrumentation, so there was no product DB code delta. This is **real recurrence of Issue #737**, not proof that the benchmark change caused a runtime SQLite leak. The failure in the outer `finally` may also mask the first exception inside the repeated-launch lifecycle loop; do not claim a proven handle owner from the error code alone.

Source review identified a *specific ownership gap*: `MainWindow.StartNavigationRefresh` replaces `_navigationOperation` with every refresh, but older cancelled operations may still be running their asynchronous `RefreshNavigationAsync` database calls. `OnClosing` cancelled the token and awaited **only that latest task** before destroying the Viewer runtime and declaring a clean shutdown. Closing while a superseded older operation is still winding down can leave SQLite statements/connections active, even after `ClearPools` (which cannot release a still-active reader). Whether this exact gap owned the locked file in the failed runner is a hypothesis until corrected and stress-tested.

The issue-scoped fix tracks **every live refresh task** in a per-window `HashSet<Task>`, removes each in the existing observer's `finally`, forbids starting new refresh after close begins, and awaits a snapshot of **all** in-flight refreshes plus the constructor's asynchronous navigation initialization before runtime disposal. It never blocks the normal UI during refresh, suppresses an exception or skips work on legitimate navigation. A real mounted App Smoke keeps an older navigation refresh behind a bounded async gate, supersedes it with a newer *completed* refresh, begins a real `MainWindow.Close`, asserts the window remains open while the older operation is outstanding, releases the barrier and requires the existing original close and directory/WAL cleanup criteria. This tests the previously unobserved ownership gap rather than increasing the cleanup grace period.

**No strict acceptance changes**: 40 bounded cleanup attempts, 10k Tag <2000ms, 10k independent source-guarded SQLite COMMITs <=8000ms, WAL/NORMAL/auto1000, NativeAOT/portable and real-library acceptance remain in force. The fix needs full Windows CI (including a *real* SQLite handle cleanup) and postmerge trials; one green CI is insufficient to close #737 definitively.
