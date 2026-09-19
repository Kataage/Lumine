package commands

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
)

const advancedVisionResultSchemaVersion = 1

var ErrAdvancedVisionModelNotReady = errors.New("Advanced Vision model is not loaded")

type AdvancedVisionResultDTO struct {
	SchemaVersion      int      `json:"schemaVersion"`
	Summary            string   `json:"summary"`
	Subjects           []string `json:"subjects"`
	Environment        string   `json:"environment"`
	Composition        string   `json:"composition"`
	Viewpoint          string   `json:"viewpoint"`
	Actions            []string `json:"actions"`
	Relationships      []string `json:"relationships"`
	Context            string   `json:"context"`
	Differences        []string `json:"differences"`
	Commonalities      []string `json:"commonalities"`
	ReversePromptHints []string `json:"reversePromptHints"`
	VisibleText        []string `json:"visibleText"`
	Notes              []string `json:"notes"`
	CompletionTokens   int      `json:"completionTokens,omitempty"`
}

type AdvancedVisionRunDTO struct {
	ID           int64                    `json:"id"`
	Operation    string                   `json:"operation"`
	Instruction  string                   `json:"instruction"`
	State        domain.AdvancedVisionRunState `json:"state"`
	Engine       string                   `json:"engine"`
	ModelID      string                   `json:"modelId"`
	ModelVersion string                   `json:"modelVersion"`
	AssetIDs     []int64                  `json:"assetIds"`
	Result       *AdvancedVisionResultDTO `json:"result,omitempty"`
	ErrorMessage string                   `json:"errorMessage,omitempty"`
	CreatedAt    string                   `json:"createdAt"`
	CompletedAt  string                   `json:"completedAt,omitempty"`
}

func (c *AppCommands) RunAdvancedVision(
	operation string,
	assetIDs []int64,
	instruction string,
) (*AdvancedVisionRunDTO, error) {
	if c.aiManager == nil || c.advancedVisionRepo == nil {
		return nil, errors.New("Advanced Vision is not available")
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return nil, err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityAdvancedVision) {
		return nil, ErrAdvancedVisionDisabled
	}
	if err := validateAdvancedVisionRequest(operation, assetIDs, instruction); err != nil {
		return nil, err
	}

	status := c.aiManager.Status(domain.AICapabilityAdvancedVision)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return nil, fmt.Errorf("%w: %s", ErrAdvancedVisionModelNotReady, status.State)
	}
	if status.Engine == "" || status.ModelID == "" || status.Version == "" {
		return nil, errors.New("Advanced Vision runtime provenance is incomplete")
	}

	filePaths := make([]string, 0, len(assetIDs))
	for _, assetID := range assetIDs {
		asset, err := c.assetRepo.GetByID(assetID)
		if err != nil {
			return nil, fmt.Errorf("get Advanced Vision asset %d: %w", assetID, err)
		}
		if asset == nil {
			return nil, fmt.Errorf("Advanced Vision asset not found: %d", assetID)
		}
		filePaths = append(filePaths, asset.FilePath)
	}

	run, err := c.advancedVisionRepo.CreateRunning(
		operation,
		instruction,
		status.Engine,
		status.ModelID,
		status.Version,
		assetIDs,
	)
	if err != nil {
		return nil, err
	}

	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	response, inferErr := c.aiManager.Infer(ctx, domain.AICapabilityAdvancedVision, ai.InferenceRequest{
		Operation: operation,
		Payload: map[string]any{
			"filePaths":   filePaths,
			"instruction": strings.TrimSpace(instruction),
		},
	})
	if inferErr != nil {
		_ = c.advancedVisionRepo.Fail(run.ID, inferErr.Error())
		return nil, fmt.Errorf("run Advanced Vision: %w", inferErr)
	}

	result, err := advancedVisionResultFromResponse(response)
	if err != nil {
		_ = c.advancedVisionRepo.Fail(run.ID, err.Error())
		return nil, err
	}
	encoded, err := json.Marshal(result)
	if err != nil {
		_ = c.advancedVisionRepo.Fail(run.ID, err.Error())
		return nil, fmt.Errorf("encode Advanced Vision result: %w", err)
	}
	if err := c.advancedVisionRepo.Complete(run.ID, string(encoded)); err != nil {
		return nil, err
	}
	completed, err := c.advancedVisionRepo.GetByID(run.ID)
	if err != nil {
		return nil, err
	}
	return advancedVisionRunDTO(completed)
}

func (c *AppCommands) GetAdvancedVisionRun(runID int64) (*AdvancedVisionRunDTO, error) {
	if c.advancedVisionRepo == nil {
		return nil, nil
	}
	run, err := c.advancedVisionRepo.GetByID(runID)
	if err != nil || run == nil {
		return nil, err
	}
	return advancedVisionRunDTO(run)
}

func (c *AppCommands) ListAdvancedVisionRunsForAsset(
	assetID int64,
	limit int,
) ([]AdvancedVisionRunDTO, error) {
	if c.advancedVisionRepo == nil {
		return []AdvancedVisionRunDTO{}, nil
	}
	runs, err := c.advancedVisionRepo.ListByAsset(assetID, limit)
	if err != nil {
		return nil, err
	}
	result := make([]AdvancedVisionRunDTO, 0, len(runs))
	for index := range runs {
		dto, err := advancedVisionRunDTO(&runs[index])
		if err != nil {
			return nil, err
		}
		if dto != nil {
			result = append(result, *dto)
		}
	}
	return result, nil
}

