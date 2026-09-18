# Private tagger fixture ground truth

The actual files live in the private `lumine-ai-core-v2` fixture pack and are not committed here.

For every tagger image whose catalog entry references a `ground_truth` JSON sidecar, the sidecar may contain:

```json
{
  "requiredTags": ["tags whose absence is a clear failure"],
  "forbiddenTags": ["clearly incorrect tags"],
  "referenceTags": ["complete human-reviewed general-tag set for P/R/F1"],
  "expectedCharacter": "canonical_character_tag"
}
```

Rules:

1. Use canonical model/Danbooru tag spelling where possible.
2. Label before looking at candidate outputs. Do not change ground truth to make either candidate look better.
3. `requiredTags` should cover the fixture's deliberately tested details (subject count, pose/composition, clothing, expression, hair/eye/body attributes, objects/background as applicable).
4. `forbiddenTags` should contain only mutually incompatible or clearly wrong concepts, not subjective omissions.
5. `referenceTags` should be a reasonably complete human-reviewed general-tag set if F1 is intended to be compared.
6. Character fixtures must use a canonical exact `expectedCharacter`.
7. Adult-only fixtures must contain only clearly adult subjects and lawful content.
8. Once evidence has been recorded, changing an image or sidecar requires a new fixture-pack ID. Re-run `hash-fixtures` after any curation change.
