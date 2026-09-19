//go:build !windows || !amd64

package tagger

import (
	"fmt"
	"runtime"

	"github.com/kataage/lumine/internal/ai"
)

func newORTBackend(
	model ai.InstalledModel,
	config modelConfig,
	options ai.LoadOptions,
) (runtimeBackend, error) {
	return nil, fmt.Errorf(
		"Tagger ONNX runtime is currently packaged for Windows amd64; current platform is %s/%s",
		runtime.GOOS,
		runtime.GOARCH,
	)
}
