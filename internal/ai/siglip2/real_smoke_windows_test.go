//go:build windows && amd64

package siglip2

import (
	"context"
	"image"
	"image/color"
	"image/png"
	"math"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/ai"
)

func installRealSigLIP2Fixture(t testing.TB) (context.Context, context.CancelFunc, ai.InstalledModel, string) {
	t.Helper()

	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Minute)
	store := ai.NewModelStore(filepath.Join(t.TempDir(), "models"))
	installed, err := store.Install(ctx, DefaultManifest(), func(progress ai.DownloadProgress) {
		if progress.Done {
			t.Logf("downloaded %s@%s", progress.ModelID, progress.Version)
		}
	})
	if err != nil {
		cancel()
		t.Fatalf("install real SigLIP2 model: %v", err)
	}

	imagePath := filepath.Join(t.TempDir(), "smoke.png")
	img := image.NewRGBA(image.Rect(0, 0, 224, 224))
	for y := 0; y < 224; y++ {
		for x := 0; x < 224; x++ {
			img.Set(x, y, color.RGBA{
				R: uint8((x * 255) / 223),
				G: uint8((y * 255) / 223),
				B: uint8(((x + y) * 255) / 446),
				A: 255,
			})
		}
	}
	file, err := os.Create(imagePath)
	if err != nil {
		cancel()
		t.Fatal(err)
	}
	if err := png.Encode(file, img); err != nil {
		_ = file.Close()
		cancel()
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		cancel()
		t.Fatal(err)
	}
	return ctx, cancel, installed, imagePath
}

func writeSigLIP2ColorSquare(t testing.TB, dir, name string, square color.RGBA) string {
	t.Helper()
	path := filepath.Join(dir, name)
	img := image.NewRGBA(image.Rect(0, 0, 224, 224))
	for y := 0; y < 224; y++ {
		for x := 0; x < 224; x++ {
			pixel := color.RGBA{R: 245, G: 245, B: 245, A: 255}
			if x >= 32 && x < 192 && y >= 32 && y < 192 {
				pixel = square
			}
			img.Set(x, y, pixel)
		}
	}
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
	return path
}

func realSigLIP2Vector(
	t testing.TB,
	ctx context.Context,
	engine ai.Engine,
	operation string,
	payload map[string]any,
) []float32 {
	t.Helper()
	result, err := engine.Infer(ctx, ai.InferenceRequest{Operation: operation, Payload: payload})
	if err != nil {
		t.Fatalf("%s inference: %v", operation, err)
	}
	vector, ok := result.Payload["embedding"].([]float32)
	if !ok || len(vector) != siglipEmbeddingSize {
		t.Fatalf("unexpected %s embedding: type=%T len=%d", operation, result.Payload["embedding"], len(vector))
	}
	return vector
}

func sigLIP2Dot(a, b []float32) float64 {
	var score float64
	for i := range a {
		score += float64(a[i]) * float64(b[i])
	}
	return score
}

func assertRealSigLIP2RetrievalSanity(t testing.TB, ctx context.Context, engine ai.Engine, dir string) {
	t.Helper()
	redPath := writeSigLIP2ColorSquare(t, dir, "red-square.png", color.RGBA{R: 230, G: 25, B: 25, A: 255})
	bluePath := writeSigLIP2ColorSquare(t, dir, "blue-square.png", color.RGBA{R: 25, G: 70, B: 230, A: 255})

	redImage := realSigLIP2Vector(t, ctx, engine, "embed_image", map[string]any{"filePath": redPath})
	blueImage := realSigLIP2Vector(t, ctx, engine, "embed_image", map[string]any{"filePath": bluePath})
	redText := realSigLIP2Vector(t, ctx, engine, "embed_text", map[string]any{"text": "this is a photo of a red square."})
	blueText := realSigLIP2Vector(t, ctx, engine, "embed_text", map[string]any{"text": "this is a photo of a blue square."})

	redCorrect := sigLIP2Dot(redText, redImage)
	redWrong := sigLIP2Dot(redText, blueImage)
	blueCorrect := sigLIP2Dot(blueText, blueImage)
	blueWrong := sigLIP2Dot(blueText, redImage)
	for label, score := range map[string]float64{
		"red_correct": redCorrect,
		"red_wrong": redWrong,
		"blue_correct": blueCorrect,
		"blue_wrong": blueWrong,
	} {
		if math.IsNaN(score) || math.IsInf(score, 0) {
			t.Fatalf("%s similarity is non-finite: %v", label, score)
		}
	}
	t.Logf(
		"retrieval sanity red(correct=%.4f wrong=%.4f) blue(correct=%.4f wrong=%.4f)",
		redCorrect, redWrong, blueCorrect, blueWrong,
	)
	if redCorrect <= redWrong {
		t.Fatalf("red text ranked blue image above red image: correct=%.4f wrong=%.4f", redCorrect, redWrong)
	}
	if blueCorrect <= blueWrong {
		t.Fatalf("blue text ranked red image above blue image: correct=%.4f wrong=%.4f", blueCorrect, blueWrong)
	}
}

