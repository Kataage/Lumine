package siglip2

import (
	"context"
	"encoding/json"
	"errors"
	"image"
	"image/color"
	"image/png"
	"math"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

type fakeORT struct {
	textInput       [siglipTextLength]int64
	imageInput      []float32
	imageBatchInput []float32
	imageBatchSize  int
}

func (f *fakeORT) EmbedText(_ context.Context, input [siglipTextLength]int64) ([]float32, error) {
	f.textInput = input
	return []float32{3, 4}, nil
}

func (f *fakeORT) EmbedImage(_ context.Context, input []float32) ([]float32, error) {
	f.imageInput = append([]float32(nil), input...)
	return []float32{0, 5}, nil
}

func (f *fakeORT) EmbedImageBatch(_ context.Context, input []float32, batchSize int) ([][]float32, error) {
	f.imageBatchInput = append([]float32(nil), input...)
	f.imageBatchSize = batchSize
	result := make([][]float32, batchSize)
	for i := 0; i < batchSize; i++ {
		result[i] = []float32{3, 4}
	}
	return result, nil
}

func (f *fakeORT) RuntimeDiagnostics() ai.RuntimeDiagnostics {
	return ai.RuntimeDiagnostics{ExecutionProvider: "test"}
}

func (f *fakeORT) Close() error { return nil }

func syntheticTokenizer(t *testing.T) *siglipTokenizer {
	t.Helper()
	payload := map[string]any{
		"model": map[string]any{
			"type":          "BPE",
			"unk_token":     "<unk>",
			"fuse_unk":      true,
			"byte_fallback": true,
			"vocab": map[string]int{
				"<pad>": 0,
				"<eos>": 1,
				"<bos>": 2,
				"<unk>": 3,
				"<mask>": 4,
				"h": 5,
				"e": 6,
				"l": 7,
				"o": 8,
				"he": 9,
				"hel": 10,
				"hell": 11,
				"hello": 12,
				"▁": 13,
				"w": 14,
				"r": 15,
				"d": 16,
				"▁w": 17,
				"▁wo": 18,
				"▁wor": 19,
				"▁worl": 20,
				"▁world": 21,
				"<0xF0>": 22,
				"<0x9F>": 23,
				"<0x99>": 24,
				"<0x82>": 25,
			},
			"merges": []string{
				"h e",
				"he l",
				"hel l",
				"hell o",
				"▁ w",
				"▁w o",
				"▁wo r",
				"▁wor l",
				"▁worl d",
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


func TestTokenizerEmptyTextKeepsEOSAtFinalPosition(t *testing.T) {
	tokenizer := syntheticTokenizer(t)
	ids, err := tokenizer.Encode64("   ")
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < siglipTextLength-1; i++ {
		if ids[i] != siglipPadID {
			t.Fatalf("token %d = %d, want pad", i, ids[i])
		}
	}
	if ids[siglipTextLength-1] != siglipEOSID {
		t.Fatalf("final token = %d, want EOS", ids[siglipTextLength-1])
	}
}

func TestTokenizerTruncationStillKeepsEOSAtFinalPosition(t *testing.T) {
	tokenizer := syntheticTokenizer(t)
	long := strings.Repeat("hello ", 100)
	ids, err := tokenizer.Encode64(long)
	if err != nil {
		t.Fatal(err)
	}
	if ids[siglipTextLength-1] != siglipEOSID {
		t.Fatalf("final token = %d, want EOS", ids[siglipTextLength-1])
	}
}

func TestTokenizerLowercasesLeftPadsAndKeepsEOSAtFinalPosition(t *testing.T) {
	tokenizer := syntheticTokenizer(t)
	ids, err := tokenizer.Encode64("  HELLO WORLD  ")
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < siglipTextLength-3; i++ {
		if ids[i] != siglipPadID {
			t.Fatalf("token %d = %d, want left pad", i, ids[i])
		}
	}
	if ids[siglipTextLength-3] != 12 ||
		ids[siglipTextLength-2] != 21 ||
		ids[siglipTextLength-1] != siglipEOSID {
		t.Fatalf("unexpected tokenization suffix: %v", ids[siglipTextLength-6:])
	}
}

func TestTokenizerPreservesRepeatedSpacesAsMetaspace(t *testing.T) {
	tokenizer := syntheticTokenizer(t)
	ids, err := tokenizer.Encode64("hello   world")
	if err != nil {
		t.Fatal(err)
	}
	want := []int64{12, 13, 13, 21, siglipEOSID}
	start := siglipTextLength - len(want)
	for index, expected := range want {
		if ids[start+index] != expected {
			t.Fatalf("token %d = %d, want %d; suffix=%v", start+index, ids[start+index], expected, ids[siglipTextLength-8:])
		}
	}
}

func TestTokenizerSupportsBPEByteFallback(t *testing.T) {
	tokenizer := syntheticTokenizer(t)
	ids, err := tokenizer.Encode64("🙂")
	if err != nil {
		t.Fatal(err)
	}
	want := []int64{22, 23, 24, 25, siglipEOSID}
	start := siglipTextLength - len(want)
	for index, expected := range want {
		if ids[start+index] != expected {
			t.Fatalf("token %d = %d, want %d; suffix=%v", start+index, ids[start+index], expected, ids[siglipTextLength-8:])
		}
	}
}

func TestTokenizerKeepsLegacyUnigramCompatibility(t *testing.T) {
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
	ids, err := tokenizer.Encode64("HELLO")
	if err != nil {
		t.Fatal(err)
	}
	if ids[siglipTextLength-2] != 4 || ids[siglipTextLength-1] != siglipEOSID {
		t.Fatalf("unexpected legacy tokenization suffix: %v", ids[siglipTextLength-4:])
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

func TestPreprocessImageRejectsUnsupportedFormatBeforeDecode(t *testing.T) {
	path := filepath.Join(t.TempDir(), "vector.svg")
	if err := os.WriteFile(path, []byte("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"), 0o600); err != nil {
		t.Fatal(err)
	}
	if _, err := PreprocessImageContext(context.Background(), path); !errors.Is(err, ErrUnsupportedSemanticImageFormat) {
		t.Fatalf("PreprocessImageContext error = %v, want ErrUnsupportedSemanticImageFormat", err)
	}
}

func TestPreprocessImageContextHonorsCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := PreprocessImageContext(ctx, "unused.png"); !errors.Is(err, context.Canceled) {
		t.Fatalf("PreprocessImageContext error = %v, want context.Canceled", err)
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
	if runtime.textInput[siglipTextLength-2] != 12 ||
		runtime.textInput[siglipTextLength-1] != siglipEOSID {
		t.Fatalf("text tokenizer was not left-padded with sticky EOS: %v", runtime.textInput[siglipTextLength-4:])
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

	preprocessed := make([]float32, 3*siglipImageSize*siglipImageSize)
	preprocessed[0] = 0.25
	preprocessed[len(preprocessed)-1] = -0.5
	tensorResponse, err := engine.Infer(context.Background(), ai.InferenceRequest{
		Operation: "embed_image_tensor",
		Payload:   map[string]any{"pixels": preprocessed},
	})
	if err != nil {
		t.Fatal(err)
	}
	tensorVector := tensorResponse.Payload["embedding"].([]float32)
	if len(tensorVector) != 2 || tensorVector[1] != 1 {
		t.Fatalf("unexpected normalized tensor embedding: %+v", tensorVector)
	}
	if len(runtime.imageInput) != len(preprocessed) ||
		runtime.imageInput[0] != 0.25 ||
		runtime.imageInput[len(runtime.imageInput)-1] != -0.5 {
		t.Fatal("preprocessed image tensor was not forwarded to ORT unchanged")
	}

	batchPixels := append(append([]float32(nil), preprocessed...), preprocessed...)
	batchResponse, err := engine.Infer(context.Background(), ai.InferenceRequest{
		Operation: "embed_image_batch_tensor",
		Payload: map[string]any{
			"pixels":    batchPixels,
			"batchSize": 2,
		},
	})
	if err != nil {
		t.Fatal(err)
	}
	batchVectors := batchResponse.Payload["embeddings"].([][]float32)
	if len(batchVectors) != 2 {
		t.Fatalf("batch embedding count = %d, want 2", len(batchVectors))
	}
	for index, item := range batchVectors {
		if len(item) != 2 ||
			math.Abs(float64(item[0]-0.6)) > 1e-5 ||
			math.Abs(float64(item[1]-0.8)) > 1e-5 {
			t.Fatalf("batch embedding %d not normalized: %+v", index, item)
		}
	}
	if runtime.imageBatchSize != 2 || len(runtime.imageBatchInput) != len(batchPixels) {
		t.Fatalf("batch tensor was not forwarded unchanged: size=%d len=%d", runtime.imageBatchSize, len(runtime.imageBatchInput))
	}
}

func TestDefaultManifestIsPinnedAndComplete(t *testing.T) {
	manifest := DefaultManifest()
	if manifest.Engine != EngineID || manifest.ID != DefaultModelID || manifest.Version != DefaultModelVersion {
		t.Fatalf("unexpected manifest identity: %+v", manifest)
	}
	if manifest.SizeBytes != 627105913 || len(manifest.Files) != 5 {
		t.Fatalf("unexpected manifest footprint: %+v", manifest)
	}
	for _, file := range manifest.Files {
		if len(file.SHA256) != 64 || file.SizeBytes <= 0 {
			t.Fatalf("unversioned model file: %+v", file)
		}
	}
}


func TestSampleBilinearAveragesFourPixelsAtCenter(t *testing.T) {
	img := image.NewRGBA(image.Rect(0, 0, 2, 2))
	img.Set(0, 0, color.RGBA{R: 255, A: 255})
	img.Set(1, 0, color.RGBA{G: 255, A: 255})
	img.Set(0, 1, color.RGBA{B: 255, A: 255})
	img.Set(1, 1, color.RGBA{R: 255, G: 255, B: 255, A: 255})

	r, g, b := sampleBilinear(img, img.Bounds(), 0.5, 0.5)
	for name, got := range map[string]float64{"r": r, "g": g, "b": b} {
		if math.Abs(got-127.5) > 0.01 {
			t.Fatalf("%s = %.4f, want 127.5", name, got)
		}
	}
}


func TestChooseDirectMLAdapterPrefersHardwareWithMostDedicatedMemory(t *testing.T) {
	selected, ok := chooseDirectMLAdapter([]directMLAdapterCandidate{
		{ID: 0, Name: "integrated", DedicatedVideoMemory: 512 << 20},
		{ID: 1, Name: "software", DedicatedVideoMemory: 16 << 30, Software: true},
		{ID: 2, Name: "discrete", DedicatedVideoMemory: 12 << 30},
		{ID: 3, Name: "smaller-discrete", DedicatedVideoMemory: 8 << 30},
	})
	if !ok {
		t.Fatal("no hardware adapter selected")
	}
	if selected.ID != 2 || selected.Name != "discrete" {
		t.Fatalf("selected adapter = %+v, want discrete adapter 2", selected)
	}
}

func TestChooseDirectMLAdapterTieBreaksByDXGIIndex(t *testing.T) {
	selected, ok := chooseDirectMLAdapter([]directMLAdapterCandidate{
		{ID: 3, Name: "later", DedicatedVideoMemory: 4 << 30},
		{ID: 1, Name: "earlier", DedicatedVideoMemory: 4 << 30},
	})
	if !ok || selected.ID != 1 {
		t.Fatalf("tie-break selected %+v, want adapter 1", selected)
	}
}
