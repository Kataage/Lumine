# Prompt Engine benchmark

Issue: #169

This benchmark decides Lumine's local Prompt Engine from Lumine-specific controlled measurements. It does not select a winner from general leaderboard scores or model-card impressions.

## Candidate refresh (2026-09-19)

The initial two-candidate issue remains the starting point, but the controlled set now includes official/base-lineage controls so that refusal attenuation and fine-tuning trade-offs can be measured directly:

- huihui-ai/Huihui-NeoHorse-1-4B-abliterated-GGUF
- soyaakinohara/Spark-X2.5-4B-Heretic-jp-gguf
- kaineone/Qwen3.5-4B-abliterated-GGUF
- openresearchtools/Qwen3.5-4B-GGUF as a base-lineage control

The checked-in profiles are initially exploratory (version: main, no artifact SHA-256). They may be used to discover the exact immutable revision and artifact hash, but they must not be used as adoption evidence. After a candidate is downloaded and inspected, copy the adapter-reported resolvedRevision, modelSha256, exact file size, runtime version and runtime hash into a pinned profile before producing evidence under results/evidence/.

### Spark runtime caveat

Spark-X2.5-4B-Heretic-jp uses the Spark2_5 architecture. Its model card requires a Spark-compatible XHToken/llama.cpp fork rather than upstream llama.cpp.

The Prompt adapter therefore refuses to silently launch upstream llama.cpp for the Spark profile. Set:

    $env:LUMINE_PROMPT_LLAMA_SERVER = "C:\path\to\spark-compatible\llama-server.exe"

Before any adoption evidence is recorded, pin the exact fork revision/build and binary SHA-256 in the evidence notes/profile. An unpinned custom server run is exploratory only.

## What is measured

The catalog catalogs/prompt-engine-v1.json covers:

- Japanese creative instruction -> image-generation prompt
- Illustrious / ILXL prompt quality
- target-model profile conversion
- strict structured JSON
- partial-edit constraint preservation
- exact LoRA syntax / trigger preservation
- lawful clearly-adult, non-graphic prompt robustness
- CPU latency / tokens per second
- RAM
- cold start
- model size
- repeated Windows runtime stability

The adult-only case is intentionally non-graphic and exists to detect unrelated refusal or silent content deletion. It is a product-quality test, not a request to generate benchmark imagery.

## Environment

Use the same Windows machine, power plan, CPU thread count, llama.cpp build, context size and quantization class for comparable candidates whenever the model architecture permits it.

Install the research adapter environment:

    py -3 -m venv .venv-prompt-bench
    .\.venv-prompt-bench\Scripts\python -m pip install -r benchmarks\ai\adapters\requirements-prompt.txt

The default upstream-runtime profiles pin the same CPU llama.cpp release used by the current Vision research tooling. GPU offload is disabled for the controlled CPU run.

## Run a candidate

Example:

    go run ./cmd/ai-bench run \
      -catalog benchmarks/ai/catalogs/prompt-engine-v1.json \
      -profile benchmarks/ai/profiles/prompt-qwen3.5-4b-abliterated-q4.json \
      -adapter .\.venv-prompt-bench\Scripts\python.exe \
      -adapter-arg benchmarks\ai\adapters\prompt_llamacpp.py \
      -hardware-id "<same-controlled-machine-id>" \
      -cpu "<exact CPU and thread configuration>" \
      -lumine-version "<git commit>" \
      -out benchmarks\ai\results\local\prompt-qwen35-abliterated.json

Prompt fixtures are text-only, so no private fixture directory is required for this scoped catalog.

For NeoHorse, the exploratory profile deliberately resolves exactly one Q4_K_M GGUF by regex because the GGUF repository was published very recently. Pin the exact filename/revision/hash before evidence.

For Spark, set LUMINE_PROMPT_LLAMA_SERVER to the validated Spark-compatible server before running.

## Review

Generate a side-by-side table:

    py -3 benchmarks\ai\adapters\prompt_report.py \
      benchmarks\ai\results\local\prompt-neohorse.json \
      benchmarks\ai\results\local\prompt-spark.json \
      benchmarks\ai\results\local\prompt-qwen35-abliterated.json \
      benchmarks\ai\results\local\prompt-qwen35-control.json \
      > benchmarks\ai\results\local\prompt-comparison.md

Do not mark any Prompt Engine record adopted until:

1. candidates used as evidence have immutable model revision + artifact SHA-256;
2. the runtime is pinned, including the Spark fork when applicable;
3. result files share the same catalog/evaluator/hardware identity;
4. structured-output failures and runtime crashes are retained rather than excluded;
5. Japanese, IL/ILXL, edit-preservation, LoRA, adult-only robustness and CPU/resource results have all been reviewed.

Abliterated/Heretic behavior is evaluated alongside ordinary prompt quality. Lower refusal is not automatically considered better if instruction following, structured JSON, prompt fidelity or runtime reliability regresses.
