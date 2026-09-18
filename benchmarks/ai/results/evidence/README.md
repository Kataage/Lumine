# Benchmark evidence

Commit only results that materially support a model adoption, rejection, runtime change, quantization change, or regression investigation.

Each committed result must:

- validate against the current catalog with `ai-bench validate-result`;
- identify the immutable fixture-pack ID;
- record a stable hardware ID and enough CPU/GPU/runtime detail to reproduce performance measurements;
- record exact model/version/engine/quantization and artifact SHA-256 when available;
- be referenced from `benchmarks/ai/adoptions.json` when it supports an `adopted` decision.

Do not commit ordinary local trial runs here. Use `benchmarks/ai/results/local/`, which is ignored by Git.
