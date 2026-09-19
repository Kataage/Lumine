//go:build windows && amd64

package llamacpp

import (
	"context"
	"fmt"
	"image"
	"image/color"
	"image/png"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/ai"
)

var offloadedLayersPattern = regexp.MustCompile(`(?i)offloaded\s+(\d+)/(\d+)\s+layers\s+to\s+GPU`)

func TestRealPinnedLlamaRuntimeSmoke(t *testing.T) {
	if os.Getenv("LUMINE_LLAMA_RUNTIME_REAL_SMOKE") != "1" {
		t.Skip("set LUMINE_LLAMA_RUNTIME_REAL_SMOKE=1 to run the pinned runtime smoke")
	}
	store := NewRuntimeStore(t.TempDir())
	for _, manifest := range []RuntimeManifest{CPURuntimeManifest(), VulkanRuntimeManifest()} {
		manifest := manifest
		t.Run(RuntimeBackend(manifest), func(t *testing.T) {
			ctx, cancel := context.WithTimeout(context.Background(), 4*time.Minute)
			defer cancel()
			installed, err := store.Install(ctx, manifest, nil)
			if err != nil {
				t.Fatalf("install %s runtime: %v", RuntimeBackend(manifest), err)
			}

			versionCtx, versionCancel := context.WithTimeout(context.Background(), 30*time.Second)
			defer versionCancel()
			output, err := exec.CommandContext(versionCtx, installed.ExecutablePath, "--version").CombinedOutput()
			if err != nil {
				t.Fatalf("%s llama-server --version: %v\n%s", RuntimeBackend(manifest), err, output)
			}
			if strings.TrimSpace(string(output)) == "" {
				t.Fatalf("%s llama-server --version returned no output", RuntimeBackend(manifest))
			}
			t.Logf("%s runtime version output: %s", RuntimeBackend(manifest), strings.TrimSpace(string(output)))
		})
	}
}

func TestRealVulkanDeviceSmoke(t *testing.T) {
	if os.Getenv("LUMINE_LLAMA_RUNTIME_REAL_GPU_SMOKE") != "1" {
		t.Skip("set LUMINE_LLAMA_RUNTIME_REAL_GPU_SMOKE=1 on a physical Vulkan GPU host")
	}
	store := NewRuntimeStore(t.TempDir())
	ctx, cancel := context.WithTimeout(context.Background(), 4*time.Minute)
	defer cancel()
	installed, err := store.Install(ctx, VulkanRuntimeManifest(), nil)
	if err != nil {
		t.Fatal(err)
	}
	deviceCtx, deviceCancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer deviceCancel()
	output, err := exec.CommandContext(deviceCtx, installed.ExecutablePath, "--list-devices").CombinedOutput()
	if err != nil {
		t.Fatalf("llama-server --list-devices: %v\n%s", err, output)
	}
	text := strings.ToLower(string(output))
	if !strings.Contains(text, "vulkan") {
		t.Fatalf("strict GPU smoke did not report a Vulkan device:\n%s", output)
	}
	t.Logf("Vulkan devices:\n%s", strings.TrimSpace(string(output)))
}

