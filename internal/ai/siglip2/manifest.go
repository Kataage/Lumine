package siglip2

import "github.com/kataage/lumine/internal/ai"

const (
	EngineID            = "siglip2-onnx"
	DefaultModelID      = "siglip2-base-patch16-224-int8"
	DefaultModelVersion = "onnx-ba1f3b0-ort1.24.4-dml1.15.4"
	AnalysisRevision    = "siglip2-text-leftpad-image-bilinear-v1"

	visionModelPath = "onnx/vision_model_int8.onnx"
	textModelPath   = "onnx/text_model_int8.onnx"
	tokenizerPath   = "tokenizer.json"

	ortDirectMLPackagePath = "runtime/Microsoft.ML.OnnxRuntime.DirectML.1.24.4.nupkg"
	directMLPackagePath    = "runtime/Microsoft.AI.DirectML.1.15.4.nupkg"
)

func DefaultManifest() ai.ModelManifest {
	const hfBase = "https://huggingface.co/onnx-community/siglip2-base-patch16-224-ONNX/resolve/ba1f3b0843f24bc5417d38e19c37b287d719b2f4/"
	return ai.ModelManifest{
		ID:          DefaultModelID,
		Version:     DefaultModelVersion,
		Engine:      EngineID,
		DisplayName: "SigLIP 2 Base 224 (INT8)",
		License:     "Apache-2.0 + Microsoft ONNX Runtime/DirectML redistributables",
		SizeBytes:   627105913,
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
				Path:      ortDirectMLPackagePath,
				URL:       "https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime.directml/1.24.4/microsoft.ml.onnxruntime.directml.1.24.4.nupkg",
				SHA256:    "57e9f11b73437bef7a309496135d4c1f96b1a8e9ddba60013fa27bfc1d788681",
				SizeBytes: 12458649,
				Role:      "onnxruntime-directml",
			},
			{
				Path:      directMLPackagePath,
				URL:       "https://api.nuget.org/v3-flatcontainer/microsoft.ai.directml/1.15.4/microsoft.ai.directml.1.15.4.nupkg",
				SHA256:    "4e7cb7ddce8cf837a7a75dc029209b520ca0101470fcdf275c1f49736a3615b9",
				SizeBytes: 202292617,
				Role:      "directml-redist",
			},
		},
		Parameters: map[string]string{
			"ort_version":      "1.24.4",
			"directml_version": "1.15.4",
			"analysis_revision": AnalysisRevision,
		},
	}
}

func AnalysisVersion(modelVersion string) string {
	if AnalysisRevision == "" {
		return modelVersion
	}
	return modelVersion + "+" + AnalysisRevision
}
