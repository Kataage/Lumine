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


func TestRuntimeStoreSupportsHashOnlyManifestSize(t *testing.T) {
	executable := []byte("fake vulkan llama server")
	archive := makeRuntimeZip(t, "llama-server.exe", executable)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(archive)
	}))
	defer server.Close()

	manifest := RuntimeManifest{
		ID:             "llama-vulkan-test",
		Version:        "v1",
		URL:            server.URL,
		SHA256:         shaHex(archive),
		SizeBytes:      0,
		ExecutableName: "llama-server.exe",
		Platform:       "windows",
		Architecture:   "amd64",
	}
	store := NewRuntimeStore(t.TempDir())
	var final RuntimeDownloadProgress
	if _, err := store.Install(context.Background(), manifest, func(progress RuntimeDownloadProgress) {
		final = progress
	}); err != nil {
		t.Fatalf("Install hash-only runtime: %v", err)
	}
	if !final.Done ||
		final.BytesDownloaded != int64(len(archive)) ||
		final.BytesTotal != int64(len(archive)) {
		t.Fatalf("unexpected final progress: %+v", final)
	}
	if _, err := store.Verify(manifest); err != nil {
		t.Fatalf("Verify hash-only runtime: %v", err)
	}
}

func TestPinnedVulkanRuntimeManifestValidates(t *testing.T) {
	manifest := VulkanRuntimeManifest()
	if err := ValidateRuntimeManifest(manifest); err != nil {
		t.Fatalf("VulkanRuntimeManifest: %v", err)
	}
	if manifest.SHA256 != "1ee3ad952f4ba71f438bd6d7bebef19e1c7af04adcaa35d08b4ddabb27d4c642" {
		t.Fatalf("unexpected Vulkan runtime hash: %s", manifest.SHA256)
	}
	if selected := RuntimeManifestForGPU(true); selected.ID != manifest.ID {
		t.Fatalf("GPU runtime = %s, want %s", selected.ID, manifest.ID)
	}
	if selected := RuntimeManifestForGPU(false); selected.ID != DefaultRuntimeManifest().ID {
		t.Fatalf("CPU runtime = %s", selected.ID)
	}
}
