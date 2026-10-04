# Lumine UI design system

This document defines the product-level interaction grammar used by Lumine v2. It is intentionally small: product screens should compose these rules instead of inventing local variants.

## Information hierarchy

- **Primary canvas first.** Browse and Focused Viewer preserve image area before persistent secondary panes.
- **Global navigation** owns Library / Folder / Tag / Publication / Settings destinations.
- **Contextual panes** are overlays below 1200 DIP and may be explicitly pinned only on wide layouts.
- **Inspector** is contextual image metadata/editing, not a second navigation system.
- **Advanced / diagnostic information** stays behind explicit disclosure.

## Typography

Use the shared LumineDesign roles:
- Emphasis: page/state title.
- Body: primary explanatory or editable content.
- Caption: labels, metadata and secondary explanation.

Readable content pages stay bounded. Long Japanese user-facing text wraps; technical details use scrollable or expandable surfaces rather than shrinking text.

## Spacing and geometry

Use the shared spacing ramp only: 2 / 4 / 6 / 8 / 12 / 16 / 24 DIP.

- standard controls use CompactControlHeight and ControlRadius
- panels use PanelRadius
- page content uses PageGutter
- related controls group by proximity before adding another border/card
- overlays keep a visible boundary from the image canvas; pinned panes become flush docked edges

## Command hierarchy

- **Primary:** the single preferred next action. Use ConfigurePrimaryButton.
- **Secondary:** ordinary alternative/action. Use ConfigureSecondaryButton.
- **Destructive:** data/file removal. Use ConfigureDangerButton and require confirmation for irreversible source deletion.
- **Icon-only:** only for strong, conventional metaphors such as close, pin, settings/fullscreen. Every icon-only command needs an accessible name and tooltip.
- Low-frequency commands belong in context menus, flyouts or Advanced/detail disclosure.

Selection and active navigation must never rely on color alone. Selected global navigation also has a physical accent indicator; selected controls retain border/weight/state cues.

## Interaction states

Neutral, hover, pressed, selected, selected-hover, disabled, focus and danger states come from LumineDesign tokens. Local controls must not invent conflicting state colors.

Keyboard rules:
- Tab order follows visual order.
- global navigation supports Up/Down/Home/End
- focused Viewer supports arrows, +/-, 0, 1, I, F11 and Esc
- transient/modal layers close predictably and restore focus to the invoking control
- icon controls expose automation names; shortcuts expose accelerator metadata

## Product states and feedback

Welcome, Loading, Empty Library, No Match, Recoverable Error and normal Workspace are distinct product states.

- one primary message per state
- one obvious recovery/next action
- central states do not duplicate the same message in the top status banner
- successful operations use short non-blocking status feedback
- technical error details are progressive disclosure

## Accessibility and review matrix

CI renders and manually reviewable evidence must cover:

- viewports: 900x600, 1024x768, 1440x900, 1920x1080
- text scale: 100%, 125%, 150%, 200%, 225%
- Browse Grid/List, navigation overlay/pinned, Inspector overlay/pinned, Focused Viewer, Tags, Settings, Welcome/Loading/Empty/No Match/Error
- visible text clipping, control containment, focus semantics, keyboard navigation, contrast and non-color selection cues

Pixel identity is not the acceptance contract. Deterministic geometry/state assertions are the gate; PNG evidence is for human visual review.
