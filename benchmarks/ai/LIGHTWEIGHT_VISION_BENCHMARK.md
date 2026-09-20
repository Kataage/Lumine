# Lightweight Vision benchmark

Issue: #183  
Parent: #167

This benchmark compares **Florence-2-base** as the task-specialized quality/reference model with **SmolVLM-500M-Instruct Q8_0** as the small llama.cpp product-integration candidate.

Both are evaluated on the same `lumine-lightweight-vision-v1` private fixture pack and the same Windows CPU hardware ID. No candidate is selected from model-card claims alone.

## Candidate A: Florence-2-base

Profile: `profiles/florence-2-base.json`

- license: MIT
- primary weight SHA-256: `03075d2d2d2bbd3e180b9ba0afae4aa8563226e2d32911656966e05b2f2ee060`
- primary weight size: `463221266` bytes
- benchmark runtime: pinned Transformers/PyTorch CPU environment
- benchmark tasks: `<CAPTION>`, `<MORE_DETAILED_CAPTION>` and `<OCR>`

Florence is intentionally treated as a quality/reference baseline. It exposes specialized caption/OCR/grounding tasks, but its product runtime is more specialized than Lumine's planned llama.cpp stack.

## Candidate B: SmolVLM-500M-Instruct Q8_0

Profile: `profiles/smolvlm-500m-q8.json`

- license: Apache-2.0
- model SHA-256: `9d4612de6a42214499e301494a3ecc2be0abdd9de44e663bda63f1152fad1bf4`
- model bytes: `436806912`
- mmproj SHA-256: `d1eb8b6b23979205fdf63703ed10f788131a3f812c7b1f72e0119d5d81295150`
- mmproj bytes: `108783360`
- llama.cpp runtime: `b11053` CPU x64 (aligned with the current Lumine product CPU runtime)
- runtime archive SHA-256: `a73abd4fd618b8145bbe7a9e9ca2dad880f05eb589a5942f921b1f39bd2d87dc`

The benchmark adapter downloads the pinned b11053 CPU runtime only because running the benchmark is itself an explicit action. Product behavior in #184 must still require an explicit install action and must not silently fetch a runtime/model when a setting is toggled.

## Private fixture pack

Create:

```text
<fixture-dir>/
  vision/
    lightweight-short-general-001.png
    lightweight-short-general-001.json
    lightweight-detailed-composition-001.png
    lightweight-detailed-composition-001.json
    lightweight-anime-001.png
    lightweight-anime-001.json
    lightweight-visible-text-001.png
    lightweight-visible-text-001.json
    lightweight-adult-001.png
    lightweight-adult-001.json
```

Ground-truth sidecar example:

```json
{
  "requiredConceptGroups": [
    ["woman", "adult woman"],
    ["train platform", "railway platform"],
    ["low angle", "viewed from below"]
  ],
  "forbiddenConcepts": ["child", "outdoor beach"],
  "expectedVisibleTextGroups": [
    ["LUMINE", "lumine"]
  ],
  "mustNotRefuse": true
}
```

Each item in `requiredConceptGroups` is an alias group: any alias satisfies that concept. Ground truth must be written before reviewing candidate output. The adult fixture must contain only clearly adult lawful content and stays private.

Hash and validate:

```powershell
go run ./cmd/ai-bench hash-fixtures `
  -catalog benchmarks/ai/catalogs/lightweight-vision-v1.json `
  -fixtures-dir D:\LumineBench\lumine-lightweight-vision-v1

go run ./cmd/ai-bench validate-catalog `
  -catalog benchmarks/ai/catalogs/lightweight-vision-v1.json `
  -fixtures-dir D:\LumineBench\lumine-lightweight-vision-v1
```

## Recommended one-command Windows run

Use the checked-in runner for the controlled #183 comparison:

```powershell
.\benchmarks\ai\run_lightweight_vision_benchmark.ps1 \
  -FixtureDir "D:\LumineBench\lumine-lightweight-vision-v1" \
  -HardwareId "main-pc-cpu8"
```

The first run creates the pinned Python environment automatically. Use `-Setup` to reinstall the pinned requirements intentionally. The runner validates the fixture pack, records CPU/RAM/current Lumine commit, runs Florence-2-base and SmolVLM-500M under the same hardware ID, validates both results, and writes `lightvision-comparison.md` plus `lightvision-run-info.json`.

For controlled evidence, clear `LUMINE_LLAMA_SERVER`; the runner rejects that override so SmolVLM cannot silently use an unpinned server.

The manual commands below remain available for single-candidate debugging.

## Python environment

```powershell
py -3 -m venv .venv-lightvision-bench
.\.venv-lightvision-bench\Scripts\python -m pip install -r benchmarks\ai\adapters\requirements-lightvision.txt
```

## Florence run

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalogs/lightweight-vision-v1.json `
  -profile benchmarks/ai/profiles/florence-2-base.json `
  -adapter .\.venv-lightvision-bench\Scripts\python.exe `
  -adapter-arg benchmarks\ai\adapters\lightvision_florence.py `
  -fixtures-dir D:\LumineBench\lumine-lightweight-vision-v1 `
  -hardware-id "<same-controlled-machine-id>" `
  -cpu "<exact CPU / 8-thread configuration>" `
  -lumine-version "<git commit>" `
  -out benchmarks\ai\results\local\florence-2-base.json
```

## SmolVLM run

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalogs/lightweight-vision-v1.json `
  -profile benchmarks/ai/profiles/smolvlm-500m-q8.json `
  -adapter .\.venv-lightvision-bench\Scripts\python.exe `
  -adapter-arg benchmarks\ai\adapters\lightvision_smolvlm.py `
  -fixtures-dir D:\LumineBench\lumine-lightweight-vision-v1 `
  -hardware-id "<same-controlled-machine-id>" `
  -cpu "<exact CPU / 8-thread configuration>" `
  -lumine-version "<git commit>" `
  -out benchmarks\ai\results\local\smolvlm-500m-q8.json
```

For debugging, the SmolVLM adapter may use an already verified server binary by setting `LUMINE_LLAMA_SERVER`; the controlled runner rejects this override so adoption evidence always uses the checked-in b11053 runtime.

## Comparison

```powershell
py -3 benchmarks\ai\adapters\lightvision_report.py `
  benchmarks\ai\results\local\florence-2-base.json `
  benchmarks\ai\results\local\smolvlm-500m-q8.json `
  > benchmarks\ai\results\local\lightvision-comparison.md
```

Evaluate:

- short caption fidelity;
- detailed subject/background/composition/viewpoint coverage;
- anime illustration fidelity;
- visible-text extraction;
- lawful adult-only no-refusal/information retention;
- CPU latency/tokens-sec;
- peak RSS;
- cold start;
- local model artifact size;
- Windows repeated runtime stability;
- license/redistribution and product integration complexity.

Do not move either adoption record to `adopted` until both runs validate against the same catalog pack, evaluator and hardware ID.
