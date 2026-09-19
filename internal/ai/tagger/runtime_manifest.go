package tagger

import "github.com/kataage/lumine/internal/ai"

const (
	ortDirectMLPackagePath = "runtime/Microsoft.ML.OnnxRuntime.DirectML.1.24.4.nupkg"
	directMLPackagePath    = "runtime/Microsoft.AI.DirectML.1.15.4.nupkg"
)

// WindowsRuntimeFiles returns the integrity-pinned runtime artifacts shared by
// every native ONNX Tagger model. They are explicit model-install artifacts so
// Lumine never downloads a runtime merely because the app starts.
func WindowsRuntimeFiles() []ai.ModelFile {
	return []ai.ModelFile{
		{
			Path:      ortDirectMLPackagePath,
			URL:       "https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime.directml/1.24.4/microsoft.ml.onnxruntime.directml.1.24.4.nupkg",
			SHA256:    "57e9f11b73437bef7a309496135d4c1f96b1a8e9ddba60013fa27bfc1d788681",
			SizeBytes: 12458649,
			Role:      roleORTDirectML,
		},
		{
			Path:      directMLPackagePath,
			URL:       "https://api.nuget.org/v3-flatcontainer/microsoft.ai.directml/1.15.4/microsoft.ai.directml.1.15.4.nupkg",
			SHA256:    "4e7cb7ddce8cf837a7a75dc029209b520ca0101470fcdf275c1f49736a3615b9",
			SizeBytes: 202292617,
			Role:      roleDirectML,
		},
	}
}
