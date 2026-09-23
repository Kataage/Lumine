# Lumine v2 Core diagnostics

Issue #286 establishes the measurement contract used by later Library, Image, Viewer and filesystem work.

## Principles

- CI validates that measurement is correct; it does not enforce host-specific performance thresholds.
- Physical and representative-library runs use the same JSON schema as CI.
- Every result records hardware ID, OS/runtime/architecture, app revision and app version.
- Measurements include elapsed duration plus before/after working set, managed heap, total allocation counters, GC collections and process CPU time.
- New Core subsystems should use stable names from `CoreMetricNames` where one already exists.

## Environment

Optional environment variables:

- `LUMINE_HARDWARE_ID`: stable name for the machine under test. Defaults to the machine name.
- `LUMINE_REVISION`: source revision. CI sets this to `github.sha`.
- `LUMINE_APP_VERSION`: product version. Defaults to `dev`.
- `LUMINE_DIAGNOSTICS_OUTPUT`: when set for the desktop app, writes startup/window-ready diagnostics to this JSON path.

## Deterministic fixture

`FixtureGenerator` produces deterministic metadata by asset index rather than using runtime random-number behavior. The same count therefore produces the same SHA-256 fixture digest.

The benchmark tool can optionally materialize the synthetic relative paths as empty placeholder files. These files are intended for filesystem/DB scale tests, not image-decode tests.

## CLI

Generate and measure the 100k metadata fixture:

```powershell
dotnet run --project tools/Lumine.Benchmarks/Lumine.Benchmarks.csproj -c Release -- --count 100000 --output artifacts/benchmarks/core-100000.json
```

Also write all deterministic metadata:

```powershell
dotnet run --project tools/Lumine.Benchmarks/Lumine.Benchmarks.csproj -c Release -- --count 100000 --output artifacts/benchmarks/core-100000.json --metadata-output artifacts/fixtures/assets-100000.json
```

Optionally create a synthetic 100k-file tree:

```powershell
dotnet run --project tools/Lumine.Benchmarks/Lumine.Benchmarks.csproj -c Release -- --count 100000 --output artifacts/benchmarks/core-100000.json --tree-root artifacts/fixtures/fs-100000
```

Performance acceptance numbers belong to the feature issue being measured. They are not hidden inside this common harness.
