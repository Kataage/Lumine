package commands

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
)

var ErrPromptEngineModelNotReady = errors.New("Prompt Engine model is not loaded")

type PromptEngineRequestDTO struct {
	Operation     string   `json:"operation"`
	Idea          string   `json:"idea,omitempty"`
	Positive      string   `json:"positive,omitempty"`
	Negative      string   `json:"negative,omitempty"`
	SourceProfile string   `json:"sourceProfile,omitempty"`
	TargetProfile string   `json:"targetProfile,omitempty"`
	Instruction   string   `json:"instruction,omitempty"`
	Characters    []string `json:"characters,omitempty"`
	LoRAs         []string `json:"loras,omitempty"`
}

type PromptEngineResultDTO struct {
	Positive         string   `json:"positive"`
	Negative         string   `json:"negative"`
	Characters       []string `json:"characters"`
	LoRAs            []string `json:"loras"`
	Composition      string   `json:"composition"`
	Notes            []string `json:"notes"`
	CompletionTokens int      `json:"completionTokens,omitempty"`
	Engine           string   `json:"engine"`
	ModelID          string   `json:"modelId"`
	ModelVersion     string   `json:"modelVersion"`
}

func (c *AppCommands) RunPromptEngine(request PromptEngineRequestDTO) (*PromptEngineResultDTO, error) {
	if c.aiManager == nil {
		return nil, errors.New("Prompt Engine is not available")
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return nil, err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityPromptEngine) {
		return nil, ErrPromptEngineDisabled
	}
	if err := validatePromptEngineRequest(request); err != nil {
		return nil, err
	}

	status := c.aiManager.Status(domain.AICapabilityPromptEngine)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return nil, fmt.Errorf("%w: %s", ErrPromptEngineModelNotReady, status.State)
	}
	if status.Engine == "" || status.ModelID == "" || status.Version == "" {
		return nil, errors.New("Prompt Engine runtime provenance is incomplete")
	}

	payload := map[string]any{
		"idea":          strings.TrimSpace(request.Idea),
		"positive":      strings.TrimSpace(request.Positive),
		"negative":      strings.TrimSpace(request.Negative),
		"sourceProfile": strings.TrimSpace(request.SourceProfile),
		"targetProfile": strings.TrimSpace(request.TargetProfile),
		"instruction":   strings.TrimSpace(request.Instruction),
		"characters":    cleanPromptInputList(request.Characters),
		"loras":         cleanPromptInputList(request.LoRAs),
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	response, err := c.aiManager.Infer(ctx, domain.AICapabilityPromptEngine, ai.InferenceRequest{
		Operation: request.Operation,
		Payload:   payload,
	})
	if err != nil {
		return nil, fmt.Errorf("run Prompt Engine: %w", err)
	}
	result, err := promptEngineResultFromResponse(response)
	if err != nil {
		return nil, err
	}
	result.Engine = status.Engine
	result.ModelID = status.ModelID
	result.ModelVersion = status.Version
	return &result, nil
}

func validatePromptEngineRequest(request PromptEngineRequestDTO) error {
	if len([]rune(request.Idea)) > 12000 ||
		len([]rune(request.Positive)) > 24000 ||
		len([]rune(request.Negative)) > 12000 ||
		len([]rune(request.Instruction)) > 8000 ||
		len([]rune(request.SourceProfile)) > 2000 ||
		len([]rune(request.TargetProfile)) > 2000 {
		return errors.New("Prompt Engine request is too long")
	}
	switch request.Operation {
	case llamacpp.PromptOperationIdea:
		if strings.TrimSpace(request.Idea) == "" {
			return errors.New("idea_to_prompt requires idea")
		}
	case llamacpp.PromptOperationImprove:
		if strings.TrimSpace(request.Positive) == "" {
			return errors.New("improve_prompt requires positive")
		}
	case llamacpp.PromptOperationConvert:
		if strings.TrimSpace(request.Positive) == "" || strings.TrimSpace(request.TargetProfile) == "" {
			return errors.New("convert_prompt requires positive and targetProfile")
		}
	case llamacpp.PromptOperationEdit:
		if strings.TrimSpace(request.Positive) == "" || strings.TrimSpace(request.Instruction) == "" {
			return errors.New("edit_prompt requires positive and instruction")
		}
	default:
		return fmt.Errorf("unsupported Prompt Engine operation %q", request.Operation)
	}
	if len(request.Characters) > 64 || len(request.LoRAs) > 64 {
		return errors.New("Prompt Engine accepts at most 64 characters and 64 LoRAs")
	}
	return nil
}

func cleanPromptInputList(values []string) []string {
	result := make([]string, 0, len(values))
	for _, value := range values {
		if cleaned := strings.TrimSpace(value); cleaned != "" {
			result = append(result, cleaned)
		}
	}
	return result
}

func promptEngineResultFromResponse(response ai.InferenceResponse) (PromptEngineResultDTO, error) {
	encoded, err := json.Marshal(response.Payload)
	if err != nil {
		return PromptEngineResultDTO{}, fmt.Errorf("encode Prompt Engine payload: %w", err)
	}
	var base struct {
		Positive         string   `json:"positive"`
		Negative         string   `json:"negative"`
		Characters       []string `json:"characters"`
		LoRAs            []string `json:"loras"`
		Composition      string   `json:"composition"`
		Notes            []string `json:"notes"`
		CompletionTokens int      `json:"completionTokens"`
	}
	if err := json.Unmarshal(encoded, &base); err != nil {
		return PromptEngineResultDTO{}, fmt.Errorf("decode Prompt Engine payload: %w", err)
	}
	base.Positive = strings.TrimSpace(base.Positive)
	if base.Positive == "" {
		return PromptEngineResultDTO{}, errors.New("Prompt Engine returned an empty positive prompt")
	}
	return PromptEngineResultDTO{
		Positive:         base.Positive,
		Negative:         strings.TrimSpace(base.Negative),
		Characters:       cleanPromptInputList(base.Characters),
		LoRAs:            cleanPromptInputList(base.LoRAs),
		Composition:      strings.TrimSpace(base.Composition),
		Notes:            cleanPromptInputList(base.Notes),
		CompletionTokens: base.CompletionTokens,
	}, nil
}
