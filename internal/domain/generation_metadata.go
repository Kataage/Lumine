package domain

import "time"

type GenerationLoRA struct {
	Name         string   `json:"name"`
	Weight       float64  `json:"weight"`
	TriggerWords []string `json:"triggerWords"`
}

type GenerationMetadata struct {
	SchemaVersion int              `json:"schemaVersion"`
	SourceFormat  string           `json:"sourceFormat"`
	Positive      string           `json:"positive"`
	Negative      string           `json:"negative"`
	Checkpoint    string           `json:"checkpoint"`
	LoRAs         []GenerationLoRA `json:"loras"`
	Sampler       string           `json:"sampler"`
	Scheduler     string           `json:"scheduler"`
	CFG           float64          `json:"cfg"`
	Steps         int              `json:"steps"`
	Seed          int64            `json:"seed"`
	Width         int              `json:"width"`
	Height        int              `json:"height"`
	RawPromptJSON string           `json:"rawPromptJson,omitempty"`
	RawWorkflowJSON string         `json:"rawWorkflowJson,omitempty"`
	Parameters    string           `json:"parameters,omitempty"`
	Extra         map[string]any   `json:"extra,omitempty"`
}

type StoredGenerationMetadata struct {
	AssetID       int64
	SchemaVersion int
	ParserVersion int
	SourceFormat  string
	FileSize      int64
	ModifiedAtFS  string
	RawJSON       string
	NormalizedJSON string
	ParsedAt      time.Time
	UpdatedAt     time.Time
}