func runRealSigLIP2Inference(t testing.TB, ctx context.Context, engine ai.Engine, imagePath string, imageIterations int) {
	t.Helper()
	for iteration := 0; iteration < imageIterations; iteration++ {
		imageResult, err := engine.Infer(ctx, ai.InferenceRequest{
			Operation: "embed_image",
			Payload: map[string]any{"filePath": imagePath},
		})
		if err != nil {
			t.Fatalf("real SigLIP2 image inference iteration %d: %v", iteration+1, err)
		}
		imageVector, ok := imageResult.Payload["embedding"].([]float32)
		if !ok || len(imageVector) != siglipEmbeddingSize {
			t.Fatalf(
				"unexpected image embedding iteration %d: type=%T len=%d",
				iteration+1,
				imageResult.Payload["embedding"],
				len(imageVector),
			)
		}
	}

	textResult, err := engine.Infer(ctx, ai.InferenceRequest{
		Operation: "embed_text",
		Payload: map[string]any{"text": "anime character standing outdoors"},
	})
	if err != nil {
		t.Fatalf("real SigLIP2 text inference: %v", err)
	}
	textVector, ok := textResult.Payload["embedding"].([]float32)
	if !ok || len(textVector) != siglipEmbeddingSize {
		t.Fatalf("unexpected text embedding: type=%T len=%d", textResult.Payload["embedding"], len(textVector))
	}

	assertRealSigLIP2RetrievalSanity(t, ctx, engine, filepath.Dir(imagePath))
}

func TestRealSigLIP2Smoke(t *testing.T) {
	if os.Getenv("LUMINE_SIGLIP2_REAL_SMOKE") != "1" {
		t.Skip("set LUMINE_SIGLIP2_REAL_SMOKE=1 to download and run the pinned real model")
	}

	ctx, cancel, installed, imagePath := installRealSigLIP2Fixture(t)
	defer cancel()

	engine := NewEngine()
	if err := engine.Load(ctx, installed, ai.LoadOptions{AllowGPU: false}); err != nil {
		t.Fatalf("load real SigLIP2 CPU engine: %v", err)
	}
	defer engine.Unload(context.Background())

	diagnostics := engine.(ai.RuntimeDiagnosticsProvider).RuntimeDiagnostics()
	if diagnostics.ExecutionProvider != "cpu" {
		t.Fatalf("CPU smoke provider = %q, want cpu", diagnostics.ExecutionProvider)
	}

	// The runtime DLLs are already mapped. Re-resolving extraction must reuse
	// immutable verified files rather than replacing a loaded Windows DLL.
	if _, err := extractORTRuntime(installed.RootDir); err != nil {
		t.Fatalf("reuse loaded ONNX Runtime DLLs: %v", err)
	}

	runRealSigLIP2Inference(t, ctx, engine, imagePath, 5)

	requestGPU := os.Getenv("LUMINE_SIGLIP2_GPU_REQUEST_SMOKE") == "1" ||
		os.Getenv("LUMINE_SIGLIP2_REAL_GPU_SMOKE") == "1"
	if requestGPU {
		if err := engine.Unload(context.Background()); err != nil {
			t.Fatalf("unload CPU engine before GPU-request smoke: %v", err)
		}
		if err := engine.Load(ctx, installed, ai.LoadOptions{AllowGPU: true}); err != nil {
			t.Fatalf("load real SigLIP2 GPU-request engine: %v", err)
		}
		diagnostics = engine.(ai.RuntimeDiagnosticsProvider).RuntimeDiagnostics()
		if diagnostics.ExecutionProvider != "directml" && diagnostics.ExecutionProvider != "cpu" {
			t.Fatalf("unexpected GPU-request provider=%q", diagnostics.ExecutionProvider)
		}
		if diagnostics.ExecutionProvider == "cpu" && diagnostics.Warning == "" {
			t.Fatal("GPU request fell back to CPU without a diagnostic warning")
		}
		t.Logf(
			"GPU-request provider=%s warning=%q",
			diagnostics.ExecutionProvider,
			diagnostics.Warning,
		)
		if os.Getenv("LUMINE_SIGLIP2_REAL_GPU_SMOKE") == "1" &&
			diagnostics.ExecutionProvider != "directml" {
			t.Fatalf(
				"strict GPU smoke did not activate DirectML: provider=%q warning=%q",
				diagnostics.ExecutionProvider,
				diagnostics.Warning,
			)
		}
		runRealSigLIP2Inference(t, ctx, engine, imagePath, 5)
	}
}

