package tagger

import (
	"context"
	"image"
	"image/color"
	"image/png"
	"os"
	"path/filepath"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

type fakeRuntime struct {
	values []float32
	diag   ai.RuntimeDiagnostics
	input  imageTensor
	size   int
	closed bool
}

func (r *fakeRuntime) Run(input imageTensor, outputSize int) ([]float32, error) {
	r.input = input
	r.size = outputSize
	return append([]float32(nil), r.values...), nil
}

func (r *fakeRuntime) RuntimeDiagnostics() ai.RuntimeDiagnostics {
	return r.diag
}

func (r *fakeRuntime) Close() error {
	r.closed = true
	return nil
}

func writeTaggerFixturePNG(t *testing.T, path string) {
	t.Helper()
	img := image.NewRGBA(image.Rect(0, 0, 2, 2))
	img.Set(0, 0, color.RGBA{R: 255, A: 255})
	img.Set(1, 0, color.RGBA{G: 255, A: 255})
	img.Set(0, 1, color.RGBA{B: 255, A: 255})
	img.Set(1, 1, color.RGBA{R: 255, G: 255, B: 255, A: 255})
	file, err := os.Create(path)
	if err != nil {
		t.Fatal(err)
	}
	if err := png.Encode(file, img); err != nil {
		_ = file.Close()
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		t.Fatal(err)
	}
}

func writeTaggerCSV(t *testing.T, path string) {
	t.Helper()
	content := "name,category\n" +
		"1girl,0\n" +
		"solo,0\n" +
		"test_character,4\n" +
		"weak_character,4\n" +
		"explicit,9\n" +
		"questionable,9\n"
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
}

func testInstalledModel(t *testing.T, family string) ai.InstalledModel {
	t.Helper()
	root := t.TempDir()
	if err := os.WriteFile(filepath.Join(root, "model.onnx"), []byte("model"), 0o644); err != nil {
		t.Fatal(err)
	}
	writeTaggerCSV(t, filepath.Join(root, "tags.csv"))
	return ai.InstalledModel{
		RootDir: root,
		Manifest: ai.ModelManifest{
			ID:      "test-tagger",
			Version: "1",
			Engine:  EngineID,
			Files: []ai.ModelFile{
				{Path: "model.onnx", Role: roleModel},
				{Path: "tags.csv", Role: roleTags},
			},
			Parameters: map[string]string{
				"family":              family,
				"image_size":          "2",
				"input_name":          "input",
				"output_name":         "output",
				"general_threshold":   "0.35",
				"character_threshold": "0.85",
				"rating_threshold":    "0",
				"rating_supported":    "true",
			},
		},
	}
}

func TestEngineAppliesThresholdsAndReturnsCommonSchema(t *testing.T) {
	model := testInstalledModel(t, familyWDV3)
	imagePath := filepath.Join(model.RootDir, "image.png")
	writeTaggerFixturePNG(t, imagePath)

	runtime := &fakeRuntime{
		values: []float32{0.95, 0.80, 0.92, 0.70, 0.88, 0.20},
		diag: ai.RuntimeDiagnostics{
			ExecutionProvider: "directml",
		},
	}
	engine := newEngine(func(
		got ai.InstalledModel,
		config modelConfig,
		options ai.LoadOptions,
	) (runtimeBackend, error) {
		if got.Manifest.ID != model.Manifest.ID {
			t.Fatalf("factory model id = %q", got.Manifest.ID)
		}
		if !options.AllowGPU {
			t.Fatal("expected GPU policy to reach Tagger runtime factory")
		}
		return runtime, nil
	})

	if err := engine.Load(context.Background(), model, ai.LoadOptions{AllowGPU: true}); err != nil {
		t.Fatalf("Load: %v", err)
	}
	response, err := engine.Infer(context.Background(), ai.InferenceRequest{
		Operation: "tag_image",
		Payload: map[string]any{
			"filePath":           imagePath,
			"generalThreshold":   0.85,
			"characterThreshold": 0.90,
			"ratingThreshold":    0.50,
		},
	})
	if err != nil {
		t.Fatalf("Infer: %v", err)
	}

	if got := response.Payload["generalThreshold"]; got != 0.85 {
		t.Fatalf("generalThreshold = %v", got)
	}
	if got := response.Payload["characterThreshold"]; got != 0.90 {
		t.Fatalf("characterThreshold = %v", got)
	}
	if got := response.Payload["ratingThreshold"]; got != 0.50 {
		t.Fatalf("ratingThreshold = %v", got)
	}

	general, ok := response.Payload["generalTags"].([]any)
	if !ok || len(general) != 1 {
		t.Fatalf("generalTags = %#v", response.Payload["generalTags"])
	}
	characters, ok := response.Payload["characterTags"].([]any)
	if !ok || len(characters) != 1 {
		t.Fatalf("characterTags = %#v", response.Payload["characterTags"])
	}
	ratings, ok := response.Payload["ratingScores"].([]any)
	if !ok || len(ratings) != 1 {
		t.Fatalf("ratingScores = %#v", response.Payload["ratingScores"])
	}
	if got := response.Payload["rating"]; got != "explicit" {
		t.Fatalf("rating = %v", got)
	}
	if runtime.size != 6 {
		t.Fatalf("runtime output size = %d, want 6", runtime.size)
	}
	if len(runtime.input.Shape) != 4 ||
		runtime.input.Shape[1] != 2 ||
		runtime.input.Shape[2] != 2 ||
		runtime.input.Shape[3] != 3 {
		t.Fatalf("WD input shape = %v", runtime.input.Shape)
	}
	if got := engine.RuntimeDiagnostics().ExecutionProvider; got != "directml" {
		t.Fatalf("provider = %q", got)
	}

	if err := engine.Unload(context.Background()); err != nil {
		t.Fatalf("Unload: %v", err)
	}
	if !runtime.closed {
		t.Fatal("runtime was not closed")
	}
}

func TestEngineAppliesSigmoidForPixAIV09(t *testing.T) {
	model := testInstalledModel(t, familyPixAIV09)
	model.Manifest.Parameters["general_threshold"] = "0.8"
	model.Manifest.Parameters["character_threshold"] = "0.7"
	model.Manifest.Parameters["rating_supported"] = "false"
	imagePath := filepath.Join(model.RootDir, "image.png")
	writeTaggerFixturePNG(t, imagePath)

	runtime := &fakeRuntime{
		values: []float32{2, 0, 1, -2, 4, 1},
		diag:   ai.RuntimeDiagnostics{ExecutionProvider: "cpu"},
	}
	engine := newEngine(func(
		ai.InstalledModel,
		modelConfig,
		ai.LoadOptions,
	) (runtimeBackend, error) {
		return runtime, nil
	})
	if err := engine.Load(context.Background(), model, ai.LoadOptions{}); err != nil {
		t.Fatal(err)
	}
	response, err := engine.Infer(context.Background(), ai.InferenceRequest{
		Operation: "tag_image",
		Payload:   map[string]any{"filePath": imagePath},
	})
	if err != nil {
		t.Fatal(err)
	}

	general := response.Payload["generalTags"].([]any)
	if len(general) != 1 {
		t.Fatalf("PixAI general tags = %#v", general)
	}
	characters := response.Payload["characterTags"].([]any)
	if len(characters) != 1 {
		t.Fatalf("PixAI character tags = %#v", characters)
	}
	if ratings := response.Payload["ratingScores"].([]any); len(ratings) != 0 {
		t.Fatalf("PixAI v0.9 rating scores = %#v, want unsupported", ratings)
	}
}

func TestEngineRejectsInvalidThresholdOverride(t *testing.T) {
	model := testInstalledModel(t, familyWDV3)
	imagePath := filepath.Join(model.RootDir, "image.png")
	writeTaggerFixturePNG(t, imagePath)
	engine := newEngine(func(
		ai.InstalledModel,
		modelConfig,
		ai.LoadOptions,
	) (runtimeBackend, error) {
		return &fakeRuntime{values: make([]float32, 6)}, nil
	})
	if err := engine.Load(context.Background(), model, ai.LoadOptions{}); err != nil {
		t.Fatal(err)
	}
	if _, err := engine.Infer(context.Background(), ai.InferenceRequest{
		Operation: "tag_image",
		Payload: map[string]any{
			"filePath":         imagePath,
			"generalThreshold": 1.1,
		},
	}); err == nil {
		t.Fatal("invalid threshold override must fail")
	}
}
