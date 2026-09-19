package generationmeta

import (
	"encoding/json"
	"strings"
	"testing"
)

func TestNormalizeComfyPromptExtractsCoreGenerationSettings(t *testing.T) {
	prompt := map[string]any{
		"1": map[string]any{
			"class_type": "CheckpointLoaderSimple",
			"inputs": map[string]any{"ckpt_name": "myIllustriousXL.safetensors"},
		},
		"2": map[string]any{
			"class_type": "CLIPTextEncode",
			"inputs": map[string]any{"text": "1girl, white hair, <lora:style:0.8>"},
		},
		"3": map[string]any{
			"class_type": "CLIPTextEncode",
			"inputs": map[string]any{"text": "lowres, watermark"},
		},
		"4": map[string]any{
			"class_type": "EmptyLatentImage",
			"inputs": map[string]any{"width": 1024, "height": 1536},
		},
		"5": map[string]any{
			"class_type": "LoraLoader",
			"inputs": map[string]any{
				"lora_name": "style.safetensors",
				"strength_model": 0.75,
				"trigger_words": "style_trigger, dramatic_light",
			},
		},
		"6": map[string]any{
			"class_type": "KSampler",
			"inputs": map[string]any{
				"seed": json.Number("123456789012345"),
				"steps": 28,
				"cfg": 5.5,
				"sampler_name": "dpmpp_2m",
				"scheduler": "karras",
				"positive": []any{"2", 0},
				"negative": []any{"3", 0},
				"latent_image": []any{"4", 0},
				"model": []any{"5", 0},
			},
		},
	}
	rawPrompt, err := json.Marshal(prompt)
	if err != nil {
		t.Fatal(err)
	}
	result := Normalize(map[string]string{"prompt": string(rawPrompt), "workflow": `{"nodes":[]}`}, "png")
	if result.Checkpoint != "myIllustriousXL.safetensors" {
		t.Fatalf("checkpoint = %q", result.Checkpoint)
	}
	if result.Positive != "1girl, white hair, <lora:style:0.8>" || result.Negative != "lowres, watermark" {
		t.Fatalf("prompts = %q / %q", result.Positive, result.Negative)
	}
	if result.Seed != 123456789012345 || result.Steps != 28 || result.CFG != 5.5 {
		t.Fatalf("sampler settings = seed %d steps %d cfg %v", result.Seed, result.Steps, result.CFG)
	}
	if result.Sampler != "dpmpp_2m" || result.Scheduler != "karras" {
		t.Fatalf("sampler = %q scheduler = %q", result.Sampler, result.Scheduler)
	}
	if result.Width != 1024 || result.Height != 1536 {
		t.Fatalf("size = %dx%d", result.Width, result.Height)
	}
	if len(result.LoRAs) != 2 {
		t.Fatalf("LoRAs = %+v", result.LoRAs)
	}
	var foundTrigger bool
	for _, lora := range result.LoRAs {
		if lora.Name == "style.safetensors" && len(lora.TriggerWords) > 0 {
			foundTrigger = true
		}
	}
	if !foundTrigger {
		t.Fatalf("trigger words missing: %+v", result.LoRAs)
	}
}

func TestNormalizeA1111CompatibleParameters(t *testing.T) {
	parameters := "masterpiece, 1girl, <lora:character:0.65>\nNegative prompt: lowres, bad anatomy\nSteps: 30, Sampler: Euler a, Schedule type: Normal, CFG scale: 7, Seed: 42, Size: 768x1024, Model: animeXL"
	result := Normalize(map[string]string{"parameters": parameters}, "webp")
	if result.Positive != "masterpiece, 1girl, <lora:character:0.65>" {
		t.Fatalf("positive = %q", result.Positive)
	}
	if result.Negative != "lowres, bad anatomy" || result.Seed != 42 || result.Steps != 30 {
		t.Fatalf("normalized parameters = %+v", result)
	}
	if result.Width != 768 || result.Height != 1024 || result.Checkpoint != "animeXL" {
		t.Fatalf("size/checkpoint = %+v", result)
	}
	if len(result.LoRAs) != 1 || !strings.Contains(strings.ToLower(result.LoRAs[0].Name), "character") {
		t.Fatalf("LoRA = %+v", result.LoRAs)
	}
}

func TestSplitMetadataPrefixRecognizesComfyWebPExif(t *testing.T) {
	raw := map[string]string{}
	if !splitMetadataPrefix(`prompt:{"1":{"class_type":"KSampler","inputs":{}}}`, raw) {
		t.Fatal("prompt EXIF prefix not recognized")
	}
	if _, ok := raw["prompt"]; !ok {
		t.Fatalf("raw metadata = %+v", raw)
	}
}
