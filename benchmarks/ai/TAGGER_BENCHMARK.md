# Tagger benchmark: current Anime/Danbooru tagger candidates

Issue: #165

This benchmark chooses Lumine's default Anime/Danbooru tagger from controlled measurements, not model-card impressions. All adoption candidates must use the same `lumine-ai-core-v2` private fixture pack and the same Windows CPU hardware ID.

The original Issue #165 comparison was WD v3 vs PixAI v0.9. That pair is retained as reproducible controls, but the candidate set is refreshed for 2026-09 because PixAI Tagger v1.0 and Camie Tagger v2 are now materially relevant. A newer model is not adopted from upstream benchmark claims alone: Lumine still measures its own anime/search/character/rating/adult-only/CPU/RAM/Windows fixture set.

## Pinned candidates

### wd-vit-tagger-v3

Profile: `profiles/wd-vit-tagger-v3.json`

- repository: `SmilingWolf/wd-vit-tagger-v3`
- pinned revision: `790b0e92cefd2a0221451604e7831fe643ab7c4f`
- ONNX SHA-256: `35f23693620b668f4d53fd3c62bf65e40af739bc52c7eb0fbc49258b58d065b6`
- model bytes: `378536310`
- general threshold: `0.35`
- character threshold: `0.85`
- direct rating output: yes

The adapter follows the WD v3 inference convention: alpha on white, square white padding, bicubic resize to 448, BGR NHWC float32, and direct model probabilities.

### PixAI Tagger v0.9

Profile: `profiles/pixai-tagger-v0.9.json`

- repository: `deepghs/pixai-tagger-v0.9-onnx`
- pinned revision: `d8cf666911a2c3d10d586d7823259192313c7eb7`
- ONNX SHA-256: `a8d479098b5e23f253543c93df42391736abbb77c21c2efd3a513b9cda7b3657`
- model bytes: `1271365854`
- general threshold: `0.30`
- character threshold: `0.85`
- direct rating output: no

The adapter follows the public PixAI ONNX convention: RGB bicubic resize to 448, scale to `[-1,1]`, CHW float32, then sigmoid over model logits.

A missing rating head is recorded as an explicit skipped `rating_tagging` case. It must not be silently treated as equivalent to a model that produces general/sensitive/questionable/explicit ratings.


### PixAI Tagger v1.0 — primary current quality candidate

Profile: `profiles/pixai-tagger-v1.0.json`

- repository: `pixai-labs/pixai-tagger-v1.0`
- pinned revision: `f33cfdb53c0c90b049bab9ce066eea1118970ef8`
- pinned `model.safetensors` SHA-256: `f29e475205cbcbc25b52a075840c7809d6215be15f45375113ba09c49bf90292`
- model bytes: `1945425796`
- input: 1008 × 1008, aspect ratio preserved
- vocabulary: 30,877 tags across general / character / style / copyright / meta / rating
- recommended thresholds: general 0.17, character 0.27, style 0.15, copyright 0.24, meta 0.17, rating 0.41
- direct rating output: yes
- benchmark runtime: upstream Transformers custom pipeline on CPU

PixAI v1.0 is deliberately benchmarked through its official Transformers/custom-code path. Lumine must not assume an unofficial ONNX conversion is equivalent. This also makes the CPU/RAM/cold-start cost of adopting the current quality model visible.

The upstream model card does not currently expose a clear license field in the repository metadata. Treat redistribution/adoption as blocked until the weight/code license is explicitly verified, regardless of benchmark quality.

### Camie Tagger v2 — ONNX / wide-vocabulary comparison candidate

Profile: `profiles/camie-tagger-v2.json`

- repository: `Camais03/camie-tagger-v2`
- pinned revision: `7d40c1b85b86ab4f607b2caf26b1b50c99db743e`
- ONNX SHA-256: `ab0aaf253e3d546090001bec9bebc776c354ab6800f442ab9167af87b4a953ac`
- model bytes: `788983561`
- input: 512 × 512, aspect ratio preserved, ImageNet-mean padding/normalization
- vocabulary: 70,527 tags
- macro-oriented threshold used by the profile: 0.492
- direct rating output: yes
- license: GPL-3.0

