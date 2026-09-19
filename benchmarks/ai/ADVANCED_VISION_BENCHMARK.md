# Advanced Vision benchmark

Issue: #189  
Parent: #168  
Benchmark framework: #176

This benchmark compares the local multimodal candidates for Lumine Advanced Vision under the **same private fixture pack, evaluator, llama.cpp runtime, CPU thread count, and Windows hardware ID**.

The candidate list is intentionally broader than vendor-default models. Official Qwen, Heretic/Abliterated derivatives, InternVL, SmolVLM, MiniCPM and Granite Vision are compared directly so that lower refusal behavior is measured together with any loss of visual understanding, Japanese instruction following, JSON reliability or runtime efficiency.

## Candidate matrix

| Profile | Role | Pin state |
| --- | --- | --- |
| `advanced-qwen3-vl-2b-q4.json` | official Qwen3-VL 2B baseline | immutable |
| `advanced-qwen3-vl-2b-abliterated-q4.json` | Qwen abliterated derivative | immutable |
| `advanced-qwen3-vl-2b-heretic-q4.json` | Qwen Heretic derivative | **exploratory until immutable revision + file hashes are written back** |
| `advanced-internvl3.5-2b-q4.json` | independent 2B-class baseline | immutable |
| `advanced-smolvlm2-2.2b-q4.json` | official ggml multi-image-oriented candidate | immutable |
| `advanced-minicpm-v4.6-q4.json` | current small MiniCPM official GGUF candidate | immutable |
| `advanced-granite-vision-4.1-4b-q4.json` | official 4B-class upper-cost comparison candidate | immutable |

MiniCPM-V 4.6 replaces the older MiniCPM-V 4.5 high-quality-control assumption. The 4.6 GGUF is small enough to be evaluated as a realistic Lumine candidate, not merely a large external control.

All immutable profiles pin:
- exact Hugging Face revision;
- main GGUF SHA-256 and byte size;
- mmproj SHA-256 and byte size;
- llama.cpp CPU runtime build/hash;
- quantization, context and thread count.

The Heretic profile intentionally contains `version: "main"` and empty model/mmproj hashes. It can be used only for an **exploratory discovery run**. The adapter records `resolvedRevision`, `modelSha256` and `mmprojSha256` in the model-size case. Update the profile with those immutable values and rerun the complete benchmark before treating Heretic results as evidence.

## Private fixture pack

Catalog: `catalogs/advanced-vision-v1.json`  
Pack ID: `lumine-advanced-vision-v1`

Create a private fixture directory with the catalog paths:

```text
<fixture-dir>/
  advanced/
    deep-scene-001.png
    deep-scene-001.json
    anime-action-001.png
    anime-action-001.json
    multi-diff-a-001.png
    multi-diff-b-001.png
    multi-diff-001.json
    reverse-prompt-001.png
    reverse-prompt-001.json
    japanese-instruction-001.png
    japanese-instruction-001.json
    ocr-context-001.png
    ocr-context-001.json
    adult-robustness-001.png
    adult-robustness-001.json
    structured-json-001.png
    structured-json-001.json
```

Ground-truth sidecars are written **before** inspecting candidate outputs. Supported assertions include:

```json
{
  "requiredConceptGroups": [
    ["adult woman", "woman"],
    ["low angle", "viewed from below"]
  ],
  "expectedDifferenceGroups": [
    ["red jacket", "jacket changed to red"]
  ],
  "expectedCommonalityGroups": [
    ["same background", "unchanged background"]
  ],
  "expectedVisibleTextGroups": [
    ["LUMINE"]
  ],
  "forbiddenConcepts": [
    "child"
  ],
  "requiredNonEmptyFields": [
    "composition",
    "viewpoint",
    "actions"
  ],
  "mustNotRefuse": true
}
```

The adult-only fixture must contain only clearly adult subjects and lawful content. It remains outside Git because redistribution rights and content sensitivity can differ from the public repository.

Hash the entire fixture pack:

```powershell
go run ./cmd/ai-bench hash-fixtures `
  -catalog benchmarks/ai/catalogs/advanced-vision-v1.json `
  -fixtures-dir D:\LumineBench\lumine-advanced-vision-v1

go run ./cmd/ai-bench validate-catalog `
  -catalog benchmarks/ai/catalogs/advanced-vision-v1.json `
  -fixtures-dir D:\LumineBench\lumine-advanced-vision-v1
```

Do not replace an image/sidecar while retaining the same pack ID after evidence exists. Create `lumine-advanced-vision-v2` instead.

## Recommended one-command Windows run

Use the checked-in runner for the controlled #189 comparison:

