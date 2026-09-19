package llamacpp

import "github.com/kataage/lumine/internal/ai"

const (
	PromptReferenceQwen35ModelID      = "qwen3.5-4b-m-q4-reference"
	PromptReferenceQwen35ModelVersion = "9e325bb0db1f4eb8a51450142227bb6a7e4d37f1"
)

func PromptEngineCandidateManifests() []ai.ModelManifest {
	return []ai.ModelManifest{
		PromptReferenceQwen35Manifest(),
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