// TestRealSmolVLMVulkanOffload proves more than Vulkan DLL availability: it
// loads Lumine's pinned real Lightweight Vision model through the production
// engine, requires the Vulkan runtime to remain selected, verifies llama.cpp's
// own layer-offload log, and completes one real image inference.
//
// This is intentionally opt-in because GitHub-hosted Windows runners do not
// expose a representative physical Vulkan GPU.
func TestRealSmolVLMVulkanOffload(t *testing.T) {
	if os.Getenv("LUMINE_LLAMA_REAL_GPU_MODEL_SMOKE") != "1" {
		t.Skip("set LUMINE_LLAMA_REAL_GPU_MODEL_SMOKE=1 on a physical Vulkan GPU host")
	}

	root := realLlamaCacheRoot(t)
	runtimeStore := NewRuntimeStore(filepath.Join(root, "runtimes"))
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Minute)
	defer cancel()

	for _, manifest := range []RuntimeManifest{CPURuntimeManifest(), VulkanRuntimeManifest()} {
		if _, err := runtimeStore.Verify(manifest); err == nil {
			continue
		}
		if _, err := runtimeStore.Install(ctx, manifest, nil); err != nil {
			t.Fatalf("install %s runtime: %v", RuntimeBackend(manifest), err)
		}
	}

	model, err := ensureRealSmolVLM(ctx, filepath.Join(root, "models"))
	if err != nil {
		t.Fatal(err)
	}
	imagePath := writeRealSmokeImage(t, root)

	engine := newEngine(runtimeStore, EngineID)
	loadCtx, loadCancel := context.WithTimeout(context.Background(), 8*time.Minute)
	defer loadCancel()
	if err := engine.Load(loadCtx, model, ai.LoadOptions{AllowGPU: true}); err != nil {
		t.Fatalf("load SmolVLM with Vulkan requested: %v", err)
	}
	defer func() {
		unloadCtx, unloadCancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer unloadCancel()
		if err := engine.Unload(unloadCtx); err != nil {
			t.Errorf("unload real Vulkan smoke runtime: %v", err)
		}
	}()

	diagnostics := engine.RuntimeDiagnostics()
	if diagnostics.ExecutionProvider != "vulkan" {
		t.Fatalf("strict real GPU smoke provider=%q warning=%q, want vulkan", diagnostics.ExecutionProvider, diagnostics.Warning)
	}

	stdout, stderr := engine.sidecar.Logs()
	offloaded, total, ok := parseOffloadedLayers(stdout + "\n" + stderr)
	if !ok || offloaded <= 0 || total <= 0 {
		t.Fatalf("llama.cpp did not confirm GPU layer offload; provider=%s\nstdout:\n%s\nstderr:\n%s", diagnostics.ExecutionProvider, tailLog(stdout), tailLog(stderr))
	}
	t.Logf("confirmed Vulkan offload: %d/%d layers", offloaded, total)
	for _, line := range gpuEvidenceLines(stdout + "\n" + stderr) {
		t.Logf("gpu evidence: %s", line)
	}

	inferCtx, inferCancel := context.WithTimeout(context.Background(), 3*time.Minute)
	defer inferCancel()
	response, err := engine.Infer(inferCtx, ai.InferenceRequest{
		Operation: "analyze_image",
		Payload: map[string]any{
			"filePath": imagePath,
			"mode":     "short",
		},
	})
	if err != nil {
		t.Fatalf("real Vulkan SmolVLM inference: %v", err)
	}
	if strings.TrimSpace(fmt.Sprint(response.Payload["shortCaption"])) == "" {
		t.Fatalf("real Vulkan inference returned no short caption: %+v", response.Payload)
	}
	t.Logf("real Vulkan inference completed: shortCaption=%q", response.Payload["shortCaption"])
}

func BenchmarkRealSmolVLMImageCPU(b *testing.B) {
	if os.Getenv("LUMINE_LLAMA_REAL_BENCH") != "1" {
		b.Skip("set LUMINE_LLAMA_REAL_BENCH=1 on the benchmark host")
	}
	benchmarkRealSmolVLMImage(b, false)
}

func BenchmarkRealSmolVLMImageVulkan(b *testing.B) {
	if os.Getenv("LUMINE_LLAMA_REAL_GPU_BENCH") != "1" {
		b.Skip("set LUMINE_LLAMA_REAL_GPU_BENCH=1 on a physical Vulkan GPU host")
	}
	benchmarkRealSmolVLMImage(b, true)
}

