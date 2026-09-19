# Prompt Engine benchmark

Issue: #169

This benchmark decides Lumine's local Prompt Engine from Lumine-specific controlled measurements. It does not select a winner from general leaderboard scores or model-card impressions.

## Candidate refresh (2026-09-19)

The initial two-candidate issue remains the starting point, but the controlled set now includes official/base-lineage controls so that refusal attenuation and fine-tuning trade-offs can be measured directly:

- huihui-ai/Huihui-NeoHorse-1-4B-abliterated-GGUF
- soyaakinohara/Spark-X2.5-4B-Heretic-jp-gguf
- kaineone/Qwen3.5-4B-abliterated-GGUF
- TheStageAI/Qwen3.5-4B-GGUF (M-TS-Q4_K_M) as a reproducible base-lineage control

### Candidates reviewed but not in the first matrix

- allenai/Tmax-4B: valid 4B Qwen3.5-derived llama.cpp candidate, but it is RL-specialized for terminal/software-engineering tasks rather than Japanese creative prompt work. Keep as an optional control if the first matrix is inconclusive.
- Ornith-1.5: the practical small GGUF is 9B-class, outside this phase's 4B-ish CPU footprint target.
- Qwen3.6 small-active MoE derivatives: current practical GGUFs have much larger total footprints (roughly 12-15 GB for representative 3B-active variants), so they are deferred from the first CPU-oriented matrix.

The checked-in profiles are initially exploratory (version: main, no artifact SHA-256). They may be used to discover the exact immutable revision and artifact hash, but they must not be used as adoption evidence. After a candidate is downloaded and inspected, copy the adapter-reported resolvedRevision, modelSha256, exact file size, runtime version and runtime hash into a pinned profile before producing evidence under results/evidence/.

### Spark runtime status

Spark-X2.5-4B-Heretic-jp uses the Spark2_5 architecture. Older model-card text says a Spark-specific llama.cpp fork is required, but upstream llama.cpp added native Spark2.5 support in v0.4.1 (#27868). Lumine's benchmark runtime is pinned to nightly b10964, which is the v0.4.1 release commit, so Spark is evaluated with the same upstream runtime family as the Qwen/NeoHorse candidates.

If the model repository documentation and upstream runtime support disagree, upstream llama.cpp release/support state is the runtime source of truth for Lumine. A custom server override remains available through LUMINE_PROMPT_LLAMA_SERVER for debugging, but is not required for the controlled Spark run.

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

For Spark, use the checked-in upstream llama.cpp b10964/v0.4.1 runtime profile. The optional LUMINE_PROMPT_LLAMA_SERVER override is for debugging only and should not be used for controlled evidence unless that alternate runtime is explicitly pinned.

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
2. the runtime is pinned; the controlled Spark run uses upstream llama.cpp b10964/v0.4.1 with native Spark2.5 support;
3. result files share the same catalog/evaluator/hardware identity;
4. structured-output failures and runtime crashes are retained rather than excluded;
5. Japanese, IL/ILXL, edit-preservation, LoRA, adult-only robustness and CPU/resource results have all been reviewed.

Abliterated/Heretic behavior is evaluated alongside ordinary prompt quality. Lower refusal is not automatically considered better if instruction following, structured JSON, prompt fidelity or runtime reliability regresses.
