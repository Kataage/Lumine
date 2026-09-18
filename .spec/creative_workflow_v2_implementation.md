# Creative Workflow v2 implementation scope

This branch implements the first usable creative-workflow foundation described in `creative_workflow_v2.md`.

## Included

- Work persistence and per-asset work context.
- Generation Group persistence with prompt/model/sampler/scheduler/steps/CFG/notes and ordered assets.
- Directed asset lineage relationships with explicit relation type and direction.
- Multi-selection organizer for creating works, groups, and two-image lineage links.
- Rich publication snapshots: ordered images, title, body/caption, tags, publish date, external ID/URL and platform metadata.
- Pixiv-oriented fields for age restriction and AI-generated status; X-oriented copy labels.
- Detail-panel creative context and descriptive publication history.
- Application-owned confirm/message dialogs for active destructive actions touched by the current UI.
- Database and frontend regression tests for the new concepts.

## Deliberately deferred

- Automatic ComfyUI PNG/workflow metadata extraction and automatic group suggestions.
- Graph visualization of multi-generation lineage beyond the current contextual relation list.
- Automatic publishing to external services.

Those are intended to build on this data model rather than change its meaning.
