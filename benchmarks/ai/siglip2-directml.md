# SigLIP2 DirectML runtime

Lumine's Windows SigLIP2 backend can use DirectML when the user enables GPU
acceleration. The runtime always keeps a CPU fallback and never requires a
system CUDA/cuDNN installation.

## Pinned runtime

The DirectML-specific ONNX Runtime NuGet line currently publishes through
1.24.4. Lumine pins these official packages:

| Package | Version | Size | SHA-256 |
| --- | --- | ---: | --- |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.4 | 12,458,649 bytes | `57e9f11b73437bef7a309496135d4c1f96b1a8e9ddba60013fa27bfc1d788681` |
| Microsoft.AI.DirectML | 1.15.4 | 202,292,617 bytes | `4e7cb7ddce8cf837a7a75dc029209b520ca0101470fcdf275c1f49736a3615b9` |

Only the required x64 native files are extracted into the model's immutable
`.runtime` directory:

| File | Size | SHA-256 |
| --- | ---: | --- |
| onnxruntime.dll | 17,328,152 bytes | `e7eedec6a6f26dc39dc948276a75ef6d2bee3fff944d874ceed0bbd3b97bff40` |
| onnxruntime_providers_shared.dll | 22,040 bytes | `265c8daf29637cb259cac8be9f08f2cd45f3883f0f0e4949cbfddd5b4cbec3b6` |
| DirectML.dll | 18,527,776 bytes | `9c9e6d822561c6c41b90e6994b3e8857cf1d66dbfb1e0c4c799c7c89b4e92da1` |

The model store verifies package size/SHA before installation. Runtime
extraction verifies each native file again before it can be loaded.

## Execution policy

- GPU disabled: create CPU ONNX Runtime sessions.
- GPU enabled: configure DirectML with memory-pattern optimization disabled and
  sequential execution, then create DirectML sessions.
- DirectML initialization/session creation failure: create CPU sessions and
  expose the fallback reason through runtime diagnostics.
- ONNX Runtime `Run` calls are serialized for this backend because DirectML
  does not support concurrent runs on the same session.
- Foreground text searches retain priority over background image embedding:
  the existing AI job queue pauses/requeues active semantic background jobs
  before the foreground query acquires the runtime.

## Diagnostics

`RuntimeStatus.executionProvider` reports `directml` or `cpu`.
`RuntimeStatus.warning` is populated when GPU was requested but CPU fallback
was required.

The AI Settings Semantic Search panel surfaces this as DirectML (GPU), CPU, or
CPU fallback.

## Real smoke and benchmark

CPU real-model smoke is run by the existing Windows SigLIP2 workflow.
Hosted Windows CI also requests GPU acceleration with
`LUMINE_SIGLIP2_GPU_REQUEST_SMOKE=1`: if no valid GPU adapter is exposed, the
test requires a diagnostic CPU fallback and still runs real image/text
inference.

```powershell
$env:LUMINE_SIGLIP2_REAL_SMOKE="1"
$env:LUMINE_SIGLIP2_GPU_REQUEST_SMOKE="1"
go test ./internal/ai/siglip2 -run TestRealSigLIP2Smoke -count=1 -v
```

A GPU-capable Windows host must explicitly require DirectML. This test fails if
the runtime silently falls back to CPU:

```powershell
$env:LUMINE_SIGLIP2_REAL_GPU_SMOKE="1"
go test ./internal/ai/siglip2 -run TestRealSigLIP2DirectMLSmoke -count=1 -v
```

Representative throughput measurements can be collected on the same installed
hardware:

```powershell
$env:LUMINE_SIGLIP2_REAL_BENCH="1"
go test ./internal/ai/siglip2 -run=^$ -bench=^BenchmarkRealSigLIP2ImageCPU$ -benchtime=10x -benchmem

$env:LUMINE_SIGLIP2_REAL_GPU_BENCH="1"
go test ./internal/ai/siglip2 -run=^$ -bench=^BenchmarkRealSigLIP2ImageDirectML$ -benchtime=10x -benchmem
```

Hosted CI runners must not be presented as representative GPU benchmark
hardware. A DirectML-capable physical GPU host is required for the GPU smoke
and CPU-vs-GPU throughput acceptance measurement.
