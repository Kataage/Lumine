package domain

import "time"

type AITagSuggestionKind string

const (
	AITagSuggestionGeneral   AITagSuggestionKind = "general"
	AITagSuggestionCharacter AITagSuggestionKind = "character"
	AITagSuggestionRating    AITagSuggestionKind = "rating"
)

type AITagSuggestionState string

const (
	AITagSuggestionPending  AITagSuggestionState = "pending"
	AITagSuggestionAccepted AITagSuggestionState = "accepted"
	AITagSuggestionRejected AITagSuggestionState = "rejected"
)

type AITagSuggestion struct {
	ID           int64                `json:"id"`
	AssetID      int64                `json:"assetId"`
	Kind         AITagSuggestionKind  `json:"kind"`
	Name         string               `json:"name"`
	Confidence   float64              `json:"confidence"`
	State        AITagSuggestionState `json:"state"`
	Threshold    float64              `json:"threshold"`
	Engine       string               `json:"engine"`
	ModelID      string               `json:"modelId"`
	ModelVersion string               `json:"modelVersion"`
	CreatedAt    time.Time            `json:"createdAt"`
	UpdatedAt    time.Time            `json:"updatedAt"`
}

func IsAITagSuggestionKind(kind AITagSuggestionKind) bool {
	switch kind {
	case AITagSuggestionGeneral, AITagSuggestionCharacter, AITagSuggestionRating:
		return true
	default:
		return false
	}
}
