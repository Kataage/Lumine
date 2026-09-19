package commands

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

var ErrLightweightVisionModelNotReady = errors.New("Lightweight Vision model is not loaded")

const lightweightVisionResultSchemaVersion = 1

type LightweightVisionResult struct {
	SchemaVersion    int      `json:"schemaVersion"`
	ShortCaption     string   `json:"shortCaption"`
	DetailedCaption  string   `json:"detailedCaption"`
	Subject          string   `json:"subject"`
	Background       string   `json:"background"`
	Composition      string   `json:"composition"`
	Viewpoint        string   `json:"viewpoint"`
	VisibleText      []string `json:"visibleText"`
	Notes            []string `json:"notes"`
	CompletionTokens int      `json:"completionTokens,omitempty"`
}

type LightweightVisionAnalysisDTO struct {
	AssetID      int64                   `json:"assetId"`
	State        domain.AIAnalysisState  `json:"state"`
	Engine       string                  `json:"engine,omitempty"`
	ModelID      string                  `json:"modelId,omitempty"`
	ModelVersion string                  `json:"modelVersion,omitempty"`
	Result       *LightweightVisionResult `json:"result,omitempty"`
	ErrorMessage string                  `json:"errorMessage,omitempty"`
	AnalyzedAt   string                  `json:"analyzedAt,omitempty"`
	UpdatedAt    string                  `json:"updatedAt"`
}

func (c *AppCommands) LightweightVisionAnalysisHandler(
	ctx context.Context,
	job domain.AIJob,
) (ai.AnalysisOutput, error) {
	if c.aiManager == nil {
		return ai.AnalysisOutput{}, errors.New("AI model manager is not available")
	}
	asset, err := c.assetRepo.GetByID(job.AssetID)
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("get Lightweight Vision asset %d: %w", job.AssetID, err)
	}
	if asset == nil {
		return ai.AnalysisOutput{}, fmt.Errorf("Lightweight Vision asset not found: %d", job.AssetID)
	}

	status := c.aiManager.Status(domain.AICapabilityLightweightVision)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return ai.AnalysisOutput{}, fmt.Errorf("%w: %s", ErrLightweightVisionModelNotReady, status.State)
	}
	if status.Engine == "" || status.ModelID == "" || status.Version == "" {
		return ai.AnalysisOutput{}, errors.New("Lightweight Vision runtime provenance is incomplete")
	}

	response, err := c.aiManager.Infer(ctx, domain.AICapabilityLightweightVision, ai.InferenceRequest{
		Operation: "analyze_image",
		Payload: map[string]any{
			"filePath": asset.FilePath,
			"mode":     "detailed",
		},
	})
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("analyze image %d: %w", asset.ID, err)
	}
	result, err := lightweightVisionResultFromResponse(response)
	if err != nil {
		return ai.AnalysisOutput{}, err
	}
	encoded, err := json.Marshal(result)
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("encode Lightweight Vision result: %w", err)
	}
	return ai.AnalysisOutput{
		Engine:       status.Engine,
		ModelID:      status.ModelID,
		ModelVersion: status.Version,
		ResultJSON:   string(encoded),
	}, nil
}

func lightweightVisionResultFromResponse(response ai.InferenceResponse) (LightweightVisionResult, error) {
	result := LightweightVisionResult{
		SchemaVersion: lightweightVisionResultSchemaVersion,
		VisibleText:   []string{},
		Notes:         []string{},
	}

	stringField := func(name string) string {
		value, _ := response.Payload[name].(string)
		return strings.TrimSpace(value)
	}
	result.ShortCaption = stringField("shortCaption")
	result.DetailedCaption = stringField("detailedCaption")
	result.Subject = stringField("subject")
	result.Background = stringField("background")
	result.Composition = stringField("composition")
	result.Viewpoint = stringField("viewpoint")

	switch raw := response.Payload["visibleText"].(type) {
	case []string:
		for _, value := range raw {
			if cleaned := strings.TrimSpace(value); cleaned != "" {
				result.VisibleText = append(result.VisibleText, cleaned)
			}
		}
	case []any:
		for _, item := range raw {
			if value, ok := item.(string); ok {
				if cleaned := strings.TrimSpace(value); cleaned != "" {
					result.VisibleText = append(result.VisibleText, cleaned)
				}
			}
		}
	case string:
		if cleaned := strings.TrimSpace(raw); cleaned != "" {
			result.VisibleText = append(result.VisibleText, cleaned)
		}
	}

	switch value := response.Payload["completionTokens"].(type) {
	case int:
		result.CompletionTokens = value
	case int64:
		result.CompletionTokens = int(value)
	case float64:
		result.CompletionTokens = int(value)
	}

	if result.ShortCaption == "" &&
		result.DetailedCaption == "" &&
		result.Subject == "" &&
		result.Background == "" &&
		result.Composition == "" &&
		result.Viewpoint == "" &&
		len(result.VisibleText) == 0 {
		return LightweightVisionResult{}, errors.New("Lightweight Vision response is empty")
	}
	result.Notes = append(result.Notes, "Local structured vision analysis; confidence is not calibrated by the current model.")
	return result, nil
}

