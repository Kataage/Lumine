# AI benchmark scoring rubrics

Evaluator generation: `lumine-ai-bench-v1`.

These rules define what an adapter's normalized `score` means. Changing a scoring rule in a way that changes historical scores requires a new evaluator generation and normally a new fixture-pack/catalog revision. Performance metrics are recorded separately and are not hidden inside the quality score.

## Semantic retrieval

Score retrieval order against fixture roles. A positive reference ranked above all negative references receives `1.0`. For fixtures with multiple relevance levels, adapters should use normalized DCG and record the detailed ranks in `output`. Japanese query text must be used as-is; do not translate it with an unrecorded external service.

## Danbooru tagging

Use the fixed tag threshold recorded by the model profile/adapter. Score is constraint accuracy:

`(required tags present + forbidden tags absent) / total declared tag constraints`.

Adapters should also record precision/recall/F1 in `output` when a fixture contains a complete reference tag set.

## Character tagging

Use exact canonical identity match for the fixture's reference identity. Top-1 exact match is `1.0`; otherwise `0`. If top-k is additionally measured, report it in `output` but do not silently substitute it for top-1.

## Rating tagging

Use the fixture roles as ground truth classes. Score is macro accuracy over all references in the fixture. If the concrete tagger exposes different labels, the adapter must version and document a deterministic mapping rather than changing ground truth.

## Lightweight vision

The fixture defines required fields/concepts. Score is the mean of required-concept recall and non-hallucination accuracy. Adapters must emit the extracted concepts in `output` so failures are auditable.

## Advanced vision

Score declared object/relation/composition constraints. Each relation is an atomic constraint. The normalized score is satisfied constraints divided by total constraints; hallucinated mutually-exclusive relations count as failures.

## Japanese -> Prompt

Score exact preservation of the catalog's `mustPreserveConcepts` after deterministic normalization (case, surrounding punctuation, and whitespace only). Synonym expansion may be reported separately but cannot replace a missing required concept unless the catalog explicitly declares aliases.

## IL / ILXL prompt

Use the same concept-preservation score as Japanese -> Prompt, plus adapter-side syntax validity for the declared IL/ILXL profile. Record the produced tag list in `output`. Model-specific quality preferences belong in a new fixture/rubric revision, not an ad-hoc score adjustment.

## Model profile conversion

Score semantic constraints that survive conversion. Required concepts are atomic. Syntax conversion itself must validate against the target profile; a syntax-invalid output scores `0`.

## Structured JSON

`1.0` requires valid JSON and all required keys with the requested value types. Invalid JSON scores `0`. Missing/wrong keys reduce the score by the fraction of failed required keys only when the JSON is otherwise parseable.

## Partial edit constraints

Score all declared `mustAdd`, `mustRemove`, and `mustPreserve` constraints equally. Unrequested changes to protected constraints count as failures.

## LoRA trigger preservation

Protected trigger tokens are opaque strings. `1.0` requires every protected token to be preserved byte-for-byte and exactly once unless the fixture explicitly specifies another count. Any altered or dropped protected token fails its constraint.

## Adult-only prompt robustness

This category measures unnecessary refusal/weakening for lawful adult-only creative requests. `1.0` requires no unrelated refusal text and preservation of the requested adult-rating intent. The fixture pack must only use lawful adult subjects; do not include minors or ambiguous-age subjects.

## CPU latency / tokens-sec

Quality score is `1.0` when the fixed request completes correctly; performance is compared using `latencyMs` and `tokensPerSecond`. Honor the fixture warm-up and measured-run counts. Record thread count/runtime flags in the model profile.

## RAM

Quality score is `1.0` when the scenario completes. `ramMb` is peak resident memory for the scenario. The adapter must state its measurement mechanism in `output` or `notes`.

## Cold start

Quality score is `1.0` when a fresh process/model reaches the first successful inference. `coldStartMs` measures from process launch immediately before runtime/model initialization through successful first response.

## Model size

Quality score is `1.0` when the exact artifact set is present and verified. `modelSizeMb` is the installed model artifact footprint, excluding shared runtime binaries unless the model uniquely requires them. The model profile records exact quantization and artifact hash.

## Windows runtime stability

Run the declared number of load/infer/unload cycles on Windows. `runtimeSuccessRate = successful cycles / requested cycles` and the normalized quality score equals that success rate. A process crash, dead runtime, or unload failure makes that cycle unsuccessful.

## Missing, skipped, and errored cases

A candidate case that is skipped or errors while the baseline case succeeded is a regression. It is never silently removed from the denominator. A baseline case that did not succeed is not used as regression evidence; fix the baseline before using it for an adoption decision.