The ONNX adapter follows Camie's official multi-output behavior: when refined logits are present, output 1 is used and sigmoid is applied before category filtering.

### DanbooruTagQuery — exploratory only

The shared ONNX research adapter understands the upstream DanbooruTagQuery preprocessing and metadata format so it can be tested locally. It is **not** an adoption-evidence candidate yet: Lumine will not add a formal profile until its exact Hugging Face revision, ONNX size/SHA-256 and redistribution implications of the DINOv3-derived license are independently pinned. Do not use an unpinned local checkout as adoption evidence.

## Private fixture pack

The repository defines paths and scoring rules but deliberately does not contain private/copyrighted/adult-only benchmark images. Create the files below under a local fixture directory:

```text
<fixture-dir>/
  tagging/
    danbooru-basic-001.png
    danbooru-basic-001.json
    danbooru-finegrained-001.png
    danbooru-finegrained-001.json
    character-known-001.png
    character-known-001.json
    character-recent-001.png
    character-recent-001.json
    adult-explicit-001.png
    adult-explicit-001.json
  rating/
    general-001.png
    sensitive-001.png
    explicit-adult-001.png
  ...other lumine-ai-core-v2 fixtures...
```

Each tagger ground-truth JSON uses this schema:

```json
{
  "requiredTags": ["1girl", "solo", "white_shirt"],
  "forbiddenTags": ["photo_(medium)"],
  "referenceTags": ["1girl", "solo", "white_shirt", "upper_body"],
  "expectedCharacter": "canonical_character_tag"
}
```

Use only the fields relevant to that fixture. `requiredTags` and `forbiddenTags` determine the normalized benchmark score according to the v1 rubric. When `referenceTags` is provided, the adapter additionally records precision, recall, and F1 in `output.referenceMetrics`. Character fixtures use `expectedCharacter` for exact top-1 scoring.

For the recent-character fixture, choose a character intentionally newer than the older candidate's training snapshot when possible and record the canonical tag in the sidecar. Do not rename the fixture or alter its ground truth after producing evidence; create a new fixture-pack generation instead.

The adult-only fixture must contain only clearly adult subjects and lawful content. It is private because redistribution rights and content sensitivity can differ from the public repository.

After curating the pack:

```powershell
go run ./cmd/ai-bench hash-fixtures `
  -catalog benchmarks/ai/catalog.json `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v2
```

Then verify it:

```powershell
go run ./cmd/ai-bench validate-catalog `
  -catalog benchmarks/ai/catalog.json `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v2
```

## Benchmark environment

Use the same machine, power plan, CPU thread policy, OS session and fixture pack for every candidate used in a controlled comparison.

The checked-in profiles pin:

- CPU-only execution for the controlled fallback benchmark;
- 8 compute threads and 1 inter-op thread where the runtime exposes those controls;
- exact candidate revision, artifact hash, thresholds and preprocessing family;
- separate dependency environments when runtime stacks differ (ONNX Runtime vs PyTorch/Transformers).

If the benchmark machine needs a different thread count, create matching profiles for every candidate in that comparison. Never change the CPU policy for only one candidate.

Install the ONNX research environment for WD v3 / PixAI v0.9 / Camie v2:

```powershell
py -3 -m venv .venv-tagger-onnx
.\.venv-tagger-onnx\Scripts\python -m pip install -r benchmarks\ai\adapters\requirements-tagger.txt
```

PixAI v1.0 has a separate, heavier PyTorch/Transformers environment so the ONNX controls do not inherit those dependencies:

```powershell
py -3 -m venv .venv-tagger-pixai-v1
.\.venv-tagger-pixai-v1\Scripts\python -m pip install -r benchmarks\ai\adapters\requirements-tagger-pixai-v1.txt
```

## Run the candidate set

The adapter is Python research tooling only. It is not the Lumine product runtime used by #166.

