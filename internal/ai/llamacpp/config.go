package llamacpp

import (
	"errors"
	"fmt"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/kataage/lumine/internal/ai"
)

const (
	runtimeParamMainFile   = "mainFile"
	runtimeParamMMProjFile = "mmprojFile"
	runtimeParamContext    = "context"
	runtimeParamThreads    = "threads"
	runtimeParamMaxTokens  = "maxTokens"
	runtimeParamReasoning  = "reasoning"
)

type modelRuntimeConfig struct {
	mainFile  string
	mmprojFile string
	context   int
	threads   int
	maxTokens int
	reasoning string
}

func parseModelRuntimeConfig(model ai.InstalledModel) (modelRuntimeConfig, error) {
	params := model.Manifest.RuntimeParameters
	allowed := map[string]struct{}{
		runtimeParamMainFile: {},
		runtimeParamMMProjFile: {},
		runtimeParamContext: {},
		runtimeParamThreads: {},
		runtimeParamMaxTokens: {},
		runtimeParamReasoning: {},
	}
	for key := range params {
		if _, ok := allowed[key]; !ok {
			return modelRuntimeConfig{}, fmt.Errorf("unsupported llama.cpp runtime parameter %q", key)
		}
	}

	mainFile := strings.TrimSpace(params[runtimeParamMainFile])
	mmprojFile := strings.TrimSpace(params[runtimeParamMMProjFile])
	if mainFile == "" || mmprojFile == "" {
		for _, file := range model.Manifest.Files {
			lower := strings.ToLower(filepath.Base(file.Path))
			if !strings.HasSuffix(lower, ".gguf") {
				continue
			}
			if strings.Contains(lower, "mmproj") {
				if mmprojFile == "" {
					mmprojFile = file.Path
				}
			} else if mainFile == "" {
				mainFile = file.Path
			}
		}
	}
	if mainFile == "" || mmprojFile == "" {
		return modelRuntimeConfig{}, errors.New("llama.cpp multimodal model requires one main GGUF and one mmproj GGUF")
	}
	if !manifestContainsFile(model.Manifest, mainFile) {
		return modelRuntimeConfig{}, fmt.Errorf("mainFile %q is not declared by the model manifest", mainFile)
	}
	if !manifestContainsFile(model.Manifest, mmprojFile) {
		return modelRuntimeConfig{}, fmt.Errorf("mmprojFile %q is not declared by the model manifest", mmprojFile)
	}

	contextSize, err := parseBoundedInt(params[runtimeParamContext], 4096, 2048, 131072, "context")
	if err != nil {
		return modelRuntimeConfig{}, err
	}
	threads, err := parseBoundedInt(params[runtimeParamThreads], 8, 1, 256, "threads")
	if err != nil {
		return modelRuntimeConfig{}, err
	}
	maxTokens, err := parseBoundedInt(params[runtimeParamMaxTokens], 512, 64, 8192, "maxTokens")
	if err != nil {
		return modelRuntimeConfig{}, err
	}
	reasoning := strings.ToLower(strings.TrimSpace(params[runtimeParamReasoning]))
	if reasoning == "" {
		reasoning = "auto"
	}
	switch reasoning {
	case "auto", "on", "off":
	default:
		return modelRuntimeConfig{}, fmt.Errorf("reasoning must be auto, on or off, got %q", reasoning)
	}

	return modelRuntimeConfig{
		mainFile: mainFile,
		mmprojFile: mmprojFile,
		context: contextSize,
		threads: threads,
		maxTokens: maxTokens,
		reasoning: reasoning,
	}, nil
}

func (c modelRuntimeConfig) serverArgs(port int, allowGPU bool) []string {
	args := []string{
		"-m", c.mainFile,
		"--mmproj", c.mmprojFile,
		"--host", "127.0.0.1",
		"--port", strconv.Itoa(port),
		"--ctx-size", strconv.Itoa(c.context),
		"--threads", strconv.Itoa(c.threads),
		"--parallel", "1",
		"--no-webui",
	}
	if allowGPU {
		args = append(args, "-ngl", "99")
	} else {
		args = append(args, "--no-mmproj-offload", "-ngl", "0")
	}
	if c.reasoning != "auto" {
		args = append(args, "--reasoning", c.reasoning)
	}
	return args
}

func manifestContainsFile(manifest ai.ModelManifest, path string) bool {
	clean := filepath.Clean(filepath.FromSlash(path))
	for _, file := range manifest.Files {
		if filepath.Clean(filepath.FromSlash(file.Path)) == clean {
			return true
		}
	}
	return false
}

func parseBoundedInt(raw string, fallback, minimum, maximum int, name string) (int, error) {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return fallback, nil
	}
	value, err := strconv.Atoi(raw)
	if err != nil {
		return 0, fmt.Errorf("%s must be an integer: %w", name, err)
	}
	if value < minimum || value > maximum {
		return 0, fmt.Errorf("%s must be between %d and %d", name, minimum, maximum)
	}
	return value, nil
}