func (c *AppCommands) GetLightweightVisionAnalysis(assetID int64) (*LightweightVisionAnalysisDTO, error) {
	if c.aiJobQueue == nil {
		return nil, nil
	}
	analyses, err := c.aiJobQueue.GetAnalysesByAsset(assetID)
	if err != nil {
		return nil, err
	}
	for _, analysis := range analyses {
		if analysis.Capability != domain.AICapabilityLightweightVision {
			continue
		}
		dto := &LightweightVisionAnalysisDTO{
			AssetID:      analysis.AssetID,
			State:        analysis.State,
			Engine:       analysis.Engine,
			ModelID:      analysis.ModelID,
			ModelVersion: analysis.ModelVersion,
			ErrorMessage: analysis.ErrorMessage,
			UpdatedAt:    analysis.UpdatedAt.Format(time.RFC3339),
		}
		if analysis.AnalyzedAt != nil {
			dto.AnalyzedAt = analysis.AnalyzedAt.Format(time.RFC3339)
		}
		if strings.TrimSpace(analysis.ResultJSON) != "" && analysis.ResultJSON != "{}" {
			var result LightweightVisionResult
			if err := json.Unmarshal([]byte(analysis.ResultJSON), &result); err != nil {
				return nil, fmt.Errorf("decode Lightweight Vision analysis for asset %d: %w", assetID, err)
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
	return nil, nil
}

func (c *AppCommands) EnqueueAutomaticLightweightVisionAssets(assetIDs []int64) (int, error) {
	if len(assetIDs) == 0 || c.aiJobQueue == nil || c.aiManager == nil {
		return 0, nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return 0, err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityLightweightVision) ||
		!settings.CapabilityEnabled(domain.AICapabilityAutoAnalyze) {
		return 0, nil
	}
	status := c.aiManager.Status(domain.AICapabilityLightweightVision)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return 0, nil
	}
	return c.aiJobQueue.EnqueueMany(
		assetIDs,
		domain.AICapabilityLightweightVision,
		-60,
		true,
	)
}

func (c *AppCommands) EnqueueLightweightVisionBackfill() (int, error) {
	if c.aiJobQueue == nil || c.aiManager == nil {
		return 0, nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return 0, err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityLightweightVision) {
		return 0, ErrLightweightVisionDisabled
	}

	status := c.aiManager.Status(domain.AICapabilityLightweightVision)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return 0, ErrLightweightVisionModelNotReady
	}
	if status.Engine == "" || status.ModelID == "" || status.Version == "" {
		return 0, errors.New("Lightweight Vision runtime provenance is incomplete")
	}

	libraries, err := c.libraryRepo.List()
	if err != nil {
		return 0, fmt.Errorf("list libraries for Lightweight Vision backfill: %w", err)
	}

	total := 0
	for _, library := range libraries {
		if !library.IsEnabled {
			continue
		}
		var afterID int64
		for {
			ids, err := c.aiJobQueue.ListNeedingAnalysis(
				library.ID,
				domain.AICapabilityLightweightVision,
				status.Engine,
				status.ModelID,
				status.Version,
				afterID,
				500,
			)
			if err != nil {
				return total, err
			}
			if len(ids) == 0 {
				break
			}
			created, err := c.aiJobQueue.EnqueueMany(
				ids,
				domain.AICapabilityLightweightVision,
				-110,
				false,
			)
			if err != nil {
				return total, err
			}
			total += created
			afterID = ids[len(ids)-1]
			if len(ids) < 500 {
				break
			}
		}
	}
	return total, nil
}