```powershell
.\benchmarks\ai\run_advanced_vision_benchmark.ps1 \
  -FixtureDir "D:\LumineBench\lumine-advanced-vision-v1" \
  -HardwareId "main-pc-cpu8"
```

The runner validates the fixture pack, records CPU/RAM/current Lumine commit, runs every immutably pinned Advanced Vision candidate, validates each result, and writes `advanced-vision-comparison.md` plus `advanced-vision-run-info.json`.

The Heretic profile is automatically included only after its revision/model/mmproj hashes and sizes are immutably pinned. While it is still exploratory, run:

```powershell
.\benchmarks\ai\run_advanced_vision_benchmark.ps1 \
  -FixtureDir "D:\LumineBench\lumine-advanced-vision-v1" \
  -HardwareId "main-pc-cpu8" \
  -RunHereticDiscovery
```

That discovery result is excluded from the evidence comparison and its model-size output is written to `advanced-heretic-pin-info.json`.

For controlled evidence, clear `LUMINE_LLAMA_SERVER`. `-AllowLlamaServerOverride` exists for debugging only.

The manual commands below remain available for single-candidate debugging.

## Environment

```powershell
py -3 -m venv .venv-advancedvision-bench
.\.venv-advancedvision-bench\Scripts\python -m pip install -r benchmarks\ai\adapters\requirements-advancedvision.txt
```

Use the same:
- Windows machine and power plan;
- 8 CPU threads;
- fixture manifest;
- Lumine commit;
- llama.cpp runtime;
- no GPU offload.

The adapter binds llama-server to `127.0.0.1` on a random port and uses `-ngl 0` plus `--no-mmproj-offload`. Candidate-specific safe server flags may be supplied by `parameters.serverArgsJson`; MiniCPM-V 4.6 currently disables reasoning for the structured-analysis benchmark.

## Run a pinned candidate

Example:

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalogs/advanced-vision-v1.json `
  -profile benchmarks/ai/profiles/advanced-qwen3-vl-2b-q4.json `
  -adapter .\.venv-advancedvision-bench\Scripts\python.exe `
  -adapter-arg benchmarks\ai\adapters\advancedvision_llamacpp.py `
  -fixtures-dir D:\LumineBench\lumine-advanced-vision-v1 `
  -hardware-id "<same-controlled-machine-id>" `
  -cpu "<exact CPU / 8-thread configuration>" `
  -lumine-version "<git commit>" `
  -out benchmarks\ai\results\local\advanced-qwen3-vl-2b-q4.json
```

Repeat with every immutable profile.

## Heretic pin workflow

1. Run only the Heretic profile as an exploratory pass.
2. Inspect `advanced-perf-model-size-001.output`:
   - `resolvedRevision`
   - `modelSha256`
   - `mmprojSha256`
   - model/mmproj byte sizes
3. Replace `version: "main"` and all empty size/hash fields in `advanced-qwen3-vl-2b-heretic-q4.json` with those exact values.
4. Commit the pinned profile.
5. Delete the exploratory result.
6. Rerun the **entire** Heretic benchmark on the same fixture/hardware setup.

An exploratory result is never referenced from `adoptions.json` as adoption evidence.

## What is measured

Quality:
- deep subject/environment/composition/action/context understanding;
- anime action and image-generation-relevant details;
- multi-image commonality/difference reasoning;
- reverse-prompt-support information recovery;
- Japanese instruction following;
- visible text/OCR in context;
- lawful adult-only analysis without unrelated refusal or silent weakening;
- strict structured JSON output.

Runtime:
- steady-state CPU latency and generated tokens/sec;
- process RSS;
- cold start through first multimodal response;
- exact model + mmproj footprint;
- repeated Windows llama-server start/infer/stop success rate.

## Report

```powershell
py -3 benchmarks\ai\adapters\advancedvision_report.py `
  benchmarks\ai\results\local\advanced-qwen3-vl-2b-q4.json `
  benchmarks\ai\results\local\advanced-qwen3-vl-2b-abliterated-q4.json `
  benchmarks\ai\results\local\advanced-internvl3.5-2b-q4.json `
  benchmarks\ai\results\local\advanced-smolvlm2-2.2b-q4.json `
  benchmarks\ai\results\local\advanced-minicpm-v4.6-q4.json `
  benchmarks\ai\results\local\advanced-granite-vision-4.1-4b-q4.json `
  > benchmarks\ai\results\local\advanced-vision-comparison.md
```

Add Heretic only after its profile has been immutably pinned and rerun.

The report is descriptive evidence; it does not calculate a hidden winner. The adoption decision must explicitly consider quality, R18 false-refusal behavior, structured output, CPU latency, RAM, disk footprint, Windows stability, license/redistribution, and integration complexity.
