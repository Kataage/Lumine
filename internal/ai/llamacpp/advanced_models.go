package llamacpp

import "github.com/kataage/lumine/internal/ai"

const (
	AdvancedQwen3VL2BModelID      = "qwen3-vl-2b-instruct-q4"
	AdvancedQwen3VL2BModelVersion = "d38d39f5972e27cd58023f9b1e9f994b0c85ca47"

	AdvancedMiniCPM46ModelID      = "minicpm-v-4.6-q4"
	AdvancedMiniCPM46ModelVersion = "1ed3097dca9264536da79039fd63c436509be6bf"
)

func AdvancedVisionCandidateManifests() []ai.ModelManifest {
	return []ai.ModelManifest{
		AdvancedQwen3VL2BManifest(),
		AdvancedMiniCPM46Manifest(),
	}
}

func AdvancedQwen3VL2BManifest() ai.ModelManifest {
	const repoBase = "https://huggingface.co/Qwen/Qwen3-VL-2B-Instruct-GGUF/resolve/" + AdvancedQwen3VL2BModelVersion + "/"
	return ai.ModelManifest{
		ID:          AdvancedQwen3VL2BModelID,
		Version:     AdvancedQwen3VL2BModelVersion,
		Engine:      AdvancedEngineID,
		DisplayName: "Qwen3-VL 2B Instruct Q4_K_M",
		License:     "Apache-2.0",
		SizeBytes:   1552463168,
		Files: []ai.ModelFile{
			{
				Path:      "Qwen3VL-2B-Instruct-Q4_K_M.gguf",
				URL:       repoBase + "Qwen3VL-2B-Instruct-Q4_K_M.gguf?download=true",
				SHA256:    "089d75c52f4b7ffc56ba998ffc50aae89fcafc755f9e7208aacca281dca6c2ae",
				SizeBytes: 1107409952,
				Role:      "model",
			},
			{
				Path:      "mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf",
				URL:       repoBase + "mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf?download=true",
				SHA256:    "f9a68fabba69c3b81e153367b2c7521030b0fa8bb0de400c9599c8e6725f9c82",
				SizeBytes: 445053216,
				Role:      "mmproj",
			},
		},
		Parameters: map[string]string{
			"context": "8192",
			"threads": "8",
		},
	}
}

func AdvancedMiniCPM46Manifest() ai.ModelManifest {
	const repoBase = "https://huggingface.co/ggml-org/MiniCPM-V-4.6-GGUF/resolve/" + AdvancedMiniCPM46ModelVersion + "/"
	return ai.ModelManifest{
		ID:          AdvancedMiniCPM46ModelID,
		Version:     AdvancedMiniCPM46ModelVersion,
		Engine:      AdvancedEngineID,
		DisplayName: "MiniCPM-V 4.6 Q4_K_M",
		License:     "Apache-2.0",
		SizeBytes:   1257056064,
		Files: []ai.ModelFile{
			{
				Path:      "MiniCPM-V-4.6-Q4_K_M.gguf",
				URL:       repoBase + "MiniCPM-V-4.6-Q4_K_M.gguf?download=true",
				SHA256:    "b1a5aa76b5ef039c2e579272ea33d4bbed7e79b49bb3ff1efdb23316d6af5199",
				SizeBytes: 529101536,
				Role:      "model",
			},
			{
				Path:      "mmproj-MiniCPM-V-4.6-Q8_0.gguf",
				URL:       repoBase + "mmproj-MiniCPM-V-4.6-Q8_0.gguf?download=true",
				SHA256:    "3d8249cdd0e1cb699644eb021fbcc04320aad89fa5dc9234ef94db0846556581",
				SizeBytes: 727954528,
				Role:      "mmproj",
			},
		},
		Parameters: map[string]string{
			"context":        "8192",
			"threads":        "8",
			"serverArgsJson": "[\"--reasoning\",\"off\"]",
		},
	}
}
