package commands

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"sort"
	"strings"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

var ErrTaggerModelNotReady = errors.New("Tagger model is not loaded")

const taggerResultSchemaVersion = 1

type TaggerScore struct {
	Name  string  `json:"name"`
	Score float64 `json:"score"`
}

type TaggerResult struct {
	SchemaVersion      int           `json:"schemaVersion"`
	GeneralTags        []TaggerScore `json:"generalTags"`
	CharacterTags      []TaggerScore `json:"characterTags"`
	RatingScores       []TaggerScore `json:"ratingScores"`
	Rating             string        `json:"rating"`
	GeneralThreshold   float64       `json:"generalThreshold,omitempty"`
	CharacterThreshold float64       `json:"characterThreshold,omitempty"`
	RatingThreshold    float64       `json:"ratingThreshold,omitempty"`
}

func (c *AppCommands) TaggerAnalysisHandler(
	ctx context.Context,
	job domain.AIJob,
) (ai.AnalysisOutput, error) {
	if c.aiManager == nil {
		return ai.AnalysisOutput{}, errors.New("AI model manager is not available")
	}
	if c.tagSuggestionRepo == nil {
		return ai.AnalysisOutput{}, errors.New("AI tag suggestion repository is not available")
	}
	asset, err := c.assetRepo.GetByID(job.AssetID)
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("get Tagger asset %d: %w", job.AssetID, err)
	}
	if asset == nil {
		return ai.AnalysisOutput{}, fmt.Errorf("Tagger asset not found: %d", job.AssetID)
	}

	status := c.aiManager.Status(domain.AICapabilityTagger)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return ai.AnalysisOutput{}, fmt.Errorf("%w: %s", ErrTaggerModelNotReady, status.State)
	}
	if status.Engine == "" || status.ModelID == "" || status.Version == "" {
		return ai.AnalysisOutput{}, errors.New("Tagger runtime provenance is incomplete")
	}

	response, err := c.aiManager.Infer(ctx, domain.AICapabilityTagger, ai.InferenceRequest{
		Operation: "tag_image",
		Payload: map[string]any{
			"filePath": asset.FilePath,
		},
	})
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("tag image %d: %w", asset.ID, err)
	}
	result, err := taggerResultFromResponse(response)
	if err != nil {
		return ai.AnalysisOutput{}, err
	}
	suggestions := taggerSuggestionsFromResult(result)
	if err := c.tagSuggestionRepo.ReplaceForAsset(
		asset.ID,
		status.Engine,
		status.ModelID,
		status.Version,
		suggestions,
	); err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("persist Tagger suggestions for asset %d: %w", asset.ID, err)
	}

	encoded, err := json.Marshal(result)
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("encode Tagger result: %w", err)
	}
	return ai.AnalysisOutput{
		Engine:       status.Engine,
		ModelID:      status.ModelID,
		ModelVersion: status.Version,
		ResultJSON:   string(encoded),
	}, nil
}

func taggerResultFromResponse(response ai.InferenceResponse) (TaggerResult, error) {
	result := TaggerResult{
		SchemaVersion: taggerResultSchemaVersion,
		GeneralTags:   []TaggerScore{},
		CharacterTags: []TaggerScore{},
		RatingScores:  []TaggerScore{},
	}
	var err error
	if result.GeneralTags, err = parseTaggerScores(response.Payload["generalTags"]); err != nil {
		return TaggerResult{}, fmt.Errorf("parse general tag scores: %w", err)
	}
	if result.CharacterTags, err = parseTaggerScores(response.Payload["characterTags"]); err != nil {
		return TaggerResult{}, fmt.Errorf("parse character tag scores: %w", err)
	}
	if result.RatingScores, err = parseTaggerScores(response.Payload["ratingScores"]); err != nil {
		return TaggerResult{}, fmt.Errorf("parse rating scores: %w", err)
	}
	if value, ok := response.Payload["rating"].(string); ok {
		result.Rating = strings.TrimSpace(value)
	}
	result.GeneralThreshold = boundedScore(response.Payload["generalThreshold"])
	result.CharacterThreshold = boundedScore(response.Payload["characterThreshold"])
	result.RatingThreshold = boundedScore(response.Payload["ratingThreshold"])

	result.GeneralTags = filterTaggerScores(result.GeneralTags, result.GeneralThreshold)
	result.CharacterTags = filterTaggerScores(result.CharacterTags, result.CharacterThreshold)
	result.RatingScores = filterTaggerScores(result.RatingScores, result.RatingThreshold)

	if result.Rating == "" && len(result.RatingScores) > 0 {
		result.Rating = result.RatingScores[0].Name
	}
	return result, nil
}