func benchmarkRealSmolVLMImage(b *testing.B, allowGPU bool) {
	root := realLlamaCacheRoot(b)
	runtimeStore := NewRuntimeStore(filepath.Join(root, "runtimes"))
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Minute)
	defer cancel()

	needed := []RuntimeManifest{CPURuntimeManifest()}
	if allowGPU {
		needed = append(needed, VulkanRuntimeManifest())
	}
	for _, manifest := range needed {
		if _, err := runtimeStore.Verify(manifest); err == nil {
			continue
		}
		if _, err := runtimeStore.Install(ctx, manifest, nil); err != nil {
			b.Fatalf("install %s runtime: %v", RuntimeBackend(manifest), err)
		}
	}
	model, err := ensureRealSmolVLM(ctx, filepath.Join(root, "models"))
	if err != nil {
		b.Fatal(err)
	}
	imagePath := writeRealSmokeImage(b, root)

	engine := newEngine(runtimeStore, EngineID)
	loadCtx, loadCancel := context.WithTimeout(context.Background(), 8*time.Minute)
	if err := engine.Load(loadCtx, model, ai.LoadOptions{AllowGPU: allowGPU}); err != nil {
		loadCancel()
		b.Fatal(err)
	}
	loadCancel()
	defer func() {
		unloadCtx, unloadCancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer unloadCancel()
		_ = engine.Unload(unloadCtx)
	}()

	diagnostics := engine.RuntimeDiagnostics()
	wantProvider := "cpu"
	if allowGPU {
		wantProvider = "vulkan"
	}
	if diagnostics.ExecutionProvider != wantProvider {
		b.Fatalf("benchmark provider=%q warning=%q, want %q", diagnostics.ExecutionProvider, diagnostics.Warning, wantProvider)
	}
	if allowGPU {
		stdout, stderr := engine.sidecar.Logs()
		offloaded, total, ok := parseOffloadedLayers(stdout + "\n" + stderr)
		if !ok || offloaded <= 0 {
			b.Fatalf("Vulkan benchmark did not confirm layer offload: %d/%d", offloaded, total)
		}
		b.Logf("Vulkan offload: %d/%d layers", offloaded, total)
		for _, line := range gpuEvidenceLines(stdout + "\n" + stderr) {
			b.Logf("gpu evidence: %s", line)
		}
	}

	request := ai.InferenceRequest{
		Operation: "analyze_image",
		Payload: map[string]any{
			"filePath": imagePath,
			"mode":     "short",
		},
	}
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		inferCtx, inferCancel := context.WithTimeout(context.Background(), 3*time.Minute)
		_, err := engine.Infer(inferCtx, request)
		inferCancel()
		if err != nil {
			b.Fatal(err)
		}
	}
}

func ensureRealSmolVLM(ctx context.Context, root string) (ai.InstalledModel, error) {
	store := ai.NewModelStore(root)
	manifest := DefaultVisionModelManifest()
	if installed, err := store.Verify(manifest.ID, manifest.Version); err == nil {
		return installed, nil
	}
	installed, err := store.Install(ctx, manifest, nil)
	if err != nil {
		return ai.InstalledModel{}, fmt.Errorf("install pinned SmolVLM model: %w", err)
	}
	return installed, nil
}

func realLlamaCacheRoot(tb testing.TB) string {
	tb.Helper()
	if root := strings.TrimSpace(os.Getenv("LUMINE_LLAMA_REAL_CACHE")); root != "" {
		if err := os.MkdirAll(root, 0o755); err != nil {
			tb.Fatalf("create LUMINE_LLAMA_REAL_CACHE: %v", err)
		}
		return root
	}
	return tb.TempDir()
}

func writeRealSmokeImage(tb testing.TB, root string) string {
	tb.Helper()
	path := filepath.Join(root, "smolvlm-smoke.png")
	if _, err := os.Stat(path); err == nil {
		return path
	}
	img := image.NewRGBA(image.Rect(0, 0, 128, 128))
	for y := 0; y < 128; y++ {
		for x := 0; x < 128; x++ {
			switch {
			case x < 64 && y < 64:
				img.Set(x, y, color.RGBA{R: 240, G: 48, B: 48, A: 255})
			case x >= 64 && y < 64:
				img.Set(x, y, color.RGBA{R: 48, G: 200, B: 80, A: 255})
			default:
				img.Set(x, y, color.RGBA{R: 48, G: 96, B: 224, A: 255})
			}
		}
	}
	file, err := os.Create(path)
	if err != nil {
		tb.Fatalf("create smoke image: %v", err)
	}
	if err := png.Encode(file, img); err != nil {
		_ = file.Close()
		tb.Fatalf("encode smoke image: %v", err)
	}
	if err := file.Close(); err != nil {
		tb.Fatalf("close smoke image: %v", err)
	}
	return path
}

func parseOffloadedLayers(logs string) (offloaded int, total int, ok bool) {
	matches := offloadedLayersPattern.FindStringSubmatch(logs)
	if len(matches) != 3 {
		return 0, 0, false
	}
	offloaded, err1 := strconv.Atoi(matches[1])
	total, err2 := strconv.Atoi(matches[2])
	return offloaded, total, err1 == nil && err2 == nil
}

func gpuEvidenceLines(logs string) []string {
	var evidence []string
	for _, raw := range strings.Split(logs, "\n") {
		line := strings.TrimSpace(raw)
		lower := strings.ToLower(line)
		if line == "" {
			continue
		}
		if strings.Contains(lower, "vulkan") ||
			strings.Contains(lower, "offloaded") ||
			(strings.Contains(lower, "buffer size") && strings.Contains(lower, "gpu")) {
			evidence = append(evidence, line)
		}
	}
	if len(evidence) > 20 {
		evidence = evidence[len(evidence)-20:]
	}
	return evidence
}
