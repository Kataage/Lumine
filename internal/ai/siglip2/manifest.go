package siglip2

import "github.com/kataage/lumine/internal/ai"

const (
	EngineID            = "siglip2-onnx"
	DefaultModelID      = "siglip2-base-patch16-224-int8"
	DefaultModelVersion = "onnx-ba1f3b0-ort1.29.0"

	visionModelPath = "onnx/vision_model_int8.onnx"
	textModelPath   = "onnx/text_model_int8.onnx"
	tokenizerPath   = "tokenizer.json"
	runtimeZipPath  = "runtime/onnxruntime-win-x64-1.29.0.zip"
)

func DefaultManifest() ai.ModelManifest {
	const hfBase = "https://huggingface.co/onnx-community/siglip2-base-patch16-224-ONNX/resolve/ba1f3b0843f24bc5417d38e19c37b287d719b2f4/"
	return ai.ModelManifest{
		ID:          DefaultModelID,
		Version:     DefaultModelVersion,
		Engine:      EngineID,
		DisplayName: "SigLIP 2 Base 224 (INT8)",
		License:     "Apache-2.0 + MIT runtime",
		SizeBytes:   492000167,
		Files: []ai.ModelFile{
			{
				Path:      visionModelPath,
				URL:       hfBase + "onnx/vision_model_int8.onnx",
				SHA256:    "0dd31785a2713f1113ef2272472165c69d580473dae38d7b47568ac587795e70",
				SizeBytes: 94553333,
			},
			{
				Path:      textModelPath,
				URL:       hfBase + "onnx/text_model_int8.onnx",
				SHA256:    "3a0603d3a00c05a80a6ded4743c16aaac7b1e62cdcc7e362e7ce418659b96400",
				SizeBytes: 283438275,
			},
			{
				Path:      tokenizerPath,
				URL:       hfBase + "tokenizer.json",
				SHA256:    "cb9140fae3ac5122c972d37adf83e1248471a38147ad76f8215c8872c6fd8322",
				SizeBytes: 34363039,
			},
			{
				Path:      runtimeZipPath,
				URL:       "https://github.com/microsoft/onnxruntime/releases/download/v1.29.0/onnxruntime-win-x64-1.29.0.zip",
				SHA256:    "c9b4b7086b529ad814f428c1bad028e20a25d7dc0699836775faace4ab5b78b2",
				SizeBytes: 79645520,
			},
		},
	}
}
