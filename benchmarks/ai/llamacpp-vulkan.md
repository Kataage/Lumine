# llama.cpp Vulkan runtime validation

Issue #224 adds a pinned Windows Vulkan llama.cpp runtime while keeping a
separately pinned CPU runtime as the deterministic fallback.

## Runtime policy

- GPU acceleration OFF: use the pinned CPU runtime with `--device none -ngl 0`.
- GPU acceleration ON: prefer the pinned Vulkan runtime with `-ngl 99`.
- If the Vulkan runtime is missing or fails to start/health-check, restart with
  the pinned CPU runtime and expose the fallback reason in runtime diagnostics.
- Lightweight Vision, Advanced Vision, and Prompt Engine all follow the same
  explicit policy.
- Installing a newly available Vulkan runtime reloads already-running llama.cpp
  capabilities so an old CPU-fallback session is not retained indefinitely.

## Pinned release line

The implementation deliberately pins the verified official llama.cpp prerelease `b11053`
(2026-09-19) for both Windows x64 backends. Newer rolling prereleases are not
automatically adopted: `b11054`-`b11056` appeared later the same day and their
three intervening commits are Hexagon-only, so they do not change this verified
Windows CPU/Vulkan runtime choice.

| Backend | Asset | Size | SHA-256 |
| --- | --- | ---: | --- |
| CPU | `llama-b11053-bin-win-cpu-x64.zip` | 18,453,883 bytes | `a73abd4fd618b8145bbe7a9e9ca2dad880f05eb589a5942f921b1f39bd2d87dc` |
| Vulkan | `llama-b11053-bin-win-vulkan-x64.zip` | 31,838,165 bytes | `e9b796976a476e5c706a7858bdfb39b67d10e5651d17ddaba7c3f45479d6e331` |

The RuntimeStore verifies archive size/SHA-256 before installation and stores a
hash of the extracted `llama-server.exe` in `runtime.json` for later
verification.

## Hosted Windows smoke

The normal PR smoke downloads both official archives and verifies that each
`llama-server.exe --version` starts successfully:

```powershell
$env:LUMINE_LLAMA_RUNTIME_REAL_SMOKE="1"
go test ./internal/ai/llamacpp -run TestRealPinnedLlamaRuntimeSmoke -count=1 -v
```

GitHub-hosted Windows runners are not representative physical GPU hosts, so
this smoke does not claim GPU offload.

## Strict physical-GPU smoke

First verify that llama.cpp can enumerate a real Vulkan device:

```powershell
$env:LUMINE_LLAMA_RUNTIME_REAL_GPU_SMOKE="1"
go test ./internal/ai/llamacpp -run TestRealVulkanDeviceSmoke -count=1 -v
```

Then run the strict model smoke. It downloads/caches Lumine's pinned SmolVLM
500M model, loads it through the production Lightweight Vision engine, requires
`executionProvider=vulkan`, requires llama.cpp's own
`offloaded N/N layers to GPU` evidence, and performs one real image inference:

```powershell
$env:LUMINE_LLAMA_REAL_CACHE="$PWD\.lumine-ai-bench-cache"
$env:LUMINE_LLAMA_REAL_GPU_MODEL_SMOKE="1"
go test ./internal/ai/llamacpp -run TestRealSmolVLMVulkanOffload -count=1 -v
```

A strict run must fail rather than silently accepting CPU fallback.

## CPU vs Vulkan latency benchmark

Use the same persistent cache to avoid including model/runtime downloads in the
measurement:

```powershell
$env:LUMINE_LLAMA_REAL_CACHE="$PWD\.lumine-ai-bench-cache"

$env:LUMINE_LLAMA_REAL_BENCH="1"
go test ./internal/ai/llamacpp -run=^$ -bench=^BenchmarkRealSmolVLMImageCPU$ -benchtime=5x -benchmem

$env:LUMINE_LLAMA_REAL_GPU_BENCH="1"
go test ./internal/ai/llamacpp -run=^$ -bench=^BenchmarkRealSmolVLMImageVulkan$ -benchtime=5x -benchmem
```

The Vulkan benchmark refuses to run as a successful GPU result unless the
production engine selected Vulkan and llama.cpp logged positive GPU layer
offload.

For Issue #224's representative-hardware evidence, record:

- GPU model and driver version.
- CPU model and system RAM.
- strict smoke `offloaded N/N layers` line.
- CPU benchmark ns/op and allocations.
- Vulkan benchmark ns/op and allocations.
- observed process/system RAM.
- observed peak dedicated GPU memory/VRAM while the Vulkan benchmark is active.

The last two measurements are intentionally not inferred from hosted CI. Record
them from Task Manager, vendor tooling, or another trusted host-side monitor on
the same physical benchmark machine.

## One-command self-hosted acceptance

For one-command physical-host evidence collection, use the self-hosted `physical-gpu-acceptance` workflow documented in `physical-gpu-acceptance.md`. It runs the strict Vulkan device/model smokes and both CPU/Vulkan benchmarks on the same host, samples NVIDIA VRAM/utilization, and uploads the complete evidence as a GitHub Actions artifact.
