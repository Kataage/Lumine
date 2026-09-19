package domain

import "time"

type AdvancedVisionRunState string

const (
	AdvancedVisionRunRunning AdvancedVisionRunState = "running"
	AdvancedVisionRunReady   AdvancedVisionRunState = "ready"
	AdvancedVisionRunFailed  AdvancedVisionRunState = "failed"
)

type AdvancedVisionRun struct {
	ID           int64                  `json:"id"`
	Operation    string                 `json:"operation"`
	Instruction  string                 `json:"instruction"`
	State        AdvancedVisionRunState `json:"state"`
	Engine       string                 `json:"engine"`
	ModelID      string                 `json:"modelId"`
	ModelVersion string                 `json:"modelVersion"`
	ResultJSON   string                 `json:"resultJson"`
	ErrorMessage string                 `json:"errorMessage"`
	AssetIDs     []int64                `json:"assetIds"`
	CreatedAt    time.Time              `json:"createdAt"`
	CompletedAt  *time.Time             `json:"completedAt,omitempty"`
}
