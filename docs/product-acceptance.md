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
