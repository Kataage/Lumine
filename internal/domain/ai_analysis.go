package domain

import "time"

type AIAnalysisState string

const (
	AIAnalysisQueued  AIAnalysisState = "queued"
	AIAnalysisRunning AIAnalysisState = "running"
	AIAnalysisReady   AIAnalysisState = "ready"
	AIAnalysisFailed  AIAnalysisState = "failed"
	AIAnalysisStale   AIAnalysisState = "stale"
)

type AIAnalysis struct {
	ID           int64           `json:"id"`
	AssetID      int64           `json:"assetId"`
	Capability   AICapability    `json:"capability"`
	State        AIAnalysisState `json:"state"`
	Engine       string          `json:"engine"`
	ModelID      string          `json:"modelId"`
	ModelVersion string          `json:"modelVersion"`
	ResultJSON   string          `json:"resultJson"`
	ErrorMessage string          `json:"errorMessage"`
	AttemptCount int             `json:"attemptCount"`
	AnalyzedAt   *time.Time      `json:"analyzedAt,omitempty"`
	CreatedAt    time.Time       `json:"createdAt"`
	UpdatedAt    time.Time       `json:"updatedAt"`
}

type AIJobStatus string

const (
	AIJobQueued    AIJobStatus = "queued"
	AIJobRunning   AIJobStatus = "running"
	AIJobCompleted AIJobStatus = "completed"
	AIJobFailed    AIJobStatus = "failed"
	AIJobCancelled AIJobStatus = "cancelled"
)

type AIJobSource string

const (
	AIJobSourceManual    AIJobSource = "manual"
	AIJobSourceAutomatic AIJobSource = "automatic"
)

type AIJobDiagnosticsCounts struct {
	Queued      int64
	Running     int64
	LongRunning int64
}

type AIJob struct {
	ID             int64        `json:"id"`
	AssetID        int64        `json:"assetId"`
	Capability     AICapability `json:"capability"`
	Source         AIJobSource  `json:"source"`
	Priority       int          `json:"priority"`
	Status         AIJobStatus  `json:"status"`
	AttemptCount   int          `json:"attemptCount"`
	MaxAttempts    int          `json:"maxAttempts"`
	LastError      string       `json:"lastError"`
	CancelRequested bool        `json:"cancelRequested"`
	CreatedAt      time.Time    `json:"createdAt"`
	StartedAt      *time.Time   `json:"startedAt,omitempty"`
	FinishedAt     *time.Time   `json:"finishedAt,omitempty"`
}
