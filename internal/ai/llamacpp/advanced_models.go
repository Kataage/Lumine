package llamacpp

import "github.com/kataage/lumine/internal/ai"

type AdvancedModelCandidate struct {
	Manifest ai.ModelManifest `json:"manifest"`
	Family   string           `json:"family"`
	Variant  string           `json:"variant"`
}

func AdvancedVisionCandidateManifests() []AdvancedModelCandidate {
	return []AdvancedModelCandidate{
		{
			Manifest: advancedManifest(
				"advanced-qwen3-vl-2b-q4",
				"d38d39f5972e27cd58023f9b1e9f994b0c85ca47",
				"Qwen3-VL 2B Instruct Q4_K_M",
				"Qwen/Qwen3-VL-2B-Instruct-GGUF",
				"Qwen3VL-2B-Instruct-Q4_K_M.gguf",
				"089d75c52f4b7ffc56ba998ffc50aae89fcafc755f9e7208aacca281dca6c2ae",
				1107409952,
				"mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf",
				"f9a68d86248f51993a9b827f3c91c01db0092fc0ef8a34ec5ec22b127111dbcc",
				445053216,
				"auto",
			),
			Family: "qwen3-vl",
			Variant: "official",
		},
		{
			Manifest: advancedManifest(
				"advanced-qwen3-vl-2b-abliterated-q4",
				"eff36d50ac46c34a7437a83af78c66e5526421ca",
				"Qwen3-VL 2B Abliterated Q4_K_M",
				"mradermacher/Qwen3-VL-2B-Instruct-abliterated-GGUF",
				"Qwen3-VL-2B-Instruct-abliterated.Q4_K_M.gguf",
				"2cd1275ba31ca10fb4304c38de6430d3287627993032bd47e1c20d8cfb441d57",
				1107410848,
				"Qwen3-VL-2B-Instruct-abliterated.mmproj-Q8_0.gguf",
				"3f4b999e7df92149b2e552ff240f5dc10010f1117927f400741a43816ba6da36",
				445053600,
				"auto",
			),
			Family: "qwen3-vl",
			Variant: "abliterated-v1",
		},
		{
			Manifest: advancedManifest(
				"advanced-internvl3.5-2b-q4",
				"cb0dccd56d81831b11b7b3784c836ab183024cd3",
				"InternVL3.5 2B Q4_K_M",
				"lmstudio-community/InternVL3_5-2B-GGUF",
				"InternVL3_5-2B-Q4_K_M.gguf",
				"701018980733225bb2b379b2937757522789076100fe44102f77e67dd6a36e8d",
				1282435904,
				"mmproj-InternVL3_5-2B-f16.gguf",
				"e83ba69776b52a4ba81281bbab8cec233c26b9a78ac996c57d801b57a16df49e",
				636106144,
				"auto",
			),
			Family: "internvl3.5",
			Variant: "official-lmstudio-quant",
		},
		{
			Manifest: advancedManifest(
				"advanced-smolvlm2-2.2b-q4",
				"1bc3c9f74ceafd4c8d4411cc9cf188bba3798f91",
				"SmolVLM2 2.2B Instruct Q4_K_M",
				"ggml-org/SmolVLM2-2.2B-Instruct-GGUF",
				"SmolVLM2-2.2B-Instruct-Q4_K_M.gguf",
				"0cf768eb184669e35bf4956618f97e8a584e6cd1cc20ebfc37397741652d4589",
				1112602656,
				"mmproj-SmolVLM2-2.2B-Instruct-Q8_0.gguf",
				"ae07eae7eed152378de77bbfe09646803d14ea7ae7891dd315cb288fb8a63889",
				592523200,
				"auto",
			),
			Family: "smolvlm2",
			Variant: "official-ggml",
		},
		{
			Manifest: advancedManifest(
				"advanced-minicpm-v4.6-q4",
				"1ed3097dca9264536da79039fd63c436509be6bf",
				"MiniCPM-V 4.6 Q4_K_M",
				"ggml-org/MiniCPM-V-4.6-GGUF",
				"MiniCPM-V-4.6-Q4_K_M.gguf",
				"b1a5aa76b5ef039c2e579272ea33d4bbed7e79b49bb3ff1efdb23316d6af5199",
				529101536,
				"mmproj-MiniCPM-V-4.6-Q8_0.gguf",
				"3d8249cdd0e1cb699644eb021fbcc04320aad89fa5dc9234ef94db0846556581",
				727954528,
				"off",
			),
			Family: "minicpm-v4.6",
			Variant: "official-ggml",
		},
	}
}

func advancedManifest(
	id string,
	version string,
	displayName string,
	repoID string,
	mainFile string,
	mainSHA string,
	mainSize int64,
	mmprojFile string,
	mmprojSHA string,
	mmprojSize int64,
	reasoning string,
) ai.ModelManifest {
	base := "https://huggingface.co/" + repoID + "/resolve/" + version + "/"
	return ai.ModelManifest{
		ID:          id,
		Version:     version,
		Engine:      EngineID,
		DisplayName: displayName,
		License:     "Apache-2.0",
		SizeBytes:   mainSize + mmprojSize,
		RuntimeParameters: map[string]string{
			"mainFile":   mainFile,
			"mmprojFile": mmprojFile,
			"context":    "8192",
			"threads":    "8",
			"maxTokens":  "1024",
			"reasoning":  reasoning,
		},
		Files: []ai.ModelFile{
			{
				Path: mainFile,
				URL: base + mainFile + "?download=true",
				SHA256: mainSHA,
				SizeBytes: mainSize,
			},
			{
				Path: mmprojFile,
				URL: base + mmprojFile + "?download=true",
				SHA256: mmprojSHA,
				SizeBytes: mmprojSize,
			},
		},
	}
}
