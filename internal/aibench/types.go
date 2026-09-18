package aibench

import "time"

type Category string

const (
	CategorySemanticRetrieval       Category = "semantic_retrieval"
	CategoryDanbooruTagging         Category = "danbooru_tagging"
	CategoryCharacterTagging        Category = "character_tagging"
	CategoryRatingTagging           Category = "rating_tagging"
	CategoryLightweightVision       Category = "lightweight_vision"
	CategoryAdvancedVision          Category = "advanced_vision"
	CategoryJapaneseToPrompt        Category = "ja_to_prompt"
	CategoryILPrompt                Category = "il_prompt"
	CategoryModelProfileConversion  Category = "model_profile_conversion"
	CategoryStructuredJSON          Category = "structured_json"
	CategoryPartialEditConstraints  Category = "partial_edit_constraints"
	CategoryLoRATriggerPreservation Category = "lora_trigger_preservation"
	CategoryAdultContentRobustness  Category = "adult_content_robustness"
	CategoryCPULatency              Category = "cpu_latency_tokens_sec"
	CategoryRAM                     Category = "ram"
	CategoryColdStart               Category = "cold_start"
	CategoryModelSize               Category = "model_size"
	CategoryWindowsRuntimeStability Category = "windows_runtime_stability"
)

var RequiredCategories = []Category{
	CategorySemanticRetrieval,
	CategoryDanbooruTagging,
	CategoryCharacterTagging,
	CategoryRatingTagging,
	CategoryLightweightVision,
	CategoryAdvancedVision,
	CategoryJapaneseToPrompt,
	CategoryILPrompt,
	CategoryModelProfileConversion,
	CategoryStructuredJSON,
	CategoryPartialEditConstraints,
	CategoryLoRATriggerPreservation,
	CategoryAdultContentRobustness,
	CategoryCPULatency,
	CategoryRAM,
	CategoryColdStart,
	CategoryModelSize,
	CategoryWindowsRuntimeStability,
}

type FixtureReference struct {
	Path string `json:"path"`
	Role string `json:"role,omitempty"`
}

type Fixture struct {
	ID          string                 `json:"id"`
	Category    Category               `json:"category"`
	Description string                 `json:"description"`
	Input       map[string]any         `json:"input,omitempty"`
	Expected    map[string]any         `json:"expected,omitempty"`
	References  []FixtureReference     `json:"references,omitempty"`
	Tags        []string               `json:"tags,omitempty"`
	Metadata    map[string]interface{} `json:"metadata,omitempty"`
}

type Catalog struct {
	SchemaVersion int       `json:"schemaVersion"`
	FixturePack   string    `json:"fixturePack"`
	Fixtures      []Fixture `json:"fixtures"`
}

type FixturePackFile struct {
	Path      string `json:"path"`
	SHA256    string `json:"sha256"`
	SizeBytes int64  `json:"sizeBytes"`
}

type FixturePackManifest struct {
	SchemaVersion int               `json:"schemaVersion"`
	PackID        string            `json:"packId"`
	Files         []FixturePackFile `json:"files"`
}

type ModelProfile struct {
	ID             string            `json:"id"`
	Version        string            `json:"version"`
	Engine         string            `json:"engine"`
	Quantization   string            `json:"quantization,omitempty"`
	ArtifactSHA256 string            `json:"artifactSha256,omitempty"`
	ModelSizeBytes int64             `json:"modelSizeBytes,omitempty"`
	Runtime        string            `json:"runtime,omitempty"`
	Parameters     map[string]string `json:"parameters,omitempty"`
}

type Environment struct {
	HardwareID      string            `json:"hardwareId"`
	OS              string            `json:"os"`
	Arch            string            `json:"arch"`
	CPU             string            `json:"cpu,omitempty"`
	GPU             string            `json:"gpu,omitempty"`
	RAMBytes        int64             `json:"ramBytes,omitempty"`
	LumineVersion   string            `json:"lumineVersion,omitempty"`
	RuntimeVersions map[string]string `json:"runtimeVersions,omitempty"`
}

type Metrics struct {
	LatencyMS          float64 `json:"latencyMs,omitempty"`
	TokensPerSecond    float64 `json:"tokensPerSecond,omitempty"`
	RAMMB              float64 `json:"ramMb,omitempty"`
	ColdStartMS        float64 `json:"coldStartMs,omitempty"`
	ModelSizeMB        float64 `json:"modelSizeMb,omitempty"`
	RuntimeSuccessRate float64 `json:"runtimeSuccessRate,omitempty"`
}

