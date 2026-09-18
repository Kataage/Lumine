//go:build !windows

package siglip2

import (
	"fmt"
	"runtime"

	"github.com/kataage/lumine/internal/ai"
)

func newORTBackend(modelRoot string, options ai.LoadOptions) (ortBackend, error) {
	return nil, fmt.Errorf("SigLIP2 ONNX runtime is currently packaged for Windows amd64; current platform is %s/%s", runtime.GOOS, runtime.GOARCH)
}
