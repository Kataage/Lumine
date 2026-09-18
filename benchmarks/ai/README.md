# Lumine AI Benchmark

This directory is the source of truth for comparing local AI models used by Lumine. The goal is to keep model changes evidence-driven: a new model, quantization, runtime, or version should be evaluated against the same catalog, fixture pack, hardware identity, and regression thresholds before an adoption record is changed to `adopted`.

## What is versioned

- `catalog.json` defines stable benchmark case IDs and covers every category required by Issue #176.
- `thresholds.json` defines default regression limits.
- `adoptions.json` records candidate/adopted/rejected decisions and the evidence used for adopted models.
- `profiles/` contains model-profile templates or immutable profiles used by recorded runs.
- `results/evidence/` is for selected benchmark results that justify an adoption or rejection.

Large, copyrighted, private, or adult-only image fixtures are **not committed**. They live in a local fixture pack. The pack identity is `lumine-ai-core-v1`; its `manifest.json` records SHA-256 and byte size for every file referenced by the catalog. A run refuses to start if the fixture pack does not match.

## Fixture pack layout

Create a directory outside the repository, or under the ignored `benchmarks/ai/fixtures/private/` directory, with paths matching `catalog.json`, for example:

```text
<fixture-dir>/
  semantic/night-cafe-positive-001.png
  semantic/day-landscape-negative-001.png
  tagging/danbooru-basic-001.png
  tagging/character-known-001.png
  rating/general-001.png
  rating/sensitive-001.png
  rating/explicit-adult-001.png
  vision/lightweight-scene-001.png
  vision/advanced-relations-001.png
```

After curating or changing the pack, generate its hash manifest:

```powershell
go run ./cmd/ai-bench hash-fixtures `
  -catalog benchmarks/ai/catalog.json `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v1
```

Validate both catalog coverage and local fixture integrity:

```powershell
go run ./cmd/ai-bench validate-catalog `
  -catalog benchmarks/ai/catalog.json `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v1
```

Do not silently replace a fixture while keeping the same pack ID. If benchmark semantics change, create a new pack ID (for example `lumine-ai-core-v2`) and update the catalog. This prevents historical results from appearing directly comparable when their inputs changed.

## Adapter protocol

The harness is model/runtime agnostic. A concrete benchmark adapter is an executable invoked once per fixture. It receives one JSON `AdapterRequest` on stdin and must write exactly one JSON `AdapterResponse` to stdout.

Request shape:

```json
{
  "schemaVersion": 1,
  "fixtureDir": "D:\\LumineBench\\lumine-ai-core-v1",
  "model": {
    "id": "vendor/model",
    "version": "1.0",
    "engine": "runtime-id",
    "quantization": "Q4_K_M"
  },
  "fixture": {
    "id": "prompt-ja-to-image-001",
    "category": "ja_to_prompt",
    "input": {}
  }
}
```

Response shape:

```json
{
  "status": "ok",
  "score": 0.94,
  "metrics": {
    "latencyMs": 420.5,
    "tokensPerSecond": 38.2,
    "ramMb": 3180,
    "coldStartMs": 1900,
    "modelSizeMb": 2350,
    "runtimeSuccessRate": 1.0
  },
  "output": {},
  "notes": ""
}
```

`score` is normalized to `0..1` using the category-specific rubric implemented by the adapter. The harness measures external wall time and uses it as `latencyMs` only when the adapter does not report a more precise value. For the cold-start fixture, the same wall measurement becomes `coldStartMs` when omitted by the adapter.

Adapters must not write logs to stdout; use stderr for diagnostics.

## Running a benchmark

Create an immutable model profile from `profiles/example.json`. Record the exact model version, engine/runtime, quantization and artifact SHA-256 whenever available.

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalog.json `
  -profile benchmarks/ai/profiles/my-model.json `
  -adapter C:\path\to\lumine-model-bench-adapter.exe `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v1 `
  -hardware-id "desktop-rtx3060-cpu8t-v1" `
  -cpu "CPU model / thread configuration" `
  -gpu "NVIDIA RTX 3060" `
  -lumine-version "<git commit>" `
  -out benchmarks/ai/results/local/my-model.json
```

The hardware ID is deliberately explicit. Performance results from different hardware IDs are rejected by `compare` unless `-allow-hardware-mismatch` is supplied. Use that override only for quality-oriented inspection; do not treat cross-hardware latency/RAM numbers as a controlled regression comparison.

Validate a saved result:

```powershell
go run ./cmd/ai-bench validate-result `
  -catalog benchmarks/ai/catalog.json `
  -result benchmarks/ai/results/local/my-model.json
```

## Regression comparison

```powershell
go run ./cmd/ai-bench compare `
  -catalog benchmarks/ai/catalog.json `
  -thresholds benchmarks/ai/thresholds.json `
  -baseline benchmarks/ai/results/evidence/baseline.json `
  -candidate benchmarks/ai/results/local/candidate.json `
  -out benchmarks/ai/results/local/comparison.json
```

The comparison fails when:

- normalized quality score drops beyond `maxScoreDrop`;
- latency, RAM, cold start, or model size regress beyond their percentage thresholds;
- tokens/sec drops beyond its threshold;
- runtime success rate drops beyond its absolute threshold;
- a baseline-successful case becomes skipped or errored.

This makes adapter/runtime crashes visible rather than accidentally excluding them from the comparison.

## Adoption records

Validate the ledger with:

```powershell
go run ./cmd/ai-bench validate-adoptions -file benchmarks/ai/adoptions.json
```

A record may remain `candidate` while evaluation is ongoing. A record marked `adopted` must include version, engine, quantization, rationale, and at least one path to benchmark evidence. When replacing an adopted model, keep the old decision as historical evidence rather than rewriting why it was chosen.

## Category policy

The catalog covers semantic retrieval; Danbooru, character, and rating tagging; lightweight and advanced vision; Japanese-to-prompt; IL/ILXL prompt quality; model-profile conversion; structured JSON; partial-edit constraint preservation; LoRA trigger preservation; adult-only prompt robustness; CPU latency/tokens-sec; RAM; cold start; model size; and Windows runtime stability.

For adult-only fixtures, use only lawful adult subjects and keep the fixture pack private when redistribution rights or content sensitivity are unclear. The benchmark checks unnecessary refusal/weakening behavior without requiring sensitive fixture content to be committed to the public repository.
