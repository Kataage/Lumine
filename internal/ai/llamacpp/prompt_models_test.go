package llamacpp

import (
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

func TestPromptReferenceManifestIsPinnedAndValid(t *testing.T) {
	manifest := PromptReferenceQwen35Manifest()
	if manifest.Engine != PromptEngineID {
		t.Fatalf("engine = %q", manifest.Engine)
	}
	if manifest.Version == "" || manifest.Version == "main" {
		t.Fatalf("version must be immutable: %q", manifest.Version)
	}
	if len(manifest.Files) != 1 || manifest.Files[0].Role != "model" {
		t.Fatalf("files = %+v", manifest.Files)
	}
	if manifest.Files[0].SHA256 != "f8e45572b9cc35161d4772b09bccfd383fe0bb03fc6d69b40a9138731302290b" {
		t.Fatalf("unexpected model sha256: %s", manifest.Files[0].SHA256)
	}
	if err := ai.ValidateManifest(manifest); err != nil {
		t.Fatalf("ValidateManifest: %v", err)
	}
}
