package llamacpp

import (
	"archive/zip"
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

func makeRuntimeZip(t *testing.T, name string, data []byte) []byte {
	t.Helper()
	var buffer bytes.Buffer
	writer := zip.NewWriter(&buffer)
	entry, err := writer.Create(name)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := entry.Write(data); err != nil {
		t.Fatal(err)
	}
	if err := writer.Close(); err != nil {
		t.Fatal(err)
	}
	return buffer.Bytes()
}

func shaHex(data []byte) string {
	sum := sha256.Sum256(data)
	return hex.EncodeToString(sum[:])
}

func TestRuntimeStoreInstallVerifyAndRemove(t *testing.T) {
	executable := []byte("fake llama server executable")
	archive := makeRuntimeZip(t, "bin/llama-server.exe", executable)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(archive)
	}))
	defer server.Close()

	manifest := RuntimeManifest{
		ID:             "llama-test",
		Version:        "v1",
		URL:            server.URL + "/runtime.zip",
		SHA256:         shaHex(archive),
		SizeBytes:      int64(len(archive)),
		ExecutableName: "llama-server.exe",
		Platform:       "windows",
		Architecture:   "amd64",
	}
	store := NewRuntimeStore(t.TempDir())
	var sawProgress bool
	installed, err := store.Install(context.Background(), manifest, func(progress RuntimeDownloadProgress) {
		sawProgress = true
	})
	if err != nil {
		t.Fatalf("Install: %v", err)
	}
	if !sawProgress {
		t.Fatal("expected runtime download progress")
	}
	if installed.RootDir == "" || installed.ExecutablePath == "" {
		t.Fatalf("incomplete installed runtime: %+v", installed)
	}
	if got, err := os.ReadFile(installed.ExecutablePath); err != nil || !bytes.Equal(got, executable) {
		t.Fatalf("installed executable mismatch: err=%v got=%q", err, got)
	}

	verified, err := store.Verify(manifest)
	if err != nil {
		t.Fatalf("Verify: %v", err)
	}
	if verified.ExecutableSHA256 != shaHex(executable) {
		t.Fatalf("executable hash = %s, want %s", verified.ExecutableSHA256, shaHex(executable))
	}

	if err := os.WriteFile(verified.ExecutablePath, []byte("tampered"), 0o755); err != nil {
		t.Fatal(err)
	}
	if _, err := store.Verify(manifest); err == nil {
		t.Fatal("tampered runtime executable should fail verification")
	}

	if _, err := store.Install(context.Background(), manifest, nil); err != nil {
		t.Fatalf("reinstall should repair runtime: %v", err)
	}
	if _, err := store.Verify(manifest); err != nil {
		t.Fatalf("Verify after reinstall: %v", err)
	}

	if err := store.Remove(manifest); err != nil {
		t.Fatalf("Remove: %v", err)
	}
	if _, err := store.Verify(manifest); err == nil {
		t.Fatal("removed runtime should no longer verify")
	}
}

func TestRuntimeStoreRejectsZipTraversal(t *testing.T) {
	archive := makeRuntimeZip(t, "../escape.exe", []byte("bad"))
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(archive)
	}))
	defer server.Close()

	manifest := RuntimeManifest{
		ID:             "llama-test",
		Version:        "v1",
		URL:            server.URL,
		SHA256:         shaHex(archive),
		SizeBytes:      int64(len(archive)),
		ExecutableName: "llama-server.exe",
		Platform:       "windows",
		Architecture:   "amd64",
	}
	store := NewRuntimeStore(t.TempDir())
	if _, err := store.Install(context.Background(), manifest, nil); err == nil {
		t.Fatal("zip path traversal should fail")
	}
}

