//go:build windows && amd64

package siglip2

import (
	"context"
	"image"
	"image/color"
	"image/png"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/ai"
)

func TestRealSigLIP2Smoke(t *testing.T) {
	if os.Getenv("LUMINE_SIGLIP2_REAL_SMOKE") != "1" {
		t.Skip("set LUMINE_SIGLIP2_REAL_SMOKE=1 to download and run the pinned real model")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Minute)
	defer cancel()

	store := ai.NewModelStore(filepath.Join(t.TempDir(), "models"))
	installed, err := store.Install(ctx, DefaultManifest(), func(progress ai.DownloadProgress) {
		if progress.Done {
			t.Logf("downloaded %s@%s", progress.ModelID, progress.Version)
		}
	})
	if err != nil {
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
		t.Fatal(err)
	}
	if err := png.Encode(file, img); err != nil {
		_ = file.Close()
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		t.Fatal(err)
	}

	engine := NewEngine()
	if err := engine.Load(ctx, installed, ai.LoadOptions{AllowGPU: false}); err != nil {
		t.Fatalf("load real SigLIP2 engine: %v", err)
	}
	defer engine.Unload(context.Background())

	for iteration := 0; iteration < 5; iteration++ {
		imageResult, err := engine.Infer(ctx, ai.InferenceRequest{
			Operation: "embed_image",
			Payload: map[string]any{"filePath": imagePath},
		})
		if err != nil {
			t.Fatalf("real SigLIP2 image inference iteration %d: %v", iteration+1, err)
		}
		imageVector, ok := imageResult.Payload["embedding"].([]float32)
		if !ok || len(imageVector) != siglipEmbeddingSize {
			t.Fatalf("unexpected image embedding iteration %d: type=%T len=%d", iteration+1, imageResult.Payload["embedding"], len(imageVector))
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
}