func validateAdvancedVisionRequest(operation string, assetIDs []int64, instruction string) error {
	switch operation {
	case llamacpp.AdvancedOperationDeep, llamacpp.AdvancedOperationReversePromptSupport:
		if len(assetIDs) != 1 {
			return fmt.Errorf("%s requires exactly one image", operation)
		}
	case llamacpp.AdvancedOperationCompare:
		if len(assetIDs) < 2 || len(assetIDs) > 8 {
			return errors.New("compare_images requires 2 to 8 images")
		}
	default:
		return fmt.Errorf("unsupported Advanced Vision operation %q", operation)
	}
	if len([]rune(instruction)) > 4000 {
		return errors.New("Advanced Vision instruction is too long")
	}
	seen := make(map[int64]struct{}, len(assetIDs))
	for _, assetID := range assetIDs {
		if assetID <= 0 {
			return fmt.Errorf("invalid Advanced Vision asset id %d", assetID)
		}
		if _, exists := seen[assetID]; exists {
			return fmt.Errorf("duplicate Advanced Vision asset id %d", assetID)
		}
		seen[assetID] = struct{}{}
	}
	return nil
}

func advancedVisionResultFromResponse(response ai.InferenceResponse) (AdvancedVisionResultDTO, error) {
	encoded, err := json.Marshal(response.Payload)
	if err != nil {
		return AdvancedVisionResultDTO{}, fmt.Errorf("encode Advanced Vision payload: %w", err)
	}
	var base llamacpp.AdvancedVisionResult
	if err := json.Unmarshal(encoded, &base); err != nil {
		return AdvancedVisionResultDTO{}, fmt.Errorf("decode Advanced Vision payload: %w", err)
	}
	result := AdvancedVisionResultDTO{
		SchemaVersion:      advancedVisionResultSchemaVersion,
		Summary:            strings.TrimSpace(base.Summary),
		Subjects:           base.Subjects,
		Environment:        strings.TrimSpace(base.Environment),
		Composition:        strings.TrimSpace(base.Composition),
		Viewpoint:          strings.TrimSpace(base.Viewpoint),
		Actions:            base.Actions,
		Relationships:      base.Relationships,
		Context:            strings.TrimSpace(base.Context),
		Differences:        base.Differences,
		Commonalities:      base.Commonalities,
		ReversePromptHints: base.ReversePromptHints,
		VisibleText:        base.VisibleText,
		Notes:              base.Notes,
	}
	switch value := response.Payload["completionTokens"].(type) {
	case int:
		result.CompletionTokens = value
	case int64:
		result.CompletionTokens = int(value)
	case float64:
		result.CompletionTokens = int(value)
	}
	if result.Summary == "" &&
		len(result.Subjects) == 0 &&
		result.Environment == "" &&
		result.Composition == "" &&
		result.Viewpoint == "" &&
		len(result.Actions) == 0 &&
		len(result.Relationships) == 0 &&
		result.Context == "" &&
		len(result.Differences) == 0 &&
		len(result.Commonalities) == 0 &&
		len(result.ReversePromptHints) == 0 &&
		len(result.VisibleText) == 0 {
		return AdvancedVisionResultDTO{}, errors.New("Advanced Vision response is empty")
	}
	return result, nil
}

func advancedVisionRunDTO(run *domain.AdvancedVisionRun) (*AdvancedVisionRunDTO, error) {
	if run == nil {
		return nil, nil
	}
	dto := &AdvancedVisionRunDTO{
		ID:           run.ID,
		Operation:    run.Operation,
		Instruction:  run.Instruction,
		State:        run.State,
		Engine:       run.Engine,
		ModelID:      run.ModelID,
		ModelVersion: run.ModelVersion,
		AssetIDs:     append([]int64(nil), run.AssetIDs...),
		ErrorMessage: run.ErrorMessage,
		CreatedAt:    run.CreatedAt.Format(time.RFC3339),
	}
	if run.CompletedAt != nil {
		dto.CompletedAt = run.CompletedAt.Format(time.RFC3339)
	}
	if strings.TrimSpace(run.ResultJSON) != "" && run.ResultJSON != "{}" {
		var result AdvancedVisionResultDTO
		if err := json.Unmarshal([]byte(run.ResultJSON), &result); err != nil {
			return nil, fmt.Errorf("decode Advanced Vision run %d result: %w", run.ID, err)
		}
		if result.Subjects == nil {
			result.Subjects = []string{}
		}
		if result.Actions == nil {
			result.Actions = []string{}
		}
		if result.Relationships == nil {
			result.Relationships = []string{}
		}
		if result.Differences == nil {
			result.Differences = []string{}
		}
		if result.Commonalities == nil {
			result.Commonalities = []string{}
		}
		if result.ReversePromptHints == nil {
			result.ReversePromptHints = []string{}
		}
		if result.VisibleText == nil {
			result.VisibleText = []string{}
		}
		if result.Notes == nil {
			result.Notes = []string{}
		}
		dto.Result = &result
	}
	return dto, nil
}
