package siglip2

import (
	"context"
	"errors"
	"fmt"
	"path/filepath"
	"strings"

	"github.com/kataage/lumine/internal/ai"
)

type ortBackend interface {
	EmbedText(input [siglipTextLength]int64) ([]float32, error)
	EmbedImage(input []float32) ([]float32, error)
	RuntimeDiagnostics() ai.RuntimeDiagnostics
	Close() error
}

type Engine struct {
	model     ai.InstalledModel
	tokenizer *siglipTokenizer
	runtime   ortBackend
}

func NewEngine() ai.Engine {
	return &Engine{}
}

func (e *Engine) ID() string {
	return EngineID
}

func (e *Engine) SupportsGPU() bool {
	return true
}

func (e *Engine) RuntimeDiagnostics() ai.RuntimeDiagnostics {
	if e.runtime == nil {
		return ai.RuntimeDiagnostics{}
	}
	return e.runtime.RuntimeDiagnostics()
}

func (e *Engine) Load(ctx context.Context, model ai.InstalledModel, options ai.LoadOptions) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	if model.Manifest.Engine != EngineID {
		return fmt.Errorf("SigLIP2 engine cannot load manifest engine %q", model.Manifest.Engine)
	}
	if model.Manifest.ID != DefaultModelID {
		return fmt.Errorf("unsupported SigLIP2 model %q", model.Manifest.ID)
	}

	tokenizer, err := loadTokenizer(filepath.Join(model.RootDir, tokenizerPath))
	if err != nil {
		return err
	}
	if err := ctx.Err(); err != nil {
		return err
	}

	runtimeBackend, err := newORTBackend(model.RootDir, options)
	if err != nil {
		return err
	}

	if e.runtime != nil {
		_ = e.runtime.Close()
	}
	e.model = model
	e.tokenizer = tokenizer
	e.runtime = runtimeBackend
	return nil
}

func (e *Engine) Infer(ctx context.Context, request ai.InferenceRequest) (ai.InferenceResponse, error) {
	if e.runtime == nil || e.tokenizer == nil {
		return ai.InferenceResponse{}, errors.New("SigLIP2 runtime is not loaded")
	}
	if err := ctx.Err(); err != nil {
		return ai.InferenceResponse{}, err
	}

	var vector []float32
	var err error
	switch request.Operation {
	case "embed_text":
		text, _ := request.Payload["text"].(string)
		text = strings.TrimSpace(text)
		if text == "" {
			return ai.InferenceResponse{}, errors.New("semantic query text is required")
		}
		tokens, tokenErr := e.tokenizer.Encode64(text)
		if tokenErr != nil {
			return ai.InferenceResponse{}, tokenErr
		}
		vector, err = e.runtime.EmbedText(tokens)
	case "embed_image":
		filePath, _ := request.Payload["filePath"].(string)
		if strings.TrimSpace(filePath) == "" {
			return ai.InferenceResponse{}, errors.New("semantic image path is required")
		}
		pixels, preprocessErr := PreprocessImageContext(ctx, filePath)
		if preprocessErr != nil {
			return ai.InferenceResponse{}, preprocessErr
		}
		if err := ctx.Err(); err != nil {
			return ai.InferenceResponse{}, err
		}
		vector, err = e.runtime.EmbedImage(pixels)
	case "embed_image_tensor":
		pixels, ok := request.Payload["pixels"].([]float32)
		if !ok || len(pixels) == 0 {
			return ai.InferenceResponse{}, errors.New("preprocessed semantic image tensor is required")
		}
		if err := ctx.Err(); err != nil {
			return ai.InferenceResponse{}, err
		}
		vector, err = e.runtime.EmbedImage(pixels)
	default:
		return ai.InferenceResponse{}, fmt.Errorf("unsupported SigLIP2 operation %q", request.Operation)
	}
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	if err := ctx.Err(); err != nil {
		return ai.InferenceResponse{}, err
	}

	vector, err = cosineUnit(vector)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	return ai.InferenceResponse{
		Payload: map[string]any{
			"embedding": vector,
		},
	}, nil
}

func (e *Engine) Unload(ctx context.Context) error {
	if e.runtime == nil {
		e.tokenizer = nil
		e.model = ai.InstalledModel{}
		return nil
	}
	err := e.runtime.Close()
	e.runtime = nil
	e.tokenizer = nil
	e.model = ai.InstalledModel{}
	return err
}
