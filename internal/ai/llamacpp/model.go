package llamacpp

import "github.com/kataage/lumine/internal/ai"

const (
	EngineID         = "llamacpp-vlm"
	AdvancedEngineID = "llamacpp-advanced-vlm"

	DefaultVisionModelID      = "smolvlm-500m-instruct-q8"
	DefaultVisionModelVersion = "72e986006ef53e37cdd3f6d4241c90b0f01df376"

	defaultVisionModelFile = "SmolVLM-500M-Instruct-Q8_0.gguf"
	defaultVisionMMProjFile = "mmproj-SmolVLM-500M-Instruct-Q8_0.gguf"
)

func DefaultVisionModelManifest() ai.ModelManifest {
	const repoBase = "https://huggingface.co/ggml-org/SmolVLM-500M-Instruct-GGUF/resolve/" + DefaultVisionModelVersion + "/"
	return ai.ModelManifest{
		ID:          DefaultVisionModelID,
		Version:     DefaultVisionModelVersion,
		Engine:      EngineID,
		DisplayName: "SmolVLM 500M Instruct Q8_0",
		License:     "Apache-2.0",
		SizeBytes:   545590272,
		Files: []ai.ModelFile{
			{
				Path:      defaultVisionModelFile,
				URL:       repoBase + defaultVisionModelFile + "?download=true",
				SHA256:    "9d4612de6a42214499e301494a3ecc2be0abdd9de44e663bda63f1152fad1bf4",
				SizeBytes: 436806912,
				Role:      "model",
			},
			{
				Path:      defaultVisionMMProjFile,
				URL:       repoBase + defaultVisionMMProjFile + "?download=true",
				SHA256:    "d1eb8b6b23979205fdf63703ed10f788131a3f812c7b1f72e0119d5d81295150",
				SizeBytes: 108783360,
				Role:      "mmproj",
			},
		},
		Parameters: map[string]string{
			"context": "4096",
			"threads": "8",
		},
	}
}