type CaseStatus string

const (
	CaseStatusOK      CaseStatus = "ok"
	CaseStatusSkipped CaseStatus = "skipped"
	CaseStatusError   CaseStatus = "error"
)

type CaseResult struct {
	FixtureID string         `json:"fixtureId"`
	Category  Category       `json:"category"`
	Status    CaseStatus     `json:"status"`
	Score     float64        `json:"score"`
	Metrics   Metrics        `json:"metrics,omitempty"`
	Output    map[string]any `json:"output,omitempty"`
	Error     string         `json:"error,omitempty"`
	Notes     string         `json:"notes,omitempty"`
}

type BenchmarkResult struct {
	SchemaVersion    int          `json:"schemaVersion"`
	EvaluatorVersion string       `json:"evaluatorVersion"`
	RunID            string       `json:"runId"`
	CreatedAt        time.Time    `json:"createdAt"`
	CatalogPack      string       `json:"catalogPack"`
	Model            ModelProfile `json:"model"`
	Environment      Environment  `json:"environment"`
	Cases            []CaseResult `json:"cases"`
}

type Thresholds struct {
	MaxScoreDrop                 float64 `json:"maxScoreDrop"`
	MaxLatencyRegressionPercent  float64 `json:"maxLatencyRegressionPercent"`
	MaxTokensPerSecondDropPercent float64 `json:"maxTokensPerSecondDropPercent"`
	MaxRAMRegressionPercent      float64 `json:"maxRamRegressionPercent"`
	MaxColdStartRegressionPercent float64 `json:"maxColdStartRegressionPercent"`
	MaxModelSizeRegressionPercent float64 `json:"maxModelSizeRegressionPercent"`
	MaxRuntimeSuccessRateDrop    float64 `json:"maxRuntimeSuccessRateDrop"`
}

type Regression struct {
	FixtureID string   `json:"fixtureId"`
	Category  Category `json:"category"`
	Metric    string   `json:"metric"`
	Baseline  float64  `json:"baseline"`
	Candidate float64  `json:"candidate"`
	Delta     float64  `json:"delta"`
	Limit     float64  `json:"limit"`
}

type ComparisonReport struct {
	SchemaVersion int          `json:"schemaVersion"`
	BaselineRunID string       `json:"baselineRunId"`
	CandidateRunID string      `json:"candidateRunId"`
	HardwareID    string       `json:"hardwareId"`
	Regressions   []Regression `json:"regressions"`
	ComparedCases int          `json:"comparedCases"`
	Passed        bool         `json:"passed"`
}

type AdoptionStatus string

const (
	AdoptionCandidate AdoptionStatus = "candidate"
	AdoptionAdopted   AdoptionStatus = "adopted"
	AdoptionRejected  AdoptionStatus = "rejected"
)

type AdoptionDecision struct {
	Capability       string         `json:"capability"`
	Status           AdoptionStatus `json:"status"`
	ModelID          string         `json:"modelId"`
	Version          string         `json:"version,omitempty"`
	Engine           string         `json:"engine,omitempty"`
	Quantization     string         `json:"quantization,omitempty"`
	ArtifactSHA256   string         `json:"artifactSha256,omitempty"`
	EvidenceResults  []string       `json:"evidenceResults,omitempty"`
	Issue            string         `json:"issue,omitempty"`
	Rationale        string         `json:"rationale"`
	UpdatedAt        string         `json:"updatedAt"`
}

type AdoptionLedger struct {
	SchemaVersion int                `json:"schemaVersion"`
	Decisions     []AdoptionDecision `json:"decisions"`
}

type AdapterRequest struct {
	SchemaVersion int          `json:"schemaVersion"`
	FixtureDir    string       `json:"fixtureDir,omitempty"`
	Model         ModelProfile `json:"model"`
	Fixture       Fixture      `json:"fixture"`
}

type AdapterResponse struct {
	Status  CaseStatus     `json:"status"`
	Score   float64        `json:"score"`
	Metrics Metrics        `json:"metrics,omitempty"`
	Output  map[string]any `json:"output,omitempty"`
	Error   string         `json:"error,omitempty"`
	Notes   string         `json:"notes,omitempty"`
}
