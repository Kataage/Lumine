# Lumine v2 bounded resource policy

Issue #332 establishes one effective Core resource policy before the real MainWindow composition in #309.

## Source of truth

`CoreResourcePolicy` is the resolved runtime policy. `ResourcePolicySettings` is the settings/persistence boundary: optional user/configuration overrides are validated and converted into one immutable effective policy before Image/Viewer runtime objects are created.

The current App uses `Program.ResourcePolicy` as its single effective-policy reference. #293 may later load persisted settings before resolving this value; #309 must consume this same resolved policy when it creates the production Image/Viewer ownership graph.

## Bounded values

The policy owns:

- thumbnail worker count
- thumbnail queue capacity
- foreground fairness burst
- decoded grid-thumbnail entry and byte budgets
- Detail preview entry and byte budgets
- Detail original decoded-byte budget
- persistent thumbnail disk-cache budget
- libvips tracked-memory budget
- effective libvips concurrency derived from processor count and thumbnail worker count

Every configurable value has an explicit upper bound. Invalid persisted/user values are rejected during resolution rather than flowing into runtime constructors.

## libvips process-global rule

libvips cache/concurrency configuration is process-global. `VipsRuntimePolicy` therefore configures it exactly once from the effective Core policy.

A later explicit attempt to configure different libvips values fails instead of silently mutating the process-global runtime while workers may already exist. Creating a cache without an explicit policy after configuration reuses the already-established runtime configuration.

The production App must resolve its policy before creating the first `ThumbnailCache`.

## Persistent cache budget

`ThumbnailCache.ConfiguredByteLimit` carries the effective disk budget and `PruneToConfiguredLimitAsync` applies it during explicit/background maintenance.

Cache construction does **not** recursively scan or prune the cache. This preserves the warm-start rule that startup must not perform an unconditional full cache walk.

## Option mapping

Image and Viewer keep their module-specific option types, but production values are mapped from the same Core policy:

- `ThumbnailPipelineOptions.FromResourcePolicy`
- `ViewerOptions.FromResourcePolicy`
- `ViewerDetailOptions.FromResourcePolicy`
- `ThumbnailCache(root, resourcePolicy)`

Tests may still construct smaller explicit option values for deterministic fixtures. Production composition in #309 must use the policy mappings rather than unrelated defaults.

## Diagnostics

`CoreResourcePolicy.ToDiagnosticMetadata()` exposes the effective values without introducing a Core -> Diagnostics dependency. The App includes this metadata in the existing app-session diagnostic JSON.

This makes the actual worker/memory/cache/libvips budgets visible in field reports and real-library acceptance results.
