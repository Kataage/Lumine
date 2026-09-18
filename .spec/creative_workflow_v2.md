# Lumine Creative Workflow v2

## Purpose
Lumine is not a file browser with tags. It is a local-first creative archive that preserves how generated images belong together, how one image was derived from another, which images became a work, and exactly how that work was published.

## Domain hierarchy

- **Asset**: one physical image file. File identity, technical metadata, rating, favorite state, local tags and notes live here.
- **Work**: a human-facing creative unit such as “白上フブキ - 夏祭り”. A work may contain many assets from multiple generation groups and may have many publications.
- **Generation Group**: images created from the same generation intent/run family. It stores generation context (prompt, negative prompt, model, sampler, scheduler, steps, CFG and workflow payload) and ordered member assets.
- **Asset Relation**: directed lineage between two assets. A group answers “these belong together”; a relation answers “this came from that”. Relation types are extensible (`variation`, `img2img`, `inpaint`, `outpaint`, `upscale`, `crop`, `edit`, `animation`, `reference`, etc.).
- **Publication**: a snapshot of what was actually published. It stores ordered assets, title, body/caption, tags/hashtags, destination/account, publish time, external ID/URL and platform-specific metadata. A publication must remain useful even if the user later changes local tags or notes.

## Invariants

1. Existing asset and post data must remain readable after migration.
2. Group membership and lineage are separate concepts and must never be inferred to mean one another.
3. Asset relations are directed and must not permit self-links.
4. Ordered image membership must be preserved for generation groups, works and publications.
5. Platform-specific publication data is stored as JSON, while common searchable fields remain first-class columns.
6. Destructive actions use Lumine-owned dialogs. Browser/JavaScript `alert`, `confirm` and `prompt` are not acceptable application UX.
7. UI must show context, not just IDs: work names, group names, relation direction/type, publication copy and ordered image membership.
8. Automatic ComfyUI metadata extraction/group suggestion is an enhancement on top of this stable model; manual organization must work without it.

## UX direction

### Image detail
The right detail panel should answer:
- What work does this image belong to?
- Which generation group is it part of and with what generation context?
- What is its parent/child lineage?
- Where was it published and with what title/body/tags?

### Multi-selection
Multi-selection is the natural place to create a generation group or work from several assets. Two selected assets can be linked with a directed relation and the direction must be explicit/reversible before saving.

### Publication editor
Publication entry is destination-aware. Pixiv-oriented records expose title, caption, tags, age restriction and AI-generated flag. X-oriented records prioritize body, hashtags and ordered images. Unknown/custom destinations retain title/body/tags and arbitrary metadata.

## Non-goals for this foundation

- Automatic posting to external services.
- Automatic grouping without user confirmation.
- Cloud synchronization.
- Replacing the local file as the source image.

These may be added later without changing the domain model above.
