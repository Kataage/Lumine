package llamacpp

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestRouterServerArgsBoundResidencyAndMemory(t *testing.T) {
	gpu := strings.Join(buildRouterServerArgs("models.ini", 1234, "vulkan"), " ")
	for _, want := range []string{
		"--models-max 1",
		"--models-autoload",
		"--parallel 1",
		"--cache-ram 256",
		"--sleep-idle-seconds 300",
		"--fit on",
		"--fit-target 1024",
		"--gpu-layers auto",
	} {
		if !strings.Contains(gpu, want) {
			t.Fatalf("GPU router args %q do not contain %q", gpu, want)
		}
	}
	if strings.Contains(gpu, "-ngl 99") {
		t.Fatalf("GPU router must not force full VRAM offload: %q", gpu)
	}

	cpu := strings.Join(buildRouterServerArgs("models.ini", 1234, "cpu"), " ")
	for _, want := range []string{"--models-max 1", "--device none", "--gpu-layers 0", "--no-mmproj-offload"} {
		if !strings.Contains(cpu, want) {
			t.Fatalf("CPU router args %q do not contain %q", cpu, want)
		}
	}
}

func TestRouterPresetRegistersMultipleLogicalModels(t *testing.T) {
	store := NewRuntimeStore(t.TempDir())
	store.routerModels["vision"] = routerModelConfig{
		Alias:       "vision",
		ModelPath:   filepath.Join(t.TempDir(), "vision model.gguf"),
		MMProjPath:  filepath.Join(t.TempDir(), "mmproj vision.gguf"),
		ContextSize: 4096,
		Threads:     8,
	}
	store.routerModels["prompt"] = routerModelConfig{
		Alias:       "prompt",
		ModelPath:   filepath.Join(t.TempDir(), "prompt model.gguf"),
		ContextSize: 8192,
		Threads:     8,
		ExtraArgs:   []string{"--reasoning", "off"},
	}

	path := filepath.Join(t.TempDir(), "models.ini")
	if err := store.writeRouterPresetForProvider(path, "vulkan"); err != nil {
		t.Fatalf("writeRouterPresetForProvider: %v", err)
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	text := string(raw)
	for _, want := range []string{
		"[vision]",
		"mmproj = ",
		"c = 4096",
		"[prompt]",
		"c = 8192",
		"reasoning = off",
	} {
		if !strings.Contains(text, want) {
			t.Fatalf("router preset missing %q:\n%s", want, text)
		}
	}
}

func TestRouterPresetRejectsCommentCharactersInPaths(t *testing.T) {
	store := NewRuntimeStore(t.TempDir())
	store.routerModels["bad"] = routerModelConfig{
		Alias:     "bad",
		ModelPath: `C:\models\bad#path.gguf`,
	}
	if err := store.writeRouterPresetForProvider(filepath.Join(t.TempDir(), "models.ini"), "vulkan"); err == nil {
		t.Fatal("expected unsupported INI path to fail")
	}
}

func TestPresetLinesForExtraArgs(t *testing.T) {
	got := strings.Join(presetLinesForExtraArgs([]string{"--reasoning", "off", "--jinja"}), "\n")
	if got != "reasoning = off\njinja = true" {
		t.Fatalf("preset lines = %q", got)
	}
}