func TestPinnedCPUAndVulkanRuntimeManifestsValidate(t *testing.T) {
	cpu := CPURuntimeManifest()
	vulkan := VulkanRuntimeManifest()
	for name, manifest := range map[string]RuntimeManifest{
		"cpu":    cpu,
		"vulkan": vulkan,
	} {
		if err := ValidateRuntimeManifest(manifest); err != nil {
			t.Fatalf("%s manifest: %v", name, err)
		}
		if manifest.Version != LlamaRuntimeVersion {
			t.Fatalf("%s version = %q, want %q", name, manifest.Version, LlamaRuntimeVersion)
		}
	}
	if cpu.ID == vulkan.ID {
		t.Fatal("CPU and Vulkan runtime IDs must be distinct")
	}
	if cpu.SHA256 != "a73abd4fd618b8145bbe7a9e9ca2dad880f05eb589a5942f921b1f39bd2d87dc" || cpu.SizeBytes != 18453883 {
		t.Fatalf("unexpected pinned CPU runtime: %+v", cpu)
	}
	if vulkan.SHA256 != "e9b796976a476e5c706a7858bdfb39b67d10e5651d17ddaba7c3f45479d6e331" || vulkan.SizeBytes != 31838165 {
		t.Fatalf("unexpected pinned Vulkan runtime: %+v", vulkan)
	}
}

func TestRuntimePolicyUsesVulkanThenCPUFallback(t *testing.T) {
	gpu := RuntimeManifestsForPolicy(true)
	if len(gpu) != 2 {
		t.Fatalf("GPU runtime candidates = %d, want 2", len(gpu))
	}
	if RuntimeBackend(gpu[0]) != "vulkan" || RuntimeBackend(gpu[1]) != "cpu" {
		t.Fatalf("GPU runtime order = %s -> %s, want vulkan -> cpu", RuntimeBackend(gpu[0]), RuntimeBackend(gpu[1]))
	}
	cpu := RuntimeManifestsForPolicy(false)
	if len(cpu) != 1 || RuntimeBackend(cpu[0]) != "cpu" {
		t.Fatalf("CPU runtime policy = %+v", cpu)
	}
	if PreferredRuntimeManifest(true).ID != VulkanRuntimeManifest().ID {
		t.Fatal("GPU policy did not select Vulkan runtime")
	}
	if PreferredRuntimeManifest(false).ID != CPURuntimeManifest().ID {
		t.Fatal("CPU policy did not select CPU runtime")
	}
}

func TestPinnedRuntimeAndVisionModelManifestsValidate(t *testing.T) {
	if err := ValidateRuntimeManifest(DefaultRuntimeManifest()); err != nil {
		t.Fatalf("DefaultRuntimeManifest: %v", err)
	}
	manifest := DefaultVisionModelManifest()
	if err := ai.ValidateManifest(manifest); err != nil {
		t.Fatalf("DefaultVisionModelManifest: %v", err)
	}
	if manifest.Engine != EngineID || len(manifest.Files) != 2 {
		t.Fatalf("unexpected vision manifest: %+v", manifest)
	}
	if manifest.SizeBytes != 545590272 {
		t.Fatalf("vision model footprint = %d", manifest.SizeBytes)
	}
}

func TestRuntimeMetadataExecutablePathIsRelativeOnDisk(t *testing.T) {
	executable := []byte("fake")
	archive := makeRuntimeZip(t, filepath.ToSlash(filepath.Join("nested", "llama-server.exe")), executable)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(archive)
	}))
	defer server.Close()

	manifest := RuntimeManifest{
		ID: "llama-test", Version: "v2", URL: server.URL,
		SHA256: shaHex(archive), SizeBytes: int64(len(archive)),
		ExecutableName: "llama-server.exe", Platform: "windows", Architecture: "amd64",
	}
	store := NewRuntimeStore(t.TempDir())
	installed, err := store.Install(context.Background(), manifest, nil)
	if err != nil {
		t.Fatal(err)
	}
	raw, err := os.ReadFile(filepath.Join(installed.RootDir, runtimeInstallManifestName))
	if err != nil {
		t.Fatal(err)
	}
	if bytes.Contains(raw, []byte(installed.RootDir)) {
		t.Fatal("runtime metadata must not persist machine-specific absolute paths")
	}
}
