# Lumine v2 — Windows keyboard and scaling acceptance matrix

Issue: #585 · Parent: #503 · related: #443, #540 · prior focused regression: #583 / PR #584.

## Meaning of a pass

This is a **manual product acceptance protocol**, not an assertion that any check below has passed. CI App/Viewer smokes and generated screenshots are supporting evidence, not substitutes for physical Windows keyboard-only and visual review. A cell stays **Not run** until a human or an explicitly linked automated assertion executes it. Do not close #503 / #443 / #540 solely because this protocol or a CI run succeeds.

## Record each execution

- Commit SHA / Portable artifact filename and Actions run:
- Windows version, renderer backend, screen scaling and text scaling:
- Physical display resolution and app window geometry:
- Input method (keyboard-only, pointer for visual review):
- Case ID; Pass / Fail / Blocked / Not run; evidence link or screenshot name:
- Focus before -> focus inside -> focus after, including whether the operator deliberately moved focus:
- Failure description, exact reproduction steps, severity P0/P1/P2 and linked follow-up Issue:

A **P0** blocks startup, causes unrecoverable data loss or crashes. A **P1** prevents a core keyboard workflow, traps or unexpectedly steals focus, hides actionable controls, or clips essential actions without a workaround. **P2** is a nonblocking presentation inconsistency. Record defects as separate scoped Issues with reproduction and evidence; no broad speculative refactor.

## Manual keyboard-only walkthrough (start fresh for each test)

| ID | Surface / action | Procedure | Required behavior |
|---|---|---|---|
| K01 | First launch / welcome | Launch Portable with keyboard only. Tab and Shift+Tab through available welcome/recovery actions; invoke primary action with Enter then Space as appropriate. | Focus visible; visual and tab order agree; no inaccessible primary command. |
| K02 | Global navigation | Tab to each destination, use Enter/Space; return via keyboard. | Destination and selected state recognizable without color alone; Settings discoverable and reachable. |
| K03 | Context pane | Open/close secondary Library/Folder/Tag/Publication navigation via keyboard at wide and compact widths. | Focus enters a useful control, does not fall behind overlay, returns to the live invoking command on dismissal unless intentionally moved. |
| K04 | Browse search and sorting | Focus search, type, clear, use sort/filter. | Search retains caret and keyboard editing; tab order is predictable; Enter/Space operate applicable commands. |
| K05 | Browse Filter flyout | Open from Filter button; navigate; dismiss with Escape and light-dismiss (pointer pass separately). | Initial focus reaches sort editor; on dismissal focus returns to eligible Filter trigger unless focus intentionally moved elsewhere. Regression reference #583. |
| K06 | Browse Display flyout | Open from Display button; navigate Grid/List; dismiss via Escape. | Initial focus reaches first display command; return to eligible Display trigger, not detached/hidden/disabled controls. Regression reference #583. |
| K07 | Browse items | Navigate visible thumbnails, select one and multiple if supported; open focused image with documented keyboard command. | Focus indication clear; selected state remains distinguishable; opening focused view does not require double-click. |
| K08 | Focused Viewer | Operate previous/next arrows, Fit, 1:1, zoom and info; Escape to Browse. | Every visible essential action reachable; no keyboard trap; return to a sensible Browse item/owner. |
| K09 | Inspector | From selected image, move through Organize, Creative, Publication, Information; edit a reversible field and cancel/reset. | Logical labels and focus order; visible focus; no unexpected data mutation; keyboard access at compact and pinned sizes. |
| K10 | Settings and diagnostics | Navigate settings, launch and close a diagnostic dialog. | Dialog focus owned by modal; on dismissal returns only to enabled, attached, visible invoking control; regression reference #581. |
| K11 | Confirm / notify dialogs | Trigger representative non-destructive notification and a cancellable confirm, dismiss with X/Escape where supported. | No focus loss, stale refocus, or focus behind modal; regression reference #579. |
| K12 | Recovery / no-match | Trigger a reproducible no-match state, activate offered Filter/Display recovery using keyboard. | Focus enters the newly opened flyout then returns to an eligible live trigger; state and recovery obvious. |

If an action is unavailable in a given state, mark **Blocked** with the setup required, not Pass.

## Size and text-scale matrix

Use the same artifact for each supported pair below. Minimum manual full-frame review: 900×600, 1024×768, 1440×900, 1920×1080; inspect text scales 100%, 125%, 150%, 200%, 225%. Record display scaling **separately** from text scaling. At a minimum, capture 900×600 at 100% and 225%, 1440×900 at 100% and 225%, then check intermediate scales and remaining window sizes; do not imply omitted combinations passed.

| ID | Surface | Observe / reject if |
|---|---|---|
| V01 | Shell / secondary pane | Navigation wraps unintelligibly, destination or Settings becomes unreachable, canvas collapses into competing fixed sidebars. |
| V02 | Browse toolbar / flyouts | Search/filter/view modes clipped; flyout action obscured, keyboard focus hidden, text overlaps another command. |
| V03 | Thumbnail grid / empty states | Essential state conveyed by color alone; tiles obscure labels or resize into unusable hit targets. |
| V04 | Focused Viewer | Top chrome, zoom commands, arrow affordances or close/back buttons obscure image or disappear; text collides. |
| V05 | Inspector all tabs | Rating glyph miscentered, color cells uneven, editor labels collide; 150%+ fails stacked form layout; actions unreachable. |
| V06 | Settings / dialogs | Buttons extend beyond viewport; dialog cannot be dismissed; body or key actions clipped. |

Always inspect the actual screenshot at full frame **and** magnified at suspect control geometry; 'artifact generated' is not a visual review outcome. Preserve filenames of original unmodified evidence. Compare generated CI evidence with actual physical Portable screens without treating rendering differences as automatic failures or passes.

## Evidence and closure gates

1. **Automated gate**: link full Windows CI run, App/Viewer and keyboard regression output, visual evidence artifact, 100k Viewer/Library/Image performance, NativeAOT lifecycle and default Portable tests. No threshold changes.
2. **Manual keyboard gate**: K01–K12 results on physical Windows; repeat failed flows after code fixes.
3. **Manual visual gate**: V01–V06 at the resolutions/scales specified by parent #503, with actual screenshot review.
4. **Product-owner gate**: record an explicit normal-use Portable acceptance verdict. CI success does not imply this verdict.
5. **Defect handling**: every reproducible P0/P1 gets a focused child Issue, regression test where feasible and independent PR. Parent #503 remains Open until all its own acceptance criteria are satisfied.

Status at creation: **all cases Not run**. The #583 keyboard regression and its Windows CI passed, but that evidence is not substituted for the full manual matrix.