func TestRealSigLIP2DirectMLSmoke(t *testing.T) {
	if os.Getenv("LUMINE_SIGLIP2_REAL_GPU_SMOKE") != "1" {
		t.Skip("set LUMINE_SIGLIP2_REAL_GPU_SMOKE=1 on a DirectML-capable Windows GPU host")
	}

	ctx, cancel, installed, imagePath := installRealSigLIP2Fixture(t)
	defer cancel()

	engine := NewEngine()
	if err := engine.Load(ctx, installed, ai.LoadOptions{AllowGPU: true}); err != nil {
		t.Fatalf("load real SigLIP2 DirectML engine: %v", err)
	}
	defer engine.Unload(context.Background())

	diagnostics := engine.(ai.RuntimeDiagnosticsProvider).RuntimeDiagnostics()
	if diagnostics.ExecutionProvider != "directml" {
		t.Fatalf(
			"GPU smoke did not activate DirectML: provider=%q warning=%q",
			diagnostics.ExecutionProvider,
			diagnostics.Warning,
		)
	}
	t.Logf("strict DirectML provider=%s", diagnostics.ExecutionProvider)

	runRealSigLIP2Inference(t, ctx, engine, imagePath, 5)
}

func benchmarkRealSigLIP2Image(b *testing.B, allowGPU bool, expectedProvider string) {
	ctx, cancel, installed, imagePath := installRealSigLIP2Fixture(b)
	defer cancel()

	engine := NewEngine()
	if err := engine.Load(ctx, installed, ai.LoadOptions{AllowGPU: allowGPU}); err != nil {
		b.Fatal(err)
	}
	defer engine.Unload(context.Background())

	diagnostics := engine.(ai.RuntimeDiagnosticsProvider).RuntimeDiagnostics()
	if diagnostics.ExecutionProvider != expectedProvider {
		b.Fatalf(
			"provider=%q, want %q; warning=%q",
			diagnostics.ExecutionProvider,
			expectedProvider,
			diagnostics.Warning,
		)
	}
	b.Logf("execution provider=%s", diagnostics.ExecutionProvider)

	// Warm session/model initialization outside the timed section.
	runRealSigLIP2Inference(b, ctx, engine, imagePath, 1)
	b.ReportAllocs()
	b.ResetTimer()
	for iteration := 0; iteration < b.N; iteration++ {
		if _, err := engine.Infer(ctx, ai.InferenceRequest{
			Operation: "embed_image",
			Payload: map[string]any{"filePath": imagePath},
		}); err != nil {
			b.Fatal(err)
		}
	}
}

func BenchmarkRealSigLIP2ImageCPU(b *testing.B) {
	if os.Getenv("LUMINE_SIGLIP2_REAL_BENCH") != "1" {
		b.Skip("set LUMINE_SIGLIP2_REAL_BENCH=1")
	}
	benchmarkRealSigLIP2Image(b, false, "cpu")
}

func BenchmarkRealSigLIP2ImageDirectML(b *testing.B) {
	if os.Getenv("LUMINE_SIGLIP2_REAL_GPU_BENCH") != "1" {
		b.Skip("set LUMINE_SIGLIP2_REAL_GPU_BENCH=1 on a DirectML-capable Windows GPU host")
	}
	benchmarkRealSigLIP2Image(b, true, "directml")
}
