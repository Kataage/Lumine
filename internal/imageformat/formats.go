package imageformat

import (
	"path/filepath"
	"strings"
)

var catalogExtensions = []string{
	".jpg", ".jpeg", ".png", ".gif",
	".bmp", ".webp", ".tiff", ".tif",
	".ico", ".svg", ".avif", ".apng",
}

// Semantic preprocessing currently uses Go's registered JPEG/GIF/PNG
// decoders. APNG is a PNG container and is decoded as its default/first image.
var semanticExtensions = []string{
	".jpg", ".jpeg", ".png", ".gif", ".apng",
}

func NormalizeExtension(extension string) string {
	extension = strings.TrimSpace(strings.ToLower(extension))
	if extension == "" {
		return ""
	}
	if !strings.HasPrefix(extension, ".") {
		extension = "." + extension
	}
	return extension
}

func Extension(path string) string {
	extension := NormalizeExtension(filepath.Ext(path))
	if extension == "" {
		return "<none>"
	}
	return extension
}

func CatalogExtensions() []string {
	return append([]string(nil), catalogExtensions...)
}

func CatalogExtensionMap() map[string]bool {
	result := make(map[string]bool, len(catalogExtensions))
	for _, extension := range catalogExtensions {
		result[extension] = true
	}
	return result
}

func SemanticExtensions() []string {
	return append([]string(nil), semanticExtensions...)
}

func IsSemanticSupportedExtension(extension string) bool {
	extension = NormalizeExtension(extension)
	for _, supported := range semanticExtensions {
		if extension == supported {
			return true
		}
	}
	return false
}

func IsSemanticSupportedPath(path string) bool {
	return IsSemanticSupportedExtension(filepath.Ext(path))
}
