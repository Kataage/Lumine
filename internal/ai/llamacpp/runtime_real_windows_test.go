//go:build windows && amd64

package llamacpp

import (
	"context"
	"os"
	"os/exec"
	"strings"
	"testing"
	"time"
)

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
