# Lumine v2 foundation architecture

## Decision

Lumine v2 is a greenfield codebase. The legacy application is not a source dependency and is not incrementally ported into v2. Old branches remain useful as product history, regression evidence, benchmark input and migration reference.

## Initial architecture

```text
Lumine.App
   |----> Lumine.Library ----> Lumine.Core
   |----> Lumine.Image   ----> Lumine.Core
   |----> Lumine.Viewer  ----> Lumine.Core
   |----> Lumine.Diagnostics
   |
   +---- Avalonia desktop composition root
```

Product sibling modules must not reference each other. Cross-cutting product contracts belong in `Lumine.Core`. `Lumine.Diagnostics` is a product-independent observability utility: feature modules may use it for measurements, but Diagnostics must not reference product feature modules. Composition happens only in `Lumine.App`.

## Technology decisions

- **Avalonia**: the v2 viewer does not depend on Chromium/WebView/React.
- **SQLite**: local transactional metadata storage, with schema/search work deferred.
- **libvips**: initial image decoder/thumbnail engine; WIC remains a benchmark fallback.
- **NativeAOT**: enabled from the first commit and treated as an architectural constraint.
- **Diagnostics**: performance measurements use a shared AOT-compatible JSON contract so Library/Image/Viewer results are comparable across CI and physical hosts.

## Non-goals for the foundation issue

No thumbnail cache, high-volume viewer, filesystem watcher, USN recovery, v1 migration, AI runtime, semantic search, tagger, vision model or Prompt Engine is implemented here.

## Resource rules

1. Large library size must not imply large UI-object count.
2. Originals are not the steady-state source for grid thumbnails.
3. Memory and GPU caches must be bounded.
4. Background work yields to foreground viewer interaction.
5. AI components are optional and lazy-loaded.
6. The app core remains usable with AI completely absent.

## Development and release branches

The greenfield source tree follows one integration line:

```text
{issue-number}-{type}
        |
        v
     develop
        |
        v
     master
        |
        v
 tag / GitHub Release
```

- `develop` is the only integration branch for active greenfield development.
- Every implementation starts from the current `develop` head on a branch named `{issue-number}-{type}`, such as `287-feat` or `295-test`.
- Pull requests from issue branches target `develop`.
- `master` contains release-ready code only. Development work does not target `master` directly.
- Version tags and GitHub Releases are created from `master`.
- `legacy/develop-v1` preserves the former v1 development line. The historical `v2` branch is retained only as migration history and is not a development base.
