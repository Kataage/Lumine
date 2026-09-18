# Benchmark result format

Canonical schema generation: `schemaVersion: 1`. Canonical evaluator generation: `evaluatorVersion: "lumine-ai-bench-v1"`.

A complete result contains:

- `runId` and UTC `createdAt`;
- `catalogPack` identifying the immutable fixture-pack generation;
- `model` with model ID, version, engine, quantization, artifact SHA-256/size when available, runtime, benchmark categories, and fixed runtime parameters;
- `environment` with stable hardware ID, OS/architecture, CPU/GPU/RAM and runtime/Lumine versions;
- exactly one `cases[]` entry for every fixture ID in `catalog.json`.

Every case records the fixture ID/category, `status` (`ok`, `skipped`, or `error`), normalized `score` in `0..1`, optional performance metrics, auditable structured output, error text, and notes.

Unsupported model categories are not removed from the result; they are stored as `skipped`. This keeps the fixture universe stable and makes capability changes visible. When a baseline case succeeded and the candidate becomes skipped/error, `compare` treats it as a regression.

Performance fields:

- `latencyMs` — measured request latency;
- `tokensPerSecond` — generation throughput where applicable;
- `ramMb` — peak resident memory for the declared scenario;
- `coldStartMs` — process/model startup through first successful result;
- `modelSizeMb` — installed model artifact footprint;
- `runtimeSuccessRate` — successful stability cycles divided by requested cycles.

The comparison command rejects differing fixture-pack IDs through result validation, differing evaluator generations, and differing hardware IDs by default. The hardware override exists for quality inspection only; it does not make cross-hardware performance numbers controlled measurements.

Selected result files that justify adoption/rejection belong under `results/evidence/` and should be referenced by `adoptions.json`. Ordinary experiments belong under ignored `results/local/`.
