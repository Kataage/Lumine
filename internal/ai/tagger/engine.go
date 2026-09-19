package tagger

import (
	"context"
	"errors"
	"fmt"
	"math"
	"sort"
	"strings"

	"github.com/kataage/lumine/internal/ai"
)

type runtimeBackend interface {
	Run(input imageTensor, outputSize int) ([]float32, error)
	RuntimeDiagnostics() ai.RuntimeDiagnostics
	Close() error
}

type backendFactory func(ai.InstalledModel, modelConfig, ai.LoadOptions) (runtimeBackend, error)

type Engine struct {
	model   ai.InstalledModel
	config  modelConfig
	tags    []tagRow
	runtime runtimeBackend
	factory backendFactory
}

func NewEngine() ai.Engine {
	return newEngine(newORTBackend)
}

func newEngine(factory backendFactory) *Engine {
	return &Engine{factory: factory}
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
	config, err := configFromModel(model)
	if err != nil {
		return err
	}
	tags, err := loadTags(config)
	if err != nil {
		return err
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	if e.factory == nil {
		return errors.New("Tagger runtime factory is not configured")
	}
	runtimeBackend, err := e.factory(model, config, options)
	if err != nil {
		return err
	}

	if e.runtime != nil {
		_ = e.runtime.Close()
	}
	e.model = model
	e.config = config
	e.tags = tags
	e.runtime = runtimeBackend
	return nil
}

func (e *Engine) Infer(ctx context.Context, request ai.InferenceRequest) (ai.InferenceResponse, error) {
	if e.runtime == nil {
		return ai.InferenceResponse{}, errors.New("Tagger runtime is not loaded")
	}
	if err := ctx.Err(); err != nil {
		return ai.InferenceResponse{}, err
	}
	if request.Operation != "tag_image" {
		return ai.InferenceResponse{}, fmt.Errorf("unsupported Tagger operation %q", request.Operation)
	}

	filePath, _ := request.Payload["filePath"].(string)
	if strings.TrimSpace(filePath) == "" {
		return ai.InferenceResponse{}, errors.New("Tagger image path is required")
	}

	generalThreshold, err := requestThreshold(
		request.Payload,
		"generalThreshold",
		e.config.generalThreshold,
	)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	characterThreshold, err := requestThreshold(
		request.Payload,
		"characterThreshold",
		e.config.characterThreshold,
	)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	ratingThreshold, err := requestThreshold(
		request.Payload,
		"ratingThreshold",
		e.config.ratingThreshold,
	)
	if err != nil {
		return ai.InferenceResponse{}, err
	}

	input, err := preprocessImage(filePath, e.config)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	if err := ctx.Err(); err != nil {
		return ai.InferenceResponse{}, err
	}

	values, err := e.runtime.Run(input, len(e.tags))
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("run Tagger model: %w", err)
	}
	if len(values) != len(e.tags) {
		return ai.InferenceResponse{}, fmt.Errorf(
			"Tagger output has %d values, want %d",
			len(values),
			len(e.tags),
		)
	}
	if e.config.activationSigmoid {
		for index := range values {
			values[index] = stableSigmoid(values[index])
		}
	}
	for index, value := range values {
		if math.IsNaN(float64(value)) || math.IsInf(float64(value), 0) {
			return ai.InferenceResponse{}, fmt.Errorf(
				"Tagger output contains non-finite score at index %d",
				index,
			)
		}
	}

	general, characters, ratings := classifyScores(
		e.tags,
		values,
		generalThreshold,
		characterThreshold,
		ratingThreshold,
		e.config.ratingSupported,
	)
	rating := ""
	if len(ratings) > 0 {
		rating = ratings[0].Name
	}

	return ai.InferenceResponse{
		Payload: map[string]any{
			"generalTags":        scorePayload(general),
			"characterTags":      scorePayload(characters),
			"ratingScores":       scorePayload(ratings),
			"rating":             rating,
			"generalThreshold":   generalThreshold,
			"characterThreshold": characterThreshold,
			"ratingThreshold":    ratingThreshold,
		},
	}, nil
}

func (e *Engine) Unload(ctx context.Context) error {
	if e.runtime == nil {
		e.reset()
		return nil
	}
	err := e.runtime.Close()
	e.reset()
	return err
}

func (e *Engine) reset() {
	e.runtime = nil
	e.tags = nil
	e.config = modelConfig{}
	e.model = ai.InstalledModel{}
}

type scoredTag struct {
	Name  string
	Score float32
}

func classifyScores(
	tags []tagRow,
	values []float32,
	generalThreshold float64,
	characterThreshold float64,
	ratingThreshold float64,
	ratingSupported bool,
) ([]scoredTag, []scoredTag, []scoredTag) {
	general := make([]scoredTag, 0)
	characters := make([]scoredTag, 0)
	ratings := make([]scoredTag, 0)

	for index, row := range tags {
		if index >= len(values) {
			break
		}
		score := values[index]
		switch {
		case isRatingTag(row):
			if ratingSupported && float64(score) >= ratingThreshold {
				ratings = append(ratings, scoredTag{Name: row.Name, Score: score})
			}
		case row.Category == 4:
			if float64(score) >= characterThreshold {
				characters = append(characters, scoredTag{Name: row.Name, Score: score})
			}
		default:
			if float64(score) >= generalThreshold {
				general = append(general, scoredTag{Name: row.Name, Score: score})
			}
		}
	}
	sortScoredTags(general)
	sortScoredTags(characters)
	sortScoredTags(ratings)
	return general, characters, ratings
}

func sortScoredTags(values []scoredTag) {
	sort.SliceStable(values, func(i, j int) bool {
		if values[i].Score == values[j].Score {
			return strings.ToLower(values[i].Name) < strings.ToLower(values[j].Name)
		}
		return values[i].Score > values[j].Score
	})
}

func isRatingTag(row tagRow) bool {
	if row.Category == 9 {
		return true
	}
	switch canonicalTagName(row.Name) {
	case "general", "safe", "sensitive", "questionable", "explicit", "g", "s", "q", "e":
		return true
	default:
		return false
	}
}

func canonicalTagName(value string) string {
	value = strings.ToLower(strings.TrimSpace(value))
	value = strings.TrimPrefix(value, "rating:")
	value = strings.TrimPrefix(value, "rating_")
	value = strings.ReplaceAll(value, " ", "_")
	return value
}

func scorePayload(values []scoredTag) []any {
	result := make([]any, 0, len(values))
	for _, value := range values {
		result = append(result, map[string]any{
			"name":  value.Name,
			"score": float64(value.Score),
		})
	}
	return result
}

func requestThreshold(
	payload map[string]any,
	key string,
	fallback float64,
) (float64, error) {
	raw, exists := payload[key]
	if !exists {
		return fallback, nil
	}
	value, ok := numericFloat64(raw)
	if !ok || math.IsNaN(value) || math.IsInf(value, 0) || value < 0 || value > 1 {
		return 0, fmt.Errorf("Tagger %s must be between 0 and 1", key)
	}
	return value, nil
}

func numericFloat64(value any) (float64, bool) {
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

func stableSigmoid(value float32) float32 {
	if value >= 0 {
		exp := math.Exp(-float64(value))
		return float32(1 / (1 + exp))
	}
	exp := math.Exp(float64(value))
	return float32(exp / (1 + exp))
}
