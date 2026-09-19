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

The four checked-in profiles are now immutably pinned to exact Hugging Face revisions, exact GGUF filenames, LFS SHA-256 values and byte sizes. NeoHorse publishes Q4_K rather than Q4_K_M, so its controlled profile uses the exact published Q4_K artifact. The metadata inspector remains available to revalidate these pins or deliberately refresh them later without downloading model weights.

### Spark runtime status

Spark-X2.5-4B-Heretic-jp uses the Spark2_5 architecture. Older model-card text says a Spark-specific llama.cpp fork is required, but upstream llama.cpp added native Spark2.5 support in v0.4.1 (#27868). Lumine's controlled CPU benchmark is pinned to b11053, matching the currently verified product CPU runtime, so Spark is evaluated with the same upstream runtime family and product-era runtime as the Qwen/NeoHorse candidates.

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

## Verify or refresh candidate pins without downloading weights

The current Prompt profiles are already immutable. Revalidate their repository/file metadata without downloading multi-gigabyte GGUF weights:

```powershell
.\benchmarks\ai\run_prompt_engine_benchmark.ps1 -InspectOnly
```

This calls the official Hugging Face model metadata API with file metadata enabled and writes `prompt-pin-info.json` containing the full repository commit SHA, exact GGUF filename, LFS SHA-256 and byte size for all four candidates. It does not modify checked-in profiles.

When intentionally refreshing a profile to newer upstream artifacts, review that output first and then explicitly apply the newly resolved pins:

```powershell
.\benchmarks\ai\run_prompt_engine_benchmark.ps1 -InspectOnly -ApplyPins
```

`-ApplyPins` is intentionally accepted only with `-InspectOnly`. It stores the resolved immutable revision plus exact model file/SHA-256/size and marks the profile as pinned. Normal benchmark runs never mutate repository profiles. Any refreshed pins must be reviewed and committed before they are used as adoption evidence.

## Recommended one-command Windows run

Prompt fixtures are text-only, so the current #169 discovery matrix can be run directly:

```powershell
.\benchmarks\ai\run_prompt_engine_benchmark.ps1 \
  -HardwareId "main-pc-cpu8"
```

The runner records CPU/RAM/current Lumine commit, runs NeoHorse, Spark-X2.5 Heretic-jp, Qwen3.5 abliterated and the base-lineage Qwen3.5 control under the same CPU policy, validates every result, and writes:

- `prompt-comparison.md`
- `prompt-pin-info.json`
- `prompt-run-info.json`

`prompt-pin-info.json` is generated before inference from official Hugging Face repository/file metadata and revalidates the checked-in immutable revision, exact filename, SHA-256 and byte size. `prompt-run-info.json` records `evidenceReady: false` if either the model or controlled runtime pin is incomplete.

For controlled evidence, clear `LUMINE_PROMPT_LLAMA_SERVER`. `-AllowLlamaServerOverride` exists for debugging only.

The manual commands below remain available for single-candidate debugging.

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

For NeoHorse, use the checked-in exact Q4_K artifact. The upstream GGUF repository does not publish a Q4_K_M file, so Lumine does not substitute or guess one.

For Spark, use the checked-in upstream llama.cpp b11053 CPU runtime profile, matching Lumine's currently verified product runtime line. The optional LUMINE_PROMPT_LLAMA_SERVER override is for debugging only and should not be used for controlled evidence unless that alternate runtime is explicitly pinned.

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
2. the runtime is pinned; all controlled candidates use the checked-in upstream llama.cpp b11053 CPU runtime, including Spark with native Spark2.5 support;
3. result files share the same catalog/evaluator/hardware identity;
4. structured-output failures and runtime crashes are retained rather than excluded;
5. Japanese, IL/ILXL, edit-preservation, LoRA, adult-only robustness and CPU/resource results have all been reviewed.

Abliterated/Heretic behavior is evaluated alongside ordinary prompt quality. Lower refusal is not automatically considered better if instruction following, structured JSON, prompt fidelity or runtime reliability regresses.