func parseTaggerScores(raw any) ([]TaggerScore, error) {
	if raw == nil {
		return []TaggerScore{}, nil
	}
	var values []TaggerScore
	switch list := raw.(type) {
	case []TaggerScore:
		values = append(values, list...)
	case []any:
		for _, item := range list {
			entry, ok := item.(map[string]any)
			if !ok {
				return nil, fmt.Errorf("unexpected score entry type %T", item)
			}
			name, _ := entry["name"].(string)
			score, ok := numberAsFloat64(entry["score"])
			if !ok {
				return nil, fmt.Errorf("score for %q is not numeric", name)
			}
			values = append(values, TaggerScore{Name: name, Score: score})
		}
	default:
		return nil, fmt.Errorf("unexpected score list type %T", raw)
	}

	best := make(map[string]float64, len(values))
	display := make(map[string]string, len(values))
	for _, value := range values {
		name := strings.TrimSpace(value.Name)
		if name == "" {
			continue
		}
		if math.IsNaN(value.Score) || math.IsInf(value.Score, 0) || value.Score < 0 || value.Score > 1 {
			return nil, fmt.Errorf("score for %q %.4f is outside [0,1]", name, value.Score)
		}
		key := strings.ToLower(name)
		if current, exists := best[key]; !exists || value.Score > current {
			best[key] = value.Score
			display[key] = name
		}
	}
	result := make([]TaggerScore, 0, len(best))
	for key, score := range best {
		result = append(result, TaggerScore{Name: display[key], Score: score})
	}
	sort.Slice(result, func(i, j int) bool {
		if result[i].Score == result[j].Score {
			return strings.ToLower(result[i].Name) < strings.ToLower(result[j].Name)
		}
		return result[i].Score > result[j].Score
	})
	return result, nil
}

func numberAsFloat64(value any) (float64, bool) {
	switch typed := value.(type) {
	case float64:
		return typed, true
	case float32:
		return float64(typed), true
	case int:
		return float64(typed), true
	case int64:
		return float64(typed), true
	default:
		return 0, false
	}
}

func boundedScore(value any) float64 {
	score, ok := numberAsFloat64(value)
	if !ok || math.IsNaN(score) || math.IsInf(score, 0) || score < 0 || score > 1 {
		return 0
	}
	return score
}

func filterTaggerScores(values []TaggerScore, threshold float64) []TaggerScore {
	if threshold <= 0 {
		return values
	}
	filtered := make([]TaggerScore, 0, len(values))
	for _, value := range values {
		if value.Score >= threshold {
			filtered = append(filtered, value)
		}
	}
	return filtered
}

func taggerSuggestionsFromResult(result TaggerResult) []domain.AITagSuggestion {
	total := len(result.GeneralTags) + len(result.CharacterTags) + len(result.RatingScores)
	suggestions := make([]domain.AITagSuggestion, 0, total)
	for _, value := range result.GeneralTags {
		suggestions = append(suggestions, domain.AITagSuggestion{
			Kind:       domain.AITagSuggestionGeneral,
			Name:       value.Name,
			Confidence: value.Score,
			Threshold:  result.GeneralThreshold,
		})
	}
	for _, value := range result.CharacterTags {
		suggestions = append(suggestions, domain.AITagSuggestion{
			Kind:       domain.AITagSuggestionCharacter,
			Name:       value.Name,
			Confidence: value.Score,
			Threshold:  result.CharacterThreshold,
		})
	}
	for _, value := range result.RatingScores {
		suggestions = append(suggestions, domain.AITagSuggestion{
			Kind:       domain.AITagSuggestionRating,
			Name:       value.Name,
			Confidence: value.Score,
			Threshold:  result.RatingThreshold,
		})
	}
	return suggestions
}

func (c *AppCommands) GetAITagSuggestions(assetID int64) ([]domain.AITagSuggestion, error) {
	if c.tagSuggestionRepo == nil {
		return []domain.AITagSuggestion{}, nil
	}
	return c.tagSuggestionRepo.ListByAsset(assetID)
}

func (c *AppCommands) AcceptAITagSuggestion(id int64) error {
	if c.tagSuggestionRepo == nil {
		return errors.New("AI tag suggestion repository is not available")
	}
	_, err := c.tagSuggestionRepo.Accept(id)
	return err
}

func (c *AppCommands) AcceptAllAITagSuggestions(assetID int64) error {
	if c.tagSuggestionRepo == nil {
		return errors.New("AI tag suggestion repository is not available")
	}
	_, err := c.tagSuggestionRepo.AcceptAll(assetID)
	return err
}

func (c *AppCommands) RejectAITagSuggestion(id int64) error {
	if c.tagSuggestionRepo == nil {
		return errors.New("AI tag suggestion repository is not available")
	}
	return c.tagSuggestionRepo.Reject(id)
}

func (c *AppCommands) RejectAllAITagSuggestions(assetID int64) error {
	if c.tagSuggestionRepo == nil {
		return errors.New("AI tag suggestion repository is not available")
	}
	return c.tagSuggestionRepo.RejectAll(assetID)
}

func (c *AppCommands) ReanalyzeTaggerAsset(assetID int64, priority int) (bool, error) {
	if c.aiJobQueue == nil {
		return false, errors.New("AI job queue is not available")
	}
	_, created, err := c.aiJobQueue.Enqueue(assetID, domain.AICapabilityTagger, priority, false)
	return created, err
}

func (c *AppCommands) EnqueueAutomaticTaggerAssets(assetIDs []int64) (int, error) {
	if len(assetIDs) == 0 || c.aiJobQueue == nil || c.aiManager == nil {
		return 0, nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return 0, err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityTagger) ||
		!settings.CapabilityEnabled(domain.AICapabilityAutoAnalyze) {
		return 0, nil
	}
	status := c.aiManager.Status(domain.AICapabilityTagger)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return 0, nil
	}
	return c.aiJobQueue.EnqueueMany(assetIDs, domain.AICapabilityTagger, 0, true)
}
