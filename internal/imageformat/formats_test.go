package imageformat

import (
	"strings"
	"testing"
)

func TestCatalogFormatsHaveDeterministicSemanticClassification(t *testing.T) {
	catalog := CatalogExtensions()
	if len(catalog) == 0 {
		t.Fatal("catalog extension list is empty")
	}
	for _, extension := range catalog {
		normalized := NormalizeExtension(extension)
		if normalized == "" {
			t.Fatalf("catalog extension %q normalized to empty", extension)
		}
		if IsSemanticSupportedExtension(normalized) != IsSemanticSupportedExtension(strings.ToUpper(extension)) {
			t.Fatalf("Semantic classification is not case-insensitive for %q", extension)
		}
	}
}

func TestSemanticFormatSetMatchesRegisteredDecoders(t *testing.T) {
	for _, extension := range []string{".jpg", ".jpeg", ".png", ".gif", ".apng"} {
		if !IsSemanticSupportedExtension(extension) {
			t.Fatalf("expected Semantic support for %s", extension)
		}
	}
	for _, extension := range []string{".bmp", ".webp", ".tiff", ".tif", ".ico", ".svg", ".avif"} {
		if IsSemanticSupportedExtension(extension) {
			t.Fatalf("unexpected Semantic support for %s", extension)
		}
	}
}
