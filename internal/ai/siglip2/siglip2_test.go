package siglip2

import (
	"context"
	"encoding/json"
	"image"
	"image/color"
	"image/png"
	"math"
	"os"
	"path/filepath"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

type fakeORT struct {
	textInput  [siglipTextLength]int64
	imageInput []float32
}

func (f *fakeORT) EmbedText(input [siglipTextLength]int64) ([]float32, error) {
	f.textInput = input
	return []float32{3, 4}, nil
}

func (f *fakeORT) EmbedImage(input []float32) ([]float32, error) {
	f.imageInput = append([]float32(nil), input...)
	return []float32{0, 5}, nil
}

func (f *fakeORT) Close() error { return nil }

func syntheticTokenizer(t *testing.T) *unigramTokenizer {
	t.Helper()
	payload := map[string]any{
		"model": map[string]any{
			"type":   "Unigram",
			"unk_id": 3,
			"vocab": []any{
				[]any{"<pad>", 0},
				[]any{"<eos>", 0},
				[]any{"<bos>", 0},
				[]any{"<unk>", 0},
				[]any{"▁hello", 2.0},
				[]any{"▁world", 2.0},
				[]any{"▁", -1.0},
				[]any{"hello", 1.0},
				[]any{"world", 1.0},
			},
		},
	}
	data, err := json.Marshal(payload)
	if err != nil {
		t.Fatal(err)
	}
	tokenizer, err := parseTokenizer(data)
	if err != nil {
		t.Fatal(err)
	}
	return tokenizer
}

func TestTokenizerNormalizesPadsAndAddsEOS(t *testing.T) {
	tokenizer := syntheticTokenizer(t)
	ids, err := tokenizer.Encode64("  HELLO   WORLD  ")
	if err != nil {
		t.Fatal(err)
	}
	if ids[0] != 4 || ids[1] != 5 || ids[2] != siglipEOSID {
		t.Fatalf("unexpected tokenization prefix: %v", ids[:5])
	}
	for i := 3; i < len(ids); i++ {
		if ids[i] != siglipPadID {
			t.Fatalf("token %d = %d, want pad", i, ids[i])
		}
	}
}

func TestPreprocessImageProducesCHWMinusOneToOne(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "image.png")
	img := image.NewNRGBA(image.Rect(0, 0, 2, 2))
	img.Set(0, 0, color.NRGBA{R: 255, G: 0, B: 127, A: 255})
	img.Set(1, 0, color.NRGBA{R: 255, G: 0, B: 127, A: 255})
	img.Set(0, 1, color.NRGBA{R: 255, G: 0, B: 127, A: 255})
	img.Set(1, 1, color.NRGBA{R: 255, G: 0, B: 127, A: 255})
	file, err := os.Create(path)
	if err != nil {
		t.Fatal(err)
	}
	if err := png.Encode(file, img); err != nil {
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		t.Fatal(err)
	}

	values, err := preprocessImage(path)
	if err != nil {
		t.Fatal(err)
	}
	plane := siglipImageSize * siglipImageSize
	if len(values) != plane*3 {
		t.Fatalf("tensor length = %d", len(values))
	}
	if math.Abs(float64(values[0]-1.0)) > 1e-5 {
		t.Fatalf("red normalized = %v", values[0])
	}
	if math.Abs(float64(values[plane]-(-1.0))) > 1e-5 {
		t.Fatalf("green normalized = %v", values[plane])
	}
	expectedBlue := float32(127.0/127.5 - 1.0)
	if math.Abs(float64(values[2*plane]-expectedBlue)) > 1e-4 {
		t.Fatalf("blue normalized = %v want %v", values[2*plane], expectedBlue)
	}
}

func TestEngineInferenceNormalizesEmbeddings(t *testing.T) {
	tokenizer := syntheticTokenizer(t)
	runtime := &fakeORT{}
	engine := &Engine{
		tokenizer: tokenizer,
		runtime:   runtime,
	}

	text, err := engine.Infer(context.Background(), ai.InferenceRequest{
		Operation: "embed_text",
		Payload:   map[string]any{"text": "hello"},
	})
	if err != nil {
		t.Fatal(err)
	}
	vector := text.Payload["embedding"].([]float32)
	if math.Abs(float64(vector[0]-0.6)) > 1e-5 || math.Abs(float64(vector[1]-0.8)) > 1e-5 {
		t.Fatalf("text embedding not unit-normalized: %+v", vector)
	}
	if runtime.textInput[0] != 4 || runtime.textInput[1] != siglipEOSID {
		t.Fatalf("text tokenizer was not used: %v", runtime.textInput[:4])
	}

	dir := t.TempDir()
	imagePath := filepath.Join(dir, "image.png")
	img := image.NewRGBA(image.Rect(0, 0, 1, 1))
	img.Set(0, 0, color.White)
	file, err := os.Create(imagePath)
	if err != nil {
		t.Fatal(err)
	}
	if err := png.Encode(file, img); err != nil {
		t.Fatal(err)
	}
	_ = file.Close()

	imageResponse, err := engine.Infer(context.Background(), ai.InferenceRequest{
		Operation: "embed_image",
		Payload:   map[string]any{"filePath": imagePath},
	})
	if err != nil {
		t.Fatal(err)
	}
	imageVector := imageResponse.Payload["embedding"].([]float32)
	if len(imageVector) != 2 || imageVector[1] != 1 {
		t.Fatalf("unexpected normalized image embedding: %+v", imageVector)
	}
	if len(runtime.imageInput) != 3*siglipImageSize*siglipImageSize {
		t.Fatalf("image preprocessing was not used: %d", len(runtime.imageInput))
	}
}

func TestDefaultManifestIsPinnedAndComplete(t *testing.T) {
	manifest := DefaultManifest()
	if manifest.Engine != EngineID || manifest.ID != DefaultModelID || manifest.Version != DefaultModelVersion {
		t.Fatalf("unexpected manifest identity: %+v", manifest)
	}
	if manifest.SizeBytes != 492000167 || len(manifest.Files) != 4 {
		t.Fatalf("unexpected manifest footprint: %+v", manifest)
	}
	for _, file := range manifest.Files {
		if len(file.SHA256) != 64 || file.SizeBytes <= 0 {
			t.Fatalf("unversioned model file: %+v", file)
		}
	}
}