WD:

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalog.json `
  -profile benchmarks/ai/profiles/wd-vit-tagger-v3.json `
  -adapter .\.venv-tagger-onnx\Scripts\python.exe `
  -adapter-arg benchmarks\ai\adapters\tagger_onnx.py `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v2 `
  -hardware-id "<same-controlled-machine-id>" `
  -cpu "<exact CPU and thread configuration>" `
  -lumine-version "<git commit>" `
  -out benchmarks\ai\results\local\wd-vit-tagger-v3.json
```

PixAI:

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalog.json `
  -profile benchmarks/ai/profiles/pixai-tagger-v0.9.json `
  -adapter .\.venv-tagger-onnx\Scripts\python.exe `
  -adapter-arg benchmarks\ai\adapters\tagger_onnx.py `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v2 `
  -hardware-id "<same-controlled-machine-id>" `
  -cpu "<exact CPU and thread configuration>" `
  -lumine-version "<git commit>" `
  -out benchmarks\ai\results\local\pixai-tagger-v0.9.json
```


PixAI v1.0:

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalog.json `
  -profile benchmarks/ai/profiles/pixai-tagger-v1.0.json `
  -adapter .\.venv-tagger-pixai-v1\Scripts\python.exe `
  -adapter-arg benchmarks\ai\adapters\tagger_pixai_v1.py `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v2 `
  -hardware-id "<same-controlled-machine-id>" `
  -cpu "<exact CPU and thread configuration>" `
  -lumine-version "<git commit>" `
  -out benchmarks\ai\results\local\pixai-tagger-v1.0.json
```

Camie v2:

```powershell
go run ./cmd/ai-bench run `
  -catalog benchmarks/ai/catalog.json `
  -profile benchmarks/ai/profiles/camie-tagger-v2.json `
  -adapter .\.venv-tagger-onnx\Scripts\python.exe `
  -adapter-arg benchmarks\ai\adapters\tagger_onnx.py `
  -fixtures-dir D:\LumineBench\lumine-ai-core-v2 `
  -hardware-id "<same-controlled-machine-id>" `
  -cpu "<exact CPU and thread configuration>" `
  -lumine-version "<git commit>" `
  -out benchmarks\ai\results\local\camie-tagger-v2.json
```

The first run may download the pinned Hugging Face artifacts. Download time is outside the adapter's reported inference metrics. The adapter verifies the model byte size and SHA-256; a small hash stamp in the local Hugging Face cache avoids re-hashing gigabyte-scale files on every case.

## Measurements

The result files contain:

- Danbooru constraint score;
- optional complete-label precision / recall / F1;
- known-character top-1 exact match;
- recent-character top-1 exact match;
- rating accuracy, or an explicit unsupported/skipped result;
- adult-only fine-grained tag constraint score and F1 when fully labelled;
- per-case inference latency;
- CPU steady-state latency and images/sec;
- process RSS after load/inference;
- cold model/session start through first inference;
- exact model artifact size;
- 20-cycle Windows load/infer/unload success rate.

For taggers, `tokensPerSecond` is intentionally not fabricated. `cpu_latency_tokens_sec` records `latencyMs` and `output.imagesPerSecond` instead.

## Result table and adoption

Generate the side-by-side Markdown table:

```powershell
py -3 benchmarks\ai\adapters\tagger_report.py `
  benchmarks\ai\results\local\wd-vit-tagger-v3.json `
  benchmarks\ai\results\local\pixai-tagger-v0.9.json `
  benchmarks\ai\results\local\pixai-tagger-v1.0.json `
  benchmarks\ai\results\local\camie-tagger-v2.json `
  > benchmarks\ai\results\local\tagger-comparison.md
```

Do not change `adoptions.json` from `candidate` to `adopted` until the compared result files validate against the same catalog pack/evaluator/hardware ID and the table has been reviewed. A candidate with unresolved redistribution/license status cannot be adopted even if it wins quality metrics.

The adoption decision should explicitly weigh tag quality, recent-character coverage, rating support, adult-only usefulness, CPU latency, RAM, model size, Windows stability, integration complexity, and redistribution/license constraints. There is no single hidden weighted score in the harness.
