package llamacpp

import "github.com/kataage/lumine/internal/ai"

const (
	PromptNeoHorseModelID      = "neohorse-1-4b-abliterated-q4"
	PromptNeoHorseModelVersion = "fb32df77e9e1852d558d1013501e19666a3eb0bb"

	PromptSparkX25ModelID      = "spark-x2.5-4b-heretic-jp-q4"
	PromptSparkX25ModelVersion = "f01809e437fb3046ae9afd31d684556f9ae5dd46"

	PromptReferenceQwen35ModelID      = "qwen3.5-4b-m-q4-reference"
	PromptReferenceQwen35ModelVersion = "9e325bb0db1f4eb8a51450142227bb6a7e4d37f1"
)

func PromptEngineCandidateManifests() []ai.ModelManifest {
	return []ai.ModelManifest{
		PromptNeoHorseManifest(),
		PromptSparkX25Manifest(),
		PromptReferenceQwen35Manifest(),
	}
}

// PromptNeoHorseManifest mirrors the immutable NeoHorse candidate pinned by
// Issue #169's benchmark profile. It is a product candidate, not an adoption
// decision; #169 still owns the final benchmark-backed default selection.
func PromptNeoHorseManifest() ai.ModelManifest {
	const fileName = "Huihui-NeoHorse-1-4B-abliterated-Q4_K.gguf"
	const repoBase = "https://huggingface.co/huihui-ai/Huihui-NeoHorse-1-4B-abliterated-GGUF/resolve/" + PromptNeoHorseModelVersion + "/"
	return ai.ModelManifest{
		ID:          PromptNeoHorseModelID,
		Version:     PromptNeoHorseModelVersion,
		Engine:      PromptEngineID,
		DisplayName: "NeoHorse 1 4B Abliterated Q4_K",
		License:     "Apache-2.0",
		SizeBytes:   2783446464,
		Files: []ai.ModelFile{
			{
				Path:      fileName,
				URL:       repoBase + fileName + "?download=true",
				SHA256:    "0900451ccd4f0a65bf0e8d3f2c224eff02ae9c961cb064de8b87e8a2118af3c2",
				SizeBytes: 2783446464,
				Role:      "model",
			},
		},
		Parameters: map[string]string{
			"context":         "8192",
			"threads":         "8",
			"maxTokens":       "1024",
			"disableThinking": "true",
		},
	}
}

// PromptSparkX25Manifest mirrors the immutable Japanese Spark-X2.5 candidate
// pinned by Issue #169. Upstream llama.cpp supports the spark2_5 architecture;
// Lumine's pinned b11053 runtime is new enough to use this model without a
// Spark-specific fork.
func PromptSparkX25Manifest() ai.ModelManifest {
	const fileName = "Spark-X2.5-4B-Heretic-jp-Q4_K_M.gguf"
	const repoBase = "https://huggingface.co/soyaakinohara/Spark-X2.5-4B-Heretic-jp-gguf/resolve/" + PromptSparkX25ModelVersion + "/"
	return ai.ModelManifest{
		ID:          PromptSparkX25ModelID,
		Version:     PromptSparkX25ModelVersion,
		Engine:      PromptEngineID,
		DisplayName: "Spark-X2.5 4B Heretic JP Q4_K_M",
		License:     "Apache-2.0",
		SizeBytes:   2600224384,
		Files: []ai.ModelFile{
			{
				Path:      fileName,
				URL:       repoBase + fileName + "?download=true",
				SHA256:    "8700b9cb4be2ddf1a6b4a9230f7b9d030aeb923d0528301bc546bb5a74f21cd7",
				SizeBytes: 2600224384,
				Role:      "model",
			},
		},
		Parameters: map[string]string{
			"context":   "8192",
			"threads":   "8",
			"maxTokens": "2048",
		},
	}
}

// PromptReferenceQwen35Manifest is a reproducibly pinned integration reference,
// not the final Issue #169 adoption decision. The Prompt Engine is deliberately
// model-swappable; #169 benchmark evidence may replace this candidate without
// changing the API or engine boundary.
func PromptReferenceQwen35Manifest() ai.ModelManifest {
	const fileName = "Qwen3.5-4B-M-TS-Q4_K_M.gguf"
	const repoBase = "https://huggingface.co/TheStageAI/Qwen3.5-4B-GGUF/resolve/" + PromptReferenceQwen35ModelVersion + "/"
	return ai.ModelManifest{
		ID:          PromptReferenceQwen35ModelID,
		Version:     PromptReferenceQwen35ModelVersion,
		Engine:      PromptEngineID,
		DisplayName: "Qwen3.5 4B M Q4_K_M (reference)",
		License:     "Apache-2.0",
		SizeBytes:   2385660192,
		Files: []ai.ModelFile{
			{
				Path:      fileName,
				URL:       repoBase + fileName + "?download=true",
				SHA256:    "f8e45572b9cc35161d4772b09bccfd383fe0bb03fc6d69b40a9138731302290b",
				SizeBytes: 2385660192,
				Role:      "model",
			},
		},
		Parameters: map[string]string{
			"context":         "8192",
			"threads":         "8",
			"maxTokens":       "1024",
			"disableThinking": "true",
		},
	}
}
