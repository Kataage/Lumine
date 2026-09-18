package ai

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"sync/atomic"
	"testing"
)

func testSHA256(data []byte) string {
	sum := sha256.Sum256(data)
	return hex.EncodeToString(sum[:])
}

func testManifest(serverURL string, data []byte) ModelManifest {
	return ModelManifest{
		ID:          "test-model",
		Version:     "1.0.0",
		Engine:      "dummy",
		DisplayName: "Test Model",
		License:     "MIT",
		SizeBytes:   int64(len(data)),
		Files: []ModelFile{
			{
				Path:      "model.bin",
				URL:       serverURL + "/model.bin",
				SHA256:    testSHA256(data),
				SizeBytes: int64(len(data)),
			},
		},
	}
}

func TestModelStoreInstallVerifyRepairAndRemove(t *testing.T) {
	data := []byte("lumine model bytes")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(data)
	}))
	defer server.Close()

	store := NewModelStore(t.TempDir())
	manifest := testManifest(server.URL, data)

	var sawProgress bool
	installed, err := store.Install(context.Background(), manifest, func(progress DownloadProgress) {
		sawProgress = true
	})
	if err != nil {
		t.Fatalf("Install: %v", err)
	}
	if !sawProgress {
		t.Fatal("expected download progress callback")
	}
	if installed.Manifest.ID != manifest.ID {
		t.Fatalf("installed model id = %q, want %q", installed.Manifest.ID, manifest.ID)
	}

	if _, err := store.Verify(manifest.ID, manifest.Version); err != nil {
		t.Fatalf("Verify: %v", err)
	}

	list, err := store.List()
	if err != nil {
		t.Fatalf("List: %v", err)
	}
	if len(list) != 1 || list[0].ID != manifest.ID {
		t.Fatalf("unexpected installed models: %+v", list)
	}

	modelPath := filepath.Join(installed.RootDir, "model.bin")
	if err := os.WriteFile(modelPath, []byte("corrupted"), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := store.Verify(manifest.ID, manifest.Version); err == nil {
		t.Fatal("corrupted model should fail verification")
	}

	if _, err := store.Install(context.Background(), manifest, nil); err != nil {
		t.Fatalf("reinstall should repair model: %v", err)
	}
	if _, err := store.Verify(manifest.ID, manifest.Version); err != nil {
		t.Fatalf("Verify after reinstall: %v", err)
	}

	if err := store.Remove(manifest.ID, manifest.Version); err != nil {
		t.Fatalf("Remove: %v", err)
	}
	if _, err := store.Verify(manifest.ID, manifest.Version); err == nil {
		t.Fatal("removed model should no longer verify")
	}
}

func TestModelStoreCanRetryAfterDownloadFailure(t *testing.T) {
	data := []byte("retryable model")
	var requests atomic.Int32
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if requests.Add(1) == 1 {
			http.Error(w, "temporary failure", http.StatusServiceUnavailable)
			return
		}
		_, _ = w.Write(data)
	}))
	defer server.Close()

	store := NewModelStore(t.TempDir())
	manifest := testManifest(server.URL, data)

	if _, err := store.Install(context.Background(), manifest, nil); err == nil {
		t.Fatal("first installation should fail")
	}
	if _, err := store.Install(context.Background(), manifest, nil); err != nil {
		t.Fatalf("retry installation: %v", err)
	}
}

func TestModelStoreCancellationLeavesNoInstalledVersion(t *testing.T) {
	data := make([]byte, 1024*1024)
	for i := range data {
		data[i] = byte(i % 251)
	}
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(data)
	}))
	defer server.Close()

	store := NewModelStore(t.TempDir())
	manifest := testManifest(server.URL, data)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	_, err := store.Install(ctx, manifest, func(progress DownloadProgress) {
		if progress.BytesDownloaded > 0 {
			cancel()
		}
	})
	if err == nil {
		t.Fatal("cancelled installation should fail")
	}
	if _, verifyErr := store.Verify(manifest.ID, manifest.Version); verifyErr == nil {
		t.Fatal("cancelled installation must not leave an installed model")
	}
}

func TestValidateManifestRejectsTraversal(t *testing.T) {
	data := []byte("x")
	manifest := testManifest("https://example.invalid", data)
	manifest.Files[0].Path = "../escape.bin"
	if err := ValidateManifest(manifest); err == nil {
		t.Fatal("path traversal should be rejected")
	}
}
